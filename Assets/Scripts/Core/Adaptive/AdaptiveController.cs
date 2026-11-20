using System;
using System.Collections.Generic;
using CourtSim.Core.Advising;
using CourtSim.Core.Json;
using CourtSim.Core.Logging;

namespace CourtSim.Core.Adaptive
{
    /// <summary>
    /// Adapts the training to one trainee using two rules:
    /// 1. Two flawed suggestions accepted in a row → later flaws get subtler.
    /// 2. Two sound suggestions rejected in a row → the next suggestion explains itself in more detail.
    /// It only remembers how the trainee treated each suggestion (a rolling history), so it is easy to save,
    /// test and explain.
    /// </summary>
    public sealed class AdaptiveController
    {
        public const int MaxSubtlety = 3;
        private const int StreakToAdapt = 2;
        private const int HistoryLength = 20;

        /// <summary>Flaw kinds from most obvious to least obvious. Higher subtlety favours the end of this list.</summary>
        public static readonly IReadOnlyList<FlawKind> ObviousToSubtle = new[]
        {
            FlawKind.MisreadFact,     // contradicts the record: checkable against the facts
            FlawKind.MisstatedLaw,    // wrong test: checkable if you know the law
            FlawKind.ImproperFactor,  // right facts, illegitimate reason
            FlawKind.OmittedFactor,   // nothing said is wrong; something is missing
        };

        private readonly List<AdviceOutcome> _history = new List<AdviceOutcome>();
        private int _acceptedFlawsInARow;
        private int _rejectedSoundInARow;

        /// <summary>0 = flaw kinds picked evenly; each level shifts the odds toward subtler kinds.</summary>
        public int Subtlety { get; private set; }

        /// <summary>True when the next suggestion should come with a detailed explanation.</summary>
        public bool DetailedExplanationPending { get; private set; }

        /// <summary>The most recent outcomes, oldest first (at most 20).</summary>
        public IReadOnlyList<AdviceOutcome> History => _history;

        /// <summary>
        /// Updates the state with one ruling. Returns a plain-language note if a rule fired, otherwise null.
        /// </summary>
        public string Record(AdviceOutcome outcome)
        {
            _history.Add(outcome);
            if (_history.Count > HistoryLength)
            {
                _history.RemoveAt(0);
            }

            // Each streak counts only its own kind of event; anything else breaks it.
            _acceptedFlawsInARow = outcome == AdviceOutcome.MisledByFlaw ? _acceptedFlawsInARow + 1 : ResetIf(outcome, AdviceOutcome.NoAdvice, _acceptedFlawsInARow);
            _rejectedSoundInARow = outcome == AdviceOutcome.OverrodeSound ? _rejectedSoundInARow + 1 : ResetIf(outcome, AdviceOutcome.NoAdvice, _rejectedSoundInARow);

            if (_acceptedFlawsInARow >= StreakToAdapt)
            {
                _acceptedFlawsInARow = 0;
                if (Subtlety < MaxSubtlety)
                {
                    Subtlety++;
                    return $"Two flawed suggestions accepted in a row: flaws are now subtler (level {Subtlety}).";
                }
            }

            if (_rejectedSoundInARow >= StreakToAdapt)
            {
                _rejectedSoundInARow = 0;
                DetailedExplanationPending = true;
                return "Two sound suggestions rejected in a row: the next suggestion will explain its reasoning in detail.";
            }

            return null;
        }

        /// <summary>Picks the flaw kind for a flawed suggestion from those the case authored, weighted by subtlety.</summary>
        public FlawKind ChooseFlaw(IReadOnlyList<FlawKind> available, Random random)
        {
            if (available == null || available.Count == 0)
            {
                return FlawKind.None;
            }

            var weights = new Dictionary<FlawKind, double>();
            foreach (FlawKind flaw in available)
            {
                // Rank 0 (most obvious) to 3 (subtlest). At level L a kind weighs (1 + rank) ^ L, so level 0 is even.
                int rank = Math.Max(0, IndexOf(flaw));
                weights[flaw] = Math.Pow(1 + rank, Subtlety);
            }

            double roll = random.NextDouble() * Sum(weights.Values);
            foreach (FlawKind flaw in available)
            {
                roll -= weights[flaw];
                if (roll < 0)
                {
                    return flaw;
                }
            }

            return available[available.Count - 1];
        }

        /// <summary>The detail level for the next suggestion. A detailed explanation is used once, then cleared.</summary>
        public ExplanationDetail TakeExplanationDetail()
        {
            bool detailed = DetailedExplanationPending;
            DetailedExplanationPending = false;
            return detailed ? ExplanationDetail.Detailed : ExplanationDetail.Standard;
        }

        public JsonValue ToJson()
        {
            var history = new List<string>();
            foreach (AdviceOutcome outcome in _history)
            {
                history.Add(LogNames.Outcome(outcome));
            }

            return JsonValue.NewObject()
                .Set("subtlety", Subtlety)
                .Set("detailedExplanationPending", DetailedExplanationPending)
                .Set("acceptedFlawsInARow", _acceptedFlawsInARow)
                .Set("rejectedSoundInARow", _rejectedSoundInARow)
                .Set("history", JsonValue.StringArray(history));
        }

        /// <summary>Restores saved state. Unknown or missing values fall back to a fresh trainee.</summary>
        public static AdaptiveController FromJson(JsonValue json)
        {
            var controller = new AdaptiveController();
            if (json == null || json.Kind != JsonKind.Object)
            {
                return controller;
            }

            controller.Subtlety = Clamp(json.GetNumber("subtlety"), MaxSubtlety);
            controller.DetailedExplanationPending = json.Get("detailedExplanationPending")?.AsBool() ?? false;
            controller._acceptedFlawsInARow = Clamp(json.GetNumber("acceptedFlawsInARow"), StreakToAdapt - 1);
            controller._rejectedSoundInARow = Clamp(json.GetNumber("rejectedSoundInARow"), StreakToAdapt - 1);
            foreach (JsonValue item in json.Get("history")?.Items ?? Array.Empty<JsonValue>())
            {
                if (LogNames.TryParseOutcome(item.AsString(), out AdviceOutcome outcome))
                {
                    controller._history.Add(outcome);
                }
            }

            // Keep only the newest entries, as Record does.
            if (controller._history.Count > HistoryLength)
            {
                controller._history.RemoveRange(0, controller._history.Count - HistoryLength);
            }

            return controller;
        }

        // Clamp in double precision first, so a huge saved number cannot overflow the int cast.
        private static int Clamp(double? value, int max) => (int)Math.Max(0, Math.Min(max, value ?? 0));

        // A ruling without advice says nothing about trust, so it neither extends nor breaks a streak.
        private static int ResetIf(AdviceOutcome outcome, AdviceOutcome keep, int streak) => outcome == keep ? streak : 0;

        private static int IndexOf(FlawKind flaw)
        {
            for (int i = 0; i < ObviousToSubtle.Count; i++)
            {
                if (ObviousToSubtle[i] == flaw)
                {
                    return i;
                }
            }

            return -1;
        }

        private static double Sum(IEnumerable<double> values)
        {
            double total = 0;
            foreach (double value in values)
            {
                total += value;
            }

            return total;
        }
    }
}
