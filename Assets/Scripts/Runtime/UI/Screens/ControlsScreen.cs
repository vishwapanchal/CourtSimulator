using CourtSim.Core.UI;
using CourtSim.Runtime.Player;
using UnityEngine;
using UnityEngine.UI;

namespace CourtSim.Runtime.UI.Screens
{
    /// <summary>Lists every binding reported by the active input source, so it can never drift from the code.</summary>
    public sealed class ControlsScreen : ModalScreen
    {
        private Button _backButton;

        public override ScreenId Id => ScreenId.Controls;

        protected override float CardWidth => 560f;

        protected override Selectable DefaultSelection => _backButton;

        protected override void BuildModal(RectTransform body, RectTransform footer)
        {
            SetHeader("HELP", "Controls");

            foreach (InputBindingInfo binding in Manager.InputSource.Bindings)
            {
                RectTransform row = Factory.CreateRect(binding.Action.ToString(), body);
                UIFactory.AddHorizontalLayout(row.gameObject, new RectOffset(0, 0, 2, 2), 12f);
                Text label = Factory.CreateText("Action", row, binding.DisplayName, TextStyle.Body);
                UIFactory.GetOrAddLayoutElement(label.gameObject).flexibleWidth = 1f;
                Factory.CreateChip("Keys", row, binding.Keys, Theme.SurfaceRaised, Theme.TextPrimary, out _);
            }

            _backButton = Factory.CreateButton("Back", footer, "Back", ButtonVariant.Secondary, Manager.Back, 160f);
        }
    }
}
