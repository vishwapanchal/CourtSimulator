using CourtSim.Core.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CourtSim.Runtime.UI.Screens
{
    /// <summary>Placeholder exhibit viewer. Phase 2 binds it to exhibits loaded from case files.</summary>
    public sealed class EvidenceScreen : ModalScreen
    {
        private Button _closeButton;

        public override ScreenId Id => ScreenId.Evidence;

        protected override float CardWidth => 720f;

        protected override Selectable DefaultSelection => _closeButton;

        protected override void BuildModal(RectTransform body, RectTransform footer)
        {
            SetHeader("EXHIBIT P-1 · PLACEHOLDER", "Evidence viewer");

            Image media = Factory.CreatePanel("Media", body, Theme.SurfaceRaised);
            UIFactory.GetOrAddLayoutElement(media.gameObject).preferredHeight = 240f;
            Factory.AddOutline(media.rectTransform, Theme.Border);
            Text mediaLabel = Factory.CreateText("Label", media.rectTransform, "Exhibit media will appear here", TextStyle.Caption, TextAnchor.MiddleCenter);
            UIFactory.Stretch(mediaLabel.rectTransform);

            AddDetail(body, "Tendered by", "Prosecution");
            AddDetail(body, "Status", "Awaiting a ruling on admissibility");
            Factory.CreateText(
                "Note",
                body,
                "Exhibits, their provenance and each party's submissions will be loaded from case files in Phase 2.",
                TextStyle.Caption);

            _closeButton = Factory.CreateButton("Close", footer, "Close", ButtonVariant.Primary, Manager.Back, 160f);
        }

        private void AddDetail(RectTransform parent, string label, string value)
        {
            RectTransform row = Factory.CreateRect(label, parent);
            UIFactory.AddHorizontalLayout(row.gameObject, new RectOffset(0, 0, 0, 0), 12f);
            Text labelText = Factory.CreateText("Label", row, label, TextStyle.Body, TextAnchor.UpperLeft, Theme.TextSecondary);
            UIFactory.GetOrAddLayoutElement(labelText.gameObject).preferredWidth = 160f;
            Text valueText = Factory.CreateText("Value", row, value, TextStyle.Body);
            UIFactory.GetOrAddLayoutElement(valueText.gameObject).flexibleWidth = 1f;
        }
    }
}
