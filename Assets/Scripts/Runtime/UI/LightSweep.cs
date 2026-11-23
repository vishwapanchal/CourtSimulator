using UnityEngine;
using UnityEngine.UI;

namespace CourtSim.Runtime.UI
{
    /// <summary>
    /// A band of light that glides across a title, like light catching polished brass. It is a soft, tilted
    /// glow drawn as a child of the text, and a <see cref="Mask"/> on the text clips it to the letter shapes, so
    /// only the letters light up, never the background.
    /// </summary>
    /// <remarks>
    /// It can sweep once after a delay, on a repeating interval, or only when <see cref="Play"/> is called. Timing
    /// uses unscaled time and restarts whenever the text is shown again.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class LightSweep : MonoBehaviour
    {
        private const float TiltDegrees = -18f;

        [SerializeField, Min(0.1f)] private float _duration = 1.3f;
        [Tooltip("Seconds after the text appears before the first sweep; negative waits for Play().")]
        [SerializeField] private float _firstDelay = 1f;
        [Tooltip("Seconds between sweeps; zero or less sweeps only once.")]
        [SerializeField] private float _interval;

        private RectTransform _target;
        private Image _beam;
        private float _peakAlpha;
        private float _countdown = -1f; // seconds until the next sweep starts; negative when none is planned
        private float _elapsed = -1f; // seconds into the sweep under way; negative when idle

        /// <summary>
        /// Adds a sweep to <paramref name="text"/>. The text becomes a mask, which changes nothing about how it looks.
        /// </summary>
        public static LightSweep AddTo(Text text, UIFactory factory, float firstDelay, float interval, float alpha = 0.8f)
        {
            Mask mask = text.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            Color color = factory.Theme.AccentBright;
            color.a = alpha;
            Image beam = factory.CreateGradient("Sweep", text.rectTransform, color, GradientShape.Glow);
            beam.gameObject.SetActive(false);

            LightSweep sweep = text.gameObject.AddComponent<LightSweep>();
            sweep._target = text.rectTransform;
            sweep._beam = beam;
            sweep._peakAlpha = alpha;
            sweep._firstDelay = firstDelay;
            sweep._interval = interval;
            sweep.OnEnable(); // AddComponent already ran OnEnable, before the fields above were set
            return sweep;
        }

        /// <summary>Starts a sweep now (or restarts the one under way).</summary>
        public void Play()
        {
            _countdown = 0f;
            _elapsed = -1f;
        }

        private void OnEnable()
        {
            _countdown = _firstDelay;
            _elapsed = -1f;
            if (_beam != null)
            {
                _beam.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (_beam == null)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;
            if (_elapsed < 0f)
            {
                if (_countdown < 0f)
                {
                    return;
                }

                _countdown -= dt;
                if (_countdown > 0f)
                {
                    return;
                }

                _countdown = -1f;
                _elapsed = 0f;
                _beam.gameObject.SetActive(true);
            }

            _elapsed += dt;
            float progress = _elapsed / _duration;
            if (progress >= 1f)
            {
                _elapsed = -1f;
                _beam.gameObject.SetActive(false);
                if (_interval > 0f)
                {
                    _countdown = _interval;
                }

                return;
            }

            // Glide from just off the left edge to just off the right, easing in and out, brightest mid-way.
            Rect area = _target.rect;
            float width = Mathf.Max(90f, area.height * 1.1f);
            float x = Mathf.Lerp(area.xMin - width, area.xMax + width, Mathf.SmoothStep(0f, 1f, progress));
            RectTransform beam = _beam.rectTransform;
            UIFactory.Place(beam, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x - area.center.x, 0f), new Vector2(width, area.height * 2.6f));
            beam.localRotation = Quaternion.Euler(0f, 0f, TiltDegrees);
            Color color = _beam.color;
            color.a = _peakAlpha * Mathf.Sin(progress * Mathf.PI);
            _beam.color = color;
        }
    }
}
