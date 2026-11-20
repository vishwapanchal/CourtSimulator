using System;
using System.Collections.Generic;
using CourtSim.Core.Cases;

namespace CourtSim.Core.Advising
{
    /// <summary>
    /// Which decision points of a session get flawed advice, and how each is flawed. Decided up front by
    /// game logic, never by the model, so scoring and debriefs rest on authored ground truth.
    /// </summary>
    public sealed class AdvicePlan
    {
        private readonly FlawKind[] _flaws;

        public AdvicePlan(IReadOnlyList<FlawKind> flaws)
        {
            if (flaws == null)
            {
                throw new ArgumentNullException(nameof(flaws));
            }

            _flaws = new FlawKind[flaws.Count];
            for (int i = 0; i < flaws.Count; i++)
            {
                _flaws[i] = flaws[i];
            }
        }

        public int Count => _flaws.Length;

        /// <summary>The flaw planned for decision point <paramref name="index"/>, or None.</summary>
        public FlawKind FlawAt(int index)
        {
            return index >= 0 && index < _flaws.Length ? _flaws[index] : FlawKind.None;
        }

        public int FlawedCount
        {
            get
            {
                int count = 0;
                foreach (FlawKind flaw in _flaws)
                {
                    if (flaw != FlawKind.None)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>A plan with sound advice everywhere.</summary>
        public static AdvicePlan AllSound(int count)
        {
            return new AdvicePlan(new FlawKind[count]);
        }
    }

    /// <summary>Builds <see cref="AdvicePlan"/>s. The flawed count and flaw weights are the difficulty knobs Phase 3 adapts.</summary>
    public static class AdvicePlanner
    {
        /// <summary>
        /// Flaws exactly <paramref name="flawedCount"/> decision points (clamped to those with authored flawed
        /// variants), chosen at random, with each flaw kind drawn from that point's authored variants.
        /// </summary>
        /// <param name="flawWeights">Optional relative weight per flaw kind; missing kinds weigh 1, zero excludes a kind where possible.</param>
        public static AdvicePlan Create(CaseFile caseFile, int flawedCount, Random random, IReadOnlyDictionary<FlawKind, double> flawWeights = null)
        {
            if (caseFile == null)
            {
                throw new ArgumentNullException(nameof(caseFile));
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            int count = caseFile.DecisionPoints.Count;
            var flaws = new FlawKind[count];
            var candidates = new List<int>();
            for (int i = 0; i < count; i++)
            {
                if (caseFile.DecisionPoints[i].AvailableFlaws().Count > 0)
                {
                    candidates.Add(i);
                }
            }

            int toFlaw = Math.Max(0, Math.Min(flawedCount, candidates.Count));
            for (int n = 0; n < toFlaw; n++)
            {
                int pick = random.Next(candidates.Count);
                int index = candidates[pick];
                candidates.RemoveAt(pick);
                flaws[index] = PickFlaw(caseFile.DecisionPoints[index].AvailableFlaws(), random, flawWeights);
            }

            return new AdvicePlan(flaws);
        }

        private static FlawKind PickFlaw(IReadOnlyList<FlawKind> available, Random random, IReadOnlyDictionary<FlawKind, double> weights)
        {
            double total = 0;
            foreach (FlawKind flaw in available)
            {
                total += WeightOf(flaw, weights);
            }

            if (total <= 0)
            {
                return available[random.Next(available.Count)];
            }

            double roll = random.NextDouble() * total;
            foreach (FlawKind flaw in available)
            {
                roll -= WeightOf(flaw, weights);
                if (roll < 0)
                {
                    return flaw;
                }
            }

            return available[available.Count - 1];
        }

        private static double WeightOf(FlawKind flaw, IReadOnlyDictionary<FlawKind, double> weights)
        {
            return weights != null && weights.TryGetValue(flaw, out double weight) ? Math.Max(0, weight) : 1;
        }
    }
}
