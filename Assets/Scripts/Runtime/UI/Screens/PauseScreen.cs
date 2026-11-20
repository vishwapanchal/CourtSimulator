using CourtSim.Core.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CourtSim.Runtime.UI.Screens
{
    /// <summary>In-session menu opened with Esc.</summary>
    public sealed class PauseScreen : ModalScreen
    {
        private Button _resumeButton;

        public override ScreenId Id => ScreenId.Pause;

        protected override float CardWidth => 480f;

        protected override Selectable DefaultSelection => _resumeButton;

        protected override void BuildModal(RectTransform body, RectTransform footer)
        {
            SetHeader("SESSION PAUSED", "Paused");
            Factory.CreateText("Info", body, "Your rulings so far are kept while the session is paused.", TextStyle.Body, TextAnchor.UpperLeft, Theme.TextSecondary);

            _resumeButton = Factory.CreateButton("Resume", body, "Resume", ButtonVariant.Primary, Manager.Back);
            Factory.CreateButton("Controls", body, "Controls", ButtonVariant.Secondary, () => Manager.Open(ScreenId.Controls));
            Factory.CreateButton("MainMenu", body, "Main menu", ButtonVariant.Secondary, Manager.ShowMainMenu);
            Factory.CreateButton("Quit", body, "Quit", ButtonVariant.Ghost, Manager.QuitApplication);
        }
    }
}
