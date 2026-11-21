using System;
using System.Collections;
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
            ui.Open(ScreenId.Controls);
            yield return Capture("02-controls");

            ui.CloseAll();
            yield return Capture("03-courtroom-hud");
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
            while (session != null && !session.IsComplete)
            {
                yield return WaitForAdvice();
                string choice = session.CurrentIndex == 0 && session.CurrentAdvice != null
                    ? session.CurrentAdvice.RecommendedOptionId
                    : FirstOtherOption(session);
                ui.SubmitDecision(choice);
            }

            ui.CloseAll();
            ui.Open(ScreenId.DecisionPoint);
            yield return Capture("07-debrief");
            ui.CloseAll();
            ui.Open(ScreenId.Pause);
            yield return Capture("08-pause");

            Debug.Log("[screenshots] Tour complete: " + _folder);
            Application.Quit();
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
