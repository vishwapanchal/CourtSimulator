using System.Collections.Generic;
using CourtSim.Core.Decisions;
using CourtSim.Core.Sessions;
using CourtSim.Runtime.Player;
using UnityEngine;
using UnityEngine.UI;

namespace CourtSim.Runtime.UI
{
    /// <summary>
    /// In-world overlay: session status, key hints, crosshair, interaction prompt, toasts and the AI
    /// assistant side panel. Owned and driven by <see cref="UIManager"/>; it holds no game state itself.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudView : MonoBehaviour
    {
        private const float ToastSeconds = 2.8f;

        private readonly List<Image> _progressPips = new List<Image>();
        private UIFactory _factory;
        private CanvasGroupFader _rootFader;
        private Text _overline;
        private Text _nextRuling;
        private RectTransform _pipRow;
        private Image _crosshair;
        private CanvasGroupFader _promptFader;
        private Text _promptText;
        private CanvasGroupFader _toastFader;
        private Text _toastText;
        private float _toastRemaining;
        private CanvasGroupFader _assistantFader;
        private AssistantCardView _assistant;
        private CaseSession _session;

        public bool IsAssistantVisible => _assistantFader != null && _assistantFader.TargetVisible;

        /// <summary>True while the assistant panel's message is still typing out.</summary>
        public bool IsAssistantTyping => _assistant != null && _assistant.IsTyping;

        internal void Build(UIFactory factory, IPlayerInputSource input)
        {
            _factory = factory;
            UITheme theme = factory.Theme;
            var root = (RectTransform)transform;
            UIFactory.Stretch(root);
            _rootFader = gameObject.AddComponent<CanvasGroupFader>();
            _rootFader.Duration = theme.FadeDuration;

            BuildStatusCard(root, theme);
            BuildHints(root, theme, input);
            BuildCrosshair(root);
            BuildPrompt(root, theme, input);
            BuildToast(root, theme);
            BuildAssistant(root);
        }

        public void SetVisible(bool visible)
        {
            _rootFader.SetVisible(visible);
        }

        public void SetPrompt(string prompt)
        {
            bool show = !string.IsNullOrEmpty(prompt);
            if (show)
            {
                _promptText.text = prompt;
            }

            _promptFader.SetVisible(show);
            _crosshair.color = show ? _factory.Theme.Accent : new Color(1f, 1f, 1f, 0.75f);
            _crosshair.rectTransform.sizeDelta = show ? new Vector2(10f, 10f) : new Vector2(6f, 6f);
        }

        public void ShowToast(string message)
        {
            _toastText.text = message;
            _toastRemaining = ToastSeconds;
            _toastFader.SetVisible(true);
        }

        public void SetAssistantVisible(bool visible)
        {
            _assistantFader.SetVisible(visible);
        }

        /// <summary>Updates the status card, progress pips and assistant message from the session's decisions.</summary>
        public void SetDecisionProgress(DecisionSequence decisions)
        {
            DecisionPointDefinition current = decisions.Current;
            _nextRuling.text = current != null ? $"Next ruling: {current.Title}" : "All rulings recorded";

            while (_progressPips.Count < decisions.Count)
            {
                Image pip = _factory.CreatePanel($"Pip_{_progressPips.Count}", _pipRow, Color.white, PanelShape.Circle);
                UIFactory.SetFixedSize(pip.gameObject, 10f, 10f);
                _progressPips.Add(pip);
            }

            for (int i = 0; i < _progressPips.Count; i++)
            {
                _progressPips[i].gameObject.SetActive(i < decisions.Count);
                _progressPips[i].color = i < decisions.CurrentIndex ? _factory.Theme.Accent : _factory.Theme.Border;
            }

            // With a case session the assistant shows its advice instead (SetSession). Writing this placeholder
            // first and the advice straight after would restart the typing effect on every refresh.
            if (_session == null)
            {
                _assistant.SetMessage(current != null
                    ? $"Your next ruling is the {current.Title.ToLowerInvariant()}. Approach the bench when you are ready and I will offer a suggested ruling there."
                    : "Every ruling in this session is recorded.");
            }
        }

        /// <summary>Shows the case title and, when the session has it, the advice for the pending ruling.</summary>
        public void SetSession(CaseSession session)
        {
            _session = session;
            if (session == null)
            {
                return;
            }

            _overline.text = "CASE · " + session.Case.Title.ToUpperInvariant();
            if (session.IsComplete)
            {
                _assistant.SetMessage("Every ruling in this case is recorded. Approach the bench to see the debrief, including where my advice was flawed.");
                _assistant.SetFootnote(string.Empty);
            }
            else
            {
                _assistant.Present(session);
            }
        }

        private void Update()
        {
            if (_toastRemaining <= 0f)
            {
                return;
            }

            _toastRemaining -= Time.unscaledDeltaTime;
            if (_toastRemaining <= 0f)
            {
                _toastFader.SetVisible(false);
            }
        }

        private void BuildStatusCard(RectTransform root, UITheme theme)
        {
            Image card = _factory.CreatePanel("Status", root, theme.Surface);
            UIFactory.Place(card.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -32f), new Vector2(380f, 0f));
            UIFactory.AddVerticalLayout(card.gameObject, new RectOffset(20, 20, 16, 16), 8f);
            UIFactory.FitContent(card.gameObject, false, true);
            _factory.AddOutline(card.rectTransform, theme.Border);

            _overline = _factory.CreateText("Overline", card.rectTransform, "SESSION · PREVIEW CASE", TextStyle.Overline);
            _nextRuling = _factory.CreateText("NextRuling", card.rectTransform, string.Empty, TextStyle.Body);
            _pipRow = _factory.CreateRect("Progress", card.rectTransform);
            UIFactory.AddHorizontalLayout(_pipRow.gameObject, new RectOffset(0, 0, 2, 0), 6f);
        }

