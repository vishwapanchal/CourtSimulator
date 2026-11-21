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
    /// Records a narrated demo video of a full session. Launch the player with
    /// <c>-courtsimRecordDemo &lt;file.mp4&gt;</c>: a scripted "judge" walks the courtroom with the same input a person
    /// uses, rules on every decision point and reaches the debrief, while captions explain each step. Every frame
    /// and the game's own sound (typing clicks and the assistant's voice) are captured and encoded with ffmpeg.
    /// </summary>
    /// <remarks>
    /// Time.captureFramerate fixes game time at 30 fps, so the video plays at true speed however long a frame takes
    /// to save. Recording pauses while the local model writes advice or the voice is being synthesised, so the video
    /// never shows long waits.
    /// </remarks>
    public sealed class DemoRecorder : MonoBehaviour
    {
        public const string Argument = "-courtsimRecordDemo";
        private const int Fps = 30;

        private readonly List<float> _audio = new List<float>();
        private CourtSimBootstrap _bootstrap;
        private ScriptedInputSource _input;
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
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == Argument)
                {
                    DemoRecorder recorder = bootstrap.gameObject.AddComponent<DemoRecorder>();
                    recorder._bootstrap = bootstrap;
                    recorder._output = Path.GetFullPath(args[i + 1]);
                    recorder._input = new ScriptedInputSource();
                    bootstrap.UseInputSource(recorder._input);
                    return;
                }
            }
        }

        private IEnumerator Start()
        {
            Application.runInBackground = true;
            yield return null; // let the UI and the first session start
            BuildCaptionBar();

            // A fixed demo: the first (easiest) case, two of three suggestions planted as flawed, a repeatable plan.
            if (_bootstrap.Sessions != null)
            {
                string firstCase = _bootstrap.Sessions.Cases.Count > 0 ? _bootstrap.Sessions.Cases[0].Id : string.Empty;
                _bootstrap.Sessions.Configure(firstCase, 2, 2026, true, true);
                _bootstrap.Sessions.StartNewSession();
            }

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

        private CaseSession Session => _bootstrap.Sessions != null ? _bootstrap.Sessions.Session : null;

        private UIManager UI => _bootstrap.UI;

        private FirstPersonController Judge => _bootstrap.Player;

        // ------------------------------------------------------------------ the scripted demo

        private IEnumerator Perform()
        {
            _recording = true;
            yield return Say("CourtSim: an adaptive courtroom simulator that trains judges to work with AI advice, on Indian criminal law.", 5f);
            yield return Say("The trainee judge enters the Sessions Court.", 0f);
            UI.CloseAll();
            yield return TurnBy(-35f, 0f, 1.6f);
            yield return TurnBy(70f, 0f, 2.4f);
            yield return TurnBy(-35f, -8f, 1.6f);

            Vector3 benchSpot = new Vector3(0f, 0f, 4.3f);
            yield return Say("Walk to the judge's bench. Press E to hear the next matter.", 0f);
            yield return WalkTo(benchSpot);
            yield return FaceBench();

            for (int decision = 0; decision < 3 && Session != null && !Session.IsComplete; decision++)
            {
                if (decision == 1)
                {
                    yield return VisitEvidence(benchSpot);
                }

                yield return Rule(decision);
            }

            yield return Hold(1f);
            UI.CloseAll();
            UI.Open(ScreenId.DecisionPoint);
            yield return Say("The debrief reveals which suggestions were planted as flawed, and scores over-trust and under-trust in the AI.", 9f);
            yield return Say("Adaptive difficulty: flaws grow subtler for judges who over-trust; explanations grow richer for judges who dismiss sound advice.", 7f);
        }

        private IEnumerator Rule(int decision)
        {
            _input.PressInteract();
            yield return WaitWhile(() => UI.CurrentScreen != ScreenId.DecisionPoint, 5f);
            yield return Say(decision == 0
                ? "The AI assistant types its suggested ruling and reads it aloud. Some suggestions are deliberately flawed."
                : "A new matter. The assistant advises again; the judge must test it against the record.", 0f);
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

        private IEnumerator VisitEvidence(Vector3 benchSpot)
        {
            yield return Say("Before ruling on admissibility, the judge examines the exhibit.", 0f);
            yield return TurnBy(-90f, 8f, 1.2f);
            yield return WalkTo(new Vector3(-4.9f, 0f, 4f));
            yield return FaceYaw(270f, 0f, 1f);
            _input.PressInteract();
            yield return Hold(6f);
            UI.Back();
            yield return Hold(0.6f);
            yield return WalkTo(benchSpot);
            yield return FaceBench();
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
            int frames = Mathf.Max(1, Mathf.RoundToInt(seconds * Fps));
            const float Sensitivity = 2f; // FirstPersonController default
            for (int i = 0; i < frames; i++)
            {
                // Pitch input is inverted: negative mouse Y looks down.
                _input.Look = new Vector2(yawDegrees / frames / Sensitivity, -pitchDegrees / frames / Sensitivity);
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

        private static IEnumerator Hold(float seconds)
        {
            int frames = Mathf.RoundToInt(seconds * Fps);
            for (int i = 0; i < frames; i++)
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
