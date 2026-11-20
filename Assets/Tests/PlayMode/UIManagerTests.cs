using System;
using System.Collections;
using CourtSim.Core.Decisions;
using CourtSim.Core.Events;
using CourtSim.Core.Interaction;
using CourtSim.Core.UI;
using CourtSim.Runtime.App;
using CourtSim.Runtime.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CourtSim.Runtime.Tests
{
    public sealed class UIManagerTests
    {
        private UIManager _ui;
        private IDisposable _blockSubscription;
        private bool? _lastBlocked;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _lastBlocked = null;
            _blockSubscription = GameServices.Events.Subscribe<GameplayInputBlockChanged>(e => _lastBlocked = e.IsBlocked);
            _ui = UIManager.Create();
            yield return null; // Start(): builds the UI and opens the main menu.
        }

        [TearDown]
        public void TearDown()
        {
            _blockSubscription.Dispose();
            if (_ui != null)
            {
                UnityEngine.Object.Destroy(_ui.gameObject);
            }
        }

        [Test]
        public void StartsOnMainMenu_WithGameplayBlocked()
        {
            Assert.That(_ui.CurrentScreen, Is.EqualTo(ScreenId.MainMenu));
            Assert.That(_ui.IsGameplayBlocked, Is.True);
            Assert.That(_lastBlocked, Is.True);
            Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
        }

        [Test]
        public void Back_OnMainMenu_DoesNothing()
        {
            _ui.Back();

            Assert.That(_ui.CurrentScreen, Is.EqualTo(ScreenId.MainMenu));
        }

        [Test]
        public void EnteringCourtroom_UnblocksGameplay()
        {
            _ui.CloseAll();

            Assert.That(_ui.IsGameplayBlocked, Is.False);
            Assert.That(_lastBlocked, Is.False);
        }

        [Test]
        public void ModalScreens_BlockGameplay_AndBackRestoresIt()
        {
            _ui.CloseAll();

            _ui.Open(ScreenId.Pause);
            Assert.That(_lastBlocked, Is.True);

            _ui.Open(ScreenId.Controls);
            Assert.That(_ui.CurrentScreen, Is.EqualTo(ScreenId.Controls));

            _ui.Back();
            Assert.That(_ui.CurrentScreen, Is.EqualTo(ScreenId.Pause));

            _ui.Back();
            Assert.That(_ui.CurrentScreen, Is.EqualTo(ScreenId.None));
            Assert.That(_lastBlocked, Is.False);
        }

        [Test]
        public void InteractionRequests_OpenMatchingScreens()
        {
            _ui.CloseAll();

            GameServices.Events.Publish(new InteractionRequested(InteractionAction.OpenEvidence));
            Assert.That(_ui.CurrentScreen, Is.EqualTo(ScreenId.Evidence));

            _ui.CloseAll();
            GameServices.Events.Publish(new InteractionRequested(InteractionAction.OpenDecisionPoint));
            Assert.That(_ui.CurrentScreen, Is.EqualTo(ScreenId.DecisionPoint));

            _ui.CloseAll();
            GameServices.Events.Publish(new InteractionRequested(InteractionAction.ShowAssistant));
            Assert.That(_ui.IsAssistantVisible, Is.True);
        }

        [Test]
        public void SubmitDecision_RecordsRuling_PublishesEvent_AndClosesDialog()
        {
            DecisionSubmitted? submitted = null;
            using (GameServices.Events.Subscribe<DecisionSubmitted>(e => submitted = e))
            {
                _ui.CloseAll();
                _ui.Open(ScreenId.DecisionPoint);

                bool accepted = _ui.SubmitDecision("bail.grant_conditions");

                Assert.That(accepted, Is.True);
                Assert.That(submitted.HasValue, Is.True);
                Assert.That(submitted.Value.Record.Kind, Is.EqualTo(DecisionPointKind.Bail));
                Assert.That(_ui.Decisions.Current.Kind, Is.EqualTo(DecisionPointKind.EvidenceAdmissibility));
                Assert.That(_ui.CurrentScreen, Is.EqualTo(ScreenId.None));
            }
        }

        [Test]
        public void SubmitDecision_RejectsOptionFromWrongDecision()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Ignoring ruling"));

            Assert.That(_ui.SubmitDecision("sentence.non_custodial"), Is.False);
            Assert.That(_ui.Decisions.CurrentIndex, Is.EqualTo(0));
        }

        [Test]
        public void RestartSession_ClearsRulings()
        {
            _ui.SubmitDecision("bail.deny");

            _ui.RestartSession();

            Assert.That(_ui.Decisions.CurrentIndex, Is.EqualTo(0));
        }
    }
}
