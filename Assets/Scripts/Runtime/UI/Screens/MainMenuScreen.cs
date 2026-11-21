using CourtSim.Core.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CourtSim.Runtime.UI.Screens
{
    /// <summary>Title screen shown over a dimmed view of the courtroom.</summary>
    public sealed class MainMenuScreen : UIScreen
    {
        private Button _enterButton;

        public override ScreenId Id => ScreenId.MainMenu;

        protected override Selectable DefaultSelection => _enterButton;

        protected override void BuildContent(RectTransform root)
        {
            Color backdrop = Theme.Scrim;
            backdrop.a = 0.82f;
            Image background = Factory.CreatePanel("Backdrop", root, backdrop, PanelShape.Square);
            background.raycastTarget = true;
            UIFactory.Stretch(background.rectTransform);

            RectTransform column = Factory.CreateRect("Column", root);
            UIFactory.Place(column, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(160f, 0f), new Vector2(640f, 0f));
            UIFactory.AddVerticalLayout(column.gameObject, new RectOffset(0, 0, 0, 0), 18f);
            UIFactory.FitContent(column.gameObject, false, true);

            Factory.CreateText("Overline", column, "ADAPTIVE COURTROOM TRAINING", TextStyle.Overline);
            Factory.CreateText("Title", column, "CourtSim", TextStyle.Display);
            Factory.CreateText(
                "Pitch",
                column,
                "Preside over a case and make the rulings that matter: bail, the admissibility of evidence, and sentence. "
                + "An AI assistant advises you, and it is not always right.",
                TextStyle.Body,
                TextAnchor.UpperLeft,
                Theme.TextSecondary);

            RectTransform tags = Factory.CreateRect("Tags", column);
            UIFactory.AddHorizontalLayout(tags.gameObject, new RectOffset(0, 0, 2, 10), 8f);
            Color tagBackground = Theme.SurfaceRaised;
            Factory.CreateChip("Phase", tags, "LOCAL AI ADVISOR · ADAPTIVE", tagBackground, Theme.TextSecondary, out _);
            Factory.CreateChip("Mode", tags, "DESKTOP MODE", tagBackground, Theme.TextSecondary, out _);

            RectTransform actions = Factory.CreateRect("Actions", column);
            UIFactory.AddVerticalLayout(actions.gameObject, new RectOffset(0, 220, 0, 0), 10f);
            _enterButton = Factory.CreateButton("Enter", actions, "Enter the courtroom", ButtonVariant.Primary, Manager.CloseAll);
            Factory.CreateButton("Demo", actions, "Watch guided demo", ButtonVariant.Secondary, Manager.RequestGuidedDemo);
            Factory.CreateButton("Controls", actions, "Controls", ButtonVariant.Secondary, () => Manager.Open(ScreenId.Controls));
            Factory.CreateButton("Quit", actions, "Quit", ButtonVariant.Ghost, Manager.QuitApplication);

            Text footer = Factory.CreateText("Footer", root, "VR support is planned. Desktop controls: WASD to move, mouse to look.", TextStyle.Caption);
            UIFactory.Place(footer.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(160f, 48f), new Vector2(900f, 24f));
        }
    }
}
