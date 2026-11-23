using System;
using System.Collections.Generic;
using CourtSim.Core.Presentation;
using CourtSim.Runtime.App;
using CourtSim.Runtime.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CourtSim.Runtime.UI
{
    /// <summary>
    /// The opening titles, played once at launch on a black screen above the home page: the drone fades in, the
    /// emblem appears with a chime, the project title lands line by line with a deep boom and a sweep of light,
    /// then everything fades away to reveal the home page. Any key or click skips it.
    /// </summary>
    /// <remarks>
    /// <para>The home page is already open underneath (the screen stack is on the main menu the whole time), so
    /// nothing else in the game has to wait for or know about the intro; it is purely a covering layer.</para>
    /// <para>The animation is a fixed timeline: each element's look is worked out from the seconds since the start,
    /// so there is no chain of callbacks to follow. Sounds are fired once when their time is passed.</para>
    /// <para>It never plays in batch mode (tests and command-line runs) or when <see cref="Enabled"/> is false. The
    /// screenshot tour and demo videos skip it too, unless <see cref="Argument"/> is on the command line.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class IntroSequence : MonoBehaviour
    {
        /// <summary>Command-line switch that plays the intro even alongside the screenshot tour or demo recorder.</summary>
        public const string Argument = "-courtsimIntro";

        /// <summary>Command-line switch that never plays the intro.</summary>
        public const string SkipArgument = "-courtsimNoIntro";

        private const int SortingOrder = 100; // above every screen and the HUD

        // The timeline, in seconds from the start.
        private const float EmblemAt = 0.5f;
        private const float RiserAt = 1.4f;
        private const float TitleAt = 3.6f; // the riser peaks here, under the boom
        private const float SecondLineAt = 3.95f;
        private const float SweepAt = 5.0f;
        private const float SubtitleAt = 5.4f;
        private const float RuleAt = 6.3f;
        private const float BrandAt = 6.9f;
        private const float EndAt = 9.2f;
        private const float FadeOutSeconds = 1.0f;
        private const float SkipFadeSeconds = 0.6f;

        /// <summary>The sound track, in time order: chime with the emblem, rising tension, the boom on the title, a gavel.</summary>
        private static readonly TimedCue[] Cues =
        {
            new TimedCue(EmblemAt, SoundCue.Shimmer, 0.7f),
            new TimedCue(RiserAt, SoundCue.Riser, 0.55f),
            new TimedCue(TitleAt, SoundCue.Impact, 1f),
            new TimedCue(BrandAt, SoundCue.Gavel, 0.55f),
        };

        private int _nextCue;
        private Action _finished;
        private CanvasGroup _root;
        private RectTransform _emblem;
        private CanvasGroup _emblemGroup;
        private Image _glow;
        private TitleLine _line1;
        private TitleLine _line2;
        private CanvasGroup _subtitle;
        private RectTransform _rule;
        private TitleLine _brand;
        private LightSweep _sweep1;
        private LightSweep _sweep2;
        private float _time;
        private float _fadeStartedAt = -1f;
        private float _fadeSeconds = FadeOutSeconds;
        private bool _swept;

        /// <summary>Set false to never play the intro (e.g. from a test or a tool). True by default.</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>True while the titles are running; false once they have started fading out (or were skipped).</summary>
        public bool IsPlaying => _fadeStartedAt < 0f;

        /// <summary>Whether the intro should play now, given the command line and whether this is a batch-mode run.</summary>
        public static bool ShouldPlay()
        {
            return ShouldPlay(Application.isBatchMode, Environment.GetCommandLineArgs());
        }

        /// <summary>The decision itself, with its inputs passed in so it is easy to reason about.</summary>
        public static bool ShouldPlay(bool batchMode, IReadOnlyList<string> args)
        {
            if (!Enabled || batchMode)
            {
                return false;
            }

            bool forced = false;
            bool automated = false;
            foreach (string arg in args)
            {
                if (arg == SkipArgument)
                {
                    return false;
                }

                forced |= arg == Argument;
                automated |= arg == ScreenshotTour.Argument || arg == DemoRecorder.Argument || arg == DemoRecorder.GuidedArgument;
            }

            return forced || !automated;
        }

        /// <summary>
        /// Builds the intro on its own canvas under <paramref name="parent"/> and starts it. <paramref name="finished"/>
        /// is called once, as the intro starts fading out to reveal what is underneath.
        /// </summary>
        public static IntroSequence Play(UIFactory factory, Transform parent, Action finished)
        {
            Canvas canvas = factory.CreateCanvas("IntroCanvas", parent, SortingOrder, true);
            IntroSequence intro = canvas.gameObject.AddComponent<IntroSequence>();
            intro._finished = finished;
            intro.Build(factory, canvas.transform);
            return intro;
        }

        /// <summary>Jumps to the end: cuts the effects and fades out quickly.</summary>
        public void Skip()
        {
            if (_fadeStartedAt >= 0f)
            {
                return;
            }

            SoundEffects.StopOneShots();
            BeginFadeOut(SkipFadeSeconds);
        }

        private void Build(UIFactory factory, Transform canvas)
        {
            UITheme theme = factory.Theme;
            _root = canvas.gameObject.AddComponent<CanvasGroup>();

            // The black backdrop also catches clicks, so nothing underneath reacts while the titles play.
            Image black = factory.CreatePanel("Black", canvas, theme.Backdrop, PanelShape.Square);
            black.raycastTarget = true;
            UIFactory.Stretch(black.rectTransform);

            RectTransform stage = factory.CreateRect("Stage", canvas);
            UIFactory.Stretch(stage);

            Color glowColor = theme.Accent;
            glowColor.a = 0f;
            _glow = factory.CreateGradient("Glow", stage, glowColor, GradientShape.Glow);
            UIFactory.Place(_glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), new Vector2(760f, 760f));

            _emblem = JusticeEmblem.Build(factory, stage, 190f);
            UIFactory.Place(_emblem, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), new Vector2(190f, 190f));
            _emblemGroup = _emblem.gameObject.AddComponent<CanvasGroup>();

            _line1 = TitleLine.Create(factory, stage, "Line1", "ADAPTIVE VIRTUAL REALITY", TextStyle.Hero, theme.TextPrimary, 40f);
            _line2 = TitleLine.Create(factory, stage, "Line2", "COURTROOM SIMULATION", TextStyle.Hero, theme.Accent, -60f);
            _sweep1 = LightSweep.AddTo(_line1.Text, factory, -1f, 0f);
            _sweep2 = LightSweep.AddTo(_line2.Text, factory, -1f, 0f);

            Text subtitle = factory.CreateText(
                "Subtitle",
                stage,
                "for AI-Assisted Legal Augmentation and Decision-Making Training",
                TextStyle.Title,
                TextAnchor.MiddleCenter,
                theme.TextSecondary);
            subtitle.fontStyle = FontStyle.Normal;
            subtitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Place(subtitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -150f), new Vector2(1800f, 50f));
            _subtitle = subtitle.gameObject.AddComponent<CanvasGroup>();

            _rule = factory.CreatePanel("Rule", stage, theme.Accent, PanelShape.Square).rectTransform;
            UIFactory.Place(_rule, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -215f), new Vector2(0f, 2f));

            // "CourtSim" in small capitals: legacy Text has no small-caps mode, so the capitals are simply set bigger.
            _brand = TitleLine.Create(factory, stage, "Brand", "<size=38>C</size>OURT<size=38>S</size>IM", TextStyle.Heading, theme.Accent, -265f);
            _brand.Text.fontSize = 28;
            _brand.Text.supportRichText = true;

            // Keyboard focus would let Enter press a home-page button hidden under the titles.
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }

            SoundEffects.StartDrone(SoundEffects.IntroDroneVolume, 3f);
            Apply();
        }

        private void Update()
        {
            // A long first frame (the scene loading) must not jump the timeline past its first sounds.
            _time += Mathf.Min(Time.unscaledDeltaTime, 0.1f);

            if (_fadeStartedAt < 0f && (SkipPressed() || _time >= EndAt))
            {
                BeginFadeOut(FadeOutSeconds);
            }

            // The cues are in time order, so only the next one needs checking. None start once the fade has begun.
            while (_fadeStartedAt < 0f && _nextCue < Cues.Length && _time >= Cues[_nextCue].At)
            {
                SoundEffects.Play(Cues[_nextCue].Cue, Cues[_nextCue].Volume);
                _nextCue++;
            }

            if (!_swept && _time >= SweepAt)
            {
                _swept = true;
                _sweep1.Play();
                _sweep2.Play();
            }

            Apply();

            if (_fadeStartedAt >= 0f && _time >= _fadeStartedAt + _fadeSeconds)
            {
                enabled = false;
                Destroy(gameObject);
            }
        }

        private void BeginFadeOut(float seconds)
        {
            _fadeStartedAt = _time;
            _fadeSeconds = seconds;
            _root.blocksRaycasts = false; // the home page can be used while the titles melt away
            SoundEffects.Play(SoundCue.Whoosh, 0.7f);
            SoundEffects.StartDrone(SoundEffects.MenuDroneVolume, 2.5f);

            Action finished = _finished;
            _finished = null;
            finished?.Invoke();
        }

        /// <summary>Sets every element's look for the current time. Only reads <see cref="_time"/>, so it can't drift.</summary>
        private void Apply()
        {
            // The emblem rises out of the dark and settles from slightly small to full size; its glow breathes.
            float emblem = Progress(EmblemAt, 1.6f);
            _emblemGroup.alpha = emblem;
            _emblem.localScale = Vector3.one * Mathf.Lerp(0.82f, 1f, EaseOut(emblem));
            Color glow = _glow.color;
            glow.a = emblem * (0.16f + (0.05f * Mathf.Sin(_time * 1.6f)));
            _glow.color = glow;

            // Each title line fades in while its letters gather from wide apart and it shrinks into place.
            _line1.Show(Progress(TitleAt, 1.0f), 1.12f, 0.35f, 0f);
            _line2.Show(Progress(SecondLineAt, 1.0f), 1.12f, 0.35f, 0f);

            float subtitle = Progress(SubtitleAt, 0.9f);
            _subtitle.alpha = subtitle;
            ((RectTransform)_subtitle.transform).anchoredPosition = new Vector2(0f, Mathf.Lerp(-165f, -150f, EaseOut(subtitle)));

            _rule.sizeDelta = new Vector2(Mathf.Lerp(0f, 380f, EaseOut(Progress(RuleAt, 0.8f))), 2f);

            // The brand keeps a little extra letter spacing for good: spaced capitals read as a logotype.
            _brand.Show(Progress(BrandAt, 0.9f), 1f, 0.6f, 0.18f);

            _root.alpha = _fadeStartedAt < 0f ? 1f : 1f - Progress(_fadeStartedAt, _fadeSeconds);
        }

        /// <summary>0 before <paramref name="start"/>, rising to 1 over <paramref name="seconds"/>.</summary>
        private float Progress(float start, float seconds)
        {
            return Mathf.Clamp01((_time - start) / seconds);
        }

        private static float EaseOut(float t)
        {
            return 1f - ((1f - t) * (1f - t) * (1f - t));
        }

        private static bool SkipPressed()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.anyKeyDown; // includes mouse buttons and Esc
