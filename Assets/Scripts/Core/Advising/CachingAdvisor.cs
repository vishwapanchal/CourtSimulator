using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CourtSim.Core.Advising
{
    /// <summary>
    /// Wraps a model advisor so the same advice is never generated twice:
    /// 1. Advice already generated (this run, or saved on disk by an earlier run) is returned at once.
    /// 2. A request for advice that is already being generated waits for that same reply instead of sending a
    ///    second request. This is how the session's own request reuses the background prefetch.
    /// 3. Fresh advice is saved to the <see cref="IAdviceStore"/> for next time.
    /// Generation keeps running even if the caller stops waiting (for example after a timeout), so the reply
    /// still lands in the cache.
    /// </summary>
    public sealed class CachingAdvisor : IAdvisor, IDisposable
    {
        private readonly IAdvisor _inner;
        private readonly IAdviceStore _store;
        private readonly Func<DateTime> _clock;
        private readonly object _gate = new object();
        private readonly Dictionary<string, Task<Advice>> _inFlight = new Dictionary<string, Task<Advice>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Advice> _remembered = new Dictionary<string, Advice>(StringComparer.Ordinal);
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();

        /// <param name="model">Model name, part of every cache key so switching models never shows another model's text.</param>
        /// <param name="store">Persistent cache, or null to remember advice only while the game runs.</param>
        public CachingAdvisor(IAdvisor inner, string model, IAdviceStore store = null, Func<DateTime> clock = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            Model = model ?? string.Empty;
            _store = store;
            _clock = clock ?? (() => DateTime.UtcNow);
        }

        /// <summary>Raised with the cache key when advice is served without asking the model.</summary>
        public event Action<string> CacheHit;

        /// <summary>Raised with the cache key and advice when the model has written new advice.</summary>
        public event Action<string, Advice> Generated;

        public string Model { get; }

        /// <summary>How many requests were answered from the cache.</summary>
        public int HitCount { get; private set; }

        /// <summary>How many requests were actually sent to the model.</summary>
        public int GeneratedCount { get; private set; }

        public Task<Advice> GetAdviceAsync(AdviceRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            cancellationToken.ThrowIfCancellationRequested();
            string key = AdviceCacheKey.For(Model, request);
            Task<Advice> shared;
            Advice ready = null;
            lock (_gate)
            {
                if (!_inFlight.TryGetValue(key, out shared))
                {
                    ready = FindCached(key, request);
                    if (ready == null)
                    {
                        GeneratedCount++;
                        shared = GenerateAsync(key, request);
                        if (!shared.IsCompleted)
                        {
                            _inFlight[key] = shared;
                        }
                    }
                    else
                    {
                        HitCount++;
                    }
                }
            }

            if (ready != null)
            {
                CacheHit?.Invoke(key);
                return Task.FromResult(ready);
            }

            return WaitAsync(shared, cancellationToken);
        }

        /// <summary>Stops any generation still running. Later requests fail.</summary>
        public void Dispose()
        {
            _shutdown.Cancel();
        }

        private Advice FindCached(string key, AdviceRequest request)
        {
            if (_remembered.TryGetValue(key, out Advice remembered))
            {
                return remembered;
            }

            string stored = null;
            try
            {
                stored = _store?.Load(key);
            }
            catch (Exception)
            {
                // A broken cache must never stop a session; behave as if nothing was saved.
            }

            // A damaged or out-of-date entry is ignored and the advice is generated again.
            if (stored != null && AdviceCacheEntry.TryRead(stored, key, request, out Advice cached))
            {
                _remembered[key] = cached;
                return cached;
            }

            return null;
        }

        private async Task<Advice> GenerateAsync(string key, AdviceRequest request)
        {
            try
            {
                // The shared token (not the caller's) so a caller giving up doesn't throw away the reply.
                Advice advice = await _inner.GetAdviceAsync(request, _shutdown.Token);
                if (advice == null || string.IsNullOrWhiteSpace(advice.Rationale))
                {
                    throw new AdvisorException("The AI model returned empty advice.");
                }

                lock (_gate)
                {
                    _remembered[key] = advice;
                }

                // Only model-written text is worth keeping; the authored advice is always available anyway.
                if (_store != null && advice.Source != AdviceSource.Scripted)
                {
                    try
                    {
                        _store.Save(key, AdviceCacheEntry.ToJson(key, advice, _clock()));
                    }
                    catch (Exception)
                    {
                        // Not being able to save only means the next run asks the model again.
                    }
                }

                Generated?.Invoke(key, advice);
                return advice;
            }
            finally
            {
                lock (_gate)
                {
                    _inFlight.Remove(key);
                }
            }
        }

        // Lets one caller stop waiting without cancelling the shared generation.
        private static async Task<Advice> WaitAsync(Task<Advice> shared, CancellationToken cancellationToken)
        {
            if (shared.IsCompleted || !cancellationToken.CanBeCanceled)
            {
                return await shared;
            }

            // If nobody is left waiting when it fails, reading the error here stops .NET reporting it as unobserved.
            _ = shared.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            var cancelled = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
            {
                Task first = await Task.WhenAny(shared, cancelled.Task);
                if (first != shared)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }

            return await shared;
        }
    }
}
