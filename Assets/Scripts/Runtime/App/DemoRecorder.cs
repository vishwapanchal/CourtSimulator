using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using CourtSim.Core.Cases;
using CourtSim.Core.Logging;
using CourtSim.Core.Sessions;
using CourtSim.Core.UI;
using CourtSim.Runtime.Common;
using CourtSim.Runtime.Player;
using CourtSim.Runtime.UI;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace CourtSim.Runtime.App
{
    /// <summary>
    /// The guided demo: a scripted "judge" walks the courtroom with the same input a person uses, hears the typed and
    /// spoken advice, rules on every decision point and reaches the debrief, while captions explain each step.
    /// It runs two ways:
    /// <list type="bullet">
    /// <item><b>Live in the game</b> — "Watch guided demo" on the main menu, or <c>-courtsimGuidedDemo</c>. Press Esc
    /// to stop it; control returns to the keyboard and mouse at the end.</item>
    /// <item><b>Recorded to video</b> — <c>-courtsimRecordDemo &lt;file.mp4&gt;</c> captures every frame and the game's own
    /// sound and encodes them with ffmpeg.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// When recording, Time.captureFramerate fixes game time at 30 fps, so the video plays at true speed however long
    /// a frame takes to save, and recording pauses while the model writes advice or the voice is synthesised.
    /// </remarks>
    public sealed class DemoRecorder : MonoBehaviour
    {
        public const string Argument = "-courtsimRecordDemo";
        public const string GuidedArgument = "-courtsimGuidedDemo";
        private const int Fps = 30;

        private readonly List<float> _audio = new List<float>();
        private CourtSimBootstrap _bootstrap;
        private ScriptedInputSource _input;
        private bool _record;
        private string _output;
        private Process _ffmpeg;
        private Stream _ffmpegInput;
        private Texture2D _frame;
        private Text _caption;
        private GameObject _captionBar;
        private bool _recording;
        private int _framesWritten;

        /// <summary>Adds the recorder to the bootstrap object if the command line asks for it.</summary>
        public static void AttachIfRequested(CourtSimBootstrap bootstrap)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == Argument && i + 1 < args.Length)
                {
                    DemoRecorder recorder = Create(bootstrap);
                    recorder._record = true;
                    recorder._output = Path.GetFullPath(args[i + 1]);
                    return;
                }

                if (args[i] == GuidedArgument)
                {
                    Create(bootstrap);
                    return;
                }
            }
        }

        /// <summary>Starts the guided demo live in the game (ignored if one is already running).</summary>
        public static void StartGuided(CourtSimBootstrap bootstrap)
        {
            if (bootstrap.GetComponent<DemoRecorder>() == null)
            {
                Create(bootstrap);
            }
        }

        private static DemoRecorder Create(CourtSimBootstrap bootstrap)
        {
            DemoRecorder demo = bootstrap.gameObject.AddComponent<DemoRecorder>();
            demo._bootstrap = bootstrap;
            demo._input = new ScriptedInputSource();
            bootstrap.UseInputSource(demo._input);
            return demo;
        }

        private IEnumerator Start()
        {
            yield return null; // let the UI and the first session start
            BuildCaptionBar();
            if (!_record)
            {
                yield return RunLive();
                yield break;
            }

            Application.runInBackground = true;
            StartFixedSession();

            string ffmpeg = FindFfmpeg();
            if (ffmpeg == null)
            {
                Debug.LogError("[demo-video] ffmpeg not found. Install it (winget install Gyan.FFmpeg) or pass -courtsimFfmpeg <path>.");
                Application.Quit(1);
                yield break;
            }

            // Wait for the first advice before the camera rolls, so the video starts cleanly.
            yield return WaitWhile(() => Session != null && Session.CurrentAdviceState == AdviceState.Pending, 180f);

            Time.captureFramerate = Fps;
            StartEncoder(ffmpeg);
            AudioRenderer.Start();
            StartCoroutine(CaptureFrames());
            yield return Perform();

            _recording = false;
            yield return null;
            yield return FinishEncoding(ffmpeg);
            Application.Quit();
        }

        /// <summary>Plays the demo in the running game, then hands control back to the keyboard and mouse.</summary>
        private IEnumerator RunLive()
        {
            StartFixedSession();
            _captionBar.SetActive(true);
            _caption.text = "Preparing the guided demo…";
            yield return WaitWhile(() => Session != null && Session.CurrentAdviceState == AdviceState.Pending, 180f);
            Debug.Log("[guided-demo] started");
            _recording = true;
            yield return Perform();
            yield return Hold(2f);
            EndLive();
        }

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            // In the live demo the keyboard is otherwise ignored; Esc stops the demo early.
            if (!_record && Input.GetKeyDown(KeyCode.Escape))
            {
                StopAllCoroutines();
                EndLive();
            }
