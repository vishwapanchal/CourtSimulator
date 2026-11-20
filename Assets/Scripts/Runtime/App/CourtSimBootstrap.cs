using System.Collections.Generic;
using CourtSim.Core.Courtroom;
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
        [Tooltip("Build the courtroom, player and UI at runtime if they are not assigned.")]
        [SerializeField] private bool _assembleMissingParts = true;

        private RuntimeMaterialProvider _runtimeMaterials;

        public CourtroomRoot Courtroom => _courtroom;

        public FirstPersonController Player => _player;

        public UIManager UI => _ui;

        /// <summary>Assigns scene references. Used by the editor scene generator.</summary>
        public void Configure(CourtroomRoot courtroom, FirstPersonController player, UIManager ui)
        {
            _courtroom = courtroom;
            _player = player;
            _ui = ui;
        }

        private void Awake()
        {
            if (_assembleMissingParts)
            {
                AssembleMissingParts();
            }

            var input = new LegacyPlayerInputSource();
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
