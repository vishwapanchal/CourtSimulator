using System;
using System.Collections.Generic;
using System.Text;
using CourtSim.Core.Advising;
using CourtSim.Core.Cases;
using CourtSim.Core.Logging;

namespace CourtSim.Core.Sessions
{
    /// <summary>What the debrief says about one ruling: how it was graded and what the advice really was.</summary>
    public sealed class DebriefItem
    {
        internal DebriefItem(DecisionLogEntry entry, CaseDecisionPoint point)
        {
            Title = point.Definition.Title;
            RulingLabel = point.Definition.FindOption(entry.Record.OptionId)?.Label ?? entry.Record.OptionId;
            Quality = entry.Quality;
            Feedback = point.Assess(entry.Record.OptionId)?.Feedback ?? string.Empty;
            Outcome = entry.Outcome;
            Advice advice = entry.Advice;
            if (advice != null)
            {
                AdviceLabel = point.Definition.FindOption(advice.RecommendedOptionId)?.Label ?? advice.RecommendedOptionId;
                AdviceFlaw = advice.Flaw;
                FlawExplanation = advice.FlawExplanation;
            }

            foreach (OptionAssessment assessment in point.Assessments)
            {
                if (assessment.Quality == OptionQuality.Sound)
                {
                    SoundLabel = point.Definition.FindOption(assessment.OptionId)?.Label;
                    break;
                }
            }
        }

        public string Title { get; }

        public string RulingLabel { get; }

        public OptionQuality Quality { get; }

        public string Feedback { get; }

        public AdviceOutcome Outcome { get; }

        /// <summary>The ruling the assistant suggested, or null if no advice was shown.</summary>
        public string AdviceLabel { get; }

        public FlawKind AdviceFlaw { get; }

        public string FlawExplanation { get; }

        /// <summary>The ruling graded sound.</summary>
        public string SoundLabel { get; }
    }

    /// <summary>Turns a session's log into debrief items and a plain-text summary.</summary>
    public static class Debrief
    {
        public static IReadOnlyList<DebriefItem> Build(CaseSession session)
        {
            var items = new List<DebriefItem>();
            foreach (DecisionLogEntry entry in session.Entries)
            {
                int index = entry.Record.SequenceIndex;
                if (index >= 0 && index < session.Case.DecisionPoints.Count)
                {
                    items.Add(new DebriefItem(entry, session.Case.DecisionPoints[index]));
                }
            }

            return items;
        }

        /// <summary>One line describing how the ruling related to the advice.</summary>
        public static string DescribeOutcome(DebriefItem item)
        {
            switch (item.Outcome)
            {
                case AdviceOutcome.CaughtFlaw:
                    return $"You did not follow the assistant's flawed suggestion ({item.AdviceLabel}). Well caught.";
                case AdviceOutcome.MisledByFlaw:
                    return $"You followed the assistant's suggestion ({item.AdviceLabel}), but it was deliberately flawed.";
                case AdviceOutcome.TrustedSound:
                    return $"You followed the assistant's suggestion ({item.AdviceLabel}), which was sound.";
                case AdviceOutcome.OverrodeSound:
                    return $"The assistant's suggestion ({item.AdviceLabel}) was sound, and you departed from it.";
                default:
                    return "No advice was available for this ruling.";
            }
        }

        public static string QualityLabel(OptionQuality quality)
        {
            switch (quality)
            {
                case OptionQuality.Sound:
                    return "Sound";
                case OptionQuality.Defensible:
                    return "Defensible";
                default:
                    return "Unsound";
            }
        }

        /// <summary>Plain-English band for a trust score: 50% or more is high, 25% or more moderate, otherwise low.</summary>
        public static string Band(double rate)
        {
            if (rate >= 0.5)
            {
                return "high";
            }

            return rate >= 0.25 ? "moderate" : "low";
        }

        /// <summary>Over-trust as a percentage, or "–" if no flawed suggestions were shown.</summary>
        public static string OverTrustPercent(SessionSummary summary) => Percent(summary.OverTrust);

        public static string UnderTrustPercent(SessionSummary summary) => Percent(summary.UnderTrust);

        /// <summary>One line explaining over-trust, e.g. "You accepted 3 of 4 flawed suggestions: high over-trust."</summary>
        public static string OverTrustLine(SessionSummary summary)
        {
            if (!summary.OverTrust.HasValue)
            {
                return "No flawed suggestions were shown, so over-trust could not be measured.";
            }

            string band = Band(summary.OverTrust.Value);
            string advice = band == "low" ? "you are checking the AI's advice well." : "test each suggestion against the record before relying on it.";
            return $"You accepted {summary.TimesMisled} of {summary.FlawedAdviceSeen} flawed suggestion{Plural(summary.FlawedAdviceSeen)}: {band} over-trust, so {advice}";
        }

        /// <summary>One line explaining under-trust, e.g. "You rejected 1 of 2 sound suggestions: high under-trust."</summary>
        public static string UnderTrustLine(SessionSummary summary)
        {
            if (!summary.UnderTrust.HasValue)
            {
                return "No sound suggestions were shown, so under-trust could not be measured.";
            }

            string band = Band(summary.UnderTrust.Value);
            string advice = band == "low" ? "you use good advice when it is right." : "good advice is being dismissed; ask what the record actually supports.";
            return $"You rejected {summary.SoundAdviceOverridden} of {summary.SoundAdviceSeen} sound suggestion{Plural(summary.SoundAdviceSeen)}: {band} under-trust, so {advice}";
        }

        private static string Percent(double? rate) => rate.HasValue ? $"{Math.Round(rate.Value * 100):0}%" : "–";

        private static string Plural(int count) => count == 1 ? string.Empty : "s";

        /// <summary>A short paragraph summarising the session.</summary>
        public static string Headline(SessionSummary summary)
        {
            var text = new StringBuilder();
            text.Append($"{summary.SoundRulings} of {summary.Rulings} rulings were sound");
            if (summary.DefensibleRulings > 0)
            {
                text.Append($", {summary.DefensibleRulings} defensible");
            }

            text.Append('.');
            if (summary.FlawedAdviceSeen > 0)
            {
                text.Append($" You caught {summary.FlawsCaught} of {summary.FlawedAdviceSeen} flawed suggestion{(summary.FlawedAdviceSeen == 1 ? string.Empty : "s")}.");
            }
            else if (summary.SoundAdviceSeen > 0)
            {
                text.Append(" Every suggestion you saw this session was sound.");
            }
            else
            {
                text.Append(" No advice was shown this session.");
            }

            return text.ToString();
        }
    }
}
