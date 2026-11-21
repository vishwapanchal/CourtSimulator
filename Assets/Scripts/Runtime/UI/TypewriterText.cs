using CourtSim.Core.Presentation;
using CourtSim.Runtime.Common;
using UnityEngine;
using UnityEngine.UI;

namespace CourtSim.Runtime.UI
{
    /// <summary>
    /// Types a <see cref="Text"/> label's message out letter by letter, with a soft keyboard click every few
    /// letters, like the assistant is writing it. The text logic lives in <see cref="RichTextTyping"/>; this
    /// component only advances the count each frame and plays the clicks.
    /// </summary>
    /// <remarks>
    /// The not-yet-typed rest of the message is drawn in a transparent colour, so the label has its final size
    /// from the first frame and the card around it does not jump as lines fill. Typing is cosmetic: nothing
    /// waits for it, and setting the same message again does not restart it.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Text))]
    public sealed class TypewriterText : MonoBehaviour
    {
        private const int ClickVariants = 4;

        /// <summary>Two labels can type at once (HUD panel and decision card); this stops their clicks doubling up.</summary>
        private const float MinSecondsBetweenClicks = 0.035f;

        private static float _lastClickTime = -1f;

        [SerializeField] private float _charactersPerSecond = 45f;
        [SerializeField] private int _charactersPerClick = 2;
        [Range(0f, 1f)]
        [SerializeField] private float _clickVolume = 0.18f;

        private Text _text;
        private AudioSource _audio;
        private AudioClip[] _clicks;
        private string _target = string.Empty;
        private int _totalChars;
        private int _shownChars;
        private float _progress;
        private int _nextClickAt;
        private int _clickIndex;

        /// <summary>The full message being typed (or already shown), with its rich-text tags.</summary>
        public string Target => _target;

        public bool IsTyping => _shownChars < _totalChars;

        /// <summary>Letters per second, in real time (unaffected by pausing the game).</summary>
        public float CharactersPerSecond
        {
            get => _charactersPerSecond;
            set => _charactersPerSecond = Mathf.Max(1f, value);
        }

        /// <summary>Shows <paramref name="message"/>, typing it out unless <paramref name="animate"/> is false.</summary>
        public void SetTarget(string message, bool animate = true)
        {
            message = message ?? string.Empty;
            EnsureInitialised();
            if (message == _target)
            {
                // A screen refresh re-sends the same advice; carry on from where the typing is.
                return;
            }

            _target = message;
            _totalChars = RichTextTyping.VisibleLength(message);
            _progress = 0f;
            _shownChars = 0;
            _nextClickAt = 1;
            if (animate && _totalChars > 0)
            {
                _text.text = RichTextTyping.PrefixWithHiddenRest(_target, 0);
            }
            else
            {
                Skip();
            }
        }

        /// <summary>Shows the whole message immediately.</summary>
        public void Skip()
        {
            EnsureInitialised();
            _shownChars = _totalChars;
            _progress = _totalChars;
            _text.text = _target;
        }

        private void Awake()
        {
            EnsureInitialised();
        }

        /// <summary>While this returns true, typing pauses (set by UIManager to "the voice is preparing").</summary>
        public static System.Func<bool> HoldWhile { get; set; }

        private void Update()
        {
            if (!IsTyping)
            {
                return;
            }

            // Wait while the voice is still being synthesised, so the typing and the speech start together.
            if (HoldWhile != null && HoldWhile())
            {
                return;
            }

            _progress += Time.unscaledDeltaTime * _charactersPerSecond;
            int shown = Mathf.Min(_totalChars, Mathf.FloorToInt(_progress));
            if (shown == _shownChars)
            {
                return;
            }

            _shownChars = shown;
            _text.text = shown >= _totalChars ? _target : RichTextTyping.PrefixWithHiddenRest(_target, shown);
            if (shown >= _nextClickAt)
            {
                _nextClickAt = shown + Mathf.Max(1, _charactersPerClick);
                PlayClick();
            }
        }

        private void OnDestroy()
        {
            if (_clicks == null)
            {
                return;
            }

            foreach (AudioClip clip in _clicks)
            {
                UnityObjectUtil.SafeDestroy(clip);
            }

            _clicks = null;
        }

        private void EnsureInitialised()
        {
            if (_text != null)
            {
                return;
            }

            _text = GetComponent<Text>();
            _text.supportRichText = true;

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f; // a UI sound: same volume wherever the camera is
            _audio.ignoreListenerPause = true;

            // A few slightly different clicks, picked in turn, so the typing doesn't sound like one repeated sample.
            _clicks = new AudioClip[ClickVariants];
            for (int i = 0; i < ClickVariants; i++)
            {
                float[] samples = ClickSynth.Generate(1000 + i);
                AudioClip clip = AudioClip.Create($"TypingClick{i}", samples.Length, 1, ClickSynth.DefaultSampleRate, false);
                clip.SetData(samples, 0);
                clip.hideFlags = HideFlags.DontSave;
                _clicks[i] = clip;
            }
        }

        private void PlayClick()
        {
            float now = Time.unscaledTime;

            // "now < last" means a new play session restarted the clock while the static value survived.
            bool tooSoon = now >= _lastClickTime && now - _lastClickTime < MinSecondsBetweenClicks;
            if (_clicks == null || _audio == null || tooSoon)
            {
                return;
            }

            _lastClickTime = now;
            _clickIndex = (_clickIndex + 1) % _clicks.Length;
            _audio.PlayOneShot(_clicks[_clickIndex], _clickVolume);
        }
    }
}
