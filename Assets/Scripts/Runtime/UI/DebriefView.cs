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
    /// The end-of-session debrief: a headline, two trust-score cards (over-trust and under-trust) and one card
    /// per ruling showing how it was graded and whether the AI's suggestion was planted as flawed.
    /// </summary>
    public sealed class DebriefView
    {
        private readonly UIFactory _factory;
        private readonly Text _headline;
        private readonly ScoreCard _overTrust;
        private readonly ScoreCard _underTrust;
        private readonly RectTransform _rulings;
        private readonly List<RulingCard> _cards = new List<RulingCard>();

        public DebriefView(UIFactory factory, RectTransform parent)
        {
            _factory = factory;
            UITheme theme = factory.Theme;
            Root = factory.CreateRect("DebriefView", parent);
            UIFactory.AddVerticalLayout(Root.gameObject, new RectOffset(0, 0, 0, 0), theme.Spacing);

            _headline = factory.CreateText("Headline", Root, string.Empty, TextStyle.Heading);

            RectTransform scores = factory.CreateRect("Scores", Root);
            UIFactory.AddHorizontalLayout(scores.gameObject, new RectOffset(0, 0, 0, 0), theme.Spacing, TextAnchor.UpperLeft);
            _overTrust = new ScoreCard(factory, scores, "OVER-TRUST", "Flawed suggestions you accepted");
            _underTrust = new ScoreCard(factory, scores, "UNDER-TRUST", "Sound suggestions you rejected");

            _rulings = factory.CreateRect("Rulings", Root);
            UIFactory.AddVerticalLayout(_rulings.gameObject, new RectOffset(0, 0, 0, 0), 10f);
        }

        public RectTransform Root { get; }

        /// <summary>All visible text, for tests and accessibility.</summary>
        public string Text
        {
            get
            {
                var text = new StringBuilder(_headline.text).Append('\n');
                text.Append(_overTrust.Describe()).Append('\n').Append(_underTrust.Describe()).Append('\n');
                foreach (RulingCard card in _cards)
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
            _headline.text = Debrief.Headline(summary);
            _overTrust.Set(Debrief.OverTrustPercent(summary), Debrief.OverTrustLine(summary), summary.OverTrust);
            _underTrust.Set(Debrief.UnderTrustPercent(summary), Debrief.UnderTrustLine(summary), summary.UnderTrust);

            IReadOnlyList<DebriefItem> items = Debrief.Build(session);
            while (_cards.Count < items.Count)
            {
                _cards.Add(new RulingCard(_factory, _rulings));
            }

            for (int i = 0; i < _cards.Count; i++)
            {
                _cards[i].Root.gameObject.SetActive(i < items.Count);
                if (i < items.Count)
                {
                    _cards[i].Set(items[i]);
                }
            }
        }

        /// <summary>Colour for a trust score: high is amber (warning), moderate is ivory, low is green.</summary>
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

        private sealed class ScoreCard
        {
            private readonly UITheme _theme;
            private readonly string _label;
            private readonly Text _value;
            private readonly Text _line;

            public ScoreCard(UIFactory factory, RectTransform parent, string label, string caption)
            {
                _theme = factory.Theme;
                _label = label;
                Image card = factory.CreatePanel(label, parent, _theme.SurfaceRaised);
                UIFactory.AddVerticalLayout(card.gameObject, new RectOffset(20, 20, 16, 16), 4f);
                UIFactory.GetOrAddLayoutElement(card.gameObject).flexibleWidth = 1f;
                factory.AddOutline(card.rectTransform, _theme.Border);
                factory.CreateText("Label", card.rectTransform, label, TextStyle.Overline);
                _value = factory.CreateText("Value", card.rectTransform, "–", TextStyle.Display);
                factory.CreateText("Caption", card.rectTransform, caption, TextStyle.Caption);
                _line = factory.CreateText("Line", card.rectTransform, string.Empty, TextStyle.Body);
            }

            public void Set(string percent, string line, double? rate)
            {
                _value.text = percent;
                _value.color = BandColour(_theme, rate);
                _line.text = line;
            }

            public string Describe() => $"{_label} {_value.text}: {_line.text}";
        }

        private sealed class RulingCard
        {
            private readonly UITheme _theme;
            private readonly Text _title;
            private readonly Image _chip;
            private readonly Text _chipText;
            private readonly Text _body;

            public RulingCard(UIFactory factory, RectTransform parent)
            {
                _theme = factory.Theme;
                Image card = factory.CreatePanel("Ruling", parent, _theme.SurfaceRaised);
                Root = card.rectTransform;
                UIFactory.AddVerticalLayout(card.gameObject, new RectOffset(18, 18, 12, 12), 6f);
                factory.AddOutline(card.rectTransform, _theme.Border);

                RectTransform header = factory.CreateRect("Header", card.rectTransform);
                UIFactory.AddHorizontalLayout(header.gameObject, new RectOffset(0, 0, 0, 0), 10f);
                _title = factory.CreateText("Title", header, string.Empty, TextStyle.Body);
                _title.fontStyle = FontStyle.Bold;
                UIFactory.GetOrAddLayoutElement(_title.gameObject).flexibleWidth = 1f;
                _chip = factory.CreateChip("Quality", header, string.Empty, _theme.Surface, _theme.TextPrimary, out _chipText).GetComponent<Image>();
                _body = factory.CreateText("Body", card.rectTransform, string.Empty, TextStyle.Caption, TextAnchor.UpperLeft, _theme.TextPrimary);
                _body.supportRichText = true;
            }

            public RectTransform Root { get; }

            public void Set(DebriefItem item)
            {
                _title.text = $"{item.Title}: {item.RulingLabel}";
                _chipText.text = Debrief.QualityLabel(item.Quality).ToUpperInvariant();
                Color tone = item.Quality == OptionQuality.Sound ? _theme.Success : item.Quality == OptionQuality.Defensible ? _theme.Accent : _theme.Warning;
                _chipText.color = tone;
                tone.a = 0.16f;
                _chip.color = tone;

                var body = new StringBuilder();
                if (item.Quality != OptionQuality.Sound && !string.IsNullOrEmpty(item.SoundLabel))
                {
                    body.Append("Sound ruling: ").Append(UIFactory.Plain(item.SoundLabel)).Append(". ");
                }

                body.Append(UIFactory.Plain(item.Feedback)).Append('\n').Append(Debrief.DescribeOutcome(item));
                if (item.AdviceFlaw != FlawKind.None)
                {
                    body.Append("\n<i>").Append(FlawKinds.Describe(item.AdviceFlaw)).Append(' ').Append(UIFactory.Plain(item.FlawExplanation)).Append("</i>");
                }

                _body.text = body.ToString();
            }

            public string Describe() => $"{_title.text} ({_chipText.text})\n{_body.text}";
        }
    }
}
