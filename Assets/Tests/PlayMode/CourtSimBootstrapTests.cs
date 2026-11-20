using System;
using System.Collections;
using CourtSim.Core.Courtroom;
using CourtSim.Core.Events;
using CourtSim.Core.Interaction;
using CourtSim.Core.Navigation;
using CourtSim.Core.UI;
using CourtSim.Runtime.App;
using CourtSim.Runtime.Interaction;
using CourtSim.Runtime.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CourtSim.Runtime.Tests
{
    /// <summary>End-to-end tests: an empty scene with only the bootstrap must be playable.</summary>
    public sealed class CourtSimBootstrapTests
    {
        private CourtSimBootstrap _bootstrap;
        private FakeInputSource _input;

        private FirstPersonController Player => _bootstrap.Player;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _bootstrap = new GameObject("Bootstrap").AddComponent<CourtSimBootstrap>();
            _input = new FakeInputSource();
            _bootstrap.UseInputSource(_input);
            _bootstrap.Sessions.Configure(string.Empty, 1, 1, false, false); // offline advice, no log files
            yield return null; // UIManager.Start opens the main menu.
        }

        [TearDown]
        public void TearDown()
        {
            if (_bootstrap == null)
            {
                return;
            }

            DestroyIfAlive(_bootstrap.Player);
            DestroyIfAlive(_bootstrap.UI);
            DestroyIfAlive(_bootstrap.Courtroom);
            UnityEngine.Object.Destroy(_bootstrap.gameObject);
        }

        [Test]
        public void Awake_AssemblesCourtroomPlayerAndUI()
        {
            Assert.That(_bootstrap.Courtroom, Is.Not.Null);
            Assert.That(_bootstrap.Player, Is.Not.Null);
            Assert.That(_bootstrap.UI, Is.Not.Null);
            Assert.That(_bootstrap.InputSource, Is.SameAs(_input));
            Assert.That(_bootstrap.UI.CurrentScreen, Is.EqualTo(ScreenId.MainMenu));
            Assert.That(UnityEngine.Object.FindObjectsOfType<Light>().Length, Is.GreaterThan(0));
            Assert.That(Camera.main, Is.Not.Null);
        }

        [Test]
        public void Player_SpawnsAtLayoutSpawnPoint()
        {
            FixtureSpec spawn = CourtroomLayout.CreateDefault().Find(FixtureKind.PlayerSpawn);

            Vector3 feet = Player.transform.position;

            Assert.That(feet.x, Is.EqualTo(spawn.Center.X).Within(0.01f));
            Assert.That(feet.z, Is.EqualTo(spawn.Center.Z).Within(0.01f));
            Assert.That(Player.Yaw, Is.EqualTo(MovementMath.WrapAngle360(spawn.FacingYawDegrees)).Within(0.01f));
        }

        [Test]
        public void UseInputSource_RejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => _bootstrap.UseInputSource(null));
        }

        [UnityTest]
        public IEnumerator Player_WalksForward_WhenNoScreenIsOpen()
        {
            _bootstrap.UI.CloseAll();
            Vector3 start = Player.transform.position;
            Vector3 forward = Player.transform.forward;

            _input.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.5f);
            _input.Move = Vector2.zero;

            float travelled = Vector3.Dot(Player.transform.position - start, forward);
            Assert.That(travelled, Is.GreaterThan(0.3f));
            Assert.That(travelled, Is.LessThan(2f), "walk speed is 2.4 m/s");
        }

        [UnityTest]
        public IEnumerator Player_SprintsFasterThanWalking()
        {
            _bootstrap.UI.CloseAll();
            float walkDistance = 0f;
            float sprintDistance = 0f;

            yield return WalkForward(false, 0.6f, d => walkDistance = d);
            Player.Teleport(_bootstrap.Courtroom.SpawnPosition, _bootstrap.Courtroom.SpawnYaw);
            yield return null;
            yield return WalkForward(true, 0.6f, d => sprintDistance = d);

            Assert.That(walkDistance, Is.GreaterThan(0.3f));
            Assert.That(sprintDistance, Is.GreaterThan(walkDistance * 1.3f));
        }

        [UnityTest]
        public IEnumerator Player_IgnoresMovement_WhileMenuIsOpen()
        {
            Assert.That(Player.InputBlocked, Is.True, "main menu blocks gameplay");
            Vector3 start = Player.transform.position;

            _input.Move = new Vector2(0f, 1f);
            _input.Look = new Vector2(30f, 0f);
            yield return new WaitForSeconds(0.3f);
            _input.Move = Vector2.zero;
            _input.Look = Vector2.zero;

            Vector3 delta = Player.transform.position - start;
            Assert.That(new Vector2(delta.x, delta.z).magnitude, Is.LessThan(0.01f));
        }

        [UnityTest]
        public IEnumerator Look_TurnsPlayer_AndClampsPitch()
        {
            _bootstrap.UI.CloseAll();
            float startYaw = Player.Yaw;

            _input.Look = new Vector2(5f, 500f);
            yield return null;
            yield return null;
            _input.Look = Vector2.zero;

            Assert.That(Mathf.DeltaAngle(startYaw, Player.Yaw), Is.Not.EqualTo(0f).Within(0.01f));
            Assert.That(Mathf.Abs(Player.Pitch), Is.LessThanOrEqualTo(85f + 1e-3f));
            Assert.That(Player.CameraPivot.localEulerAngles.y, Is.EqualTo(0f).Within(0.01f), "only the pivot pitches");
        }

        [UnityTest]
        public IEnumerator Player_LandsOnFloor_UnderGravity()
        {
            _bootstrap.UI.CloseAll();
            Player.Teleport(_bootstrap.Courtroom.SpawnPosition + Vector3.up * 1.5f, 0f);

            yield return new WaitForSeconds(1f);

            Assert.That(Player.transform.position.y, Is.EqualTo(0f).Within(0.1f));
        }

        [UnityTest]
        public IEnumerator FacingEvidenceDisplay_FocusesIt_AndInteractOpensEvidence()
        {
            InteractionFocusChanged? focus = null;
            using (GameServices.Events.Subscribe<InteractionFocusChanged>(e => focus = e))
            {
                _bootstrap.UI.CloseAll();
                FixtureSpec display = CourtroomLayout.CreateDefault().Find(FixtureKind.EvidenceDisplay);
                Player.Teleport(new Vector3(display.Center.X + 1.6f, 0f, display.Center.Z), 270f);
                yield return new WaitForFixedUpdate();
                yield return null;

                InteractionProbe probe = Player.GetComponentInChildren<InteractionProbe>();
                Assert.That(probe.Focused, Is.Not.Null);
                Assert.That(probe.Focused.Action, Is.EqualTo(InteractionAction.OpenEvidence));
                Assert.That(focus.HasValue && focus.Value.HasFocus, Is.True);

                _input.InteractPressed = true;
                yield return null;
                _input.InteractPressed = false;

                Assert.That(_bootstrap.UI.CurrentScreen, Is.EqualTo(ScreenId.Evidence));
                Assert.That(probe.Focused, Is.Null, "opening a screen clears focus");
                Assert.That(focus.Value.HasFocus, Is.False);
            }
        }

        [UnityTest]
        public IEnumerator BackInput_OpensPause_FromGameplay()
        {
            _bootstrap.UI.CloseAll();

            _input.BackPressed = true;
            yield return null;
            _input.BackPressed = false;

            Assert.That(_bootstrap.UI.CurrentScreen, Is.EqualTo(ScreenId.Pause));
        }

        [UnityTest]
        public IEnumerator ToggleAssistantInput_ShowsAssistantPanel()
        {
            _bootstrap.UI.CloseAll();

            _input.ToggleAssistantPressed = true;
            yield return null;
            _input.ToggleAssistantPressed = false;

            Assert.That(_bootstrap.UI.IsAssistantVisible, Is.True);
        }

        private IEnumerator WalkForward(bool sprint, float seconds, Action<float> reportDistance)
        {
            Vector3 start = Player.transform.position;
            _input.SprintHeld = sprint;
            _input.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(seconds);
            _input.Move = Vector2.zero;
            _input.SprintHeld = false;

            Vector3 delta = Player.transform.position - start;
            reportDistance(new Vector2(delta.x, delta.z).magnitude);
        }

        private static void DestroyIfAlive(Component component)
        {
            if (component != null)
            {
                UnityEngine.Object.Destroy(component.gameObject);
            }
        }
    }
}
