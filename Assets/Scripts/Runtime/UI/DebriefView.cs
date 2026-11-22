using System.Collections.Generic;
using System.Text;
using CourtSim.Core.Advising;
using CourtSim.Core.Cases;
using CourtSim.Core.Logging;
using CourtSim.Core.Sessions;
using UnityEngine;
using UnityEngine.UI;

namespace CourtSim.Runtime.UI
{
    /// <summary>
    /// The results page, kept deliberately simple:
    /// 1. one headline sentence,
    /// 2. three score tiles (sound rulings, over-trust, under-trust), each with one line of meaning,
    /// 3. a table with one row per ruling: your ruling, the sound ruling, what the AI suggested, and the result,
    ///    with a single "why" line underneath.
    /// </summary>
    public sealed class DebriefView
    {
        private const float DecisionWidth = 170f;
        private const float RulingWidth = 260f;
        private const float ResultWidth = 120f;

        private readonly UIFactory _factory;
        private readonly Text _headline;
        private readonly Text _subline;
        private readonly ScoreTile _sound;
        private readonly ScoreTile _overTrust;
        private readonly ScoreTile _underTrust;
        private readonly RectTransform _rows;
        private readonly List<RulingRow> _rowViews = new List<RulingRow>();

        public DebriefView(UIFactory factory, RectTransform parent)
        {
            _factory = factory;
            UITheme theme = factory.Theme;
            Root = factory.CreateRect("DebriefView", parent);
            UIFactory.AddVerticalLayout(Root.gameObject, new RectOffset(0, 0, 0, 0), 12f);

            _headline = factory.CreateText("Headline", Root, string.Empty, TextStyle.Heading);
            _subline = factory.CreateText("Subline", Root, string.Empty, TextStyle.Caption, TextAnchor.UpperLeft, theme.TextSecondary);

            RectTransform tiles = factory.CreateRect("Scores", Root);
            UIFactory.AddHorizontalLayout(tiles.gameObject, new RectOffset(0, 0, 0, 0), 12f, TextAnchor.UpperLeft);
            _sound = new ScoreTile(factory, tiles, "SOUND RULINGS");
            _overTrust = new ScoreTile(factory, tiles, "OVER-TRUST IN AI");
            _underTrust = new ScoreTile(factory, tiles, "UNDER-TRUST IN AI");

            // Table header.
            RectTransform header = factory.CreateRect("TableHeader", Root);
            UIFactory.AddHorizontalLayout(header.gameObject, new RectOffset(16, 16, 6, 0), 12f, TextAnchor.MiddleLeft);
            HeaderCell(header, "DECISION", DecisionWidth);
            HeaderCell(header, "YOUR RULING", RulingWidth);
            HeaderCell(header, "SOUND RULING", RulingWidth);
            HeaderCell(header, "AI SUGGESTED", RulingWidth);
            HeaderCell(header, "RESULT", ResultWidth);

            _rows = factory.CreateRect("Rows", Root);
            UIFactory.AddVerticalLayout(_rows.gameObject, new RectOffset(0, 0, 0, 0), 8f);
        }

        public RectTransform Root { get; }

        /// <summary>All visible text, for tests and accessibility.</summary>
        public string Text
        {
            get
            {
                var text = new StringBuilder(_headline.text).Append('\n').Append(_subline.text).Append('\n');
                text.Append(_sound.Describe()).Append('\n').Append(_overTrust.Describe()).Append('\n').Append(_underTrust.Describe()).Append('\n');
                foreach (RulingRow row in _rowViews)
                {
                    if (row.Root.gameObject.activeSelf)
                    {
                        text.Append(row.Describe()).Append('\n');
                    }
                }

                return text.ToString().TrimEnd();
            }
        }

