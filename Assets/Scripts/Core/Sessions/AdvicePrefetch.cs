using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CourtSim.Core.Adaptive;
using CourtSim.Core.Advising;
using CourtSim.Core.Cases;

namespace CourtSim.Core.Sessions
{
    /// <summary>
    /// Prepares a session's advice in the background, one request after another, so it is ready before the
    /// judge reaches each ruling. Pair it with <see cref="CachingAdvisor"/>: the session's own request for a
    /// point then reuses the prepared (or still-running) reply instead of asking the model again.
    /// </summary>
    public static class AdvicePrefetch
    {
        /// <summary>
        /// The requests the session is likely to make, most useful first: one per decision point in hearing
        /// order, then any alternatives. With adaptation on, a flawed point's flaw kind is only chosen when the
        /// judge gets there, so every authored flaw of that point is prepared, the likelier kind first.
        /// </summary>
        /// <param name="adaptive">The session's adaptive controller, or null if adaptation is off.</param>
        /// <param name="detailAt">The explanation detail per point (the session's <c>DetailAt</c>), or null for standard.</param>
        public static IReadOnlyList<AdviceRequest> PlanRequests(CaseFile caseFile, AdvicePlan plan, AdaptiveController adaptive = null, Func<int, ExplanationDetail> detailAt = null)
        {
            if (caseFile == null)
            {
                throw new ArgumentNullException(nameof(caseFile));
            }

            plan = plan ?? AdvicePlan.AllSound(caseFile.DecisionPoints.Count);
            var first = new List<AdviceRequest>();
            var alternatives = new List<AdviceRequest>();
            for (int i = 0; i < caseFile.DecisionPoints.Count; i++)
            {
                CaseDecisionPoint point = caseFile.DecisionPoints[i];
                if (point.Advice.Count == 0)
                {
                    // Nothing authored: no advisor can answer this point, so there is nothing to prepare.
                    continue;
                }

                ExplanationDetail detail = detailAt != null ? detailAt(i) : ExplanationDetail.Standard;
                FlawKind planned = plan.FlawAt(i);
                List<FlawKind> flaws = planned == FlawKind.None || adaptive == null
                    ? new List<FlawKind> { planned }
                    : LikeliestFirst(point.AvailableFlaws(), adaptive.Subtlety);

                for (int n = 0; n < flaws.Count; n++)
                {
                    (n == 0 ? first : alternatives).Add(new AdviceRequest(caseFile, i, flaws[n], null, detail));
                }
            }

            first.AddRange(alternatives);
            return first;
        }

        /// <summary>
        /// Asks <paramref name="advisor"/> for each request in turn. A failed request is reported and skipped;
        /// cancelling stops before the next request. Returns how many requests produced advice.
        /// </summary>
        public static async Task<int> RunAsync(IAdvisor advisor, IReadOnlyList<AdviceRequest> requests, CancellationToken cancellationToken, Action<AdviceRequest, Exception> onFailed = null)
        {
            if (advisor == null)
            {
                throw new ArgumentNullException(nameof(advisor));
            }

            int prepared = 0;
            foreach (AdviceRequest request in requests ?? Array.Empty<AdviceRequest>())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    Advice advice = await advisor.GetAdviceAsync(request, cancellationToken);
                    if (advice != null)
                    {
                        prepared++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    onFailed?.Invoke(request, ex);
                }
            }

            return prepared;
        }

        // At subtlety 0 the authored order is as likely as any; above it the subtler kinds are favoured.
        private static List<FlawKind> LikeliestFirst(IReadOnlyList<FlawKind> available, int subtlety)
        {
            var flaws = new List<FlawKind>(available);
            if (subtlety > 0)
            {
                flaws.Sort((a, b) => Rank(b).CompareTo(Rank(a)));
            }

            return flaws;
        }

        private static int Rank(FlawKind flaw)
        {
            for (int i = 0; i < AdaptiveController.ObviousToSubtle.Count; i++)
            {
                if (AdaptiveController.ObviousToSubtle[i] == flaw)
                {
                    return i;
                }
            }

            return 0;
        }
    }
}
