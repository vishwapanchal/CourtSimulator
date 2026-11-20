using UnityEngine;
using UnityEngine.UI;

namespace CourtSim.Runtime.UI
{
    /// <summary>
    /// A centred card over a dimming scrim: overline, title, body and an optional footer row.
    /// Subclasses only fill in <see cref="BuildModal"/>, so every dialog shares one structure.
    /// </summary>
    public abstract class ModalScreen : UIScreen
    {
        private Text _overline;
        private Text _title;

        /// <summary>Card width in reference pixels (1920×1080 canvas).</summary>
        protected virtual float CardWidth => 640f;

        protected sealed override void BuildContent(RectTransform root)
        {
            Image scrim = Factory.CreatePanel("Scrim", root, Theme.Scrim, PanelShape.Square);
            scrim.raycastTarget = true;
            UIFactory.Stretch(scrim.rectTransform);

            Image card = Factory.CreatePanel("Card", root, Theme.Surface);
            UIFactory.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CardWidth, 0f));
            int pad = Mathf.RoundToInt(Theme.Padding);
            UIFactory.AddVerticalLayout(card.gameObject, new RectOffset(pad, pad, pad, pad), Theme.Spacing);
            UIFactory.FitContent(card.gameObject, false, true);
            Factory.AddOutline(card.rectTransform, Theme.Border);

            RectTransform header = Factory.CreateRect("Header", card.rectTransform);
            UIFactory.AddVerticalLayout(header.gameObject, new RectOffset(0, 0, 0, 4), 6f);
            _overline = Factory.CreateText("Overline", header, string.Empty, TextStyle.Overline);
            _title = Factory.CreateText("Title", header, string.Empty, TextStyle.Title);

            RectTransform body = Factory.CreateRect("Body", card.rectTransform);
            UIFactory.AddVerticalLayout(body.gameObject, new RectOffset(0, 0, 0, 0), Theme.Spacing);

            Image divider = Factory.CreateDivider(card.rectTransform);
            RectTransform footer = Factory.CreateRect("Footer", card.rectTransform);
            UIFactory.AddHorizontalLayout(footer.gameObject, new RectOffset(0, 0, 4, 0), 12f, TextAnchor.MiddleRight);
            Factory.CreateSpacer(footer);

            BuildModal(body, footer);

            // Only the spacer: no footer actions, so hide the footer and its divider.
            if (footer.childCount <= 1)
            {
                footer.gameObject.SetActive(false);
                divider.gameObject.SetActive(false);
            }
        }

        /// <summary>Adds content to <paramref name="body"/> and right-aligned actions to <paramref name="footer"/>.</summary>
        protected abstract void BuildModal(RectTransform body, RectTransform footer);

        /// <summary>Sets the header. Write <paramref name="overline"/> in capitals.</summary>
        protected void SetHeader(string overline, string title)
        {
            _overline.text = overline;
            _overline.gameObject.SetActive(!string.IsNullOrEmpty(overline));
            _title.text = title;
        }
    }
}
