using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using CourtSim.Core.Presentation;
using CourtSim.Runtime.Common;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CourtSim.Runtime.Audio
{
    /// <summary>
    /// Reads the AI assistant's advice aloud with the speech voices built into Windows, so it works offline
    /// and needs no extra package.
    /// </summary>
    /// <remarks>
    /// <para>Unity has no text-to-speech of its own, so <see cref="Speak"/> starts a hidden PowerShell process
    /// that uses .NET's <c>System.Speech</c> synthesiser to write the speech to a temporary .wav file. It prefers
    /// an Indian English (en-IN) voice such as Microsoft Heera or Ravi when one is installed.</para>
    /// <para>The process runs in the background; <see cref="Update"/> only checks whether it has finished, so the
    /// game never freezes. The finished file is decoded by <see cref="WavReader"/> and played through an
    /// <see cref="AudioSource"/>. A new <see cref="Speak"/> call cancels whatever was being prepared or played.</para>
    /// <para>If speech is unavailable (another platform, no voices, PowerShell blocked) one warning is logged and
    /// the assistant simply stays silent; the text on screen is unaffected.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class AssistantVoice : MonoBehaviour
    {
        /// <summary>Synthesis normally takes 1–3 seconds; anything longer means PowerShell is stuck.</summary>
        private const float TimeoutSeconds = 30f;

        [Tooltip("Speak the assistant's advice aloud (Windows only).")]
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

        /// <summary>
        /// Switches speech off for every voice, e.g. for tests or a "mute assistant" setting. When false,
        /// <see cref="Speak"/> does nothing.
        /// </summary>
        public static bool SpeechAllowed { get; set; } = true;

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

        /// <summary>True while Windows is still turning the text into sound (before anything is heard).</summary>
        public bool IsPreparing => _process != null;

        /// <summary>True while the speech is audible.</summary>
        public bool IsPlaying => _audio != null && _audio.isPlaying;

        /// <summary>Says <paramref name="plainText"/> aloud, cancelling anything said before. Returns at once.</summary>
        /// <param name="plainText">Text without rich-text tags (see <see cref="RichTextTyping.StripTags"/>).</param>
        public void Speak(string plainText)
        {
            Stop();
            if (!_enabled || !SpeechAllowed || string.IsNullOrWhiteSpace(plainText))
            {
                return;
            }

            // Batch mode (command-line builds and test runs) has no one listening.
            if (Application.isBatchMode)
            {
                return;
            }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            StartSynthesis(plainText);
#else
            WarnOnce("The assistant's voice uses Windows speech and is not available on this platform.");
#endif
        }

        /// <summary>Stops speaking and cancels any speech still being prepared.</summary>
        public void Stop()
        {
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

        private void Awake()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f; // the assistant is a voice in your ear, not a person in the room
            _audio.ignoreListenerPause = true;
        }

        private void Update()
        {
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
                    Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + BuildScript(_textPath, _wavPath) + "\"",
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
        private static string BuildScript(string textPath, string wavPath)
        {
            string text = "'" + textPath.Replace("'", "''") + "'";
            string wav = "'" + wavPath.Replace("'", "''") + "'";
            return "$ErrorActionPreference = 'Stop'; "
                + "Add-Type -AssemblyName System.Speech; "
                + "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer; "
                + "try { "
                + "$v = $s.GetInstalledVoices() | Where-Object { $_.Enabled -and $_.VoiceInfo.Culture.Name -eq 'en-IN' } | Select-Object -First 1; "
                + "if ($v) { $s.SelectVoice($v.VoiceInfo.Name) }; "
                + "$s.Rate = 0; "
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

                WavData wav = WavReader.Read(File.ReadAllBytes(wavPath));
                if (wav.FrameCount == 0)
                {
                    return;
                }

                _clip = AudioClip.Create("AssistantSpeech", wav.FrameCount, wav.Channels, wav.SampleRate, false);
                _clip.SetData(wav.Samples, 0);
                _clip.hideFlags = HideFlags.DontSave;
                _audio.clip = _clip;
                _audio.volume = _volume;
                _audio.Play();
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
            Debug.LogWarning(message + " The assistant will stay silent; its advice is still shown on screen.");
        }
    }
}
