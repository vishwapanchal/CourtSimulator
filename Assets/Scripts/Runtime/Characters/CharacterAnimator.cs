using UnityEngine;

namespace CourtSim.Runtime.Characters
{
    /// <summary>
    /// Brings a <see cref="CharacterFactory"/> figure to life with simple procedural motion: breathing, a slight
    /// sway, turning the head towards whoever is speaking, standing up from a chair, and hand gestures while talking.
    /// </summary>
    /// <remarks>Everything is computed from sine waves each frame; there are no animation assets.</remarks>
    [DisallowMultipleComponent]
    public sealed class CharacterAnimator : MonoBehaviour
    {
        private const float StandSeconds = 0.6f;

        // Saved with the scene, so characters baked by the scene generator keep their joints.
        [SerializeField] private string _displayName;
        [SerializeField] private Transform _hips;
        [SerializeField] private Transform _torso;
        [SerializeField] private Transform _head;
        [SerializeField] private Transform _armL;
        [SerializeField] private Transform _armR;
        [SerializeField] private Transform _thighL;
        [SerializeField] private Transform _thighR;
        [SerializeField] private Transform _shinL;
        [SerializeField] private Transform _shinR;
        [SerializeField] private float _seatedHipHeight;
        [SerializeField] private bool _seatedByDefault;
        private float _stand;       // 0 = sitting, 1 = standing
        private bool _standing;
        private float _speak;       // 0..1, eases gestures in and out
        private bool _speaking;
        private float _phase;       // so people don't all breathe in step
        private bool _hasLookTarget;
        private Vector3 _lookTarget;
        private Quaternion _headRotation = Quaternion.identity;

        /// <summary>Role shown in subtitles, e.g. "Additional Public Prosecutor".</summary>
        public string DisplayName => _displayName;

        public bool IsStanding => _standing;

        public bool IsSpeaking => _speaking;

        /// <summary>World position of the face, for aiming cameras and other people's gaze.</summary>
        public Vector3 HeadPosition => _head != null ? _head.position + (Vector3.up * 0.12f) : transform.position + (Vector3.up * 1.6f);

        /// <summary>Called by <see cref="CharacterFactory"/> with the joints it built.</summary>
        public void Configure(
            string displayName,
            Transform hips,
            Transform torso,
            Transform head,
            Transform armL,
            Transform armR,
            Transform thighL,
            Transform thighR,
            Transform shinL,
            Transform shinR,
            bool seated,
            float seatHeight)
        {
            _displayName = displayName;
            _hips = hips;
            _torso = torso;
            _head = head;
            _armL = armL;
            _armR = armR;
            _thighL = thighL;
            _thighR = thighR;
            _shinL = shinL;
            _shinR = shinR;
            _seatedByDefault = seated;
            _seatedHipHeight = seatHeight + 0.04f;
            _standing = !seated;
            _stand = _standing ? 1f : 0f;
            _phase = Mathf.Abs(displayName.GetHashCode() % 1000) * 0.0137f + transform.position.x;
            ApplyPose(0f);
        }

        /// <summary>Seated characters stand up (or sit back down). Characters built standing ignore this.</summary>
        public void SetStanding(bool standing)
        {
            if (_seatedByDefault)
            {
                _standing = standing;
            }
        }

        public void SetSpeaking(bool speaking)
        {
            _speaking = speaking;
        }

        /// <summary>Turns the head towards a point in the world (within natural limits).</summary>
        public void LookAt(Vector3 worldPoint)
        {
            _hasLookTarget = true;
            _lookTarget = worldPoint;
        }

        public void ClearLookAt()
        {
            _hasLookTarget = false;
        }

        private void Awake()
        {
            // Characters loaded from a saved scene start in their default pose.
            _standing = !_seatedByDefault;
            _stand = _standing ? 1f : 0f;
            _phase = (Mathf.Abs((_displayName ?? string.Empty).GetHashCode() % 1000) * 0.0137f) + transform.position.x;
        }

        private void Update()
        {
            if (_hips == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            _stand = Mathf.MoveTowards(_stand, _standing ? 1f : 0f, dt / StandSeconds);
            _speak = Mathf.MoveTowards(_speak, _speaking ? 1f : 0f, dt * 3f);
            ApplyPose(Time.time);
        }

        private void ApplyPose(float t)
        {
            if (_hips == null)
            {
                return;
            }

            float s = Mathf.SmoothStep(0f, 1f, _stand);
            float breathe = Mathf.Sin((t * Mathf.PI * 0.5f) + _phase); // a breath every 4 seconds

            // Legs: thighs swing forward and shins hang down when seated; straight when standing.
            float hipHeight = Mathf.Lerp(_seatedHipHeight, CharacterFactory.StandingHipHeight, s);
            _hips.localPosition = new Vector3(0f, hipHeight, Mathf.Lerp(0.05f, 0f, s));
            _hips.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin((t * 0.7f) + _phase) * 0.8f * s);
            Quaternion thigh = Quaternion.Euler(Mathf.Lerp(-88f, 0f, s), 0f, 0f);
            Quaternion shin = Quaternion.Euler(Mathf.Lerp(88f, 0f, s), 0f, 0f);
            _thighL.localRotation = thigh;
            _thighR.localRotation = thigh;
            _shinL.localRotation = shin;
            _shinR.localRotation = shin;

            // Torso: breathing, and a slight lean towards the bench while speaking.
            _torso.localScale = new Vector3(1f, 1f + (breathe * 0.012f), 1f + (breathe * 0.02f));
            _torso.localRotation = Quaternion.Euler(_speak * 5f, 0f, 0f);

            // Arms: resting forward in the lap when seated, by the side when standing, gesturing while speaking.
            float restForward = Mathf.Lerp(-55f, -6f, s);
            float gesture = _speak * ((Mathf.Sin((t * 2.6f) + _phase) * 18f) + 32f);
            float sway = Mathf.Sin((t * 1.9f) + (_phase * 2f)) * 10f * _speak;
            _armR.localRotation = Quaternion.Euler(restForward - gesture, 0f, -6f - sway);
            _armL.localRotation = Quaternion.Euler(restForward - (gesture * 0.35f), 0f, 6f + (sway * 0.5f));

            // Head: towards the look target if any, with small idle turns and nods while speaking.
            Quaternion desired = Quaternion.Euler(Mathf.Sin((t * 0.37f) + _phase) * 2f, Mathf.Sin((t * 0.23f) + (_phase * 3f)) * 6f, 0f);
            if (_hasLookTarget)
            {
                Vector3 local = _torso.InverseTransformPoint(_lookTarget) - _head.localPosition;
                float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -70f, 70f);
                float pitch = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -25f, 25f);
                desired = Quaternion.Euler(pitch, yaw, 0f);
            }

            desired *= Quaternion.Euler(Mathf.Sin(t * 5f) * 4f * _speak, 0f, 0f);
            _headRotation = Quaternion.Slerp(_headRotation, desired, 1f - Mathf.Exp(-4f * Time.deltaTime));
            _head.localRotation = _headRotation;
        }
    }
}
