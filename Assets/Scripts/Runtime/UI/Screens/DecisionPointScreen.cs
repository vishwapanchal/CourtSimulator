using System.Collections.Generic;
using CourtSim.Core.Decisions;
using CourtSim.Core.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CourtSim.Runtime.UI.Screens
{
    /// <summary>
    /// Presents the pending decision point (bail → admissibility → sentencing), the options and the
    /// AI assistant's card. Option buttons are pooled and relabelled each time the screen opens.
    /// </summary>
    public sealed class DecisionPointScreen : ModalScreen
    {
        private readonly List<Button> _optionButtons = new List<Button>();
        private readonly List<string> _optionIds = new List<string>();
        private RectTransform _options;
        private Text _prompt;
        private AssistantCardView _assistant;
        private Button _restartButton;

        public override ScreenId Id => ScreenId.DecisionPoint;

        protected override float CardWidth => 760f;

        protected override Selectable DefaultSelection =>
            _optionButtons.Count > 0 && _optionButtons[0].gameObject.activeSelf ? _optionButtons[0] : _restartButton;

        protected override void BuildModal(RectTransform body, RectTransform footer)
        {
            _prompt = Factory.CreateText("Prompt", body, string.Empty, TextStyle.Body, TextAnchor.UpperLeft, Theme.TextSecondary);

            _options = Factory.CreateRect("Options", body);
            UIFactory.AddVerticalLayout(_options.gameObject, new RectOffset(0, 0, 4, 4), 10f);
            _restartButton = Factory.CreateButton("Restart", _options, "Start a new session", ButtonVariant.Primary, OnRestart);

            _assistant = AssistantCardView.Create(Factory, body, false);

            Factory.CreateButton("Return", footer, "Return to courtroom", ButtonVariant.Ghost, Manager.Back, 220f);
        }

        protected override void OnShown()
        {
            Refresh();
        }

        private void Refresh()
        {
            DecisionSequence decisions = Manager.Decisions;
            DecisionPointDefinition current = decisions.Current;

            if (current == null)
            {
                SetHeader("SESSION COMPLETE", "All rulings recorded");
                _prompt.text = "You have ruled on every decision point in this preview session. "
                    + "Feedback on your rulings and adaptive difficulty arrive in later phases.";
                SetOptionCount(0);
                _restartButton.gameObject.SetActive(true);
                _assistant.Root.gameObject.SetActive(false);
                return;
            }

            string progress = $"DECISION {decisions.CurrentIndex + 1} OF {decisions.Count} · {DecisionPointCatalog.ShortName(current.Kind).ToUpperInvariant()}";
            SetHeader(progress, current.Title);
            _prompt.text = current.Prompt;
            _restartButton.gameObject.SetActive(false);

            SetOptionCount(current.Options.Count);
            for (int i = 0; i < current.Options.Count; i++)
            {
                _optionIds[i] = current.Options[i].Id;
                UIFactory.SetButtonLabel(_optionButtons[i], current.Options[i].Label);
            }

            _assistant.Root.gameObject.SetActive(true);
            _assistant.SetMessage(
                "My suggested ruling will appear here, with my reasoning. Some of my suggestions are wrong on purpose: "
                + "part of your training is noticing when.");
        }

        private void SetOptionCount(int count)
        {
            while (_optionButtons.Count < count)
            {
                int index = _optionButtons.Count;
                Button button = Factory.CreateButton($"Option_{index}", _options, string.Empty, ButtonVariant.Secondary, () => OnOptionSelected(index));
                button.transform.SetSiblingIndex(index);
                _optionButtons.Add(button);
                _optionIds.Add(string.Empty);
            }

            for (int i = 0; i < _optionButtons.Count; i++)
            {
                _optionButtons[i].gameObject.SetActive(i < count);
            }
        }

        private void OnOptionSelected(int index)
        {
            if (index < _optionIds.Count)
            {
                Manager.SubmitDecision(_optionIds[index]);
            }
        }

        private void OnRestart()
        {
            Manager.RestartSession();
            Refresh();
            Selectable first = DefaultSelection;
            if (first != null)
            {
                first.Select();
            }
        }
    }
}
