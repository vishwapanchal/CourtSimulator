using System.Collections.Generic;
using System.Text;
using CourtSim.Core.Cases;
using CourtSim.Core.Logging;
using CourtSim.Core.Sessions;
using UnityEngine;
using UnityEngine.UI;

namespace CourtSim.Runtime.UI
{
    /// <summary>
    /// The final report, written to be read in a few seconds:
    /// 1. a big score ("2 of 3 rulings were sound") with a one-word grade,
    /// 2. two plain counts about the AI: wrong advice caught, and good advice ignored,
    /// 3. one card per phase saying what you ruled, the best ruling if yours was not, what the AI said, and why.
    /// </summary>
    public sealed class DebriefView
    {
        private readonly UIFactory _factory;
        private readonly Text _score;
        private readonly Text _grade;
        private readonly Text _headline;
        private readonly CountTile _caught;
        private readonly CountTile _ignored;
        private readonly RectTransform _cards;
        private readonly List<PhaseCard> _cardViews = new List<PhaseCard>();

        public DebriefView(UIFactory factory, RectTransform parent)
        {
            _factory = factory;
            UITheme theme = factory.Theme;
            Root = factory.CreateRect("DebriefView", parent);
            UIFactory.AddVerticalLayout(Root.gameObject, new RectOffset(0, 0, 0, 0), 12f);

            // Score row: "2 / 3" big, the grade beside it, and the one-sentence headline below.
            RectTransform scoreRow = factory.CreateRect("ScoreRow", Root);
            UIFactory.AddHorizontalLayout(scoreRow.gameObject, new RectOffset(0, 0, 0, 0), 16f, TextAnchor.MiddleLeft);
            _score = factory.CreateText("Score", scoreRow, "-", TextStyle.Title);
            _score.fontSize = Mathf.RoundToInt(_score.fontSize * 1.6f);
            _grade = factory.CreateText("Grade", scoreRow, string.Empty, TextStyle.Heading);
            _headline = factory.CreateText("Headline", Root, string.Empty, TextStyle.Body, TextAnchor.UpperLeft, theme.TextSecondary);

            RectTransform tiles = factory.CreateRect("AiTrust", Root);
            UIFactory.AddHorizontalLayout(tiles.gameObject, new RectOffset(0, 0, 0, 0), 12f, TextAnchor.UpperLeft);
            _caught = new CountTile(factory, tiles, "WRONG AI ADVICE YOU CAUGHT");
            _ignored = new CountTile(factory, tiles, "GOOD AI ADVICE YOU IGNORED");

            _cards = factory.CreateRect("Phases", Root);
            UIFactory.AddVerticalLayout(_cards.gameObject, new RectOffset(0, 0, 0, 0), 8f);
        }

        public RectTransform Root { get; }

        /// <summary>All visible text, for tests and accessibility.</summary>
        public string Text
        {
            get
            {
                var text = new StringBuilder();
                text.Append(_score.text).Append(' ').Append(_grade.text).Append('\n').Append(_headline.text).Append('\n');
                text.Append(_caught.Describe()).Append('\n').Append(_ignored.Describe()).Append('\n');
                foreach (PhaseCard card in _cardViews)
                {
                    if (card.Root.gameObject.activeSelf)
                    {
                        text.Append(card.Describe()).Append('\n');
                    }
                }

                return text.ToString().TrimEnd();
            }
        }

        public void Show(CaseSession session)
        {
            SessionSummary summary = session.Summary;
            UITheme theme = _factory.Theme;

            _score.text = $"{summary.SoundRulings} / {summary.Rulings}";
            Color tone = GradeColour(theme, summary);
            _score.color = tone;
            _grade.text = Grade(summary);
            _grade.color = tone;
            _headline.text = Debrief.Headline(summary);

            _caught.Set(summary.FlawedAdviceSeen == 0 ? "None given" : $"{summary.FlawsCaught} of {summary.FlawedAdviceSeen}",
                summary.FlawedAdviceSeen == 0 ? "The AI gave no planted wrong advice this time." : "Catching wrong advice is the key skill.",
                summary.FlawedAdviceSeen == 0 ? theme.TextSecondary : summary.FlawsCaught == summary.FlawedAdviceSeen ? theme.Success : theme.Warning);
            _ignored.Set(summary.SoundAdviceSeen == 0 ? "None given" : $"{summary.SoundAdviceOverridden} of {summary.SoundAdviceSeen}",
                summary.SoundAdviceSeen == 0 ? "The AI gave no correct advice this time." : "Good advice is worth following.",
                summary.SoundAdviceSeen == 0 ? theme.TextSecondary : summary.SoundAdviceOverridden == 0 ? theme.Success : theme.Warning);

            IReadOnlyList<DebriefItem> items = Debrief.Build(session);
            while (_cardViews.Count < items.Count)
            {
                _cardViews.Add(new PhaseCard(_factory, _cards));
            }

            for (int i = 0; i < _cardViews.Count; i++)
            {
                bool used = i < items.Count;
                _cardViews[i].Root.gameObject.SetActive(used);
                if (used)
                {
                    var kind = session.Case.DecisionPoints[i].Definition.Kind;
                    _cardViews[i].Set($"{HearingPhase.Number(i, items.Count)}  ·  {HearingPhase.Title(kind)}", items[i]);
                }
            }
        }

        public static string DifficultyName(int difficulty)
        {
            return difficulty <= 1 ? "Easy" : difficulty == 2 ? "Medium" : "Hard";
        }

        /// <summary>A one-word verdict on the session.</summary>
        public static string Grade(SessionSummary summary)
        {
            if (summary.Rulings == 0)
            {
                return string.Empty;
            }

            if (summary.SoundRulings == summary.Rulings)
            {
                return "Excellent judgment";
            }

            if (summary.SoundRulings * 2 >= summary.Rulings)
            {
                return "Good judgment";
            }

            return summary.SoundRulings > 0 ? "Needs practice" : "Review the law and try again";
        }

