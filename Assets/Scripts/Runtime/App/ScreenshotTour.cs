using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CourtSim.Core.Cases;
using CourtSim.Core.Sessions;
using CourtSim.Core.UI;
using CourtSim.Runtime.UI;
using UnityEngine;

namespace CourtSim.Runtime.App
{
    /// <summary>
    /// Visual-check mode: launch the player with <c>-courtsimScreenshots &lt;folder&gt;</c> and it walks through every
    /// screen (menu, controls, HUD, assistant, decision with advice, evidence, debrief, pause), saves a PNG of each
    /// and quits. Lets the UI be reviewed from real rendered frames without anyone clicking through it.
    /// </summary>
    public sealed class ScreenshotTour : MonoBehaviour
    {
        public const string Argument = "-courtsimScreenshots";
        private const float AdviceTimeoutSeconds = 90f;

        private CourtSimBootstrap _bootstrap;
        private string _folder;
        private bool _sampling;

        /// <summary>Adds the tour to the bootstrap object if the command line asks for it.</summary>
        public static void AttachIfRequested(CourtSimBootstrap bootstrap)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == Argument)
                {
                    ScreenshotTour tour = bootstrap.gameObject.AddComponent<ScreenshotTour>();
                    tour._bootstrap = bootstrap;
                    tour._folder = Path.GetFullPath(args[i + 1]);
                    return;
                }
            }
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(_folder);
            UIManager ui = _bootstrap.UI;
            yield return new WaitForSecondsRealtime(2f); // let the main menu fade in

            yield return Capture("01-main-menu");
            ui.Open(ScreenId.CaseSelect);
            yield return Capture("01b-case-select");
            ui.CloseAll();
            ui.Open(ScreenId.Controls);
            yield return Capture("02-controls");

            ui.CloseAll();
            yield return Capture("03-courtroom-hud");
            yield return SampleFrames("courtroom (no UI open)", 3f);

            // The hearing: ask for the first ruling at the bench, as pressing E would, and capture both advocates.
            CourtSim.Runtime.App.GameServices.Events.Publish(new CourtSim.Core.Events.InteractionRequested(CourtSim.Core.Interaction.InteractionAction.OpenDecisionPoint));
            yield return WaitUntilSpeaking(() => _bootstrap.Cast != null ? _bootstrap.Cast.Prosecutor : null);
            yield return Capture("03b-hearing-prosecutor");
            yield return WaitUntilSpeaking(() => _bootstrap.Cast != null ? _bootstrap.Cast.Defence : null);
            yield return Capture("03c-hearing-defence");
            float hearingDeadline = Time.realtimeSinceStartup + 120f;
            while (ui.CurrentScreen != ScreenId.DecisionPoint && Time.realtimeSinceStartup < hearingDeadline)
            {
                yield return null;
            }

            ui.CloseAll();
            ui.SetAssistantVisible(true);
            yield return WaitForAdvice();
            yield return Capture("04-assistant-panel");
            ui.SetAssistantVisible(false);

            ui.Open(ScreenId.DecisionPoint);
            yield return Capture("05-decision-with-advice");
            ui.CloseAll();
            ui.Open(ScreenId.Evidence);
            yield return Capture("06-evidence");

            // Rule on every point: follow the first suggestion, reject the rest, so the debrief has variety.
            CaseSession session = _bootstrap.Sessions != null ? _bootstrap.Sessions.Session : null;
            _sampling = true; // frame times while the AI writes advice must stay smooth: requests never block the main thread
            StartCoroutine(SampleWhile("rulings while the AI generates advice"));
            while (session != null && !session.IsComplete)
            {
                yield return WaitForAdvice();
                string choice = session.CurrentIndex == 0 && session.CurrentAdvice != null
                    ? session.CurrentAdvice.RecommendedOptionId
                    : FirstOtherOption(session);
                ui.SubmitDecision(choice);
            }

            _sampling = false;

            ui.CloseAll();
            ui.Open(ScreenId.DecisionPoint);
            yield return Capture("07-debrief");
            ui.CloseAll();
            ui.Open(ScreenId.Pause);
            yield return Capture("08-pause");

            Debug.Log("[screenshots] Tour complete: " + _folder);
            Application.Quit();
        }

        /// <summary>Records frame times for a few seconds and logs average, 99th percentile and worst frame.</summary>
        private IEnumerator SampleFrames(string label, float seconds)
        {
            var times = new List<float>();
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                times.Add(Time.unscaledDeltaTime * 1000f);
            }

            LogFrameStats(label, times);
        }

        private IEnumerator SampleWhile(string label)
        {
            var times = new List<float>();
            while (_sampling)
            {
                yield return null;
                times.Add(Time.unscaledDeltaTime * 1000f);
            }

            LogFrameStats(label, times);
        }

        private static void LogFrameStats(string label, List<float> times)
        {
            if (times.Count == 0)
            {
                return;
            }

            times.Sort();
            float total = 0f;
            foreach (float t in times)
            {
                total += t;
            }

            float p99 = times[System.Math.Min(times.Count - 1, (int)(times.Count * 0.99f))];
            Debug.Log($"[perf] {label}: {times.Count} frames, avg {total / times.Count:F1} ms ({1000f * times.Count / total:F0} fps), p99 {p99:F1} ms, worst {times[times.Count - 1]:F1} ms");
        }

        private static IEnumerator WaitUntilSpeaking(System.Func<CourtSim.Runtime.Characters.CharacterAnimator> who)
        {
            float deadline = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < deadline && (who() == null || !who().IsSpeaking))
            {
                yield return null;
            }

            yield return new WaitForSecondsRealtime(1.5f);
        }

        private IEnumerator WaitForAdvice()
        {
            CaseSession session = _bootstrap.Sessions != null ? _bootstrap.Sessions.Session : null;
            float deadline = Time.realtimeSinceStartup + AdviceTimeoutSeconds;
            while (session != null && session.CurrentAdviceState == AdviceState.Pending && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        private IEnumerator Capture(string name)
        {
            yield return new WaitForSecondsRealtime(0.8f); // let fades finish
            string path = Path.Combine(_folder, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            yield return null;
            yield return null;
            Debug.Log("[screenshots] " + path);
        }

        private static string FirstOtherOption(CaseSession session)
        {
            string advised = session.CurrentAdvice != null ? session.CurrentAdvice.RecommendedOptionId : null;
            foreach (OptionAssessment assessment in session.CurrentPoint.Assessments)
            {
                if (assessment.OptionId != advised)
                {
                    return assessment.OptionId;
                }
            }

            return session.CurrentPoint.Assessments[0].OptionId;
        }
    }
}
