using System.Collections.Generic;
using CourtSim.Core.Cases;
using CourtSim.Core.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CourtSim.Runtime.UI.Screens
{
    /// <summary>
    /// Lets the trainee pick a case. Cases are shown in three columns by difficulty (Easy, Medium, Hard); each entry
    /// shows the case title and the main charge. Choosing one starts the session and enters the courtroom.
    /// </summary>
    public sealed class CaseSelectScreen : ModalScreen
    {
        private static readonly string[] ColumnNames = { "EASY", "MEDIUM", "HARD" };

        private readonly List<Button> _entries = new List<Button>();
        private RectTransform[] _columns;
        private Text[] _counts;
        private Button _backButton;
        private int _shownCatalogSize = -1;

        public override ScreenId Id => ScreenId.CaseSelect;

        protected override float CardWidth => 1500f;

        protected override Selectable DefaultSelection => _entries.Count > 0 ? _entries[0] : _backButton;

        protected override void BuildModal(RectTransform body, RectTransform footer)
        {
            SetHeader("CHOOSE A CASE", "Which matter will you hear today?");
            Factory.CreateText("Hint", body, "Each case has three rulings: bail, admissibility of evidence and sentence. Start with Easy if this is your first time.", TextStyle.Body, TextAnchor.UpperLeft, Theme.TextSecondary);

            RectTransform row = Factory.CreateRect("Columns", body);
            UIFactory.AddHorizontalLayout(row.gameObject, new RectOffset(0, 0, 4, 0), 18f, TextAnchor.UpperLeft);
            _columns = new RectTransform[ColumnNames.Length];
            _counts = new Text[ColumnNames.Length];
            for (int i = 0; i < ColumnNames.Length; i++)
            {
                Image column = Factory.CreatePanel(ColumnNames[i], row, Theme.SurfaceRaised);
                UIFactory.AddVerticalLayout(column.gameObject, new RectOffset(14, 14, 14, 14), 8f);
                UIFactory.GetOrAddLayoutElement(column.gameObject).flexibleWidth = 1f;
                Factory.AddOutline(column.rectTransform, Theme.Border);

                RectTransform header = Factory.CreateRect("Header", column.rectTransform);
                UIFactory.AddHorizontalLayout(header.gameObject, new RectOffset(0, 0, 0, 2), 8f);
                Factory.CreateText("Level", header, ColumnNames[i], TextStyle.Overline);
                Factory.CreateSpacer(header);
                _counts[i] = Factory.CreateText("Count", header, string.Empty, TextStyle.Caption, TextAnchor.MiddleRight, Theme.TextSecondary);
                _columns[i] = column.rectTransform;
            }

            _backButton = Factory.CreateButton("Back", footer, "Back", ButtonVariant.Ghost, Manager.Back, 160f);
        }

        protected override void OnShown()
        {
            IReadOnlyList<CaseFile> cases = Manager.CaseCatalog;
            if (cases.Count == _shownCatalogSize)
            {
                return;
            }

            _shownCatalogSize = cases.Count;
            foreach (Button entry in _entries)
            {
                Destroy(entry.gameObject);
            }

            _entries.Clear();
            var perColumn = new int[ColumnNames.Length];
            foreach (CaseFile caseFile in cases)
            {
                int column = Mathf.Clamp(caseFile.Difficulty - 1, 0, ColumnNames.Length - 1);
                perColumn[column]++;
                string charge = caseFile.Charges.Count > 0 ? caseFile.Charges[0] : string.Empty;
                string id = caseFile.Id;
                Button entry = Factory.CreateButton(id, _columns[column], caseFile.Title, ButtonVariant.Secondary, () => Manager.ChooseCase(id));
                UIFactory.GetOrAddLayoutElement(entry.gameObject).preferredHeight = 64f;

                // Two lines per entry: the title in bold and the main charge below it.
                Text label = entry.GetComponentInChildren<Text>();
                label.supportRichText = true;
                label.text = $"<b>{UIFactory.Plain(caseFile.Title)}</b>\n<size={Mathf.RoundToInt(label.fontSize * 0.72f)}>{UIFactory.Plain(Shorten(charge, 60))}</size>";
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.alignment = TextAnchor.MiddleLeft;
                label.rectTransform.offsetMin = new Vector2(16f, 4f);
                label.rectTransform.offsetMax = new Vector2(-12f, -4f);
                _entries.Add(entry);
            }

            for (int i = 0; i < ColumnNames.Length; i++)
            {
                _counts[i].text = perColumn[i] == 1 ? "1 case" : $"{perColumn[i]} cases";
            }
        }

        private static string Shorten(string text, int max)
        {
            return text.Length <= max ? text : text.Substring(0, max - 1).TrimEnd() + "...";
        }
    }
}
