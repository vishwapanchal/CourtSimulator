using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CourtSim.Core.Adaptive;
using CourtSim.Core.Advising;
using CourtSim.Core.Cases;
using CourtSim.Core.Decisions;
using CourtSim.Core.Http;
using CourtSim.Core.Json;
using CourtSim.Core.Logging;
using CourtSim.Core.Ollama;
using CourtSim.Core.Sessions;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    /// <summary>In-memory <see cref="IAdviceStore"/> that counts reads and writes.</summary>
    internal sealed class MemoryAdviceStore : IAdviceStore
    {
        public Dictionary<string, string> Entries { get; } = new Dictionary<string, string>();

        public int Saves { get; private set; }

        public string Load(string key)
        {
            return Entries.TryGetValue(key, out string json) ? json : null;
        }

        public void Save(string key, string json)
        {
            Saves++;
            Entries[key] = json;
        }
    }

    public sealed class AdviceCacheTests
    {
        private const string Model = "qwen3.5:9b";

        private SynchronizationContext _savedContext;

        // Inside Unity the test thread has a context that runs "await" continuations on the main thread later.
        // These tests wait for results synchronously on that same thread, which would deadlock, so they run
        // without a context, exactly as they do in the plain .NET test run.
        [SetUp]
        public void ClearSynchronizationContext()
        {
            _savedContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
        }

        [TearDown]
        public void RestoreSynchronizationContext()
        {
            SynchronizationContext.SetSynchronizationContext(_savedContext);
        }

        private static AdviceRequest Request(int point = 0, FlawKind flaw = FlawKind.None, ExplanationDetail detail = ExplanationDetail.Standard, CaseFile caseFile = null)
        {
            return new AdviceRequest(caseFile ?? TestCases.Load(), point, flaw, null, detail);
        }

        private static Advice LocalAdvice(AdviceRequest request, string rationale = "I would release him on conditions.")
        {
            AdviceScript target = request.TargetScript;
            return new Advice(request.Point.Kind, target.RecommendedOptionId, rationale, target.CitedFactorIds, AdviceConfidence.High, target.Flaw, target.FlawExplanation, AdviceSource.LocalModel);
        }

        private static Advice Get(IAdvisor advisor, AdviceRequest request)
        {
            return advisor.GetAdviceAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        }

        // ---- Keys --------------------------------------------------------------------------------------------

        [Test]
        public void Key_SameRequest_IsStable()
        {
            Assert.That(AdviceCacheKey.For(Model, Request()), Is.EqualTo(AdviceCacheKey.For(Model, Request())));
        }

        [Test]
        public void Key_IsReadableAndSafeAsFileName()
        {
            string key = AdviceCacheKey.For(Model, Request(1, FlawKind.MisstatedLaw));

            Assert.That(key, Does.StartWith("v" + AdviceCacheKey.PromptVersion + "_qwen3.5-9b_test-case_p1-evidenceadmissibility_misstated_law_standard_"));
            Assert.That(key, Does.Match("^[a-z0-9._-]+$"));
        }

        [Test]
        public void Key_DiffersByModelPointFlawAndDetail()
        {
            var keys = new HashSet<string>
            {
                AdviceCacheKey.For(Model, Request()),
                AdviceCacheKey.For("qwen3.5:0.8b", Request()),
                AdviceCacheKey.For(Model, Request(1)),
                AdviceCacheKey.For(Model, Request(0, FlawKind.MisreadFact)),
                AdviceCacheKey.For(Model, Request(0, FlawKind.None, ExplanationDetail.Detailed)),
            };

            Assert.That(keys.Count, Is.EqualTo(5));
        }

        [Test]
        public void Key_IgnoresEarlierRulings()
        {
            CaseFile caseFile = TestCases.Load();
            var withHistory = new AdviceRequest(caseFile, 1, FlawKind.None, new[] { new DecisionRecord(0, DecisionPointKind.Bail, "bail.grant") });

            Assert.That(AdviceCacheKey.For(Model, withHistory), Is.EqualTo(AdviceCacheKey.For(Model, Request(1, caseFile: caseFile))));
        }

        [Test]
        public void Key_EditedCaseText_ChangesFingerprint()
        {
            JsonValue json = TestCases.BuildJson();
            TestCases.PointAt(json, 0).Set("prompt", "The accused applies for bail again.");
            CaseFile edited = TestCases.LoadJson(json.ToJson());

            Assert.That(AdviceCacheKey.For(Model, Request(caseFile: edited)), Is.Not.EqualTo(AdviceCacheKey.For(Model, Request())));
        }

        [Test]
        public void Key_UnauthoredFlaw_UsesTheSoundScriptsKey()
        {
            // Point 0 has no omitted-factor variant, so the request falls back to the sound script and shares its key.
            Assert.That(AdviceCacheKey.For(Model, Request(0, FlawKind.OmittedFactor)), Is.EqualTo(AdviceCacheKey.For(Model, Request())));
        }

        [Test]
        public void Key_NullRequest_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => AdviceCacheKey.For(Model, null));
        }

        // ---- Entries -----------------------------------------------------------------------------------------

        [Test]
        public void Entry_RoundTrip_RestoresAdvice()
        {
            AdviceRequest request = Request(0, FlawKind.MisreadFact);
            string key = AdviceCacheKey.For(Model, request);
            Advice original = LocalAdvice(request, "I would remand him; he has failed to appear before.");

            string json = AdviceCacheEntry.ToJson(key, original, new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc));
            bool read = AdviceCacheEntry.TryRead(json, key, request, out Advice restored);

            Assert.That(read, Is.True);
            Assert.That(restored.RecommendedOptionId, Is.EqualTo("bail.deny"));
            Assert.That(restored.Rationale, Is.EqualTo(original.Rationale));
            Assert.That(restored.CitedFactorIds, Is.EqualTo(new[] { "record" }));
            Assert.That(restored.Confidence, Is.EqualTo(AdviceConfidence.High));
            Assert.That(restored.Flaw, Is.EqualTo(FlawKind.MisreadFact));
            Assert.That(restored.FlawExplanation, Is.EqualTo("Invents a failure to appear."));
            Assert.That(restored.Source, Is.EqualTo(AdviceSource.LocalModel));
            Assert.That(restored.Kind, Is.EqualTo(DecisionPointKind.Bail));
        }

        [TestCase(AdviceSource.Claude)]
        [TestCase(AdviceSource.Scripted)]
        public void Entry_RoundTrip_KeepsSource(AdviceSource source)
        {
            AdviceRequest request = Request();
            string key = AdviceCacheKey.For(Model, request);
            var advice = new Advice(DecisionPointKind.Bail, "bail.grant", "I would release him.", null, AdviceConfidence.Low, FlawKind.None, null, source);

            Assert.That(AdviceCacheEntry.TryRead(AdviceCacheEntry.ToJson(key, advice, DateTime.UtcNow), key, request, out Advice restored), Is.True);
            Assert.That(restored.Source, Is.EqualTo(source));
        }

        [Test]
        public void Entry_DamagedOrForeign_IsRejected()
        {
            AdviceRequest request = Request();
            string key = AdviceCacheKey.For(Model, request);
            string good = AdviceCacheEntry.ToJson(key, LocalAdvice(request), DateTime.UtcNow);

            Assert.That(AdviceCacheEntry.TryRead(null, key, request, out _), Is.False);
            Assert.That(AdviceCacheEntry.TryRead("{ not json", key, request, out _), Is.False);
            Assert.That(AdviceCacheEntry.TryRead("[]", key, request, out _), Is.False);
            Assert.That(AdviceCacheEntry.TryRead(good, "another-key", request, out _), Is.False);
            Assert.That(AdviceCacheEntry.TryRead(good, key, null, out _), Is.False);
            Assert.That(AdviceCacheEntry.TryRead(Edit(good, "schema", "old/0"), key, request, out _), Is.False);
            Assert.That(AdviceCacheEntry.TryRead(Edit(good, "rationale", "   "), key, request, out _), Is.False);
            Assert.That(AdviceCacheEntry.TryRead(Edit(good, "recommendedOptionId", "bail.deny"), key, request, out _), Is.False);
            Assert.That(AdviceCacheEntry.TryRead(Edit(good, "flaw", "misread_fact"), key, request, out _), Is.False);
            Assert.That(AdviceCacheEntry.TryRead(Edit(good, "source", "martian"), key, request, out _), Is.False);
        }

        [Test]
        public void Entry_UnknownFactorsAndConfidence_AreCleanedUp()
        {
            AdviceRequest request = Request();
            string key = AdviceCacheKey.For(Model, request);
            JsonValue json = Parse(AdviceCacheEntry.ToJson(key, LocalAdvice(request), DateTime.UtcNow))
                .Set("citedFactorIds", JsonValue.StringArray(new[] { "ties", "ghost", "ties" }))
                .Set("confidence", "certain");

            Assert.That(AdviceCacheEntry.TryRead(json.ToJson(), key, request, out Advice restored), Is.True);
            Assert.That(restored.CitedFactorIds, Is.EqualTo(new[] { "ties" }));
            Assert.That(restored.Confidence, Is.EqualTo(AdviceConfidence.High), "Falls back to the authored confidence.");
        }

        [Test]
        public void Entry_NullAdvice_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => AdviceCacheEntry.ToJson("k", null, DateTime.UtcNow));
        }

        // ---- CachingAdvisor ----------------------------------------------------------------------------------

        [Test]
        public void Caching_FirstRequest_AsksModelAndSaves()
        {
            var store = new MemoryAdviceStore();
            StubAdvisor model = new StubAdvisor((request, token) => Task.FromResult(LocalAdvice(request)));
            var advisor = new CachingAdvisor(model, Model, store);
            var generated = new List<string>();
            advisor.Generated += (key, advice) => generated.Add(key);

            Advice advice = Get(advisor, Request());

            Assert.That(advice.Source, Is.EqualTo(AdviceSource.LocalModel));
            Assert.That(model.Requests.Count, Is.EqualTo(1));
            Assert.That(store.Saves, Is.EqualTo(1));
            Assert.That(generated, Is.EqualTo(new[] { AdviceCacheKey.For(Model, Request()) }));
            Assert.That(advisor.GeneratedCount, Is.EqualTo(1));
        }

        [Test]
        public void Caching_SecondRequest_IsServedFromMemory()
        {
            StubAdvisor model = new StubAdvisor((request, token) => Task.FromResult(LocalAdvice(request)));
            var advisor = new CachingAdvisor(model, Model);
            int hits = 0;
            advisor.CacheHit += key => hits++;

            Advice first = Get(advisor, Request());
            Task<Advice> second = advisor.GetAdviceAsync(Request(), CancellationToken.None);

            Assert.That(second.IsCompleted, Is.True, "A cache hit returns at once.");
            Assert.That(second.Result, Is.SameAs(first));
            Assert.That(model.Requests.Count, Is.EqualTo(1));
            Assert.That(hits, Is.EqualTo(1));
            Assert.That(advisor.HitCount, Is.EqualTo(1));
        }

        [Test]
        public void Caching_EntryOnDisk_IsUsedWithoutAskingTheModel()
        {
            var store = new MemoryAdviceStore();
            var earlierRun = new CachingAdvisor(new StubAdvisor((request, token) => Task.FromResult(LocalAdvice(request, "Saved last time."))), Model, store);
            Get(earlierRun, Request());

            StubAdvisor model = StubAdvisor.Failing(new InvalidOperationException("must not be called"));
            var advisor = new CachingAdvisor(model, Model, store);
            Advice advice = Get(advisor, Request());

            Assert.That(advice.Rationale, Is.EqualTo("Saved last time."));
            Assert.That(model.Requests, Is.Empty);
        }

        [Test]
        public void Caching_DamagedEntryOnDisk_IsRegenerated()
        {
            var store = new MemoryAdviceStore();
            store.Entries[AdviceCacheKey.For(Model, Request())] = "{ broken";
            StubAdvisor model = new StubAdvisor((request, token) => Task.FromResult(LocalAdvice(request, "Fresh.")));
            var advisor = new CachingAdvisor(model, Model, store);

            Advice advice = Get(advisor, Request());

            Assert.That(advice.Rationale, Is.EqualTo("Fresh."));
            Assert.That(model.Requests.Count, Is.EqualTo(1));
            Assert.That(AdviceCacheEntry.TryRead(store.Entries[AdviceCacheKey.For(Model, Request())], AdviceCacheKey.For(Model, Request()), Request(), out _), Is.True);
        }

        [Test]
        public void Caching_RequestWhileGenerating_SharesTheSameReply()
        {
            var pending = new TaskCompletionSource<Advice>();
            var model = new StubAdvisor((request, token) => pending.Task);
            var advisor = new CachingAdvisor(model, Model);

            Task<Advice> prefetch = advisor.GetAdviceAsync(Request(), CancellationToken.None);
            Task<Advice> session = advisor.GetAdviceAsync(Request(), CancellationToken.None);
            pending.SetResult(LocalAdvice(Request()));

            Assert.That(model.Requests.Count, Is.EqualTo(1), "The second request must not reach the model.");
            Assert.That(session.Result, Is.SameAs(prefetch.Result));
        }

        [Test]
        public void Caching_CallerStopsWaiting_GenerationStillFillsTheCache()
        {
            var store = new MemoryAdviceStore();
            var pending = new TaskCompletionSource<Advice>();
            var model = new StubAdvisor((request, token) => pending.Task);
            var advisor = new CachingAdvisor(model, Model, store);
            using (var cancellation = new CancellationTokenSource())
            {
                Task<Advice> waiting = advisor.GetAdviceAsync(Request(), cancellation.Token);
                cancellation.Cancel();

                Assert.That(waiting.IsCanceled, Is.True);
            }

            pending.SetResult(LocalAdvice(Request(), "Arrived late."));

            Assert.That(store.Saves, Is.EqualTo(1));
            Assert.That(Get(advisor, Request()).Rationale, Is.EqualTo("Arrived late."));
            Assert.That(model.Requests.Count, Is.EqualTo(1));
        }

        [Test]
        public void Caching_Failure_IsNotCachedAndIsRetried()
        {
            int calls = 0;
            var model = new StubAdvisor((request, token) => ++calls == 1
                ? Task.FromException<Advice>(new AdvisorException("Ollama down"))
                : Task.FromResult(LocalAdvice(request)));
            var store = new MemoryAdviceStore();
            var advisor = new CachingAdvisor(model, Model, store);

            Assert.Throws<AdvisorException>(() => Get(advisor, Request()));
            Advice retry = Get(advisor, Request());

            Assert.That(retry, Is.Not.Null);
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(store.Saves, Is.EqualTo(1));
        }

        [TestCase("")]
        [TestCase("   \n ")]
        public void Caching_BlankAdvice_IsAFailure(string rationale)
        {
            var store = new MemoryAdviceStore();
            var model = new StubAdvisor((request, token) => Task.FromResult(LocalAdvice(request, rationale)));
            var advisor = new CachingAdvisor(model, Model, store);

            Assert.Throws<AdvisorException>(() => Get(advisor, Request()));
            Assert.That(store.Saves, Is.EqualTo(0));
        }

        [Test]
        public void Caching_ScriptedAdvice_IsNotSavedToDisk()
        {
            var store = new MemoryAdviceStore();
            var advisor = new CachingAdvisor(new ScriptedAdvisor(), Model, store);

            Get(advisor, Request());

            Assert.That(store.Saves, Is.EqualTo(0));
        }

        [Test]
        public void Caching_StoreThatThrows_DoesNotBreakAdvice()
        {
            var advisor = new CachingAdvisor(new StubAdvisor((request, token) => Task.FromResult(LocalAdvice(request))), Model, new ThrowingStore());

            Assert.That(Get(advisor, Request()), Is.Not.Null);
        }

        [Test]
        public void Caching_DifferentModels_DoNotShareAdvice()
        {
            var store = new MemoryAdviceStore();
            Get(new CachingAdvisor(new StubAdvisor((request, token) => Task.FromResult(LocalAdvice(request))), "qwen3.5:0.8b", store), Request());
            StubAdvisor model = new StubAdvisor((request, token) => Task.FromResult(LocalAdvice(request)));

            Get(new CachingAdvisor(model, Model, store), Request());

            Assert.That(model.Requests.Count, Is.EqualTo(1));
        }

        [Test]
        public void Caching_InvalidArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new CachingAdvisor(null, Model));
            var advisor = new CachingAdvisor(new ScriptedAdvisor(), Model);
            Assert.Throws<ArgumentNullException>(() => advisor.GetAdviceAsync(null, CancellationToken.None));
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => advisor.GetAdviceAsync(Request(), cancellation.Token));
            }
        }

        [Test]
        public void Caching_Dispose_CancelsGenerationToken()
        {
            CancellationToken seen = default;
            var advisor = new CachingAdvisor(new StubAdvisor((request, token) =>
            {
                seen = token;
                return new TaskCompletionSource<Advice>().Task;
            }), Model);

            advisor.GetAdviceAsync(Request(), CancellationToken.None);
            advisor.Dispose();

            Assert.That(seen.IsCancellationRequested, Is.True);
        }

        // ---- TimeoutAdvisor and FallbackAdvisor --------------------------------------------------------------

        [Test]
        public void Timeout_ModelTooSlow_FallsBackToScriptedAdvice()
        {
            var slow = new StubAdvisor((request, token) => new TaskCompletionSource<Advice>().Task);
            var timeout = new TimeoutAdvisor(slow, TimeSpan.FromSeconds(25), (time, token) => Task.CompletedTask);
            var advisor = new FallbackAdvisor(timeout, new ScriptedAdvisor());

            Advice advice = Get(advisor, Request());

            Assert.That(advice.Source, Is.EqualTo(AdviceSource.Scripted));
            Assert.That(advisor.LastFailure.Message, Does.Contain("did not answer within 25 seconds"));
        }

        [Test]
        public void Timeout_ModelInTime_ReturnsItsAdvice()
        {
            var pending = new TaskCompletionSource<Advice>();
            var never = new TaskCompletionSource<bool>();
            var timeout = new TimeoutAdvisor(new StubAdvisor((request, token) => pending.Task), TimeSpan.FromSeconds(25), (time, token) => never.Task);

            Task<Advice> advice = timeout.GetAdviceAsync(Request(), CancellationToken.None);
            pending.SetResult(LocalAdvice(Request()));

            Assert.That(advice.Result.Source, Is.EqualTo(AdviceSource.LocalModel));
            Assert.That(timeout.Timeout, Is.EqualTo(TimeSpan.FromSeconds(25)));
        }

        [Test]
        public void Timeout_AlreadyAnswered_SkipsTheTimer()
        {
            int timers = 0;
            var timeout = new TimeoutAdvisor(new ScriptedAdvisor(), TimeSpan.FromSeconds(1), (time, token) =>
            {
                timers++;
                return Task.CompletedTask;
            });

            Assert.That(Get(timeout, Request()).Source, Is.EqualTo(AdviceSource.Scripted));
            Assert.That(timers, Is.EqualTo(0));
        }

        [Test]
        public void Timeout_CallerCancels_IsCancellationNotFallback()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var slow = new StubAdvisor((request, token) => new TaskCompletionSource<Advice>().Task);
                var timer = new TaskCompletionSource<bool>();
                var timeout = new TimeoutAdvisor(slow, TimeSpan.FromSeconds(25), (time, token) =>
                {
                    token.Register(() => timer.TrySetCanceled());
                    return timer.Task;
                });

                Task<Advice> advice = timeout.GetAdviceAsync(Request(), cancellation.Token);
                cancellation.Cancel();

                Assert.Catch<OperationCanceledException>(() => advice.GetAwaiter().GetResult());
            }
        }

        [Test]
        public void Timeout_InvalidArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new TimeoutAdvisor(null, TimeSpan.FromSeconds(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TimeoutAdvisor(new ScriptedAdvisor(), TimeSpan.Zero));
            Assert.That(TimeoutAdvisor.DefaultTimeout, Is.EqualTo(TimeSpan.FromSeconds(25)));
        }

        [TestCase(null)]
        [TestCase("  ")]
        public void Fallback_BlankPrimaryAdvice_UsesScriptedAdvice(string rationale)
        {
            Advice blank = rationale == null ? null : new Advice(DecisionPointKind.Bail, "bail.grant", rationale, null, AdviceConfidence.High, FlawKind.None, null, AdviceSource.LocalModel);
            var advisor = new FallbackAdvisor(StubAdvisor.Returning(blank), new ScriptedAdvisor());

            Advice advice = Get(advisor, Request());

            Assert.That(advice.Source, Is.EqualTo(AdviceSource.Scripted));
            Assert.That(advice.Rationale, Is.Not.Empty);
            Assert.That(advisor.LastFailure.Message, Does.Contain("empty advice"));
        }

        // ---- Prefetch ----------------------------------------------------------------------------------------

        [Test]
        public void Prefetch_Plan_OneRequestPerPointInOrder()
        {
            CaseFile caseFile = TestCases.Load();
            var plan = new AdvicePlan(new[] { FlawKind.None, FlawKind.MisstatedLaw, FlawKind.None });

            IReadOnlyList<AdviceRequest> requests = AdvicePrefetch.PlanRequests(caseFile, plan);

            Assert.That(requests.Count, Is.EqualTo(3));
            Assert.That(requests[0].PointIndex, Is.EqualTo(0));
            Assert.That(requests[1].Flaw, Is.EqualTo(FlawKind.MisstatedLaw));
            Assert.That(requests[2].PointIndex, Is.EqualTo(2));
            Assert.That(requests[2].PriorRulings, Is.Empty);
        }

        [Test]
        public void Prefetch_PlanWithAdaptation_PreparesEveryFlawOfAFlawedPoint_AlternativesLast()
        {
            var plan = new AdvicePlan(new[] { FlawKind.None, FlawKind.MisstatedLaw, FlawKind.None });
            var adaptive = new AdaptiveController();
            adaptive.Record(AdviceOutcome.MisledByFlaw);
            adaptive.Record(AdviceOutcome.MisledByFlaw);

            IReadOnlyList<AdviceRequest> requests = AdvicePrefetch.PlanRequests(TestCases.Load(), plan, adaptive);

            Assert.That(requests.Count, Is.EqualTo(4));
            Assert.That(requests[1].PointIndex, Is.EqualTo(1));
            Assert.That(requests[1].Flaw, Is.EqualTo(FlawKind.OmittedFactor), "At subtlety 1 the subtler flaw is likelier.");
            Assert.That(requests[2].PointIndex, Is.EqualTo(2));
            Assert.That(requests[3].Flaw, Is.EqualTo(FlawKind.MisstatedLaw));
        }

        [Test]
        public void Prefetch_Plan_UsesTheSessionsDetail()
        {
            IReadOnlyList<AdviceRequest> requests = AdvicePrefetch.PlanRequests(TestCases.Load(), null, null, index => index == 0 ? ExplanationDetail.Detailed : ExplanationDetail.Standard);

            Assert.That(requests[0].Detail, Is.EqualTo(ExplanationDetail.Detailed));
            Assert.That(requests[1].Detail, Is.EqualTo(ExplanationDetail.Standard));
            Assert.Throws<ArgumentNullException>(() => AdvicePrefetch.PlanRequests(null, null));
        }

        [Test]
        public void Prefetch_Run_ThenSessionRequests_AreAllCacheHits()
        {
            StubAdvisor model = new StubAdvisor((request, token) => Task.FromResult(LocalAdvice(request)));
            var caching = new CachingAdvisor(model, Model);
            CaseFile caseFile = TestCases.Load();
            var plan = AdvicePlan.AllSound(3);

            int prepared = AdvicePrefetch.RunAsync(caching, AdvicePrefetch.PlanRequests(caseFile, plan), CancellationToken.None).GetAwaiter().GetResult();
            var session = new CaseSession(caseFile, plan, caching, new MemoryLogSink());
            session.Start();
            session.Submit("bail.grant");
            session.Submit("evidence.admit");

            Assert.That(prepared, Is.EqualTo(3));
            Assert.That(model.Requests.Count, Is.EqualTo(3), "The session's own requests reuse the prepared advice.");
            Assert.That(caching.HitCount, Is.EqualTo(3));
            Assert.That(session.CurrentAdvice, Is.Not.Null);
        }

        [Test]
        public void Prefetch_Run_SkipsFailuresAndStopsWhenCancelled()
        {
            var failures = new List<int>();
            var model = new StubAdvisor((request, token) => request.PointIndex == 1
                ? Task.FromException<Advice>(new AdvisorException("bad reply"))
                : Task.FromResult(LocalAdvice(request)));
            IReadOnlyList<AdviceRequest> requests = AdvicePrefetch.PlanRequests(TestCases.Load(), null);

            int prepared = AdvicePrefetch.RunAsync(model, requests, CancellationToken.None, (request, error) => failures.Add(request.PointIndex)).GetAwaiter().GetResult();

            Assert.That(prepared, Is.EqualTo(2));
            Assert.That(failures, Is.EqualTo(new[] { 1 }));

            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var unused = new StubAdvisor((request, token) => Task.FromResult(LocalAdvice(request)));
                Assert.That(AdvicePrefetch.RunAsync(unused, requests, cancellation.Token).GetAwaiter().GetResult(), Is.EqualTo(0));
                Assert.That(unused.Requests, Is.Empty);
            }

            Assert.Throws<ArgumentNullException>(() => AdvicePrefetch.RunAsync(null, requests, CancellationToken.None).GetAwaiter().GetResult());
        }

        [Test]
        public void Prefetch_Run_CancelledMidRequest_Stops()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var model = new StubAdvisor((request, token) =>
                {
                    cancellation.Cancel();
                    return Task.FromException<Advice>(new OperationCanceledException(token));
                });

                int prepared = AdvicePrefetch.RunAsync(model, AdvicePrefetch.PlanRequests(TestCases.Load(), null), cancellation.Token).GetAwaiter().GetResult();

                Assert.That(prepared, Is.EqualTo(0));
                Assert.That(model.Requests.Count, Is.EqualTo(1));
            }
        }

        // ---- Ollama GPU check --------------------------------------------------------------------------------

        private static string PsReply(string name, long size, long vram)
        {
            return JsonValue.NewObject()
                .Set("models", TestCases.ArrayOf(JsonValue.NewObject()
                    .Set("name", name)
                    .Set("model", name)
                    .Set("size", size)
                    .Set("size_vram", vram)))
                .ToJson();
        }

        [Test]
        public void Ps_AllInVram_IsGpu()
        {
            OllamaModelStatus status = OllamaModelStatus.FromPsReply(PsReply("qwen3.5:9b", 6_000_000_000, 6_000_000_000), "qwen3.5:9b");

            Assert.That(status.Placement, Is.EqualTo(ModelPlacement.Gpu));
            Assert.That(status.Warning, Is.Null);
            Assert.That(status.SizeBytes, Is.EqualTo(6_000_000_000));
        }

        [Test]
        public void Ps_NoVram_IsCpuWithRestartWarning()
        {
            OllamaModelStatus status = OllamaModelStatus.FromPsReply(PsReply("qwen3.5:9b", 6_000_000_000, 0), "qwen3.5:9b");

            Assert.That(status.Placement, Is.EqualTo(ModelPlacement.Cpu));
            Assert.That(status.Warning, Does.StartWith("Ollama is running the model on the CPU; restart Ollama to use the GPU"));
        }

        [Test]
        public void Ps_MostlyInSystemMemory_IsPartlyCpu()
        {
            OllamaModelStatus status = OllamaModelStatus.FromPsReply(PsReply("qwen3.5:9b", 6_000_000_000, 2_000_000_000), "qwen3.5:9b");

            Assert.That(status.Placement, Is.EqualTo(ModelPlacement.PartlyCpu));
            Assert.That(status.Warning, Does.Contain("restart Ollama to use the GPU"));
        }

        [Test]
        public void Ps_ModelMissingOrReplyUnreadable_IsNotLoaded()
        {
            Assert.That(OllamaModelStatus.FromPsReply(PsReply("llama3:8b", 1, 1), "qwen3.5:9b").Placement, Is.EqualTo(ModelPlacement.NotLoaded));
            Assert.That(OllamaModelStatus.FromPsReply("{\"models\":[]}", "qwen3.5:9b").IsLoaded, Is.False);
            Assert.That(OllamaModelStatus.FromPsReply("{}", "qwen3.5:9b").Warning, Is.Null);
            Assert.That(OllamaModelStatus.FromPsReply("oops", "qwen3.5:9b").Placement, Is.EqualTo(ModelPlacement.NotLoaded));
            Assert.That(OllamaModelStatus.FromPsReply(PsReply("qwen3.5:9b", 1, 1), " ").Placement, Is.EqualTo(ModelPlacement.NotLoaded));
        }

        [Test]
        public void Ps_TaglessName_MatchesLatest()
        {
            Assert.That(OllamaModelStatus.FromPsReply(PsReply("llama3:latest", 100, 100), "llama3").Placement, Is.EqualTo(ModelPlacement.Gpu));
        }

        [Test]
        public void Client_GetModelStatus_SendsGetToApiPs()
        {
            var transport = new FakeTransport().Enqueue(new HttpResponseData(200, PsReply("qwen3.5:9b", 10, 0)));
            var client = new OllamaClient(new OllamaClientOptions { BaseUrl = "http://127.0.0.1:11434/" }, transport);

            OllamaModelStatus status = client.GetModelStatusAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(transport.Requests[0].Method, Is.EqualTo("GET"));
            Assert.That(transport.Requests[0].Url, Is.EqualTo("http://127.0.0.1:11434/api/ps"));
            Assert.That(status.Placement, Is.EqualTo(ModelPlacement.Cpu));
        }

        [Test]
        public void Client_GetModelStatus_ErrorsThrowOllamaException()
        {
            var transport = new FakeTransport()
                .Enqueue(HttpResponseData.Failed("refused"))
                .Enqueue(new HttpResponseData(500, "{}"));
            var client = new OllamaClient(new OllamaClientOptions(), transport);

            Assert.Throws<OllamaException>(() => client.GetModelStatusAsync(CancellationToken.None).GetAwaiter().GetResult());
            OllamaException error = Assert.Throws<OllamaException>(() => client.GetModelStatusAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.That(error.StatusCode, Is.EqualTo(500));
        }

        [Test]
        public void HttpRequest_DefaultsToPost()
        {
            Assert.That(new HttpRequestData("http://x", null, "{}", TimeSpan.FromSeconds(1)).Method, Is.EqualTo("POST"));
            Assert.That(new HttpRequestData("http://x", null, "{}", TimeSpan.FromSeconds(1), null).Method, Is.EqualTo("POST"));
        }

        private static JsonValue Parse(string json)
        {
            Assert.That(JsonReader.TryParse(json, out JsonValue value, out string error), Is.True, error);
            return value;
        }

        private static string Edit(string json, string field, string value)
        {
            return Parse(json).Set(field, value).ToJson();
        }

        private sealed class ThrowingStore : IAdviceStore
        {
            public string Load(string key)
            {
                throw new InvalidOperationException("disk gone");
            }

            public void Save(string key, string json)
            {
                throw new InvalidOperationException("disk gone");
            }
        }
    }
}
