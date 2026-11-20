using System.Threading;
using System.Threading.Tasks;
using CourtSim.Core.Cases;

namespace CourtSim.Core.Advising
{
    /// <summary>
    /// Offline advisor: returns the case file's authored advice. Deterministic, instant and free, so it backs
    /// tests, sessions without an API key, and any request the Claude advisor fails.
    /// </summary>
    public sealed class ScriptedAdvisor : IAdvisor
    {
        public Task<Advice> GetAdviceAsync(AdviceRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Build(request));
        }

        /// <summary>Synchronous form of <see cref="GetAdviceAsync"/>.</summary>
        public static Advice Build(AdviceRequest request)
        {
            AdviceScript script = request.TargetScript;
            if (script == null)
            {
                throw new AdvisorException($"Case '{request.Case.Id}' has no authored advice for decision point {request.PointIndex}.");
            }

            return new Advice(
                request.Point.Kind,
                script.RecommendedOptionId,
                script.Rationale,
                script.CitedFactorIds,
                script.Confidence,
                script.Flaw,
                script.FlawExplanation,
                AdviceSource.Scripted);
        }
    }
}
