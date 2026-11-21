using CourtSim.Core.Advising;
using CourtSim.Core.Logging;
using CourtSim.Core.Presentation;
using CourtSim.Core.Sessions;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CourtSim.Runtime.UI
{
    /// <summary>
    /// The AI assistant's card: badge, title, advisory tag, message, a standing caution, Accept/Challenge
    /// actions and a source footnote. Used by the HUD side panel and inside the decision dialog, so the
    /// assistant always looks and reads the same wherever it appears.
    /// </summary>
    public sealed class AssistantCardView
    {
        public const string Caution = "Suggestions can be deliberately flawed. Test every one against the evidence and the law before relying on it.";

        private readonly TypewriterText _message;
        private readonly Text _footnote;
        private readonly RectTransform _actions;

        private AssistantCardView(RectTransform root, TypewriterText message, Text footnote, RectTransform actions, Button accept, Button challenge)
        {
            Root = root;
            _message = message;
            _footnote = footnote;
            _actions = actions;
            AcceptButton = accept;
            ChallengeButton = challenge;
        }

        public RectTransform Root { get; }

        public Button AcceptButton { get; }

        public Button ChallengeButton { get; }

        /// <summary>The full message, even while it is still typing out (the tests rely on this).</summary>
        public string Message => _message.Target;

        /// <summary>True while the message is still typing out.</summary>
        public bool IsTyping => _message.IsTyping;

        /// <param name="standalone">True when not inside a layout group; the card then sizes its own height.</param>
        /// <param name="onAccept">Accept action; when both actions are null the action row is hidden.</param>
        public static AssistantCardView Create(UIFactory factory, Transform parent, bool standalone, UnityAction onAccept = null, UnityAction onChallenge = null)
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
            message.supportRichText = true; // our own bold/italic markup; model text is passed through UIFactory.Plain
            TypewriterText typewriter = message.gameObject.AddComponent<TypewriterText>();
            factory.CreateText("Caution", card.rectTransform, Caution, TextStyle.Caption, TextAnchor.UpperLeft, theme.Warning);

            RectTransform actions = factory.CreateRect("Actions", card.rectTransform);
            UIFactory.AddHorizontalLayout(actions.gameObject, new RectOffset(0, 0, 4, 0), 10f);
            Button accept = factory.CreateButton("Accept", actions, "Accept advice", ButtonVariant.Secondary, onAccept, 170f);
            Button challenge = factory.CreateButton("Challenge", actions, "Challenge it", ButtonVariant.Secondary, onChallenge, 170f);
            UIFactory.SetInteractable(accept, false);
            UIFactory.SetInteractable(challenge, false);
            actions.gameObject.SetActive(onAccept != null || onChallenge != null);
            Text footnote = factory.CreateText("Footnote", card.rectTransform, string.Empty, TextStyle.Caption);

            return new AssistantCardView(card.rectTransform, typewriter, footnote, actions, accept, challenge);
        }

        /// <summary>Types <paramref name="message"/> out; sending the same message again carries on instead of restarting.</summary>
        public void SetMessage(string message)
        {
            _message.SetTarget(message);
        }

        /// <summary>Shows the whole message at once, cutting the typing short.</summary>
        public void SkipTyping()
        {
            _message.Skip();
        }

        public void SetFootnote(string footnote)
        {
            _footnote.text = footnote ?? string.Empty;
            _footnote.gameObject.SetActive(!string.IsNullOrEmpty(footnote));
        }

        /// <summary>Shows the session's advice for the pending decision, with actions enabled only when it is ready.</summary>
        public void Present(CaseSession session)
        {
            if (session == null || session.IsComplete)
            {
                SetActions(false, AdviceStance.None);
                return;
            }

            SetMessage(AdviceText.Describe(session));
            SetFootnote(AdviceText.Footnote(session));
            SetActions(session.CurrentAdviceState == AdviceState.Ready, session.CurrentStance);
        }

        private void SetActions(bool enabled, AdviceStance stance)
        {
            if (!_actions.gameObject.activeSelf)
            {
                return;
            }

            UIFactory.SetInteractable(AcceptButton, enabled && stance != AdviceStance.Accepted);
            UIFactory.SetInteractable(ChallengeButton, enabled && stance != AdviceStance.Challenged);
            UIFactory.SetButtonLabel(AcceptButton, stance == AdviceStance.Accepted ? "Accepted" : "Accept advice");
            UIFactory.SetButtonLabel(ChallengeButton, stance == AdviceStance.Challenged ? "Challenged" : "Challenge it");
        }
    }

    /// <summary>Wording for the assistant's messages.</summary>
    public static class AdviceText
    {
        public static string Describe(CaseSession session)
        {
            switch (session.CurrentAdviceState)
            {
                case AdviceState.Ready:
                    Advice advice = session.CurrentAdvice;
                    string label = session.CurrentPoint.Definition.FindOption(advice.RecommendedOptionId)?.Label ?? advice.RecommendedOptionId;
                    return $"<b>Suggested ruling: {UIFactory.Plain(label)}</b>\n{UIFactory.Plain(advice.Rationale)}\n<i>Confidence: {AdviceConfidences.ToWireName(advice.Confidence)}</i>";
                case AdviceState.Failed:
                    return "I could not prepare advice for this ruling. Rule on the record and the submissions.";
                default:
                    return "I am reviewing the record for this ruling. My suggestion will appear here shortly.";
            }
        }

        /// <summary>
        /// What the assistant's voice says for the ready advice, "My suggested ruling: (option). (reasons)",
        /// with no rich-text tags. Empty when no advice is ready.
        /// </summary>
        public static string Spoken(CaseSession session)
        {
            if (session == null || session.IsComplete || session.CurrentAdviceState != AdviceState.Ready || session.CurrentAdvice == null)
            {
                return string.Empty;
            }

            Advice advice = session.CurrentAdvice;
            string label = session.CurrentPoint.Definition.FindOption(advice.RecommendedOptionId)?.Label ?? advice.RecommendedOptionId;
            string ruling = RichTextTyping.StripTags(label).Trim().TrimEnd('.');
            string rationale = RichTextTyping.StripTags(advice.Rationale).Trim();
            return rationale.Length > 0 ? $"My suggested ruling: {ruling}. {rationale}" : $"My suggested ruling: {ruling}.";
        }

        public static string Footnote(CaseSession session)
        {
            Advice advice = session.CurrentAdvice;
            if (advice == null)
            {
                return string.Empty;
            }

            if (advice.Source == AdviceSource.Claude)
            {
                return "Written by Claude for this case.";
            }

            if (advice.Source == AdviceSource.LocalModel)
            {
                return "Written by a local AI model for this case.";
            }

            return session.AdviceErrorAt(session.CurrentIndex) != null
                ? "Offline advice: the AI model was unavailable."
                : "Offline advice from the case file.";
        }
    }
}
