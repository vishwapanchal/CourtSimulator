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
    /// 1. a 3D picture of the result (see <see cref="ResultsDiorama"/>) with the big score and a grade on top of it,
    /// 2. two plain counts about the AI: wrong advice caught, and good advice ignored,
    /// 3. one colour-coded card per phase, side by side like the pillars in the picture: what you ruled, the best
    ///    ruling if yours was not, what the AI said, and why.
    /// </summary>
    /// <remarks>Result colours (emerald, amber, coral) come from <see cref="ResultsDiorama"/>'s palette so each card
    /// matches its pillar in the picture.</remarks>
    public sealed class DebriefView
    {
        /// <summary>Height of the 3D picture. Its width is the card's inner width (1240 − 2 × 28 = 1184), so 4:1.</summary>
        private const float HeroHeight = 296f;
        private const float ScoreBlockWidth = 440f;

        private readonly UIFactory _factory;
        private readonly ResultsDiorama _diorama;
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

            // The hero: a rounded frame that clips (Mask) the live 3D picture, with the score written over its left side.
            Image frame = factory.CreatePanel("Hero", Root, ResultsDiorama.Backdrop);
            LayoutElement frameLayout = UIFactory.GetOrAddLayoutElement(frame.gameObject);
            frameLayout.minHeight = HeroHeight;
            frameLayout.preferredHeight = HeroHeight;
            frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;

            // The RawImage shows the texture the diorama's own camera draws into every frame.
            RectTransform pictureRect = factory.CreateRect("Diorama", frame.rectTransform);
            UIFactory.Stretch(pictureRect);
            var picture = pictureRect.gameObject.AddComponent<RawImage>();
            picture.raycastTarget = false;
            _diorama = pictureRect.gameObject.AddComponent<ResultsDiorama>();
            factory.AddOutline(frame.rectTransform, theme.Border);

            RectTransform scoreBlock = factory.CreateRect("ScoreBlock", frame.rectTransform);
            UIFactory.Place(scoreBlock, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(ScoreBlockWidth, HeroHeight - 40f));
            UIFactory.AddVerticalLayout(scoreBlock.gameObject, new RectOffset(0, 0, 0, 0), 2f, TextAnchor.MiddleLeft);
            factory.CreateText("Label", scoreBlock, "YOUR SCORE", TextStyle.Overline);
            _score = factory.CreateText("Score", scoreBlock, "-", TextStyle.Display);
            _score.fontSize = Mathf.RoundToInt(_score.fontSize * 1.4f);
            AddShadow(_score, theme);
            _grade = factory.CreateText("Grade", scoreBlock, string.Empty, TextStyle.Heading);
            _grade.fontSize = Mathf.RoundToInt(_grade.fontSize * 1.35f);
            AddShadow(_grade, theme);
            _headline = factory.CreateText("Headline", scoreBlock, string.Empty, TextStyle.Caption, TextAnchor.UpperLeft, theme.TextSecondary);

            RectTransform tiles = factory.CreateRect("AiTrust", Root);
            UIFactory.AddHorizontalLayout(tiles.gameObject, new RectOffset(0, 0, 0, 0), 12f, TextAnchor.UpperLeft);
            _caught = new CountTile(factory, tiles, "WRONG AI ADVICE YOU CAUGHT");
            _ignored = new CountTile(factory, tiles, "GOOD AI ADVICE YOU IGNORED");

            // Phase cards sit side by side, one under each pillar of the picture, all stretched to the same height.
            _cards = factory.CreateRect("Phases", Root);
            HorizontalLayoutGroup row = UIFactory.AddHorizontalLayout(_cards.gameObject, new RectOffset(0, 0, 0, 0), 12f, TextAnchor.UpperLeft);
            row.childForceExpandHeight = true;
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
            Color tone = GradeColour(summary);
            _score.color = tone;
            _grade.text = Grade(summary);
            _grade.color = tone;
            _headline.text = Debrief.Headline(summary);

            _caught.Set(summary.FlawedAdviceSeen == 0 ? "None" : $"{summary.FlawsCaught} of {summary.FlawedAdviceSeen}",
                summary.FlawedAdviceSeen == 0 ? "The AI gave no planted wrong advice this time." : "Catching wrong advice is the key skill.",
                summary.FlawedAdviceSeen == 0 ? theme.TextSecondary : summary.FlawsCaught == summary.FlawedAdviceSeen ? ResultsDiorama.Emerald : ResultsDiorama.Amber);
            _ignored.Set(summary.SoundAdviceSeen == 0 ? "None" : $"{summary.SoundAdviceOverridden} of {summary.SoundAdviceSeen}",
                summary.SoundAdviceSeen == 0 ? "The AI gave no correct advice this time." : "Good advice is worth following.",
                summary.SoundAdviceSeen == 0 ? theme.TextSecondary : summary.SoundAdviceOverridden == 0 ? ResultsDiorama.Emerald : ResultsDiorama.Amber);

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

            _diorama.Show(session);
        }

        /// <summary>Stops the 3D picture while the report is not on screen.</summary>
        public void Hide()
        {
            _diorama.Hide();
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

        private static Color GradeColour(SessionSummary summary)
        {
            if (summary.Rulings > 0 && summary.SoundRulings == summary.Rulings)
            {
                return ResultsDiorama.Emerald;
            }

            return summary.SoundRulings * 2 >= summary.Rulings ? ResultsDiorama.Gold : ResultsDiorama.Coral;
        }

        /// <summary>A soft drop shadow keeps big text readable over the 3D picture.</summary>
        private static void AddShadow(Text text, UITheme theme)
        {
            var shadow = text.gameObject.AddComponent<Shadow>();
            Color colour = theme.Backdrop;
            colour.a = 0.8f;
            shadow.effectColor = colour;
            shadow.effectDistance = new Vector2(0f, -3f);
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
            // A sentence ends at ". " unless the word before the full stop is an abbreviation such as "Rs.", "s."
            // (section), "No." or "v." (versus), which are common in Indian legal text.
            int searchFrom = 0;
            while (true)
            {
                int end = text.IndexOf(". ", searchFrom, System.StringComparison.Ordinal);
                if (end <= 0)
                {
                    return text;
                }

                int wordStart = text.LastIndexOf(' ', end - 1) + 1;
                string word = text.Substring(wordStart, end - wordStart);
                if (!IsAbbreviation(word))
                {
                    return text.Substring(0, end + 1);
                }

                searchFrom = end + 2;
            }
        }

        private static bool IsAbbreviation(string word)
        {
            switch (word.TrimStart('(').ToLowerInvariant())
            {
                case "rs":
                case "s":
                case "ss":
                case "no":
                case "nos":
                case "v":
                case "vs":
                case "dr":
                case "mr":
                case "mrs":
                case "sh":
                case "smt":
                case "i.e":
                case "e.g":
                    return true;
                default:
                    return word.Length == 1; // initials such as "B." in "B. R. Ambedkar"
            }
        }

        /// <summary>A coloured bar drawn as a UI rectangle, turned by <paramref name="angle"/> degrees.</summary>
        private static void Bar(UIFactory factory, RectTransform parent, Vector2 position, Vector2 size, float angle)
        {
            Image bar = factory.CreatePanel("Bar", parent, factory.Theme.OnAccent, PanelShape.Rounded, 3f);
            UIFactory.Place(bar.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);
            bar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
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
                UIFactory.AddHorizontalLayout(tile.gameObject, new RectOffset(18, 18, 10, 10), 14f, TextAnchor.MiddleLeft);
                UIFactory.GetOrAddLayoutElement(tile.gameObject).flexibleWidth = 1f;
                factory.AddOutline(tile.rectTransform, theme.Border);

                _value = factory.CreateText("Value", tile.rectTransform, "-", TextStyle.Title, TextAnchor.MiddleLeft);
                _value.horizontalOverflow = HorizontalWrapMode.Overflow;

                RectTransform words = factory.CreateRect("Words", tile.rectTransform);
                UIFactory.AddVerticalLayout(words.gameObject, new RectOffset(0, 0, 0, 0), 2f);
                UIFactory.GetOrAddLayoutElement(words.gameObject).flexibleWidth = 1f;
                factory.CreateText("Label", words, label, TextStyle.Overline);
                _meaning = factory.CreateText("Meaning", words, string.Empty, TextStyle.Caption, TextAnchor.UpperLeft, theme.TextSecondary);
            }

            public void Set(string value, string meaning, Color colour)
            {
                _value.text = value;
                _value.color = colour;
                _meaning.text = meaning;
            }

            public string Describe() => $"{_label}: {_value.text}. {_meaning.text}";
        }

        /// <summary>
        /// One phase: a coloured stripe and badge (tick, dash or cross), the result word, what you ruled, the best
        /// ruling (only if yours wasn't it), what the AI said, and why.
        /// </summary>
        private sealed class PhaseCard
        {
            private readonly Image _tint;
            private readonly Image _stripe;
            private readonly Image _badge;
            private readonly RectTransform _tick;
            private readonly RectTransform _dash;
            private readonly RectTransform _cross;
            private readonly Text _result;
            private readonly Text _phase;
            private readonly Text _title;
            private readonly Text _body;

            public PhaseCard(UIFactory factory, RectTransform parent)
            {
                UITheme theme = factory.Theme;
                Image card = factory.CreatePanel("Phase", parent, theme.SurfaceRaised);
                Root = card.rectTransform;
                LayoutElement layout = UIFactory.GetOrAddLayoutElement(card.gameObject);

                // Same small base width and equal stretch, so the three cards are always equally wide.
                layout.minWidth = 10f;
                layout.preferredWidth = 10f;
                layout.flexibleWidth = 1f;
                UIFactory.AddVerticalLayout(card.gameObject, new RectOffset(28, 18, 14, 16), 6f);

                // A faint wash of the result colour over the card, and a solid stripe down its left edge.
                _tint = factory.CreatePanel("Tint", card.rectTransform, Color.clear);
                UIFactory.Stretch(_tint.rectTransform);
                UIFactory.GetOrAddLayoutElement(_tint.gameObject).ignoreLayout = true;
                _stripe = factory.CreatePanel("Stripe", card.rectTransform, Color.clear, PanelShape.Rounded, 3f);
                UIFactory.GetOrAddLayoutElement(_stripe.gameObject).ignoreLayout = true;
                RectTransform stripe = _stripe.rectTransform;
                stripe.anchorMin = new Vector2(0f, 0f);
                stripe.anchorMax = new Vector2(0f, 1f);
                stripe.pivot = new Vector2(0f, 0.5f);
                stripe.offsetMin = new Vector2(10f, 16f);
                stripe.offsetMax = new Vector2(16f, -16f);
                factory.AddOutline(card.rectTransform, theme.Border);

                RectTransform top = factory.CreateRect("Top", card.rectTransform);
                UIFactory.AddHorizontalLayout(top.gameObject, new RectOffset(0, 0, 0, 0), 12f, TextAnchor.MiddleLeft);

                // The badge's tick and cross are drawn from rotated bars, so they never depend on the font having ✓ or ✗.
                _badge = factory.CreatePanel("Badge", top, Color.white, PanelShape.Circle);
                UIFactory.SetFixedSize(_badge.gameObject, 44f, 44f);
                _tick = Glyph(factory, _badge.rectTransform, "Tick");
                Bar(factory, _tick, new Vector2(-7f, -4f), new Vector2(6f, 13f), 45f);
                Bar(factory, _tick, new Vector2(4f, 1f), new Vector2(6f, 25f), -37.9f);
                _dash = Glyph(factory, _badge.rectTransform, "Dash");
                Bar(factory, _dash, Vector2.zero, new Vector2(22f, 6f), 0f);
                _cross = Glyph(factory, _badge.rectTransform, "Cross");
                Bar(factory, _cross, Vector2.zero, new Vector2(6f, 24f), 45f);
                Bar(factory, _cross, Vector2.zero, new Vector2(6f, 24f), -45f);

                RectTransform names = factory.CreateRect("Names", top);
                UIFactory.AddVerticalLayout(names.gameObject, new RectOffset(0, 0, 0, 0), 0f);
                UIFactory.GetOrAddLayoutElement(names.gameObject).flexibleWidth = 1f;
                _result = factory.CreateText("Result", names, string.Empty, TextStyle.Heading);
                _phase = factory.CreateText("Phase", names, string.Empty, TextStyle.Overline, TextAnchor.UpperLeft, theme.TextSecondary);

                _title = factory.CreateText("Title", card.rectTransform, string.Empty, TextStyle.Body);
                _title.fontStyle = FontStyle.Bold;
                _body = factory.CreateText("Body", card.rectTransform, string.Empty, TextStyle.Caption, TextAnchor.UpperLeft, theme.TextPrimary);
                _body.supportRichText = true;
            }

            public RectTransform Root { get; }

            public void Set(string phase, DebriefItem item)
            {
                _phase.text = phase;
                _title.text = item.Title;

                bool sound = item.Quality == OptionQuality.Sound;
                bool acceptable = item.Quality == OptionQuality.Defensible;
                _result.text = sound ? "CORRECT" : acceptable ? "ACCEPTABLE" : "WRONG";
                Color tone = ResultsDiorama.ResultColour(item.Quality);
                _result.color = tone;
                _badge.color = tone;
                _stripe.color = tone;
                tone.a = 0.09f;
                _tint.color = tone;
                _tick.gameObject.SetActive(sound);
                _dash.gameObject.SetActive(acceptable);
                _cross.gameObject.SetActive(!sound && !acceptable);

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

            public string Describe() => $"{_phase.text} {_title.text} [{_result.text}]\n{_body.text}";

            private static RectTransform Glyph(UIFactory factory, RectTransform badge, string name)
            {
                RectTransform glyph = factory.CreateRect(name, badge);
                UIFactory.Stretch(glyph);
                return glyph;
            }

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