        private static Color GradeColour(UITheme theme, SessionSummary summary)
        {
            if (summary.Rulings > 0 && summary.SoundRulings == summary.Rulings)
            {
                return theme.Success;
            }

            return summary.SoundRulings * 2 >= summary.Rulings ? theme.Accent : theme.Warning;
        }

        /// <summary>The first sentence of a longer explanation, so each card stays short.</summary>
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

        private sealed class CountTile
        {
            private readonly string _label;
            private readonly Text _value;
            private readonly Text _meaning;

            public CountTile(UIFactory factory, RectTransform parent, string label)
            {
                UITheme theme = factory.Theme;
                _label = label;
                Image tile = factory.CreatePanel(label, parent, theme.SurfaceRaised);
                UIFactory.AddVerticalLayout(tile.gameObject, new RectOffset(18, 18, 10, 10), 2f);
                UIFactory.GetOrAddLayoutElement(tile.gameObject).flexibleWidth = 1f;
                factory.AddOutline(tile.rectTransform, theme.Border);
                factory.CreateText("Label", tile.rectTransform, label, TextStyle.Overline);
                _value = factory.CreateText("Value", tile.rectTransform, "-", TextStyle.Heading);
                _meaning = factory.CreateText("Meaning", tile.rectTransform, string.Empty, TextStyle.Caption, TextAnchor.UpperLeft, theme.TextSecondary);
            }

            public void Set(string value, string meaning, Color colour)
            {
                _value.text = value;
                _value.color = colour;
                _meaning.text = meaning;
            }

            public string Describe() => $"{_label}: {_value.text}. {_meaning.text}";
        }

        /// <summary>One phase: what you ruled, the best ruling (only if yours wasn't it), what the AI said, and why.</summary>
        private sealed class PhaseCard
        {
            private readonly UITheme _theme;
            private readonly Text _phase;
            private readonly Text _title;
            private readonly Image _chip;
            private readonly Text _chipText;
            private readonly Text _body;

            public PhaseCard(UIFactory factory, RectTransform parent)
            {
                _theme = factory.Theme;
                Image card = factory.CreatePanel("Phase", parent, _theme.SurfaceRaised);
                Root = card.rectTransform;
                UIFactory.AddVerticalLayout(card.gameObject, new RectOffset(18, 18, 10, 12), 4f);
                factory.AddOutline(card.rectTransform, _theme.Border);

                RectTransform top = factory.CreateRect("Top", card.rectTransform);
                UIFactory.AddHorizontalLayout(top.gameObject, new RectOffset(0, 0, 0, 0), 12f, TextAnchor.MiddleLeft);
                RectTransform names = factory.CreateRect("Names", top);
                UIFactory.AddVerticalLayout(names.gameObject, new RectOffset(0, 0, 0, 0), 0f);
                UIFactory.GetOrAddLayoutElement(names.gameObject).flexibleWidth = 1f;
                _phase = factory.CreateText("Phase", names, string.Empty, TextStyle.Overline);
                _title = factory.CreateText("Title", names, string.Empty, TextStyle.Body);
                _title.fontStyle = FontStyle.Bold;
                _chip = factory.CreateChip("Result", top, string.Empty, _theme.Surface, _theme.TextPrimary, out _chipText).GetComponent<Image>();

                _body = factory.CreateText("Body", card.rectTransform, string.Empty, TextStyle.Caption, TextAnchor.UpperLeft, _theme.TextPrimary);
                _body.supportRichText = true;
            }

            public RectTransform Root { get; }

            public void Set(string phase, DebriefItem item)
            {
                _phase.text = phase;
                _title.text = item.Title;

                bool sound = item.Quality == OptionQuality.Sound;
                _chipText.text = sound ? "CORRECT" : item.Quality == OptionQuality.Defensible ? "ACCEPTABLE" : "WRONG";
                Color tone = sound ? _theme.Success : item.Quality == OptionQuality.Defensible ? _theme.Accent : _theme.Warning;
                _chipText.color = tone;
                tone.a = 0.16f;
                _chip.color = tone;

                var body = new StringBuilder();
                body.Append("<b>You ruled:</b>  ").Append(UIFactory.Plain(item.RulingLabel));
                if (!sound && !string.IsNullOrEmpty(item.SoundLabel))
                {
                    body.Append("\n<b>Best ruling:</b>  ").Append(UIFactory.Plain(item.SoundLabel));
                }

                body.Append("\n<b>AI advice:</b>  ").Append(DescribeAdvice(item));
                body.Append("\n<b>Why:</b>  ").Append(UIFactory.Plain(FirstSentence(item.Feedback)));
                _body.text = body.ToString();
            }

            public string Describe() => $"{_phase.text} {_title.text} [{_chipText.text}]\n{_body.text}";

            private static string DescribeAdvice(DebriefItem item)
            {
                switch (item.Outcome)
                {
                    case AdviceOutcome.TrustedSound:
                        return "correct, and you followed it. Well done.";
                    case AdviceOutcome.OverrodeSound:
                        return "correct, but you did not follow it.";
                    case AdviceOutcome.MisledByFlaw:
                        return "<b>wrong</b>, and you followed it. " + UIFactory.Plain(FirstSentence(item.FlawExplanation));
                    case AdviceOutcome.CaughtFlaw:
                        return "wrong, and you caught it. Well done. " + UIFactory.Plain(FirstSentence(item.FlawExplanation));
                    default:
                        return "none was given for this ruling.";
                }
            }
        }
    }
}
