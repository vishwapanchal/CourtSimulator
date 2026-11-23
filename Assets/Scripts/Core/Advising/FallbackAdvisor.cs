using System;
using System.Threading;
using System.Threading.Tasks;

namespace CourtSim.Core.Advising
{
    /// <summary>
    /// Tries <c>primary</c> and, if it fails for any reason other than cancellation, answers with <c>fallback</c>.
    /// A training session never stalls because the network or the API is unavailable.
    /// </summary>
    public sealed class FallbackAdvisor : IAdvisor
    {
        private readonly IAdvisor _primary;
        private readonly IAdvisor _fallback;

        public FallbackAdvisor(IAdvisor primary, IAdvisor fallback)
        {
            _primary = primary ?? throw new ArgumentNullException(nameof(primary));
            _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        }

        /// <summary>Raised with the primary advisor's error each time the fallback is used.</summary>
        public event Action<Exception> PrimaryFailed;

        /// <summary>The most recent primary failure, or null.</summary>
        public Exception LastFailure { get; private set; }

        public async Task<Advice> GetAdviceAsync(AdviceRequest request, CancellationToken cancellationToken)
        {
            try
            {
                Advice advice = await _primary.GetAdviceAsync(request, cancellationToken);

                // A blank reply would leave the assistant panel empty, so it counts as a failure.
                if (advice == null || string.IsNullOrWhiteSpace(advice.Rationale))
                {
                    throw new AdvisorException("The AI model returned empty advice.");
                }

                return advice;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LastFailure = ex;
                PrimaryFailed?.Invoke(ex);
                return await _fallback.GetAdviceAsync(request, cancellationToken);
            }
        }
    }
}
