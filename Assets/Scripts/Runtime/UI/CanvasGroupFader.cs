using UnityEngine;

namespace CourtSim.Runtime.UI
{
    /// <summary>
    /// Fades a <see cref="CanvasGroup"/> in and out using unscaled time. A fully hidden element is
    /// deactivated so it costs nothing to render or lay out, and this component stops updating when idle.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    [DisallowMultipleComponent]
    public sealed class CanvasGroupFader : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float _duration = 0.16f;

        private CanvasGroup _group;
        private bool _targetVisible = true;

        public float Duration
        {
            get => _duration;
            set => _duration = Mathf.Max(0f, value);
        }

        /// <summary>Where the fade is heading (not necessarily where it is now).</summary>
        public bool TargetVisible => _targetVisible;

        private CanvasGroup Group
        {
            get
            {
                if (_group == null)
                {
                    _group = GetComponent<CanvasGroup>();
                }

                return _group;
            }
        }

        public void SetVisible(bool visible, bool instant = false)
        {
            _targetVisible = visible;
            CanvasGroup group = Group;
            group.interactable = visible;
            group.blocksRaycasts = visible;

            if (visible && !gameObject.activeSelf)
            {
                group.alpha = 0f;
                gameObject.SetActive(true);
            }

            if (instant || _duration <= 0f || !gameObject.activeInHierarchy)
            {
                Finish();
                return;
            }

            enabled = true;
        }

        private void Update()
        {
            CanvasGroup group = Group;
            float target = _targetVisible ? 1f : 0f;
            group.alpha = Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime / _duration);
            if (Mathf.Approximately(group.alpha, target))
            {
                Finish();
            }
        }

        private void Finish()
        {
            Group.alpha = _targetVisible ? 1f : 0f;
            enabled = false;
            if (!_targetVisible)
            {
                gameObject.SetActive(false);
            }
        }
    }
}
