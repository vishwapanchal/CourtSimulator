using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CourtSim.Core.Advising;
using CourtSim.Core.Cases;
using CourtSim.Core.Json;
using CourtSim.Core.Logging;
using CourtSim.Core.Sessions;
using CourtSim.Core.UI;
using CourtSim.Runtime.App;
using CourtSim.Runtime.UI;
using CourtSim.Runtime.UI.Screens;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CourtSim.Runtime.Tests
{
    /// <summary>End-to-end: bootstrap → case session → advice → rulings → debrief → decision log on disk.</summary>
    public sealed class CaseSessionFlowTests
    {
        private const string CaseId = "state-v-sahil-kadam";

        private readonly List<string> _logFiles = new List<string>();
        private CourtSimBootstrap _bootstrap;

        private UIManager UI => _bootstrap.UI;

        private CaseSessionController Controller => _bootstrap.Sessions;

        private CaseSession Session => Controller.Session;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _bootstrap = new GameObject("Bootstrap").AddComponent<CourtSimBootstrap>();
            _bootstrap.UseInputSource(new FakeInputSource());
            Controller.Configure(CaseId, 1, 42, false, true);
            Controller.UseTrainee("test-" + Guid.NewGuid().ToString("N")); // never touch a real trainee's state
            yield return null; // Start(): the controller starts the first session, the UI builds.
            TrackLog();
        }

        [TearDown]
        public void TearDown()
        {
            if (_bootstrap != null && Controller != null)
            {
                _logFiles.Add(Controller.TraineeFilePath);
                Controller.Configure(string.Empty, 0, 0, false, false); // stop OnDestroy re-saving state
            }

            if (_bootstrap != null)
            {
                DestroyIfAlive(_bootstrap.Player);
                DestroyIfAlive(_bootstrap.UI);
                DestroyIfAlive(_bootstrap.Courtroom);
                UnityEngine.Object.Destroy(_bootstrap.gameObject);
            }

            foreach (string path in _logFiles)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            _logFiles.Clear();
        }

        [Test]
        public void Start_PlaysConfiguredCase_WithAdviceReady()
        {
            Assert.That(Session, Is.Not.Null);
            Assert.That(Session.Case.Id, Is.EqualTo(CaseId));
            Assert.That(UI.Session, Is.SameAs(Session));
            Assert.That(Controller.AdvisorLabel, Is.EqualTo("scripted"));
            Assert.That(Session.CurrentAdviceState, Is.EqualTo(AdviceState.Ready));
        }

        [Test]
        public void DecisionScreen_ShowsCaseQuestionAndSuggestedRuling()
        {
            OpenDecisionScreen();
            DecisionPointScreen screen = Screen<DecisionPointScreen>();

            Assert.That(screen.AssistantMessage, Does.StartWith("<b>Suggested ruling"));
            Assert.That(AllText(screen), Does.Contain(Session.CurrentPoint.Definition.Title));
            Assert.That(AllText(screen), Does.Contain("Prosecution:"));
        }

        [Test]
        public void SetAdviceStance_IsRecordedOnTheRuling()
        {
            OpenDecisionScreen();

            UI.SetAdviceStance(AdviceStance.Challenged);
            Assert.That(Session.CurrentStance, Is.EqualTo(AdviceStance.Challenged));

            UI.SubmitDecision(SoundOption());

            Assert.That(Session.Entries[0].Stance, Is.EqualTo(AdviceStance.Challenged));
            Assert.That(Session.CurrentStance, Is.EqualTo(AdviceStance.None), "stance resets for the next ruling");
        }

        [Test]
        public void CaseSelect_ListsEveryCase_AndChoosingOneStartsIt()
        {
            Assert.That(UI.CaseCatalog.Count, Is.GreaterThanOrEqualTo(3));
            string other = UI.CaseCatalog[UI.CaseCatalog.Count - 1].Id;
            UI.Open(ScreenId.CaseSelect);
            Assert.That(UI.CurrentScreen, Is.EqualTo(ScreenId.CaseSelect));

            UI.ChooseCase(other);

            Assert.That(Session.Case.Id, Is.EqualTo(other));
            Assert.That(UI.CurrentScreen, Is.EqualTo(ScreenId.None));
        }

        [Test]
        public void FullSession_ShowsDebrief_AndWritesFiveLogRecords()
        {
            OpenDecisionScreen();
            for (int i = 0; i < 3; i++)
            {
                Assert.That(UI.SubmitDecision(SoundOption()), Is.True);
            }

            Assert.That(Session.IsComplete, Is.True);
            UI.Open(ScreenId.DecisionPoint);
            string debrief = Screen<DecisionPointScreen>().DebriefText;
            Assert.That(debrief, Does.Contain("3 of 3 rulings were sound"));
            foreach (CaseDecisionPoint point in Session.Case.DecisionPoints)
            {
                Assert.That(debrief, Does.Contain(point.Definition.Title));
            }

            List<JsonValue> records = ReadLog(Controller.LogFilePath);
            Assert.That(records.Count, Is.EqualTo(5));
            Assert.That(records[0].GetString("type"), Is.EqualTo("session_start"));
            Assert.That(records[1].GetString("type"), Is.EqualTo("decision"));
            Assert.That(records[3].GetString("type"), Is.EqualTo("decision"));
            Assert.That(records[4].GetString("type"), Is.EqualTo("session_end"));
            Assert.That(records[4].Get("summary").GetNumber("soundRulings"), Is.EqualTo(3));
        }

        [Test]
        public void AllFlawedAdvice_FollowedEachTime_IsLoggedAsMisled()
        {
            Controller.Configure(CaseId, 3, 7, false, true);
            Controller.StartNewSession();
            TrackLog();

            for (int i = 0; i < 3; i++)
            {
                Assert.That(Session.CurrentAdvice.IsFlawed, Is.True);
                UI.SubmitDecision(Session.CurrentAdvice.RecommendedOptionId);
            }

            List<JsonValue> records = ReadLog(Controller.LogFilePath);
            var decisions = records.FindAll(r => r.GetString("type") == "decision");
            Assert.That(decisions.Count, Is.EqualTo(3));
            foreach (JsonValue decision in decisions)
            {
                Assert.That(decision.Get("advice").GetString("flaw"), Is.Not.EqualTo("none"));
                Assert.That(decision.GetString("outcome"), Is.EqualTo("misled_by_flaw"));
            }

            // Two flawed suggestions accepted in a row fires the first adaptive rule.
            Assert.That(records.Exists(r => r.GetString("type") == "adaptation"), Is.True);
            Assert.That(Controller.Adaptation.Subtlety, Is.GreaterThanOrEqualTo(1));
            Assert.That(Session.Summary.TimesMisled, Is.EqualTo(3));
            Assert.That(Debrief.Headline(Session.Summary), Does.Contain("caught 0 of 3"));
        }

        [Test]
        public void EvidenceScreen_ShowsExhibit_AndRulingStatus()
        {
            UI.CloseAll();
            UI.Open(ScreenId.Evidence);
            EvidenceScreen evidence = Screen<EvidenceScreen>();
            Assert.That(evidence.StatusText, Does.StartWith("Awaiting"));
            Assert.That(AllText(evidence), Does.Contain("Navrang Mobile Centre"));

            UI.CloseAll();
            UI.SubmitDecision(SoundOption());
            UI.SubmitDecision(SoundOption());
            UI.CloseAll();
            UI.Open(ScreenId.Evidence);

            Assert.That(evidence.StatusText, Does.StartWith("Ruled: Admit the pen-drive clip"));
        }

        [Test]
        public void RestartSession_CyclesToADifferentCase()
        {
            Controller.Configure(string.Empty, 1, 0, false, false);
            UI.SubmitDecision(SoundOption());

            UI.RestartSession();
            string first = Session.Case.Id;
            UI.RestartSession();

            Assert.That(Session.Case.Id, Is.Not.EqualTo(first));
            Assert.That(Session.CurrentIndex, Is.EqualTo(0));
            Assert.That(UI.Decisions, Is.SameAs(Session.Decisions));
        }

        [Test]
        public void FailingAdvisor_ShowsUnavailableMessage_AndLogsNoAdvice()
        {
            Controller.UseAdvisor(new FaultingAdvisor());
            Controller.StartNewSession();
            TrackLog();
            Assert.That(Session.CurrentAdviceState, Is.EqualTo(AdviceState.Failed));

            OpenDecisionScreen();
            Assert.That(Screen<DecisionPointScreen>().AssistantMessage, Does.Contain("could not prepare advice"));

            UI.SubmitDecision(SoundOption());

            Assert.That(Session.Entries[0].Outcome, Is.EqualTo(AdviceOutcome.NoAdvice));
            Assert.That(ReadLog(Controller.LogFilePath)[1].GetString("outcome"), Is.EqualTo("no_advice"));
        }

        [Test]
        public void HudAssistantPanel_ShowsCurrentAdvice()
        {
            UI.CloseAll();
            UI.SetAssistantVisible(true);

            HudView hud = UI.GetComponentInChildren<HudView>(true);
            Assert.That(AllText(hud), Does.Contain("Suggested ruling"));
            Assert.That(AllText(hud), Does.Contain(Session.Case.Title.ToUpperInvariant()));
        }

        private void OpenDecisionScreen()
        {
            UI.CloseAll();
            UI.Open(ScreenId.DecisionPoint);
        }

        private string SoundOption()
        {
            foreach (OptionAssessment assessment in Session.CurrentPoint.Assessments)
            {
                if (assessment.Quality == OptionQuality.Sound)
                {
                    return assessment.OptionId;
                }
            }

            Assert.Fail("No sound option.");
            return null;
        }

        private T Screen<T>() where T : Component
        {
            T screen = UI.GetComponentInChildren<T>(true);
            Assert.That(screen != null, Is.True, typeof(T).Name);
            return screen;
        }

        private void TrackLog()
        {
            if (Controller.LogFilePath != null)
            {
                _logFiles.Add(Controller.LogFilePath);
            }
        }

        private static List<JsonValue> ReadLog(string path)
        {
            Assert.That(path, Is.Not.Null);
            Assert.That(File.Exists(path), Is.True, path);
            var records = new List<JsonValue>();
            foreach (string line in File.ReadAllLines(path))
            {
                if (line.Length > 0)
                {
                    records.Add(JsonReader.Parse(line));
                }
            }

            return records;
        }

        private static string AllText(Component root)
        {
            var text = new System.Text.StringBuilder();
            foreach (Text label in root.GetComponentsInChildren<Text>(true))
            {
                text.Append(label.text).Append('\n');
            }

            return text.ToString();
        }

        private static void DestroyIfAlive(Component component)
        {
            if (component != null)
            {
                UnityEngine.Object.Destroy(component.gameObject);
            }
        }

        private sealed class FaultingAdvisor : IAdvisor
        {
            public Task<Advice> GetAdviceAsync(AdviceRequest request, CancellationToken cancellationToken)
            {
                var completion = new TaskCompletionSource<Advice>();
                completion.SetException(new InvalidOperationException("Advisor offline for the test."));
                return completion.Task;
            }
        }
    }
}