        public void Show(CaseSession session)
        {
            SessionSummary summary = session.Summary;
            UITheme theme = _factory.Theme;
            _headline.text = Debrief.Headline(summary);
            _subline.text = $"Difficulty: {DifficultyName(session.Case.Difficulty)}";

            float soundShare = summary.Rulings == 0 ? 0f : (float)summary.SoundRulings / summary.Rulings;
            _sound.Set($"{summary.SoundRulings} / {summary.Rulings}",
                soundShare >= 0.99f ? "Every ruling was sound." : soundShare >= 0.5f ? "Most rulings were sound." : "Review the sound rulings below.",
                soundShare >= 0.99f ? theme.Success : soundShare >= 0.5f ? theme.TextPrimary : theme.Warning);
            _overTrust.Set(Debrief.OverTrustPercent(summary), ShortTrustLine(summary.OverTrust, "flawed AI advice accepted"), BandColour(theme, summary.OverTrust));
            _underTrust.Set(Debrief.UnderTrustPercent(summary), ShortTrustLine(summary.UnderTrust, "sound AI advice rejected"), BandColour(theme, summary.UnderTrust));

            IReadOnlyList<DebriefItem> items = Debrief.Build(session);
            while (_rowViews.Count < items.Count)
            {
                _rowViews.Add(new RulingRow(_factory, _rows));
            }

            for (int i = 0; i < _rowViews.Count; i++)
            {
                _rowViews[i].Root.gameObject.SetActive(i < items.Count);
                if (i < items.Count)
                {
                    _rowViews[i].Set(items[i]);
                }
            }
        }

        public static string DifficultyName(int difficulty)
        {
            return difficulty <= 1 ? "Easy" : difficulty == 2 ? "Medium" : "Hard";
        }

        private static string ShortTrustLine(double? rate, string what)
        {
            if (!rate.HasValue)
            {
                return "Not measured this session.";
            }

            string band = Debrief.Band(rate.Value);
            return $"{char.ToUpperInvariant(band[0])}{band.Substring(1)}: {what}.";
        }

        private static Color BandColour(UITheme theme, double? rate)
        {
            if (!rate.HasValue)
            {
                return theme.TextSecondary;
            }

            switch (Debrief.Band(rate.Value))
            {
                case "high":
                    return theme.Warning;
                case "moderate":
                    return theme.TextPrimary;
                default:
                    return theme.Success;
            }
        }

        /// <summary>The first sentence of a longer explanation, so each row stays on one or two lines.</summary>
        private static string FirstSentence(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            // Drop internal factor codes such as "(F4)" that the local model sometimes copies from its prompt.
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s*\(F\d+\)", string.Empty);
            int end = text.IndexOf(". ", System.StringComparison.Ordinal);
            return end > 0 ? text.Substring(0, end + 1) : text;
        }

        private void HeaderCell(RectTransform parent, string label, float width)
        {
            Text cell = _factory.CreateText(label, parent, label, TextStyle.Overline);
            LayoutElement layout = UIFactory.GetOrAddLayoutElement(cell.gameObject);
            layout.preferredWidth = width;
            layout.minWidth = width;
        }

        private sealed class ScoreTile
        {
            private readonly string _label;
            private readonly Text _value;
            private readonly Text _meaning;

            public ScoreTile(UIFactory factory, RectTransform parent, string label)
            {
                UITheme theme = factory.Theme;
                _label = label;
                Image tile = factory.CreatePanel(label, parent, theme.SurfaceRaised);
                UIFactory.AddVerticalLayout(tile.gameObject, new RectOffset(18, 18, 12, 12), 2f);
                UIFactory.GetOrAddLayoutElement(tile.gameObject).flexibleWidth = 1f;
                factory.AddOutline(tile.rectTransform, theme.Border);
                factory.CreateText("Label", tile.rectTransform, label, TextStyle.Overline);
                _value = factory.CreateText("Value", tile.rectTransform, "-", TextStyle.Title);
                _meaning = factory.CreateText("Meaning", tile.rectTransform, string.Empty, TextStyle.Caption, TextAnchor.UpperLeft, theme.TextSecondary);
            }

            public void Set(string value, string meaning, Color colour)
            {
                _value.text = value;
                _value.color = colour;
                _meaning.text = meaning;
            }

            public string Describe() => $"{_label} {_value.text}: {_meaning.text}";
        }

