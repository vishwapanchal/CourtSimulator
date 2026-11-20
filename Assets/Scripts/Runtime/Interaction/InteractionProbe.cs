using System;
using CourtSim.Core.Events;
using CourtSim.Core.Interaction;
using CourtSim.Runtime.App;
using CourtSim.Runtime.Player;
using UnityEngine;

namespace CourtSim.Runtime.Interaction
{
    /// <summary>
    /// Casts a ray along this transform's forward axis (attach to the camera) and tracks which
    /// <see cref="Interactable"/> the player is looking at.
    /// </summary>
    /// <remarks>Publishes <see cref="InteractionFocusChanged"/> only when focus changes, and
    /// <see cref="InteractionRequested"/> when the interact button is pressed while focused. One
    /// non-allocating raycast per frame; the component lookup is cached per collider.</remarks>
    [DisallowMultipleComponent]
    public sealed class InteractionProbe : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] private float _maxDistance = 3.5f;
        [SerializeField] private LayerMask _layerMask = Physics.DefaultRaycastLayers;

        private IPlayerInputSource _input;
        private IDisposable _blockSubscription;
        private bool _inputBlocked;
        private Interactable _focused;
        private Collider _lastCollider;
        private Interactable _lastInteractable;

        /// <summary>The interactable under the crosshair, or null.</summary>
        public Interactable Focused => _focused;

        public void Initialize(IPlayerInputSource input)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
        }

        private void OnEnable()
        {
            _blockSubscription = GameServices.Events.Subscribe<GameplayInputBlockChanged>(OnInputBlockChanged);
        }

        private void OnDisable()
        {
            _blockSubscription?.Dispose();
            _blockSubscription = null;
            SetFocus(null);
        }

        private void Update()
        {
            if (_inputBlocked)
            {
                return;
            }

            SetFocus(Probe());

            if (_input == null)
            {
                _input = new LegacyPlayerInputSource();
            }

            if (_focused != null && _input.InteractPressed)
            {
                GameServices.Events.Publish(new InteractionRequested(_focused.Action));
            }
        }

        private Interactable Probe()
        {
            if (!Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, _maxDistance, _layerMask, QueryTriggerInteraction.Ignore))
            {
                return null;
            }

            if (hit.collider != _lastCollider)
            {
                _lastCollider = hit.collider;
                _lastInteractable = hit.collider.GetComponentInParent<Interactable>();
            }

            return _lastInteractable != null && _lastInteractable.Action != InteractionAction.None ? _lastInteractable : null;
        }

        private void SetFocus(Interactable interactable)
        {
            if (interactable == _focused)
            {
                return;
            }

            _focused = interactable;
            InteractionFocusChanged evt = interactable != null
                ? new InteractionFocusChanged(interactable.Action, interactable.Prompt)
                : new InteractionFocusChanged(InteractionAction.None, string.Empty);
            GameServices.Events.Publish(evt);
        }

        private void OnInputBlockChanged(GameplayInputBlockChanged evt)
        {
            _inputBlocked = evt.IsBlocked;
            if (_inputBlocked)
            {
                SetFocus(null);
            }
        }
    }
}
