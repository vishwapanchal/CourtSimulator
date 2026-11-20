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
                request.Detail == ExplanationDetail.Detailed ? WithFactors(script, request) : script.Rationale,
                script.CitedFactorIds,
                script.Confidence,
                script.Flaw,
                script.FlawExplanation,
                AdviceSource.Scripted);
        }

        // Detailed explanations spell out each factor the advice relies on, in plain words.
        private static string WithFactors(AdviceScript script, AdviceRequest request)
        {
            var text = new System.Text.StringBuilder(script.Rationale).Append("\n\nWhat I relied on:");
            foreach (string id in script.CitedFactorIds)
            {
                CaseFactor factor = request.Point.FindFactor(id);
                if (factor != null)
                {
                    text.Append("\n- ").Append(factor.Text);
                }
            }

            return text.ToString();
        }
    }
}
