using UnityEngine;
using UnityEngine.UI;

namespace CourtSim.Runtime.UI
{
    /// <summary>
    /// The AI assistant's card: badge, title, advisory tag, message, a standing caution and (for now
    /// disabled) Accept/Challenge actions. Used by the HUD side panel and inside the decision dialog, so
    /// the assistant always looks and reads the same wherever it appears.
    /// </summary>
    public sealed class AssistantCardView
    {
        public const string Caution = "Suggestions can be deliberately flawed. Test every one against the evidence and the law before relying on it.";

        private readonly Text _message;

        private AssistantCardView(RectTransform root, Text message, Button accept, Button challenge)
        {
            Root = root;
            _message = message;
            AcceptButton = accept;
            ChallengeButton = challenge;
        }

        public RectTransform Root { get; }

        public Button AcceptButton { get; }

        public Button ChallengeButton { get; }

        /// <param name="standalone">True when not inside a layout group; the card then sizes its own height.</param>
        public static AssistantCardView Create(UIFactory factory, Transform parent, bool standalone)
        {
            UITheme theme = factory.Theme;
            Image card = factory.CreatePanel("AssistantCard", parent, theme.SurfaceRaised);
            UIFactory.AddVerticalLayout(card.gameObject, new RectOffset(20, 20, 18, 18), 10f);
            if (standalone)
            {
                UIFactory.FitContent(card.gameObject, false, true);
            }

            Color accentLine = theme.Accent;
            accentLine.a = 0.35f;
            factory.AddOutline(card.rectTransform, accentLine);

            // Header: [AI] AI Assistant ............ ADVISORY
            RectTransform header = factory.CreateRect("Header", card.rectTransform);
            UIFactory.AddHorizontalLayout(header.gameObject, new RectOffset(0, 0, 0, 0), 10f);
            Image badge = factory.CreatePanel("Badge", header, theme.Accent, PanelShape.Circle);
            UIFactory.SetFixedSize(badge.gameObject, 30f, 30f);
            Text badgeLabel = factory.CreateText("Label", badge.rectTransform, "AI", TextStyle.Caption, TextAnchor.MiddleCenter, theme.OnAccent);
            badgeLabel.fontStyle = FontStyle.Bold;
            UIFactory.Stretch(badgeLabel.rectTransform);
            factory.CreateText("Title", header, "AI Assistant", TextStyle.Heading);
            factory.CreateSpacer(header);
            Color tagBackground = theme.Warning;
            tagBackground.a = 0.16f;
            factory.CreateChip("Tag", header, "ADVISORY", tagBackground, theme.Warning, out _);

            Text message = factory.CreateText("Message", card.rectTransform, string.Empty, TextStyle.Body);
            factory.CreateText("Caution", card.rectTransform, Caution, TextStyle.Caption, TextAnchor.UpperLeft, theme.Warning);

            RectTransform actions = factory.CreateRect("Actions", card.rectTransform);
            UIFactory.AddHorizontalLayout(actions.gameObject, new RectOffset(0, 0, 4, 0), 10f);
            Button accept = factory.CreateButton("Accept", actions, "Accept advice", ButtonVariant.Secondary, null, 170f);
            Button challenge = factory.CreateButton("Challenge", actions, "Challenge it", ButtonVariant.Secondary, null, 170f);
            UIFactory.SetInteractable(accept, false);
            UIFactory.SetInteractable(challenge, false);
            factory.CreateText("Footnote", card.rectTransform, "Interactive advice arrives in Phase 2.", TextStyle.Caption);

            return new AssistantCardView(card.rectTransform, message, accept, challenge);
        }

        public void SetMessage(string message)
        {
            _message.text = message;
        }
    }
}
