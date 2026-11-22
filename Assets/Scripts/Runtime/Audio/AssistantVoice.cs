using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CourtSim.Core.Presentation;
using CourtSim.Core.Sarvam;
using CourtSim.Runtime.Advising;
using CourtSim.Runtime.Common;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CourtSim.Runtime.Audio
{
    /// <summary>
    /// Speaks the game's lines aloud: the AI assistant's advice and the courtroom characters' submissions.
    /// With a Sarvam AI key the speech is Kannada-English code-mixed; without one (or if Sarvam fails) it falls
    /// back to the English voices built into Windows. The text on screen is always English.
    /// </summary>
    /// <remarks>
    /// <para><b>Sarvam path.</b> <see cref="Speak(string, SarvamVoiceRole)"/> sends the English line to Sarvam's
    /// translate endpoint (English to Kannada-English, with the speaker's gender), then sends that text to the
    /// text-to-speech endpoint with the role's speaker, and plays the returned WAV. The requests use
    /// <see cref="UnityWebRequestTransport"/> with async/await on the main thread, so the game never freezes.
    /// Every finished clip is saved in <c>persistentDataPath/voice-cache/</c> under a hash of the line and voice,
    /// so a repeated line (or a replayed demo) plays at once and costs nothing.</para>
    /// <para><b>Windows path.</b> Unity has no text-to-speech of its own, so a hidden PowerShell process uses .NET's
    /// <c>System.Speech</c> synthesiser to write a temporary .wav file, preferring an Indian English voice.
    /// <see cref="Update"/> only checks whether it has finished.</para>
    /// <para>A new <see cref="Speak(string, SarvamVoiceRole)"/> call, or <see cref="Stop"/>, cancels whatever was
    /// being prepared or played. Problems are logged as one warning; the text on screen is never affected.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class AssistantVoice : MonoBehaviour
    {
        /// <summary>Windows synthesis normally takes 1–3 seconds; anything longer means PowerShell is stuck.</summary>
        private const float TimeoutSeconds = 30f;

        /// <summary>Translate plus speech normally takes 2–5 seconds; after this the Windows voice is used instead.</summary>
        private const float SarvamTimeoutSeconds = 60f;

        /// <summary>After this many failures in a row (e.g. no internet), stop trying Sarvam for this session.</summary>
        private const int MaxSarvamFailures = 3;

        /// <summary>The cache keeps the most recently used clips; older ones are deleted beyond this count.</summary>
        private const int MaxCachedClips = 300;

        private const string CacheFolderName = "voice-cache";

        // Shared by every voice in the game: one key lookup, one client, one warning.
        private static SarvamClient _sarvam;
        private static bool _keyChecked;
        private static bool _sarvamDisabled;
        private static bool _sarvamWarned;
        private static int _sarvamFailures;

        [Tooltip("Speak lines aloud (Kannada-English with a Sarvam key, otherwise the Windows English voice).")]
        [SerializeField] private bool _enabled = true;
        [Range(0f, 1f)]
        [SerializeField] private float _volume = 1f;

        private readonly List<string> _staleFiles = new List<string>();
        private readonly StringBuilder _errors = new StringBuilder();
        private AudioSource _audio;
        private AudioClip _clip;
        private Process _process;
        private string _textPath;
        private string _wavPath;
        private float _startedAt;
        private bool _warned;
        private bool _female;
        private int _rate;

        // The Sarvam request in flight, if any. Each request gets a new ticket number; when an answer arrives
        // with an old ticket, the line was cancelled or replaced, so the answer is ignored.
        private CancellationTokenSource _sarvamRequest;
        private int _ticket;
        private bool _sarvamTimedOut;

        /// <summary>
        /// Switches speech off for every voice, e.g. for tests or a "mute assistant" setting. When false,
        /// <see cref="Speak(string, SarvamVoiceRole)"/> does nothing.
        /// </summary>
        public static bool SpeechAllowed { get; set; } = true;

        /// <summary>
        /// The case being heard. It picks the prosecutor, defence and clerk voices (see <see cref="SarvamVoiceCast"/>);
        /// the assistant always sounds the same. Null uses the default cast.
        /// </summary>
        public string CaseId { get; set; }

        /// <summary>Whether this voice speaks. Turning it off stops any speech in progress.</summary>
        public bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                if (!value)
                {
                    Stop();
                }
            }
        }

        /// <summary>True while speech is being prepared or played.</summary>
        public bool IsSpeaking => IsPreparing || IsPlaying;

        /// <summary>True while the line is still being turned into sound (before anything is heard).</summary>
        /// <remarks>The typewriter text and the demo recorder wait while this is true.</remarks>
        public bool IsPreparing => _process != null || _sarvamRequest != null;

        /// <summary>True while the speech is audible.</summary>
        public bool IsPlaying => _audio != null && _audio.isPlaying;

        /// <summary>Says <paramref name="plainText"/> in the AI assistant's voice. Returns at once.</summary>
        /// <param name="plainText">Text without rich-text tags (see <see cref="RichTextTyping.StripTags"/>).</param>
        public void Speak(string plainText)
        {
            Speak(plainText, SarvamVoiceRole.Assistant);
        }

        /// <summary>
        /// Older overload kept so existing callers still work: female maps to the defence voice, male to the
        /// prosecutor. The Windows fallback uses exactly the given gender and rate (-10 to 10).
        /// </summary>
        public void Speak(string plainText, bool female, int rate)
        {
            SpeakAs(plainText, female ? SarvamVoiceRole.Defence : SarvamVoiceRole.Prosecutor, female, rate);
        }

        /// <summary>Says an English line in <paramref name="role"/>'s voice for the current <see cref="CaseId"/>.</summary>
        public void Speak(string englishText, SarvamVoiceRole role)
        {
            SarvamVoice voice = SarvamVoiceCast.For(CaseId).Get(role);
            int rate = role == SarvamVoiceRole.Prosecutor || role == SarvamVoiceRole.Defence ? 0 : 1;
            SpeakAs(englishText, role, voice.IsFemale, rate);
        }

        /// <summary>Sets <see cref="CaseId"/> and then says the line in <paramref name="role"/>'s voice.</summary>
        public void Speak(string englishText, SarvamVoiceRole role, string caseId)
        {
            CaseId = caseId;
            Speak(englishText, role);
        }

        /// <summary>Stops speaking and cancels any speech still being prepared (including Sarvam requests).</summary>
        public void Stop()
        {
            _ticket++;
            CancelSarvamRequest();

            if (_process != null)
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill();
                    }
                }
                catch (Exception)
                {
                    // The process ended on its own between the check and the kill; nothing to do.
                }

                _process.Dispose();
                _process = null;
            }

            if (_audio != null)
            {
                _audio.Stop();
                _audio.clip = null;
            }

            UnityObjectUtil.SafeDestroy(_clip);
            _clip = null;
            ForgetFile(ref _textPath);
            ForgetFile(ref _wavPath);
            DeleteStaleFiles();
        }

        /// <summary>Static state survives between play sessions when domain reload is off; start each one fresh.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSharedState()
        {
            _sarvam = null;
            _keyChecked = false;
            _sarvamDisabled = false;
            _sarvamWarned = false;
            _sarvamFailures = 0;
        }

        private static string CacheFolder => Path.Combine(Application.persistentDataPath, CacheFolderName);

        private void Awake()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f; // the assistant is a voice in your ear, not a person in the room
            _audio.ignoreListenerPause = true;
        }

        private void Update()
        {
            if (_sarvamRequest != null && Time.unscaledTime - _startedAt > SarvamTimeoutSeconds)
            {
                // Cancelled here, on the main thread, because UnityWebRequest may only be aborted from it.
                _sarvamTimedOut = true;
                _sarvamRequest.Cancel();
            }

            if (_process == null)
            {
                return;
            }

            if (!_process.HasExited)
            {
                if (Time.unscaledTime - _startedAt > TimeoutSeconds)
                {
                    WarnOnce("The assistant's voice took too long to prepare and was cancelled.");
                    Stop();
                }

                return;
            }

            int exitCode = _process.ExitCode;
            _process.Dispose();
            _process = null;
            PlayResult(exitCode);
        }

        private void OnApplicationQuit()
        {
            Stop();
        }

        private void OnDisable()
        {
            Stop();
        }

        private void OnDestroy()
        {
            Stop();
        }

        /// <summary>The common start of every Speak call: Sarvam if possible, else the Windows voice.</summary>
        private void SpeakAs(string text, SarvamVoiceRole role, bool fallbackFemale, int fallbackRate)
        {
            Stop();
            if (!_enabled || !SpeechAllowed || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            // Batch mode (command-line builds and test runs) has no one listening, and must not call paid APIs.
            if (Application.isBatchMode)
            {
                return;
            }

            _female = fallbackFemale;
            _rate = Mathf.Clamp(fallbackRate, -10, 10);
            SarvamClient sarvam = GetSarvamClient();
            if (sarvam != null)
            {
                SpeakWithSarvam(sarvam, text, role, SarvamVoiceCast.For(CaseId).Get(role));
            }
            else
            {
                SpeakWithWindows(text);
            }
        }

        /// <summary>The shared Sarvam client, or null when there is no key or Sarvam was given up on.</summary>
        private static SarvamClient GetSarvamClient()
        {
            if (_sarvamDisabled)
            {
                return null;
            }

            if (!_keyChecked)
            {
                _keyChecked = true;
                string key = SarvamKeyLocator.Find(); // never logged
                if (key == null)
                {
                    _sarvamDisabled = true;
                    WarnSarvamOnce($"No Sarvam AI key found (set {SarvamKeyLocator.EnvironmentVariable} or create "
                        + $"{SarvamKeyLocator.KeyFilePath}), so lines are spoken in English with the Windows voice.");
                    return null;
                }

                _sarvam = new SarvamClient(new SarvamClientOptions(), key, new UnityWebRequestTransport());
            }

            return _sarvam;
        }

        /// <summary>
        /// Runs the Sarvam pipeline for one line. "async void" because nobody waits for it: progress is visible
        /// through <see cref="IsPreparing"/>. Every exception is caught here, so none can escape into Unity.
        /// </summary>
        private async void SpeakWithSarvam(SarvamClient sarvam, string text, SarvamVoiceRole role, SarvamVoice voice)
        {
            int ticket = ++_ticket;
            var request = new CancellationTokenSource();
            _sarvamRequest = request;
            _sarvamTimedOut = false;
            _startedAt = Time.unscaledTime;
            try
            {
                WavData wav = await LoadOrSynthesizeAsync(sarvam, text, role, voice, request.Token);
                if (ticket != _ticket)
                {
                    return; // cancelled or replaced while the answer was on its way
                }

                CancelSarvamRequest();
                _sarvamFailures = 0;
                PlayClip(wav);
            }
            catch (OperationCanceledException)
            {
                if (ticket == _ticket && _sarvamTimedOut)
                {
                    FallBack(text, new TimeoutException($"Sarvam AI took longer than {SarvamTimeoutSeconds:0} seconds."));
                }

                // Otherwise Stop() or a newer line cancelled it on purpose: stay quiet.
            }
            catch (Exception ex)
            {
                if (ticket == _ticket)
                {
                    FallBack(text, ex);
                }
            }
        }

        /// <summary>Plays the line from the cache if it was spoken before; otherwise translate, synthesise and cache it.</summary>
        private static async Task<WavData> LoadOrSynthesizeAsync(
            SarvamClient sarvam, string text, SarvamVoiceRole role, SarvamVoice voice, CancellationToken cancellationToken)
        {
            string path = Path.Combine(CacheFolder, sarvam.CacheKey(role, voice, text) + ".wav");
            WavData cached = TryReadCache(path);
            if (cached != null)
            {
                return cached;
            }

            string codeMixed = await sarvam.TranslateAsync(text, voice.IsFemale, cancellationToken);
            IReadOnlyList<byte[]> parts = await sarvam.SpeakAsync(codeMixed, voice.Speaker, cancellationToken);
            if (parts.Count == 0)
            {
                throw new SarvamException("Sarvam AI returned no audio for this line.");
            }

            byte[] bytes = WavWriter.Concatenate(parts);
            WavData wav = WavReader.Read(bytes); // check it decodes before it is cached
            SaveToCache(path, bytes);
            return wav;
        }

        /// <summary>The cached clip, or null if there is none or it is damaged (then it is deleted).</summary>
        private static WavData TryReadCache(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                WavData wav = WavReader.Read(File.ReadAllBytes(path));

                // "Touch" the file so the clean-up treats it as recently used.
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                return wav;
            }
            catch (FormatException)
            {
                TryDelete(path);
            }
            catch (IOException)
            {
                // Unreadable right now; synthesise again instead.
            }
            catch (UnauthorizedAccessException)
            {
                // Same as above.
            }

            return null;
        }

        /// <summary>Saves a clip (via a temporary file, so a crash never leaves half a clip) and trims old clips.</summary>
        private static void SaveToCache(string path, byte[] wav)
        {
            try
            {
                Directory.CreateDirectory(CacheFolder);
                string temporary = path + ".part";
                File.WriteAllBytes(temporary, wav);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temporary, path);
                TrimCache();
            }
            catch (IOException)
            {
                // The cache is only a speed-up; the clip still plays.
            }
            catch (UnauthorizedAccessException)
            {
                // Same as above.
            }
        }

        /// <summary>Deletes the least recently used clips so the cache holds at most <see cref="MaxCachedClips"/>.</summary>
        private static void TrimCache()
        {
            FileInfo[] files = new DirectoryInfo(CacheFolder).GetFiles("*.wav");
            if (files.Length <= MaxCachedClips)
            {
                return;
            }

            Array.Sort(files, (a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
            for (int i = 0; i < files.Length - MaxCachedClips; i++)
            {
                TryDelete(files[i].FullName);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Locked; it will be trimmed another time.
            }
            catch (UnauthorizedAccessException)
            {
                // Same as above.
            }
        }

        /// <summary>Sarvam failed for this line: warn once, maybe give up on Sarvam, and say it in English instead.</summary>
        private void FallBack(string text, Exception error)
        {
            CancelSarvamRequest();
            _sarvamFailures++;
            if ((error is SarvamException sarvamError && sarvamError.IsKeyRejected) || _sarvamFailures >= MaxSarvamFailures)
            {
                _sarvamDisabled = true;
            }

            // Sarvam's messages never contain the key (SarvamClient removes it), so they are safe to log.
            WarnSarvamOnce("The Kannada voice (Sarvam AI) failed: " + error.Message
                + " Lines are spoken in English with the Windows voice instead.");
            SpeakWithWindows(text);
        }

        private void CancelSarvamRequest()
        {
            if (_sarvamRequest == null)
            {
                return;
            }

            CancellationTokenSource request = _sarvamRequest;
            _sarvamRequest = null;
            request.Cancel();
            request.Dispose();
        }

        private static void WarnSarvamOnce(string message)
        {
            if (_sarvamWarned)
            {
                return;
            }

            _sarvamWarned = true;
            Debug.LogWarning(message);
        }

        private void SpeakWithWindows(string text)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            StartSynthesis(text);
#else
            WarnOnce("The offline voice uses Windows speech and is not available on this platform.");
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private void StartSynthesis(string plainText)
        {
            try
            {
                string stem = Path.Combine(Path.GetTempPath(), "courtsim-voice-" + Guid.NewGuid().ToString("N"));
                _textPath = stem + ".txt";
                _wavPath = stem + ".wav";

                // The text goes through a file so quotes or symbols in the advice can't break the command line.
                File.WriteAllText(_textPath, plainText, new UTF8Encoding(false));

                var info = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + BuildScript(_textPath, _wavPath, _female, _rate) + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardError = true,
                };

                lock (_errors)
                {
                    _errors.Length = 0;
                }

                var process = new Process { StartInfo = info };

                // Error output is collected on a background thread, so the pipe never fills up and blocks PowerShell.
                process.ErrorDataReceived += OnErrorData;
                process.Start();
                process.BeginErrorReadLine();
                _process = process;
                _startedAt = Time.unscaledTime;
            }
            catch (Exception ex)
            {
                WarnOnce("The assistant's voice could not start Windows speech: " + ex.Message);
                Stop();
            }
        }

        /// <summary>
        /// The PowerShell commands, as one line. Paths are put in single quotes, where PowerShell treats
        /// everything literally except a doubled quote.
        /// </summary>
        private static string BuildScript(string textPath, string wavPath, bool female, int rate)
        {
            string text = "'" + textPath.Replace("'", "''") + "'";
            string wav = "'" + wavPath.Replace("'", "''") + "'";
            return "$ErrorActionPreference = 'Stop'; "
                + "Add-Type -AssemblyName System.Speech; "
                + "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer; "
                + "try { "
                + "$g = '" + (female ? "Female" : "Male") + "'; "
                + "$all = $s.GetInstalledVoices() | Where-Object { $_.Enabled }; "
                + "$v = $all | Where-Object { $_.VoiceInfo.Culture.Name -eq 'en-IN' -and $_.VoiceInfo.Gender -eq $g } | Select-Object -First 1; "
                + "if (-not $v) { $v = $all | Where-Object { $_.VoiceInfo.Gender -eq $g } | Select-Object -First 1 }; "
                + "if ($v) { $s.SelectVoice($v.VoiceInfo.Name) }; "
                + "$s.Rate = " + rate.ToString(System.Globalization.CultureInfo.InvariantCulture) + "; "
                + "$s.SetOutputToWaveFile(" + wav + "); "
                + "$s.Speak([System.IO.File]::ReadAllText(" + text + ", [System.Text.Encoding]::UTF8)) "
                + "} finally { $s.Dispose() }";
        }
#endif

        private void OnErrorData(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data))
            {
                return;
            }

            lock (_errors)
            {
                if (_errors.Length < 2000)
                {
                    _errors.AppendLine(e.Data);
                }
            }
        }

        private void PlayResult(int exitCode)
        {
            ForgetFile(ref _textPath);
            string wavPath = _wavPath;
            _wavPath = null;
            try
            {
                if (exitCode != 0 || wavPath == null || !File.Exists(wavPath))
                {
                    string errors;
                    lock (_errors)
                    {
                        errors = _errors.ToString().Trim();
                    }

                    WarnOnce($"Windows speech failed (exit code {exitCode}). {errors}");
                    return;
                }

                PlayClip(WavReader.Read(File.ReadAllBytes(wavPath)));
            }
            catch (Exception ex)
            {
                WarnOnce("The assistant's speech could not be played: " + ex.Message);
            }
            finally
            {
                ForgetFile(ref wavPath);
            }
        }

        /// <summary>Turns decoded samples into an AudioClip and plays it.</summary>
        private void PlayClip(WavData wav)
        {
            if (wav.FrameCount == 0 || _audio == null)
            {
                return;
            }

            UnityObjectUtil.SafeDestroy(_clip);
            _clip = AudioClip.Create("Speech", wav.FrameCount, wav.Channels, wav.SampleRate, false);
            _clip.SetData(wav.Samples, 0);
            _clip.hideFlags = HideFlags.DontSave;
            _audio.clip = _clip;
            _audio.volume = _volume;
            _audio.Play();
        }

        /// <summary>Deletes a temporary file now, or later if a just-killed process still has it open.</summary>
        private void ForgetFile(ref string path)
        {
            if (path == null)
            {
                return;
            }

            _staleFiles.Add(path);
            path = null;
            DeleteStaleFiles();
        }

        private void DeleteStaleFiles()
        {
            for (int i = _staleFiles.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (File.Exists(_staleFiles[i]))
                    {
                        File.Delete(_staleFiles[i]);
                    }

                    _staleFiles.RemoveAt(i);
                }
                catch (IOException)
                {
                    // Still locked; try again on the next Speak or Stop.
                }
                catch (UnauthorizedAccessException)
                {
                    // Still locked; try again on the next Speak or Stop.
                }
            }
        }

        private void WarnOnce(string message)
        {
            if (_warned)
            {
                return;
            }

            _warned = true;
            Debug.LogWarning(message + " The voice will stay silent; the text is still shown on screen.");
        }
    }
}
