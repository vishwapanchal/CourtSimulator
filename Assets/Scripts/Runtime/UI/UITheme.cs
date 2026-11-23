using System;
using CourtSim.Runtime.Common;
using UnityEngine;

namespace CourtSim.Runtime.UI
{
    /// <summary>Typographic roles. Every piece of UI text uses exactly one.</summary>
    public enum TextStyle
    {
        Display,
        Title,
        Heading,
        Body,
        Caption,
        Button,

        /// <summary>Small, bold, accent-coloured label above a title. Write the text in capitals.</summary>
        Overline,

        /// <summary>The biggest type, bold: the project title in the opening titles.</summary>
        Hero,
    }

    /// <summary>Visual weight of a button.</summary>
    public enum ButtonVariant
    {
        Primary,
        Secondary,
        Ghost,
    }

    /// <summary>
    /// The single source of truth for UI colours, type sizes and spacing. Assign a theme asset to
    /// <see cref="UIManager"/> to restyle every screen; without one, <see cref="CreateDefault"/> is used.
    /// </summary>
    /// <remarks>Palette: deep navy surfaces, ivory text and a brass accent, echoing courtroom timber and fittings.
    /// Text/background pairs meet WCAG AA contrast (primary text on surface ≈ 14:1, accent text on accent ≈ 7:1).</remarks>
    [CreateAssetMenu(menuName = "CourtSim/UI Theme", fileName = "UITheme")]
    public sealed class UITheme : ScriptableObject
    {
        [Header("Palette")]
        [SerializeField] private Color _scrim = new Color(0.02f, 0.03f, 0.06f, 0.72f);
        [SerializeField] private Color _surface = new Color(0.078f, 0.106f, 0.176f, 0.96f);
        [SerializeField] private Color _surfaceRaised = new Color(0.118f, 0.153f, 0.251f, 1f);
        [SerializeField] private Color _border = new Color(1f, 1f, 1f, 0.09f);
        [SerializeField] private Color _textPrimary = new Color(0.949f, 0.933f, 0.890f, 1f);
        [SerializeField] private Color _textSecondary = new Color(0.682f, 0.710f, 0.788f, 1f);
        [SerializeField] private Color _accent = new Color(0.788f, 0.635f, 0.153f, 1f);
        [SerializeField] private Color _onAccent = new Color(0.078f, 0.106f, 0.176f, 1f);
        [SerializeField] private Color _warning = new Color(0.902f, 0.659f, 0.263f, 1f);
        [SerializeField] private Color _success = new Color(0.412f, 0.718f, 0.537f, 1f);
        [Tooltip("Near-black navy behind the opening titles and cinematic vignettes.")]
        [SerializeField] private Color _backdrop = new Color(0.008f, 0.012f, 0.03f, 1f);
        [Tooltip("A brighter gold for glows and light sweeps over the accent.")]
        [SerializeField] private Color _accentBright = new Color(1f, 0.878f, 0.541f, 1f);

        [Header("Typography")]
        [Tooltip("Leave empty to use Unity's built-in font.")]
        [SerializeField] private Font _font;
        [SerializeField, Min(8)] private int _heroSize = 84;
        [SerializeField, Min(8)] private int _displaySize = 64;
        [SerializeField, Min(8)] private int _titleSize = 36;
        [SerializeField, Min(8)] private int _headingSize = 22;
        [SerializeField, Min(8)] private int _bodySize = 19;
        [SerializeField, Min(8)] private int _captionSize = 15;
        [SerializeField, Min(8)] private int _buttonSize = 18;
        [SerializeField, Min(8)] private int _overlineSize = 13;

        [Header("Metrics")]
        [SerializeField, Min(0f)] private float _cornerRadius = 14f;
        [SerializeField, Min(0f)] private float _padding = 28f;
        [SerializeField, Min(0f)] private float _spacing = 14f;
        [SerializeField, Min(24f)] private float _buttonHeight = 52f;
        [SerializeField, Min(0f)] private float _fadeDuration = 0.16f;
        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920f, 1080f);

        public Color Scrim => _scrim;

        public Color Surface => _surface;

        public Color SurfaceRaised => _surfaceRaised;

        public Color Border => _border;

        public Color TextPrimary => _textPrimary;

        public Color TextSecondary => _textSecondary;

        public Color Accent => _accent;

        public Color OnAccent => _onAccent;

        public Color Warning => _warning;

        public Color Success => _success;

        public Color Backdrop => _backdrop;

        public Color AccentBright => _accentBright;

        public Font Font => _font != null ? _font : BuiltinAssets.DefaultFont;

        public float CornerRadius => _cornerRadius;

        public float Padding => _padding;

        public float Spacing => _spacing;

        public float ButtonHeight => _buttonHeight;

        public float FadeDuration => _fadeDuration;

        public Vector2 ReferenceResolution => _referenceResolution;

        /// <summary>A transient theme with the default values above. Not saved with the scene.</summary>
        public static UITheme CreateDefault()
        {
            var theme = CreateInstance<UITheme>();
            theme.name = "UITheme (Default)";
            theme.hideFlags = HideFlags.DontSave;
            return theme;
        }

        public int FontSize(TextStyle style)
        {
            switch (style)
            {
                case TextStyle.Display:
                    return _displaySize;
                case TextStyle.Title:
                    return _titleSize;
                case TextStyle.Heading:
                    return _headingSize;
                case TextStyle.Body:
                    return _bodySize;
                case TextStyle.Caption:
                    return _captionSize;
                case TextStyle.Button:
                    return _buttonSize;
                case TextStyle.Overline:
                    return _overlineSize;
                case TextStyle.Hero:
                    return _heroSize;
                default:
                    throw new ArgumentOutOfRangeException(nameof(style), style, null);
            }
        }

        public FontStyle FontStyleFor(TextStyle style)
        {
            switch (style)
            {
                case TextStyle.Hero:
                case TextStyle.Display:
                case TextStyle.Title:
                case TextStyle.Heading:
                case TextStyle.Button:
                case TextStyle.Overline:
                    return FontStyle.Bold;
                default:
                    return FontStyle.Normal;
            }
        }

        public Color TextColor(TextStyle style)
        {
            switch (style)
            {
                case TextStyle.Caption:
                    return _textSecondary;
                case TextStyle.Overline:
                    return _accent;
                default:
                    return _textPrimary;
            }
        }
    }
}
