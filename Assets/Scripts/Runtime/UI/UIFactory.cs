using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CourtSim.Runtime.UI
{
    /// <summary>Background shape for panels.</summary>
    public enum PanelShape
    {
        Rounded,
        Square,
        Circle,

        /// <summary>A circle drawn from a bigger texture, for discs a few hundred pixels across.</summary>
        LargeCircle,
    }

    /// <summary>Soft, tinted overlays used for cinematic lighting (see <see cref="UIFactory.CreateGradient"/>).</summary>
    public enum GradientShape
    {
        /// <summary>Solid on the left, fading out to the right: darkens the side where text sits.</summary>
        FadeRight,

        /// <summary>Solid at the bottom, fading out upwards.</summary>
        FadeUp,

        /// <summary>A soft spot of light, brightest in the middle.</summary>
        Glow,

        /// <summary>Clear in the middle, darker towards the edges, like an old film lens.</summary>
        Vignette,
    }

    /// <summary>
    /// Builds themed uGUI elements. Every screen and HUD element is assembled from these few
    /// primitives, which is what keeps the look consistent and the screen code short.
    /// </summary>
    /// <remarks>Graphics are created with <c>raycastTarget = false</c> unless they need input; fewer raycast
    /// targets make the GraphicRaycaster cheaper.</remarks>
    public sealed class UIFactory
    {
        private const int UiLayer = 5;
        private readonly UISpriteCache _sprites;

        public UIFactory(UITheme theme, UISpriteCache sprites)
        {
            Theme = theme != null ? theme : throw new ArgumentNullException(nameof(theme));
            _sprites = sprites ?? throw new ArgumentNullException(nameof(sprites));
        }

        public UITheme Theme { get; }

        // ---------------------------------------------------------------- Containers

        public Canvas CreateCanvas(string name, Transform parent, int sortingOrder, bool receivesInput)
        {
            var canvasObject = new GameObject(name, typeof(RectTransform)) { layer = UiLayer };
            canvasObject.transform.SetParent(parent, false);

            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = Theme.ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (receivesInput)
            {
                canvasObject.AddComponent<GraphicRaycaster>();
            }

            return canvas;
        }

        public RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = UiLayer };
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        public Image CreatePanel(string name, Transform parent, Color color, PanelShape shape = PanelShape.Rounded, float radius = -1f)
        {
            RectTransform rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            ApplyShape(image, shape, radius);
            return image;
        }

        /// <summary>Draws a themed hairline border over <paramref name="target"/>; excluded from layout.</summary>
        public Image AddOutline(RectTransform target, Color color, float radius = -1f)
        {
            Image outline = CreatePanel("Outline", target, color, PanelShape.Rounded, radius);
            outline.sprite = _sprites.Outline;
            Stretch(outline.rectTransform);
            GetOrAddLayoutElement(outline.gameObject).ignoreLayout = true;
            return outline;
        }

        /// <summary>A tinted soft gradient; stretch or place it like any panel. It never takes input.</summary>
        public Image CreateGradient(string name, Transform parent, Color color, GradientShape shape)
        {
            Image image = CreatePanel(name, parent, color, PanelShape.Square);
            switch (shape)
            {
                case GradientShape.FadeRight:
                    image.sprite = _sprites.FadeRight;
                    break;
                case GradientShape.FadeUp:
                    image.sprite = _sprites.FadeUp;
                    break;
                case GradientShape.Glow:
                    image.sprite = _sprites.Glow;
                    break;
                default:
                    image.sprite = _sprites.Vignette;
                    break;
            }

            return image;
        }

        public Image CreateDivider(Transform parent)
        {
            Image divider = CreatePanel("Divider", parent, Theme.Border, PanelShape.Square);
            LayoutElement layout = GetOrAddLayoutElement(divider.gameObject);
            layout.minHeight = 1f;
            layout.preferredHeight = 1f;
            return divider;
        }

        public LayoutElement CreateSpacer(Transform parent, float flexibleWidth = 1f, float height = 0f)
        {
            RectTransform rect = CreateRect("Spacer", parent);
            LayoutElement layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.flexibleWidth = flexibleWidth;
            layout.minHeight = height;
            layout.preferredHeight = height;
            return layout;
        }

        // ---------------------------------------------------------------- Content

        public Text CreateText(string name, Transform parent, string content, TextStyle style, TextAnchor alignment = TextAnchor.UpperLeft, Color? color = null)
        {
            RectTransform rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Theme.Font;
            text.fontSize = Theme.FontSize(style);
            text.fontStyle = Theme.FontStyleFor(style);
            text.color = color ?? Theme.TextColor(style);
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.lineSpacing = style == TextStyle.Body || style == TextStyle.Caption ? 1.2f : 1f;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.text = content;
            return text;
        }

        /// <summary>
        /// Makes text safe to show inside a rich-text label: model or case text could contain "&lt;b&gt;"-style tags,
        /// so angle brackets are swapped for look-alike characters that Unity does not treat as markup.
        /// </summary>
        public static string Plain(string text)
        {
            return string.IsNullOrEmpty(text) ? string.Empty : text.Replace("<", "‹").Replace(">", "›");
        }

        /// <summary>A pill-shaped label, e.g. a key cap or a status tag. Sizes itself to its text inside layouts.</summary>
        public RectTransform CreateChip(string name, Transform parent, string label, Color background, Color foreground, out Text text)
        {
            Image chip = CreatePanel(name, parent, background, PanelShape.Rounded, 13f);
            AddHorizontalLayout(chip.gameObject, new RectOffset(12, 12, 5, 5), 6f, TextAnchor.MiddleCenter);
            text = CreateText("Label", chip.rectTransform, label, TextStyle.Caption, TextAnchor.MiddleCenter, foreground);
            text.fontStyle = FontStyle.Bold;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            return chip.rectTransform;
        }

        public Button CreateButton(string name, Transform parent, string label, ButtonVariant variant, UnityAction onClick, float preferredWidth = -1f)
        {
            Image background = CreatePanel(name, parent, Color.white);
            background.raycastTarget = true;

            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.colors = BuildColors(variant);

            // A faint tick on hover and a soft click on press. Added first, so the click sounds before the action runs.
            background.gameObject.AddComponent<ButtonSounds>();
            button.onClick.AddListener(ButtonSounds.PlayClick);
            if (variant == ButtonVariant.Secondary)
            {
                AddOutline(background.rectTransform, Theme.Border);
            }

            Text text = CreateText("Label", background.rectTransform, label, TextStyle.Button, TextAnchor.MiddleCenter, LabelColor(variant));
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            Stretch(text.rectTransform, 18f, 18f, 0f, 0f);

            LayoutElement layout = GetOrAddLayoutElement(background.gameObject);
            layout.minHeight = Theme.ButtonHeight;
            layout.preferredHeight = Theme.ButtonHeight;
            layout.minWidth = 120f;
            if (preferredWidth > 0f)
            {
                layout.preferredWidth = preferredWidth;
            }

            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            return button;
        }

        /// <summary>Enables or disables a button made by <see cref="CreateButton"/>, dimming its label to match.</summary>
        public static void SetInteractable(Button button, bool interactable)
        {
            button.interactable = interactable;
            Text label = button.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                Color color = label.color;
                color.a = interactable ? 1f : 0.45f;
                label.color = color;
            }
        }

        public static void SetButtonLabel(Button button, string label)
        {
            Text text = button.GetComponentInChildren<Text>(true);
            if (text != null)
            {
                text.text = label;
            }
        }

        // ---------------------------------------------------------------- Layout

        public static VerticalLayoutGroup AddVerticalLayout(GameObject go, RectOffset padding, float spacing, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var group = go.AddComponent<VerticalLayoutGroup>();
            group.padding = padding;
            group.spacing = spacing;
            group.childAlignment = alignment;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            return group;
        }

        public static HorizontalLayoutGroup AddHorizontalLayout(GameObject go, RectOffset padding, float spacing, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var group = go.AddComponent<HorizontalLayoutGroup>();
            group.padding = padding;
            group.spacing = spacing;
            group.childAlignment = alignment;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            return group;
        }

        /// <summary>Sizes a free-standing element to its content. Do not use on children of layout groups.</summary>
        public static ContentSizeFitter FitContent(GameObject go, bool horizontal, bool vertical)
        {
            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = horizontal ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = vertical ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            return fitter;
        }

        public static LayoutElement GetOrAddLayoutElement(GameObject go)
        {
            var layout = go.GetComponent<LayoutElement>();
            if (layout == null)
            {
                layout = go.AddComponent<LayoutElement>();
            }

            return layout;
        }

        public static void SetFixedSize(GameObject go, float width, float height)
        {
            LayoutElement layout = GetOrAddLayoutElement(go);
            layout.minWidth = width;
            layout.preferredWidth = width;
            layout.minHeight = height;
            layout.preferredHeight = height;
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, height);
        }

        public static void Stretch(RectTransform rect, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>Anchors <paramref name="rect"/> to a single point of its parent.</summary>
        public static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        // ---------------------------------------------------------------- Internals

        private void ApplyShape(Image image, PanelShape shape, float radius)
        {
            switch (shape)
            {
                case PanelShape.Rounded:
                    float target = radius >= 0f ? radius : Theme.CornerRadius;
                    image.sprite = _sprites.Rounded;
                    image.type = Image.Type.Sliced;
                    // Sliced borders render at border / multiplier, so this maps the baked radius to the target radius.
                    image.pixelsPerUnitMultiplier = UISpriteCache.BakedRadius / Mathf.Max(1f, target);
                    break;
                case PanelShape.Circle:
                    image.sprite = _sprites.Circle;
                    image.type = Image.Type.Simple;
                    image.preserveAspect = true;
                    break;
                case PanelShape.LargeCircle:
                    image.sprite = _sprites.LargeCircle;
                    image.type = Image.Type.Simple;
                    image.preserveAspect = true;
                    break;
                default:
                    image.sprite = null;
                    image.type = Image.Type.Simple;
                    break;
            }
        }

        private ColorBlock BuildColors(ButtonVariant variant)
        {
            Color normal;
            Color highlighted;
            Color pressed;
            Color disabled;
            switch (variant)
            {
                case ButtonVariant.Primary:
                    normal = Theme.Accent;
                    highlighted = Lighten(normal, 0.16f);
                    pressed = Darken(normal, 0.14f);
                    disabled = WithAlpha(normal, 0.35f);
                    break;
                case ButtonVariant.Secondary:
                    normal = Theme.SurfaceRaised;
                    highlighted = Lighten(normal, 0.1f);
                    pressed = Darken(normal, 0.12f);
                    disabled = WithAlpha(normal, 0.45f);
                    break;
                default:
                    normal = new Color(1f, 1f, 1f, 0f);
                    highlighted = new Color(1f, 1f, 1f, 0.07f);
                    pressed = new Color(1f, 1f, 1f, 0.12f);
                    disabled = normal;
                    break;
            }

            ColorBlock colors = ColorBlock.defaultColorBlock;
            colors.normalColor = normal;
            colors.highlightedColor = highlighted;
            colors.pressedColor = pressed;
            colors.selectedColor = highlighted;
            colors.disabledColor = disabled;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            return colors;
        }

        private Color LabelColor(ButtonVariant variant)
        {
            switch (variant)
            {
                case ButtonVariant.Primary:
                    return Theme.OnAccent;
                case ButtonVariant.Ghost:
                    return Theme.TextSecondary;
                default:
                    return Theme.TextPrimary;
            }
        }

        private static Color Lighten(Color color, float amount)
        {
            return WithAlpha(Color.Lerp(color, Color.white, amount), color.a);
        }

        private static Color Darken(Color color, float amount)
        {
            return WithAlpha(Color.Lerp(color, Color.black, amount), color.a);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
