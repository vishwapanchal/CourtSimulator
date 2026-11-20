using System;
using CourtSim.Core.Events;
using CourtSim.Core.Navigation;
using CourtSim.Runtime.App;
using UnityEngine;

namespace CourtSim.Runtime.Player
{
    /// <summary>
    /// Desktop first-person locomotion on a <see cref="CharacterController"/>: walk, sprint, mouse look
    /// and gravity. All math lives in <see cref="MovementMath"/> so the future VR rig can share it.
    /// </summary>
    /// <remarks>Stops reading input while any UI screen is open (<see cref="GameplayInputBlockChanged"/>).</remarks>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public sealed class FirstPersonController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField, Min(0f)] private float _walkSpeed = 2.4f;
        [SerializeField, Min(0f)] private float _sprintSpeed = 4.2f;
        [Tooltip("How quickly velocity approaches the target speed. Higher is snappier; 0 is instant.")]
        [SerializeField, Min(0f)] private float _acceleration = 14f;
        [SerializeField, Min(0f)] private float _gravity = 9.81f;

        [Header("Look")]
        [Tooltip("Pitches up and down; usually the camera. Found automatically if left empty.")]
        [SerializeField] private Transform _cameraPivot;
        [SerializeField, Range(0.1f, 10f)] private float _lookSensitivity = 2f;
        [SerializeField] private bool _invertY;
        [SerializeField, Range(10f, 89f)] private float _pitchLimit = MovementMath.DefaultPitchLimit;

        private CharacterController _controller;
        private IPlayerInputSource _input;
        private IDisposable _blockSubscription;
        private PlanarVector _planarVelocity;
        private float _verticalVelocity;
        private float _yaw;
        private float _pitch;
        private bool _inputBlocked;

        public bool InputBlocked => _inputBlocked;

        public Transform CameraPivot => _cameraPivot;

        public float Yaw => _yaw;

        public float Pitch => _pitch;

        private CharacterController Controller
        {
            get
            {
                if (_controller == null)
                {
                    _controller = GetComponent<CharacterController>();
                }

                return _controller;
            }
        }

        public void Initialize(IPlayerInputSource input)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
        }

        /// <summary>Sets the transform that pitches. Used by <see cref="PlayerFactory"/> and editor tooling.</summary>
        public void AssignCameraPivot(Transform pivot)
        {
            _cameraPivot = pivot;
        }

        public void SetInputBlocked(bool blocked)
        {
            _inputBlocked = blocked;
            if (blocked)
            {
                _planarVelocity = PlanarVector.Zero;
            }
        }

        /// <summary>Moves the player instantly. The controller is disabled briefly so it doesn't fight the move.</summary>
        public void Teleport(Vector3 position, float yawDegrees)
        {
            CharacterController controller = Controller;
            bool wasEnabled = controller.enabled;
            controller.enabled = false;
            transform.position = position;
            controller.enabled = wasEnabled;

            _yaw = MovementMath.WrapAngle360(yawDegrees);
            _pitch = 0f;
            _planarVelocity = PlanarVector.Zero;
            _verticalVelocity = 0f;
            ApplyRotation();
        }

        private void Awake()
        {
            if (_cameraPivot == null)
            {
                Camera childCamera = GetComponentInChildren<Camera>();
                if (childCamera != null)
                {
                    _cameraPivot = childCamera.transform;
                }
            }

            _yaw = MovementMath.WrapAngle360(transform.eulerAngles.y);
            if (_cameraPivot != null)
            {
                float pitch = _cameraPivot.localEulerAngles.x;
                _pitch = MovementMath.ClampPitch(pitch > 180f ? pitch - 360f : pitch, _pitchLimit);
            }
        }

        private void OnEnable()
        {
            _blockSubscription = GameServices.Events.Subscribe<GameplayInputBlockChanged>(OnInputBlockChanged);
        }

        private void OnDisable()
        {
            _blockSubscription?.Dispose();
            _blockSubscription = null;
        }

        private void Update()
        {
            if (_input == null)
            {
                _input = new LegacyPlayerInputSource();
            }

            float deltaTime = Time.deltaTime;
            Vector2 move = Vector2.zero;
            bool sprint = false;

            if (!_inputBlocked)
            {
                Vector2 look = _input.Look;
                MovementMath.ApplyLook(_yaw, _pitch, look.x, look.y, _lookSensitivity, _invertY, _pitchLimit, out _yaw, out _pitch);
                ApplyRotation();
                move = _input.Move;
                sprint = _input.SprintHeld;
            }

            PlanarVector target = MovementMath.PlanarVelocity(move.x, move.y, _yaw, sprint ? _sprintSpeed : _walkSpeed);
            _planarVelocity = MovementMath.Damp(_planarVelocity, target, _acceleration, deltaTime);

            CharacterController controller = Controller;
            _verticalVelocity = MovementMath.ApplyGravity(_verticalVelocity, controller.isGrounded, _gravity, deltaTime);
            controller.Move(new Vector3(_planarVelocity.X, _verticalVelocity, _planarVelocity.Z) * deltaTime);
        }

        private void ApplyRotation()
        {
            transform.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            if (_cameraPivot != null)
            {
                _cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            }
        }

        private void OnInputBlockChanged(GameplayInputBlockChanged evt)
        {
            SetInputBlocked(evt.IsBlocked);
        }
    }
}
