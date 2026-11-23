using System.Collections.Generic;
using System.Text;
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
        private DebriefView _debriefView;
        private Button _restartButton;

        public override ScreenId Id => ScreenId.DecisionPoint;

        /// <summary>The debrief text currently shown, for tests.</summary>
        public string DebriefText => _debriefView != null && _debriefView.Root.gameObject.activeSelf ? _debriefView.Text : (_debriefText != null ? _debriefText.text : string.Empty);

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
            _submissions.supportRichText = true;
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
            _debriefView = new DebriefView(Factory, _debrief);
            _debriefText = Factory.CreateText("Text", _debrief, string.Empty, TextStyle.Body);
            RectTransform debriefActions = Factory.CreateRect("DebriefActions", _debrief);
            UIFactory.AddHorizontalLayout(debriefActions.gameObject, new RectOffset(0, 0, 4, 0), 12f, TextAnchor.MiddleLeft);
            Factory.CreateButton("ChooseCase", debriefActions, "Choose another case", ButtonVariant.Primary, () => Manager.Open(ScreenId.CaseSelect), 260f);
            _restartButton = Factory.CreateButton("Restart", debriefActions, "Replay this case", ButtonVariant.Secondary, OnRestart, 220f);

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
            _debriefView.Hide(); // stop the report's 3D picture while a decision is on screen
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
            _debriefView.Root.gameObject.SetActive(session != null);
            _debriefText.gameObject.SetActive(session == null);
            if (session != null)
            {
                _debriefView.Show(session);
            }
            else
            {
                _debriefView.Hide();
                _debriefText.text = "You have ruled on every decision point. No case file was loaded, so there is no debrief.";
            }
        }

        private static string FormatSubmissions(CaseDecisionPoint point)
        {
            var text = new StringBuilder();
            if (point.ProsecutionSubmission.Length > 0)
            {
                text.Append("<b>Prosecution:</b> ").Append(UIFactory.Plain(point.ProsecutionSubmission));
            }

            if (point.DefenceSubmission.Length > 0)
            {
                if (text.Length > 0)
                {
                    text.Append("\n\n");
                }

                text.Append("<b>Defence:</b> ").Append(UIFactory.Plain(point.DefenceSubmission));
            }

            return text.ToString();
        }

        private void SetOptionCount(int count)
        {
            while (_optionButtons.Count < count)
            {
                int index = _optionButtons.Count;
                Button button = Factory.CreateButton($"Option_{index}", _options, string.Empty, ButtonVariant.Secondary, () => OnOptionSelected(index));

                // Case files can have long option labels: wrap them and shrink the text a little if needed.
                Text label = button.GetComponentInChildren<Text>();
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.resizeTextForBestFit = true;
                label.resizeTextMaxSize = label.fontSize;
                label.resizeTextMinSize = 14;
                RectTransform labelRect = label.rectTransform;
                labelRect.offsetMin = new Vector2(16f, 4f);
                labelRect.offsetMax = new Vector2(-16f, -4f);
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