#else
            return false;
#endif
        }

        /// <summary>A sound to fire once at a moment of the timeline.</summary>
        private readonly struct TimedCue
        {
            public TimedCue(float at, SoundCue cue, float volume)
            {
                At = at;
                Cue = cue;
                Volume = volume;
            }

            public float At { get; }

            public SoundCue Cue { get; }

            public float Volume { get; }
        }

        /// <summary>One centred line of title text that can fade, scale and gather its letters.</summary>
        private sealed class TitleLine
        {
            private CanvasGroup _group;
            private LetterSpread _spread;

            public Text Text { get; private set; }

            public static TitleLine Create(UIFactory factory, RectTransform stage, string name, string content, TextStyle style, Color color, float y)
            {
                Text text = factory.CreateText(name, stage, content, style, TextAnchor.MiddleCenter, color);
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                UIFactory.Place(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(1800f, 100f));
                return new TitleLine
                {
                    Text = text,
                    _group = text.gameObject.AddComponent<CanvasGroup>(),
                    _spread = text.gameObject.AddComponent<LetterSpread>(),
                };
            }

            /// <param name="t">0 (hidden) to 1 (fully shown).</param>
            /// <param name="startScale">Scale at the very start; it eases to 1.</param>
            /// <param name="startSpread">Letter spread at the start; it eases to <paramref name="endSpread"/>.</param>
            public void Show(float t, float startScale, float startSpread, float endSpread)
            {
                float eased = EaseOut(t);
                _group.alpha = t;
                Text.rectTransform.localScale = Vector3.one * Mathf.Lerp(startScale, 1f, eased);
                _spread.Spread = Mathf.Lerp(startSpread, endSpread, eased);
            }
        }
    }
}
