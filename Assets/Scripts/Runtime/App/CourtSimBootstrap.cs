using System.Collections.Generic;
using CourtSim.Core.Courtroom;
using CourtSim.Runtime.Characters;
using CourtSim.Runtime.Courtroom;
using CourtSim.Runtime.Interaction;
using CourtSim.Runtime.Player;
using CourtSim.Runtime.UI;
using UnityEngine;

namespace CourtSim.Runtime.App
{
    /// <summary>
    /// Composition root for the CourtRoom scene: wires the shared input source into the player, the
    /// interaction probe and the UI. Builds any missing part (courtroom, player, UI) at runtime, so even
    /// an empty scene containing only this component is playable.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class CourtSimBootstrap : MonoBehaviour
    {
        [SerializeField] private CourtroomRoot _courtroom;
        [SerializeField] private FirstPersonController _player;
        [SerializeField] private UIManager _ui;
        [SerializeField] private CaseSessionController _sessions;
        [SerializeField] private CourtroomCast _cast;
        [Tooltip("Build the courtroom, player and UI at runtime if they are not assigned.")]
        [SerializeField] private bool _assembleMissingParts = true;

        private RuntimeMaterialProvider _runtimeMaterials;
        private IPlayerInputSource _input;

        public CourtroomRoot Courtroom => _courtroom;

        public FirstPersonController Player => _player;

        public UIManager UI => _ui;

        public CaseSessionController Sessions => _sessions;

        /// <summary>The people in the courtroom (advocates, accused, clerk, public), or null.</summary>
        public CourtroomCast Cast => _cast;

        /// <summary>Plays the advocates' submissions before each ruling.</summary>
        public HearingDirector Hearing { get; private set; }

        /// <summary>The input source shared by the player, the interaction probe and the UI.</summary>
        public IPlayerInputSource InputSource => _input;

        /// <summary>Assigns scene references. Used by the editor scene generator.</summary>
        public void Configure(CourtroomRoot courtroom, FirstPersonController player, UIManager ui)
        {
            _courtroom = courtroom;
            _player = player;
            _ui = ui;
        }

        private void Awake()
        {
#if !UNITY_EDITOR
            // Always borderless full screen at the monitor's own resolution, even if Windows remembered an
            // older windowed size for the game.
            Resolution native = Screen.currentResolution;
            Screen.SetResolution(native.width, native.height, FullScreenMode.FullScreenWindow);
#endif
            if (_assembleMissingParts)
            {
                AssembleMissingParts();
            }

            UseInputSource(new LegacyPlayerInputSource());

            if (_sessions == null)
            {
                _sessions = GetComponent<CaseSessionController>();
            }

            if (_sessions == null && _assembleMissingParts)
            {
                _sessions = gameObject.AddComponent<CaseSessionController>();
            }

            if (_sessions != null)
            {
                _sessions.Initialize(_ui);
            }

            if (_cast == null && _courtroom != null)
            {
                _cast = _courtroom.GetComponentInChildren<CourtroomCast>(true);
            }

            Hearing = gameObject.AddComponent<HearingDirector>();
            Hearing.Initialize(_ui, _sessions, _cast);

            ScreenshotTour.AttachIfRequested(this);
            DemoRecorder.AttachIfRequested(this);
            if (_ui != null)
            {
                _ui.GuidedDemoRequested += () => DemoRecorder.StartGuided(this);
            }
        }

        /// <summary>
        /// Routes all gameplay and UI input through <paramref name="input"/>. Call it to swap devices at
        /// runtime (e.g. an XR rig) or to drive the scene from tests.
        /// </summary>
        public void UseInputSource(IPlayerInputSource input)
        {
            _input = input ?? throw new System.ArgumentNullException(nameof(input));
            if (_player != null)
            {
                _player.Initialize(input);
                InteractionProbe probe = _player.GetComponentInChildren<InteractionProbe>();
                if (probe != null)
                {
                    probe.Initialize(input);
                }
            }

            if (_ui != null)
            {
                _ui.Initialize(input);
            }
        }

        private void OnDestroy()
        {
            _runtimeMaterials?.Dispose();
            _runtimeMaterials = null;
        }

        private void AssembleMissingParts()
        {
            if (_courtroom == null)
            {
                CourtroomLayout layout = CourtroomLayout.CreateDefault();
                IReadOnlyList<string> errors = layout.Validate();
                foreach (string error in errors)
                {
                    Debug.LogError($"Courtroom layout: {error}", this);
                }

                _runtimeMaterials = new RuntimeMaterialProvider();
                _courtroom = CourtroomBuilder.Build(layout, _runtimeMaterials);
                CourtroomLighting.Build(_courtroom.transform, new Vector3(layout.RoomSize.X, layout.RoomSize.Y, layout.RoomSize.Z));
                CourtroomLighting.ApplyAmbient();
                CourtroomAmbience.Build(_courtroom.transform, new Vector3(layout.RoomSize.X, layout.RoomSize.Y, layout.RoomSize.Z), _runtimeMaterials);
                _cast = CourtroomCast.Build(_courtroom.transform, _runtimeMaterials);
            }

            if (_player == null)
            {
                _player = PlayerFactory.Create(_courtroom.SpawnPosition, _courtroom.SpawnYaw);
            }

            if (_ui == null)
            {
                _ui = UIManager.Create();
            }
        }
    }
}