        private void BuildHints(RectTransform root, UITheme theme, IPlayerInputSource input)
        {
            RectTransform hints = _factory.CreateRect("Hints", root);
            UIFactory.Place(hints, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-32f, -32f), Vector2.zero);
            UIFactory.AddHorizontalLayout(hints.gameObject, new RectOffset(0, 0, 0, 0), 18f, TextAnchor.MiddleRight);
            UIFactory.FitContent(hints.gameObject, true, true);

            foreach (InputBindingInfo binding in input.Bindings)
            {
                if (!binding.ShowInHud)
                {
                    continue;
                }

                RectTransform hint = _factory.CreateRect(binding.Action.ToString(), hints);
                UIFactory.AddHorizontalLayout(hint.gameObject, new RectOffset(0, 0, 0, 0), 8f);
                _factory.CreateChip("Key", hint, binding.Keys, theme.Surface, theme.TextPrimary, out _);
                Text label = _factory.CreateText("Label", hint, binding.DisplayName, TextStyle.Caption, TextAnchor.MiddleLeft, theme.TextPrimary);
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
        }

        private void BuildCrosshair(RectTransform root)
        {
            _crosshair = _factory.CreatePanel("Crosshair", root, new Color(1f, 1f, 1f, 0.75f), PanelShape.Circle);
            UIFactory.Place(_crosshair.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6f, 6f));
        }

        private void BuildPrompt(RectTransform root, UITheme theme, IPlayerInputSource input)
        {
            Image prompt = _factory.CreatePanel("Prompt", root, theme.Surface, PanelShape.Rounded, 22f);
            UIFactory.Place(prompt.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 140f), Vector2.zero);
            UIFactory.AddHorizontalLayout(prompt.gameObject, new RectOffset(10, 22, 9, 9), 12f, TextAnchor.MiddleCenter);
            UIFactory.FitContent(prompt.gameObject, true, true);

            _factory.CreateChip("Key", prompt.rectTransform, input.KeyLabel(PlayerAction.Interact), theme.Accent, theme.OnAccent, out _);
            _promptText = _factory.CreateText("Label", prompt.rectTransform, string.Empty, TextStyle.Body, TextAnchor.MiddleLeft);
            _promptText.horizontalOverflow = HorizontalWrapMode.Overflow;

            _promptFader = prompt.gameObject.AddComponent<CanvasGroupFader>();
            _promptFader.Duration = theme.FadeDuration;
            _promptFader.SetVisible(false, true);
        }

        private void BuildToast(RectTransform root, UITheme theme)
        {
            Image toast = _factory.CreatePanel("Toast", root, theme.SurfaceRaised, PanelShape.Rounded, 22f);
            UIFactory.Place(toast.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -32f), Vector2.zero);
            UIFactory.AddHorizontalLayout(toast.gameObject, new RectOffset(18, 22, 12, 12), 10f, TextAnchor.MiddleCenter);
            UIFactory.FitContent(toast.gameObject, true, true);
            _factory.AddOutline(toast.rectTransform, theme.Border, 22f);

            Image dot = _factory.CreatePanel("Dot", toast.rectTransform, theme.Success, PanelShape.Circle);
            UIFactory.SetFixedSize(dot.gameObject, 10f, 10f);
            _toastText = _factory.CreateText("Message", toast.rectTransform, string.Empty, TextStyle.Body, TextAnchor.MiddleLeft);
            _toastText.horizontalOverflow = HorizontalWrapMode.Overflow;

            _toastFader = toast.gameObject.AddComponent<CanvasGroupFader>();
            _toastFader.Duration = theme.FadeDuration;
            _toastFader.SetVisible(false, true);
        }

        private void BuildAssistant(RectTransform root)
        {
            _assistant = AssistantCardView.Create(_factory, root, true);
            UIFactory.Place(_assistant.Root, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-32f, 0f), new Vector2(420f, 0f));
            _assistantFader = _assistant.Root.gameObject.AddComponent<CanvasGroupFader>();
            _assistantFader.Duration = _factory.Theme.FadeDuration;
            _assistantFader.SetVisible(false, true);
        }
    }
}