        private sealed class RulingRow
        {
            private readonly UITheme _theme;
            private readonly Text _decision;
            private readonly Text _yours;
            private readonly Text _sound;
            private readonly Text _ai;
            private readonly Image _chip;
            private readonly Text _chipText;
            private readonly Text _why;

            public RulingRow(UIFactory factory, RectTransform parent)
            {
                _theme = factory.Theme;
                Image card = factory.CreatePanel("Ruling", parent, _theme.SurfaceRaised);
                Root = card.rectTransform;
                UIFactory.AddVerticalLayout(card.gameObject, new RectOffset(16, 16, 10, 10), 6f);
                factory.AddOutline(card.rectTransform, _theme.Border);

                RectTransform cells = factory.CreateRect("Cells", card.rectTransform);
                UIFactory.AddHorizontalLayout(cells.gameObject, new RectOffset(0, 0, 0, 0), 12f, TextAnchor.MiddleLeft);
                _decision = Cell(factory, cells, "Decision", DecisionWidth, TextStyle.Body, true);
                _yours = Cell(factory, cells, "Yours", RulingWidth, TextStyle.Caption, false);
                _sound = Cell(factory, cells, "Sound", RulingWidth, TextStyle.Caption, false);
                _ai = Cell(factory, cells, "AI", RulingWidth, TextStyle.Caption, false);
                RectTransform chipHolder = factory.CreateRect("Result", cells);
                UIFactory.AddHorizontalLayout(chipHolder.gameObject, new RectOffset(0, 0, 0, 0), 0f, TextAnchor.MiddleLeft);
                LayoutElement holder = UIFactory.GetOrAddLayoutElement(chipHolder.gameObject);
                holder.preferredWidth = ResultWidth;
                holder.minWidth = ResultWidth;
                _chip = factory.CreateChip("Chip", chipHolder, string.Empty, _theme.Surface, _theme.TextPrimary, out _chipText).GetComponent<Image>();

                _why = factory.CreateText("Why", card.rectTransform, string.Empty, TextStyle.Caption, TextAnchor.UpperLeft, _theme.TextSecondary);
                _why.supportRichText = true;
            }

            public RectTransform Root { get; }

            public void Set(DebriefItem item)
            {
                _decision.text = item.Title;
                _yours.text = item.RulingLabel;
                _sound.text = item.Quality == OptionQuality.Sound ? "Same as yours" : (item.SoundLabel ?? "-");
                _ai.text = item.AdviceLabel == null ? "No advice" : item.AdviceFlaw == FlawKind.None ? item.AdviceLabel : item.AdviceLabel + "  (flawed)";

                _chipText.text = Debrief.QualityLabel(item.Quality).ToUpperInvariant();
                Color tone = item.Quality == OptionQuality.Sound ? _theme.Success : item.Quality == OptionQuality.Defensible ? _theme.Accent : _theme.Warning;
                _chipText.color = tone;
                tone.a = 0.16f;
                _chip.color = tone;

                // One short "why" line: the reason for the grade, plus the AI's flaw if it had one.
                var why = new StringBuilder("<b>Why:</b> ").Append(UIFactory.Plain(FirstSentence(item.Feedback)));
                if (item.AdviceFlaw != FlawKind.None)
                {
                    why.Append("   <b>AI flaw:</b> ").Append(UIFactory.Plain(FirstSentence(item.FlawExplanation)));
                }

                _why.text = why.ToString();
            }

            public string Describe() => $"{_decision.text}: {_yours.text} | sound: {_sound.text} | AI: {_ai.text} | {_chipText.text}\n{_why.text}";

            private static Text Cell(UIFactory factory, RectTransform parent, string name, float width, TextStyle style, bool bold)
            {
                Text cell = factory.CreateText(name, parent, string.Empty, style);
                if (bold)
                {
                    cell.fontStyle = FontStyle.Bold;
                }

                LayoutElement layout = UIFactory.GetOrAddLayoutElement(cell.gameObject);
                layout.preferredWidth = width;
                layout.minWidth = width;
                return cell;
            }
        }
    }
}
