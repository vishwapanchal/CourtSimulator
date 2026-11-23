using CourtSim.Core.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CourtSim.Runtime.UI.Screens
{
    /// <summary>
    /// The home page: the project title and the main actions over a dimmed, vignetted view of the courtroom, with
    /// the gold emblem glowing softly on the right.
    /// </summary>
    /// <remarks>
    /// Layers, back to front: a light dimming of the 3D room, a dark fade on the left where the text sits, a
    /// darker band along the bottom and a vignette on the corners (so the eye goes to the title), then the emblem
    /// and the text column. Motion is kept slow and small: the column eases in when the page opens, light sweeps
    /// across the title every few seconds, and the emblem's glow breathes. The menu drone is started by
    /// <see cref="UIManager"/>, which knows when the player leaves for the courtroom.
    /// </remarks>
    public sealed class MainMenuScreen : UIScreen
    {
        private const float ColumnLeft = 140f;
        private const float EntranceSeconds = 0.9f;
        private const float SweepInterval = 8f;
        private const float PulseSeconds = 5f;

        private Button _enterButton;
        private Text _casesTag;
        private RectTransform _column;
        private CanvasGroup _columnGroup;
        private RectTransform _emblem;
        private Image _emblemGlow;
        private float _shownAt;

        public override ScreenId Id => ScreenId.MainMenu;

        protected override Selectable DefaultSelection => _enterButton;

        protected override void BuildContent(RectTransform root)
        {
            BuildBackdrop(root);
            BuildEmblem(root);
            BuildColumn(root);

            Text footer = Factory.CreateText("Footer", root, "Desktop mode: WASD to move, mouse to look. VR support is planned.", TextStyle.Caption);
            UIFactory.Place(footer.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(ColumnLeft, 44f), new Vector2(900f, 24f));
        }

        protected override void OnShown()
        {
            _shownAt = Time.unscaledTime;
            int cases = Manager.CaseCatalog.Count;
            _casesTag.text = cases > 0 ? $"{cases} INDIAN-LAW CASES" : "INDIAN-LAW CASES";
            Animate(0f);
        }

        private void Update()
        {
            Animate(Time.unscaledTime - _shownAt);
        }

        /// <summary>Sets the moving parts for <paramref name="seconds"/> since the page was shown.</summary>
        private void Animate(float seconds)
        {
            // The column rises a little and fades in, easing to a stop.
            float entrance = Mathf.Clamp01(seconds / EntranceSeconds);
            float eased = 1f - ((1f - entrance) * (1f - entrance) * (1f - entrance));
            _columnGroup.alpha = entrance;
            _column.anchoredPosition = new Vector2(ColumnLeft, Mathf.Lerp(-28f, 0f, eased));

            // A slow breath: the glow brightens and the emblem swells by under two percent.
            float breath = 0.5f + (0.5f * Mathf.Sin(seconds * (2f * Mathf.PI / PulseSeconds)));
            Color glow = _emblemGlow.color;
            glow.a = 0.1f + (0.08f * breath);
            _emblemGlow.color = glow;
            _emblem.localScale = Vector3.one * (1f + (0.015f * breath));
        }

        private void BuildBackdrop(RectTransform root)
        {
            // A light all-over dimming that also catches clicks, so the room behind cannot be clicked.
            Color dim = Theme.Backdrop;
            dim.a = 0.35f;
            Image dimmer = Factory.CreatePanel("Dimmer", root, dim, PanelShape.Square);
            dimmer.raycastTarget = true;
            UIFactory.Stretch(dimmer.rectTransform);

            Color shade = Theme.Backdrop;
            shade.a = 0.96f;
            Image side = Factory.CreateGradient("SideShade", root, shade, GradientShape.FadeRight);
            StretchOver(side.rectTransform, Vector2.zero, new Vector2(0.78f, 1f));

            shade.a = 0.85f;
            Image bottom = Factory.CreateGradient("BottomShade", root, shade, GradientShape.FadeUp);
            StretchOver(bottom.rectTransform, Vector2.zero, new Vector2(1f, 0.35f));

            shade.a = 0.9f;
            Image vignette = Factory.CreateGradient("Vignette", root, shade, GradientShape.Vignette);
            UIFactory.Stretch(vignette.rectTransform);
        }

        private void BuildEmblem(RectTransform root)
        {
            var centre = new Vector2(-400f, 30f); // measured from the right edge, a touch above the middle
            Color glow = Theme.Accent;
            glow.a = 0.14f;
            _emblemGlow = Factory.CreateGradient("EmblemGlow", root, glow, GradientShape.Glow);
            UIFactory.Place(_emblemGlow.rectTransform, new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), centre, new Vector2(760f, 760f));

            _emblem = JusticeEmblem.Build(Factory, root, 300f);
            UIFactory.Place(_emblem, new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), centre, new Vector2(300f, 300f));
        }

        private void BuildColumn(RectTransform root)
        {
            _column = Factory.CreateRect("Column", root);
            UIFactory.Place(_column, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(ColumnLeft, 0f), new Vector2(1100f, 0f));
            UIFactory.AddVerticalLayout(_column.gameObject, new RectOffset(0, 0, 0, 0), 14f);
            UIFactory.FitContent(_column.gameObject, false, true);
            _columnGroup = _column.gameObject.AddComponent<CanvasGroup>();

            Factory.CreateText("Overline", _column, "COURTSIM  ·  JUDICIAL TRAINING SIMULATOR", TextStyle.Overline);

            // The title in two colours, ivory then gold. Light sweeps across each line, the second just after the first.
            RectTransform title = Factory.CreateRect("Title", _column);
            UIFactory.AddVerticalLayout(title.gameObject, new RectOffset(0, 0, 0, 0), -6f);
            Text lineA = Factory.CreateText("TitleA", title, "ADAPTIVE VIRTUAL REALITY", TextStyle.Display);
            Text lineB = Factory.CreateText("TitleB", title, "COURTROOM SIMULATION", TextStyle.Display, TextAnchor.UpperLeft, Theme.Accent);
            LightSweep.AddTo(lineA, Factory, 1.2f, SweepInterval, 0.55f);
            LightSweep.AddTo(lineB, Factory, 1.45f, SweepInterval, 0.7f);

            RectTransform ruleRow = Factory.CreateRect("RuleRow", _column);
            UIFactory.AddHorizontalLayout(ruleRow.gameObject, new RectOffset(0, 0, 4, 4), 0f);
            Image rule = Factory.CreatePanel("Rule", ruleRow, Theme.Accent, PanelShape.Square);
            UIFactory.SetFixedSize(rule.gameObject, 140f, 2f);

            // The second, lighter weight of the title.
            Text subtitle = Factory.CreateText(
                "Subtitle",
                _column,
                "for AI-Assisted Legal Augmentation and Decision-Making Training",
                TextStyle.Heading,
                TextAnchor.UpperLeft,
                Theme.TextPrimary);
            subtitle.fontStyle = FontStyle.Normal;
            subtitle.fontSize = Theme.FontSize(TextStyle.Heading) + 6;

            Factory.CreateText(
                "Pitch",
                _column,
                "Preside over a case and make the rulings that matter: bail, the admissibility of evidence, and sentence. "
                + "An AI assistant advises you, and it is not always right.",
                TextStyle.Body,
                TextAnchor.UpperLeft,
                Theme.TextSecondary);

            RectTransform tags = Factory.CreateRect("Tags", _column);
            UIFactory.AddHorizontalLayout(tags.gameObject, new RectOffset(0, 0, 6, 14), 10f);
            _casesTag = AddTag(tags, "Cases", "INDIAN-LAW CASES");
            AddTag(tags, "Language", "KANNADA-ENGLISH HEARINGS");
            AddTag(tags, "Advisor", "LOCAL AI ADVISOR ON GPU");

            RectTransform actions = Factory.CreateRect("Actions", _column);
            UIFactory.AddHorizontalLayout(actions.gameObject, new RectOffset(0, 0, 0, 0), 14f);
            _enterButton = Factory.CreateButton("Enter", actions, "Enter the courtroom", ButtonVariant.Primary, () => Manager.Open(ScreenId.CaseSelect), 340f);
            LayoutElement enterLayout = UIFactory.GetOrAddLayoutElement(_enterButton.gameObject);
            enterLayout.minHeight = Theme.ButtonHeight + 8f;
            enterLayout.preferredHeight = Theme.ButtonHeight + 8f;
            Factory.CreateButton("Demo", actions, "Watch guided demo", ButtonVariant.Secondary, Manager.RequestGuidedDemo, 250f);
            Factory.CreateButton("Controls", actions, "Controls", ButtonVariant.Secondary, () => Manager.Open(ScreenId.Controls), 170f);
            Factory.CreateButton("Quit", actions, "Quit", ButtonVariant.Ghost, Manager.QuitApplication, 120f);
        }

        /// <summary>A gold-outlined tag; returns its label so the text can change later.</summary>
        private Text AddTag(RectTransform row, string name, string label)
        {
            Color background = Theme.Surface;
            background.a = 0.7f;
            RectTransform chip = Factory.CreateChip(name, row, label, background, Theme.TextPrimary, out Text text);
            Color outline = Theme.Accent;
            outline.a = 0.55f;
            Factory.AddOutline(chip, outline, 13f);
            return text;
        }

        /// <summary>Stretches <paramref name="rect"/> over the part of its parent between two anchor corners.</summary>
        private static void StretchOver(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
