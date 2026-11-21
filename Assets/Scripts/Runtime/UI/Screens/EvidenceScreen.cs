using CourtSim.Core.Cases;
using CourtSim.Core.Decisions;
using CourtSim.Core.Logging;
using CourtSim.Core.Sessions;
using CourtSim.Core.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CourtSim.Runtime.UI.Screens
{
    /// <summary>Shows the case's contested exhibit: what it is, how it was obtained, the objection and its ruling status.</summary>
    public sealed class EvidenceScreen : ModalScreen
    {
        private Button _closeButton;
        private Text _mediaLabel;
        private Text _tenderedBy;
        private Text _provenance;
        private Text _objection;
        private Text _status;

        public override ScreenId Id => ScreenId.Evidence;

        /// <summary>The ruling status line, for tests.</summary>
        public string StatusText => _status != null ? _status.text : string.Empty;

        protected override float CardWidth => 760f;

        protected override Selectable DefaultSelection => _closeButton;

        protected override void BuildModal(RectTransform body, RectTransform footer)
        {
            Image media = Factory.CreatePanel("Media", body, Theme.SurfaceRaised);
            UIFactory.GetOrAddLayoutElement(media.gameObject).preferredHeight = 160f;
            Factory.AddOutline(media.rectTransform, Theme.Border);
            _mediaLabel = Factory.CreateText("Label", media.rectTransform, string.Empty, TextStyle.Body, TextAnchor.MiddleCenter);
            UIFactory.Stretch(_mediaLabel.rectTransform, 24f, 24f, 16f, 16f);

            _tenderedBy = AddDetail(body, "Tendered by");
            _provenance = AddDetail(body, "Provenance");
            _objection = AddDetail(body, "Objection");
            _status = AddDetail(body, "Status");

            _closeButton = Factory.CreateButton("Close", footer, "Close", ButtonVariant.Primary, Manager.Back, 160f);
        }

        protected override void OnShown()
        {
            CaseSession session = Manager.Session;
            Exhibit exhibit = FindContestedExhibit(session, out int pointIndex);
            if (exhibit == null)
            {
                SetHeader("EXHIBIT · PLACEHOLDER", "Evidence viewer");
                _mediaLabel.text = "No case is loaded. Exhibits appear here when a case file is playing.";
                _tenderedBy.text = "-";
                _provenance.text = "-";
                _objection.text = "-";
                _status.text = "-";
                return;
            }

            SetHeader($"EXHIBIT {exhibit.Id} · {session.Case.Title.ToUpperInvariant()}", exhibit.Title);
            _mediaLabel.text = exhibit.Description;
            _tenderedBy.text = exhibit.TenderedBy;
            _provenance.text = exhibit.Provenance;
            _objection.text = exhibit.Objection.Length > 0 ? exhibit.Objection : "None";
            _status.text = DescribeStatus(session, pointIndex);
        }

        private static Exhibit FindContestedExhibit(CaseSession session, out int pointIndex)
        {
            pointIndex = -1;
            if (session == null)
            {
                return null;
            }

            for (int i = 0; i < session.Case.DecisionPoints.Count; i++)
            {
                CaseDecisionPoint point = session.Case.DecisionPoints[i];
                if (point.ExhibitId != null)
                {
                    pointIndex = i;
                    return session.Case.FindExhibit(point.ExhibitId);
                }
            }

            return session.Case.Exhibits.Count > 0 ? session.Case.Exhibits[0] : null;
        }

        private static string DescribeStatus(CaseSession session, int pointIndex)
        {
            foreach (DecisionLogEntry entry in session.Entries)
            {
                if (entry.Record.SequenceIndex == pointIndex)
                {
                    DecisionOption option = session.Case.DecisionPoints[pointIndex].Definition.FindOption(entry.Record.OptionId);
                    return "Ruled: " + (option != null ? option.Label : entry.Record.OptionId);
                }
            }

            return "Awaiting your ruling on admissibility at the bench";
        }

        private Text AddDetail(RectTransform parent, string label)
        {
            RectTransform row = Factory.CreateRect(label, parent);
            UIFactory.AddHorizontalLayout(row.gameObject, new RectOffset(0, 0, 0, 0), 12f, TextAnchor.UpperLeft);
            Text labelText = Factory.CreateText("Label", row, label, TextStyle.Body, TextAnchor.UpperLeft, Theme.TextSecondary);
            LayoutElement labelLayout = UIFactory.GetOrAddLayoutElement(labelText.gameObject);
            labelLayout.preferredWidth = 160f;
            labelLayout.minWidth = 160f; // stops long values squeezing the label into one letter per line
            Text valueText = Factory.CreateText("Value", row, string.Empty, TextStyle.Body);
            UIFactory.GetOrAddLayoutElement(valueText.gameObject).flexibleWidth = 1f;
            return valueText;
        }
    }
}