#endif
        }

        private void EndLive()
        {
            Debug.Log("[guided-demo] finished; control returned to the player");
            _input.Move = Vector2.zero;
            _input.Look = Vector2.zero;
            _bootstrap.UseInputSource(new LegacyPlayerInputSource());
            if (_bootstrap.Sessions != null)
            {
                _bootstrap.Sessions.Configure(string.Empty, 1, 0, true, true); // back to normal sessions
                _bootstrap.Sessions.StartNewSession();
            }

            UI.ShowMainMenu();
            Destroy(_captionBar.transform.parent.gameObject);
            Destroy(this);
        }

        /// <summary>The case the guided demo always plays, so its script and captions match.</summary>
        public const string DemoCaseId = "state-v-sahil-kadam";

        // A fixed demo: an easy case, two of three suggestions planted as flawed, a repeatable plan.
        private void StartFixedSession()
        {
            if (_bootstrap.Sessions != null)
            {
                _bootstrap.Sessions.Configure(DemoCaseId, 2, 2026, true, true);
                _bootstrap.Sessions.StartNewSession();
            }
        }

        private CaseSession Session => _bootstrap.Sessions != null ? _bootstrap.Sessions.Session : null;

        private UIManager UI => _bootstrap.UI;

        private FirstPersonController Judge => _bootstrap.Player;

        // ------------------------------------------------------------------ the scripted demo

        private IEnumerator Perform()
        {
            _recording = true;
            if (_record)
            {
                // The video opens with the dramatic titles, then settles on the home page.
                _captionBar.SetActive(false);
                UI.PlayIntro();
                yield return Hold(1f);
                while (UI.IsIntroPlaying)
                {
                    yield return null;
                }

                yield return Hold(3f);
            }
            else
            {
                UI.ShowMainMenu();
                yield return Hold(1.5f);
            }

            yield return Say("CourtSim trains judges to work with AI advice on Indian criminal law. The judge picks one of 21 cases, sorted Easy, Medium and Hard.", 0f);
            UI.Open(ScreenId.CaseSelect);
            yield return Hold(5f);
            UI.ChooseCase(DemoCaseId);
            yield return Say("The trainee judge enters the Sessions Court.", 0f);
            yield return TurnBy(-35f, 0f, 1.6f);
            yield return TurnBy(70f, 0f, 2.4f);
            yield return TurnBy(-35f, -8f, 1.6f);

            Vector3 benchSpot = new Vector3(0f, 0f, 4.3f);
            yield return VisitEvidence();
            yield return Say("The judge takes the bench. Pressing E calls the first hearing.", 0f);
            yield return WalkTo(benchSpot);
            yield return FaceBench();

            // Once seated, the judge stays on the bench: each ruling leads straight into the next phase's hearing.
            for (int decision = 0; decision < 3 && Session != null && !Session.IsComplete; decision++)
            {
                yield return Rule(decision);
            }

            // After the last ruling the final report opens by itself.
            float reportDeadline = Time.realtimeSinceStartup + 15f;
            while (UI.CurrentScreen != ScreenId.DecisionPoint && Time.realtimeSinceStartup < reportDeadline)
            {
                yield return null;
            }

            yield return Say("The final report: how many rulings were sound, which AI suggestions were planted as wrong, and whether the judge caught them.", 9f);
            yield return Say("Adaptive difficulty: flaws grow subtler for judges who over-trust; explanations grow richer for judges who dismiss sound advice.", 7f);
        }

        private IEnumerator Rule(int decision)
        {
            if (decision == 0)
            {
                _input.PressInteract();
            }

            yield return WaitForHearing(decision);
            yield return Say(decision == 0
                ? "The AI assistant types its suggested ruling and reads it aloud. Some suggestions are deliberately flawed."
                : "The judge stays on the bench and the next phase is heard. The assistant advises again; test it against the record.", 0f);
            yield return WaitForAssistant();
            yield return Hold(1.5f);

            CaseDecisionPoint point = Session.CurrentPoint;
            bool flawed = Session.CurrentAdvice != null && Session.CurrentAdvice.IsFlawed;

            // The last ruling follows the assistant whatever it says, so the debrief has something to teach.
            bool followAdvice = decision == 2 || !flawed;
            UI.SetAdviceStance(followAdvice ? AdviceStance.Accepted : AdviceStance.Challenged);
            yield return Say(followAdvice
                ? "The judge accepts the suggestion and rules."
                : "The judge spots a problem with the advice, challenges it, and rules on the record instead.", 2.5f);

            string optionId = followAdvice && Session.CurrentAdvice != null ? Session.CurrentAdvice.RecommendedOptionId : SoundOption(point);
            UI.SubmitDecision(optionId);
            yield return Hold(2f);
        }

        /// <summary>Waits while the advocates argue; recording pauses only while a voice is being synthesised.</summary>
        private IEnumerator WaitForHearing(int decision)
        {
            HearingDirector hearing = _bootstrap.Hearing;
            if (hearing != null && decision == 0)
            {
                yield return Say("A hearing: the Public Prosecutor and the defence advocate each argue their side.", 0f);
            }

            float deadline = Time.realtimeSinceStartup + 240f;
            while (UI.CurrentScreen != ScreenId.DecisionPoint && Time.realtimeSinceStartup < deadline)
            {
                _recording = !_record || hearing == null || !hearing.IsPreparingSpeech;
                yield return null;
            }

            _recording = true;
        }

        private IEnumerator VisitEvidence()
        {
            yield return Say("Before the hearings, the judge examines the exhibits on the evidence table.", 0f);
            yield return TurnBy(-90f, 8f, 1.2f);
            yield return WalkTo(new Vector3(-4.9f, 0f, 4f));
            yield return FaceYaw(270f, 0f, 1f);
            _input.PressInteract();
            yield return Hold(6f);
            UI.Back();
            yield return Hold(0.6f);
        }

        private IEnumerator FaceBench()
        {
            // Look slightly down so the interaction ray meets the front of the bench.
            yield return FaceYaw(0f, 12f, 1f);
        }

        private static string SoundOption(CaseDecisionPoint point)
        {
            foreach (OptionAssessment assessment in point.Assessments)
            {
                if (assessment.Quality == OptionQuality.Sound)
                {
                    return assessment.OptionId;
                }
            }

            return point.Assessments[0].OptionId;
        }

        // ------------------------------------------------------------------ movement helpers

        private IEnumerator WalkTo(Vector3 target)
        {
            float started = Time.time;
            Vector3 lastPosition = Judge.transform.position;
            float lastProgress = Time.time;
            while (true)
            {
                Vector3 to = target - Judge.transform.position;
                to.y = 0f;
                if (to.magnitude < 0.25f)
                {
                    break;
                }

                float desiredYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                float turn = Mathf.DeltaAngle(Judge.Yaw, desiredYaw);
                _input.Look = new Vector2(Mathf.Clamp(turn * 0.15f, -2.5f, 2.5f), 0f);
                _input.Move = new Vector2(0f, Mathf.Abs(turn) < 30f ? 1f : 0.15f);

                // Safety net: if something blocks the path, step there directly.
                if ((Judge.transform.position - lastPosition).sqrMagnitude > 0.01f)
                {
                    lastPosition = Judge.transform.position;
                    lastProgress = Time.time;
                }
                else if (Time.time - lastProgress > 2f || Time.time - started > 20f)
                {
                    Judge.Teleport(target, desiredYaw);
                    break;
                }

                yield return null;
            }

            _input.Move = Vector2.zero;
            _input.Look = Vector2.zero;
        }

        private IEnumerator FaceYaw(float yaw, float pitch, float seconds)
        {
            yield return TurnBy(Mathf.DeltaAngle(Judge.Yaw, yaw), pitch - Judge.Pitch, seconds);
        }

        /// <summary>Turns smoothly by the given angles (degrees; positive pitch looks down) over a duration.</summary>
        private IEnumerator TurnBy(float yawDegrees, float pitchDegrees, float seconds)
        {
            const float Sensitivity = 2f; // FirstPersonController default
            float done = 0f;
            while (done < 1f)
            {
                // Share of the turn for this frame, based on game time so it takes the same time at any frame rate.
                float step = Mathf.Min(1f - done, Time.deltaTime / Mathf.Max(0.01f, seconds));
                done += step;

                // Pitch input is inverted: negative mouse Y looks down.
                _input.Look = new Vector2(yawDegrees * step / Sensitivity, -pitchDegrees * step / Sensitivity);
                yield return null;
            }

            _input.Look = Vector2.zero;
        }

        // ------------------------------------------------------------------ waiting and captions

        private IEnumerator WaitForAssistant()
        {
            // Pause the recording while advice or speech is being generated, then wait for typing and speech to end.
            float deadline = Time.realtimeSinceStartup + 240f;
            while (Time.realtimeSinceStartup < deadline)
            {
                bool preparing = (Session != null && Session.CurrentAdviceState == AdviceState.Pending) || UI.IsAssistantPreparing;
                _recording = !preparing;
                if (!preparing && !UI.IsAssistantTalking)
                {
                    break;
                }

                yield return null;
            }

            _recording = true;
        }

        private IEnumerator Say(string caption, float holdSeconds)
        {
            _caption.text = caption;
            _captionBar.SetActive(!string.IsNullOrEmpty(caption));
            if (holdSeconds > 0f)
            {
                yield return Hold(holdSeconds);
            }
        }

        // Game time: real time when live, fixed 1/30 s steps when recording, so both play at true speed.
        private static IEnumerator Hold(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                yield return null;
            }
        }

        private static IEnumerator WaitWhile(Func<bool> condition, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        private void BuildCaptionBar()
        {
            var canvasObject = new GameObject("DemoCaptions", typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            _captionBar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            _captionBar.transform.SetParent(canvasObject.transform, false);
            var bar = (RectTransform)_captionBar.transform;
            bar.anchorMin = new Vector2(0.1f, 0f);
            bar.anchorMax = new Vector2(0.9f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.anchoredPosition = new Vector2(0f, 40f);
            bar.sizeDelta = new Vector2(0f, 96f);
            _captionBar.GetComponent<Image>().color = new Color(0.05f, 0.08f, 0.16f, 0.88f);

            var textObject = new GameObject("Caption", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(bar, false);
            var textRect = (RectTransform)textObject.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(28f, 10f);
            textRect.offsetMax = new Vector2(-28f, -10f);
            _caption = textObject.GetComponent<Text>();
            _caption.font = BuiltinAssets.DefaultFont;
            _caption.fontSize = 30;
            _caption.alignment = TextAnchor.MiddleCenter;
            _caption.color = new Color(0.96f, 0.93f, 0.86f);
            _captionBar.SetActive(false);
        }

        // ------------------------------------------------------------------ capture and encoding

        private IEnumerator CaptureFrames()
        {
            var endOfFrame = new WaitForEndOfFrame();
            int channels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
            while (_ffmpegInput != null)
            {
                yield return endOfFrame;
                if (_ffmpegInput == null)
                {
                    break; // recording finished while this frame was rendering
                }


                // Always drain the audio renderer so sound stays aligned; keep it only for recorded frames.
                int samples = AudioRenderer.GetSampleCountForCaptureFrame() * channels;
                if (samples > 0)
                {
                    using (var buffer = new NativeArray<float>(samples, Allocator.Temp))
                    {
                        AudioRenderer.Render(buffer);
                        if (_recording)
                        {
                            _audio.AddRange(buffer);
                        }
                    }
                }

                if (!_recording || _ffmpegInput == null)
                {
                    continue;
                }

                _frame.ReadPixels(new Rect(0, 0, _frame.width, _frame.height), 0, 0, false);
                NativeArray<byte> pixels = _frame.GetRawTextureData<byte>();
                byte[] bytes = pixels.ToArray();
                _ffmpegInput.Write(bytes, 0, bytes.Length);
                _framesWritten++;
            }
        }

        private void StartEncoder(string ffmpeg)
        {
            int width = Screen.width - (Screen.width % 2);
            int height = Screen.height - (Screen.height % 2);
            _frame = new Texture2D(width, height, TextureFormat.RGB24, false);
            string videoOnly = Path.ChangeExtension(_output, ".video.mp4");
            Directory.CreateDirectory(Path.GetDirectoryName(_output));
            var info = new ProcessStartInfo(ffmpeg,
                $"-y -f rawvideo -pix_fmt rgb24 -s {width}x{height} -r {Fps} -i - -vf vflip -c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p \"{videoOnly}\"")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                CreateNoWindow = true,
            };
            _ffmpeg = Process.Start(info);
            _ffmpegInput = _ffmpeg.StandardInput.BaseStream;
            Debug.Log($"[demo-video] Recording {width}x{height} at {Fps} fps to {videoOnly}");
        }

        private IEnumerator FinishEncoding(string ffmpeg)
        {
            Stream input = _ffmpegInput;
            _ffmpegInput = null;
            input.Close();
            _ffmpeg.WaitForExit();
            AudioRenderer.Stop();
            Time.captureFramerate = 0;

            string videoOnly = Path.ChangeExtension(_output, ".video.mp4");
            string wav = Path.ChangeExtension(_output, ".wav");
            int channels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
            WriteWav(wav, _audio, AudioSettings.outputSampleRate, channels);

            var mux = Process.Start(new ProcessStartInfo(ffmpeg,
                $"-y -i \"{videoOnly}\" -i \"{wav}\" -c:v copy -c:a aac -b:a 192k -shortest \"{_output}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            mux.WaitForExit();
            if (mux.ExitCode == 0)
            {
                File.Delete(videoOnly);
                File.Delete(wav);
            }

            Debug.Log($"[demo-video] Finished: {_output} ({_framesWritten} frames, {_framesWritten / (float)Fps:F0} s), mux exit {mux.ExitCode}");
            yield break;
        }

        private static void WriteWav(string path, List<float> samples, int sampleRate, int channels)
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                int dataBytes = samples.Count * 2;
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataBytes);
                writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1); // PCM
                writer.Write((short)channels);
                writer.Write(sampleRate);
                writer.Write(sampleRate * channels * 2);
                writer.Write((short)(channels * 2));
                writer.Write((short)16);
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(dataBytes);
                foreach (float sample in samples)
                {
                    writer.Write((short)(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue));
                }
            }
        }

        private static string FindFfmpeg()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-courtsimFfmpeg" && File.Exists(args[i + 1]))
                {
                    return args[i + 1];
                }
            }

            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                string candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (dir.Length > 0 && File.Exists(candidate))
                {
                    return candidate;
                }
            }

            string winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(winget))
            {
                foreach (string file in Directory.GetFiles(winget, "ffmpeg.exe", SearchOption.AllDirectories))
                {
                    return file;
                }
            }

            return null;
        }
    }
}
