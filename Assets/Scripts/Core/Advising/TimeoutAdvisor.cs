using System;
using System.Threading;
using System.Threading.Tasks;

namespace CourtSim.Core.Advising
{
    /// <summary>
    /// Gives up waiting for <c>inner</c> after a fixed time and throws <see cref="AdvisorException"/>, so a
    /// <see cref="FallbackAdvisor"/> around it shows the case file's own advice instead of an empty panel.
    /// With a <see cref="CachingAdvisor"/> inside, the model keeps writing in the background and its reply is
    /// cached for the next time this decision point is played.
    /// </summary>
    public sealed class TimeoutAdvisor : IAdvisor
    {
        /// <summary>Long enough for a GPU-loaded model (a few seconds), short enough that a trainee isn't left waiting.</summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(25);

        private readonly IAdvisor _inner;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;

        /// <param name="delay">Waits for the given time; tests pass one that finishes at once.</param>
        public TimeoutAdvisor(IAdvisor inner, TimeSpan timeout, Func<TimeSpan, CancellationToken, Task> delay = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "The timeout must be positive.");
            }

            Timeout = timeout;
            _delay = delay ?? ((time, token) => Task.Delay(time, token));
        }

        public TimeSpan Timeout { get; }

        public async Task<Advice> GetAdviceAsync(AdviceRequest request, CancellationToken cancellationToken)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                Task<Advice> work = _inner.GetAdviceAsync(request, linked.Token);
                if (!work.IsCompleted)
                {
                    Task timer = _delay(Timeout, linked.Token);
                    Task first = await Task.WhenAny(work, timer);
                    if (first != work)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        linked.Cancel();
                        _ = work.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                        throw new AdvisorException($"The AI model did not answer within {Timeout.TotalSeconds:0} seconds.");
                    }

                    // Stop the timer now that the answer is in.
                    linked.Cancel();
                }

                return await work;
            }
        }
    }
}
