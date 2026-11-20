using System.Collections.Generic;
using System.Text;
using CourtSim.Core.Advising;
using CourtSim.Core.Cases;
using CourtSim.Core.Decisions;
using CourtSim.Core.Logging;
using CourtSim.Core.Sessions;
using CourtSim.Core.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CourtSim.Runtime.UI.Screens
{
    /// <summary>
    /// Presents the pending decision point (bail → admissibility → sentencing): the question, both parties'
    /// submissions and the options on the left, the AI assistant's suggestion on the right. When the session
    /// is complete it shows the debrief instead. Option buttons are pooled and relabelled on every refresh.
    /// </summary>
    public sealed class DecisionPointScreen : ModalScreen
    {
        private const float AssistantColumnWidth = 460f;

        private readonly List<Button> _optionButtons = new List<Button>();
        private readonly List<string> _optionIds = new List<string>();
        private RectTransform _columns;
        private RectTransform _options;
        private Text _prompt;
        private Text _submissions;
        private AssistantCardView _assistant;
        private RectTransform _debrief;
        private Text _debriefText;
        private Button _restartButton;

        public override ScreenId Id => ScreenId.DecisionPoint;

        /// <summary>The debrief text currently shown, for tests.</summary>
        public string DebriefText => _debriefText != null ? _debriefText.text : string.Empty;

        /// <summary>The assistant's current message, for tests.</summary>
        public string AssistantMessage => _assistant != null ? _assistant.Message : string.Empty;

        internal AssistantCardView Assistant => _assistant;

        protected override float CardWidth => 1240f;

        protected override Selectable DefaultSelection =>
            _optionButtons.Count > 0 && _optionButtons[0].gameObject.activeInHierarchy ? _optionButtons[0] : _restartButton;

        protected override void BuildModal(RectTransform body, RectTransform footer)
        {
            _columns = Factory.CreateRect("Columns", body);
            UIFactory.AddHorizontalLayout(_columns.gameObject, new RectOffset(0, 0, 0, 0), 28f, TextAnchor.UpperLeft);

            RectTransform left = Factory.CreateRect("Ruling", _columns);
            UIFactory.AddVerticalLayout(left.gameObject, new RectOffset(0, 0, 0, 0), Theme.Spacing);
            UIFactory.GetOrAddLayoutElement(left.gameObject).flexibleWidth = 1f;
            _prompt = Factory.CreateText("Prompt", left, string.Empty, TextStyle.Body, TextAnchor.UpperLeft, Theme.TextSecondary);
            _submissions = Factory.CreateText("Submissions", left, string.Empty, TextStyle.Caption);
            _options = Factory.CreateRect("Options", left);
            UIFactory.AddVerticalLayout(_options.gameObject, new RectOffset(0, 0, 4, 4), 10f);

            RectTransform right = Factory.CreateRect("Advice", _columns);
            UIFactory.AddVerticalLayout(right.gameObject, new RectOffset(0, 0, 0, 0), 0f);
            UIFactory.GetOrAddLayoutElement(right.gameObject).preferredWidth = AssistantColumnWidth;
            _assistant = AssistantCardView.Create(
                Factory,
                right,
                false,
                () => Manager.SetAdviceStance(AdviceStance.Accepted),
                () => Manager.SetAdviceStance(AdviceStance.Challenged));

            _debrief = Factory.CreateRect("Debrief", body);
            UIFactory.AddVerticalLayout(_debrief.gameObject, new RectOffset(0, 0, 0, 0), Theme.Spacing);
            _debriefText = Factory.CreateText("Text", _debrief, string.Empty, TextStyle.Body);
            _restartButton = Factory.CreateButton("Restart", _debrief, "Start the next case", ButtonVariant.Primary, OnRestart, 260f);

            Factory.CreateButton("Return", footer, "Return to courtroom", ButtonVariant.Ghost, Manager.Back, 220f);
        }

        protected override void OnShown()
        {
            CaseSession session = Manager.Session;
            DecisionSequence decisions = Manager.Decisions;
            DecisionPointDefinition current = decisions.Current;

            if (current == null)
            {
                ShowDebrief(session);
                return;
            }

            _columns.gameObject.SetActive(true);
            _debrief.gameObject.SetActive(false);
            session?.MarkPresented();

            string progress = $"DECISION {decisions.CurrentIndex + 1} OF {decisions.Count} · {DecisionPointCatalog.ShortName(current.Kind).ToUpperInvariant()}";
            SetHeader(progress, current.Title);
            _prompt.text = current.Prompt;

            CaseDecisionPoint point = session?.CurrentPoint;
            string submissions = point != null ? FormatSubmissions(point) : string.Empty;
            _submissions.text = submissions;
            _submissions.gameObject.SetActive(submissions.Length > 0);

            SetOptionCount(current.Options.Count);
            for (int i = 0; i < current.Options.Count; i++)
            {
                _optionIds[i] = current.Options[i].Id;
                UIFactory.SetButtonLabel(_optionButtons[i], current.Options[i].Label);
            }

            if (session != null)
            {
                _assistant.Present(session);
            }
            else
            {
                _assistant.SetMessage("No case is loaded, so this preview runs on placeholder content without advice.");
                _assistant.SetFootnote(string.Empty);
            }
        }

        private void ShowDebrief(CaseSession session)
        {
            _columns.gameObject.SetActive(false);
            _debrief.gameObject.SetActive(true);
            SetHeader("SESSION COMPLETE · DEBRIEF", session != null ? session.Case.Title : "All rulings recorded");
            _debriefText.text = session != null
                ? BuildDebrief(session)
                : "You have ruled on every decision point in this preview session.";
        }

        private static string FormatSubmissions(CaseDecisionPoint point)
        {
            var text = new StringBuilder();
            if (point.ProsecutionSubmission.Length > 0)
            {
                text.Append("<b>Prosecution:</b> ").Append(point.ProsecutionSubmission);
            }

            if (point.DefenceSubmission.Length > 0)
            {
                if (text.Length > 0)
                {
                    text.Append("\n\n");
                }

                text.Append("<b>Defence:</b> ").Append(point.DefenceSubmission);
            }

            return text.ToString();
        }

        private static string BuildDebrief(CaseSession session)
        {
            var text = new StringBuilder();
            text.Append(Debrief.Headline(session.Summary)).Append('\n');
            foreach (DebriefItem item in Debrief.Build(session))
            {
                text.Append("\n<b>").Append(item.Title).Append(":</b> ").Append(item.RulingLabel)
                    .Append(" (").Append(Debrief.QualityLabel(item.Quality)).Append(")\n");
                if (item.Quality != OptionQuality.Sound && !string.IsNullOrEmpty(item.SoundLabel))
                {
                    text.Append("Sound ruling: ").Append(item.SoundLabel).Append(". ");
                }

                text.Append(item.Feedback).Append('\n');
                text.Append(Debrief.DescribeOutcome(item));
                if (item.AdviceFlaw != FlawKind.None)
                {
                    text.Append(" <i>").Append(FlawKinds.Describe(item.AdviceFlaw)).Append(' ').Append(item.FlawExplanation).Append("</i>");
                }

                text.Append('\n');
            }

            return text.ToString().TrimEnd();
        }

        private void SetOptionCount(int count)
        {
            while (_optionButtons.Count < count)
            {
                int index = _optionButtons.Count;
                Button button = Factory.CreateButton($"Option_{index}", _options, string.Empty, ButtonVariant.Secondary, () => OnOptionSelected(index));
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
            OnShown();
            Selectable first = DefaultSelection;
            if (first != null)
            {
                first.Select();
            }
        }
    }
}
