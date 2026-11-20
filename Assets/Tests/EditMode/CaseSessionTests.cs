using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CourtSim.Core.Advising;
using CourtSim.Core.Cases;
using CourtSim.Core.Json;
using CourtSim.Core.Logging;
using CourtSim.Core.Sessions;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    public sealed class CaseSessionTests
    {
        private CaseFile _case;
        private MemoryLogSink _sink;
        private FakeClock _clock;

        [SetUp]
        public void SetUp()
        {
            _case = TestCases.Load();
            _sink = new MemoryLogSink();
            _clock = new FakeClock();
        }

        [Test]
        public void Constructor_NullCaseOrAdvisor_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new CaseSession(null, null, new ScriptedAdvisor(), _sink));
            Assert.Throws<ArgumentNullException>(() => new CaseSession(_case, null, null, _sink));
        }

        [Test]
        public void Constructor_Defaults_AreSensible()
        {
            var session = new CaseSession(_case, null, new ScriptedAdvisor(), null, null);

            Assert.That(session.Plan.Count, Is.EqualTo(3));
            Assert.That(session.Plan.FlawedCount, Is.EqualTo(0));
            Assert.That(session.SessionId, Does.Match("^[0-9a-f]{32}$"));
            Assert.That(session.Case, Is.SameAs(_case));
            Assert.That(session.CurrentIndex, Is.EqualTo(0));
            Assert.That(session.CurrentPoint, Is.SameAs(_case.DecisionPoints[0]));
            Assert.That(session.CurrentAdviceState, Is.EqualTo(AdviceState.NotRequested));
            Assert.That(session.CurrentAdvice, Is.Null);
            Assert.That(session.Entries, Is.Empty);
            Assert.That(session.IsComplete, Is.False);
        }

        [Test]
        public void Start_WritesSessionStartRecord()
        {
            var plan = new AdvicePlan(new[] { FlawKind.MisreadFact, FlawKind.None, FlawKind.ImproperFactor });
            var session = new CaseSession(_case, plan, new ScriptedAdvisor(), _sink, "claude-test", _clock.Read, "sess-1");

            session.Start();

            Assert.That(_sink.Records.Count, Is.EqualTo(1));
            JsonValue start = _sink.Records[0];
            Assert.That(start.GetString("type"), Is.EqualTo("session_start"));
            Assert.That(start.GetString("schema"), Is.EqualTo(LogNames.Schema));
            Assert.That(start.GetString("sessionId"), Is.EqualTo("sess-1"));
            Assert.That(start.GetString("caseId"), Is.EqualTo(TestCases.CaseId));
            Assert.That(start.GetString("caseTitle"), Is.EqualTo("State v. Alex Doe"));
            Assert.That(start.GetNumber("difficulty"), Is.EqualTo(2));
            Assert.That(start.GetString("advisor"), Is.EqualTo("claude-test"));
            Assert.That(start.GetString("timestampUtc"), Is.EqualTo("2026-03-04T09:00:00.000Z"));
            Assert.That(start.Get("plan").ToJson(), Is.EqualTo("[\"misread_fact\",\"none\",\"improper_factor\"]"));
        }

        [Test]
        public void Start_NullAdvisorLabel_LogsUnknown()
        {
            var session = new CaseSession(_case, null, new ScriptedAdvisor(), _sink, null);

            session.Start();

            Assert.That(_sink.Records[0].GetString("advisor"), Is.EqualTo("unknown"));
        }

        [Test]
        public void Start_CalledTwice_StartsOnce()
        {
            var advisor = new StubAdvisor((request, token) => Task.FromResult(ScriptedAdvisor.Build(request)));
            CaseSession session = Session(advisor);

            session.Start();
            session.Start();

            Assert.That(_sink.Records.Count, Is.EqualTo(1));
            Assert.That(advisor.Requests.Count, Is.EqualTo(1));
        }

        [Test]
        public void Start_FetchesAdviceForFirstPointOnly()
        {
            var advisor = new StubAdvisor((request, token) => Task.FromResult(ScriptedAdvisor.Build(request)));
            CaseSession session = Session(advisor, new AdvicePlan(new[] { FlawKind.MisreadFact, FlawKind.None, FlawKind.None }));
            var changed = new List<int>();
            session.AdviceChanged += changed.Add;

            session.Start();

            Assert.That(advisor.Requests.Count, Is.EqualTo(1));
            Assert.That(advisor.Requests[0].PointIndex, Is.EqualTo(0));
            Assert.That(advisor.Requests[0].Flaw, Is.EqualTo(FlawKind.MisreadFact));
            Assert.That(advisor.Requests[0].PriorRulings, Is.Empty);
            Assert.That(changed, Is.EqualTo(new[] { 0 }));
            Assert.That(session.CurrentAdviceState, Is.EqualTo(AdviceState.Ready));
            Assert.That(session.CurrentAdvice.Flaw, Is.EqualTo(FlawKind.MisreadFact));
            Assert.That(session.CurrentAdvice.RecommendedOptionId, Is.EqualTo("bail.deny"));
            Assert.That(session.AdviceAt(1), Is.Null);
        }

        [Test]
        public void Submit_PrefetchesAdviceForNextPoint()
        {
            var advisor = new StubAdvisor((request, token) => Task.FromResult(ScriptedAdvisor.Build(request)));
            CaseSession session = Session(advisor);
            var changed = new List<int>();
            session.AdviceChanged += changed.Add;
            session.Start();

            session.Submit("bail.grant");

            Assert.That(advisor.Requests.Count, Is.EqualTo(2));
            AdviceRequest second = advisor.Requests[1];
            Assert.That(second.PointIndex, Is.EqualTo(1));
            Assert.That(second.PriorRulings.Count, Is.EqualTo(1));
            Assert.That(second.PriorRulings[0].OptionId, Is.EqualTo("bail.grant"));
            Assert.That(changed, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(session.CurrentIndex, Is.EqualTo(1));
            Assert.That(session.CurrentAdviceState, Is.EqualTo(AdviceState.Ready));
            Assert.That(session.CurrentAdvice.RecommendedOptionId, Is.EqualTo("evidence.admit"));
        }

        [Test]
        public void RequestAdvice_SameIndex_ReusesTask()
        {
            var advisor = new StubAdvisor((request, token) => Task.FromResult(ScriptedAdvisor.Build(request)));
            CaseSession session = Session(advisor);

            Task<Advice> first = session.RequestAdvice(2);
            Task<Advice> second = session.RequestAdvice(2);

            Assert.That(second, Is.SameAs(first));
            Assert.That(advisor.Requests.Count, Is.EqualTo(1));
            Assert.That(first.GetAwaiter().GetResult().RecommendedOptionId, Is.EqualTo("sentence.community"));
            Assert.That(session.AdviceAt(2), Is.SameAs(first.GetAwaiter().GetResult()));
        }

        [TestCase(-1)]
        [TestCase(3)]
        public void RequestAdvice_OutOfRange_ReturnsNullAdvice(int index)
        {
            CaseSession session = Session(new ScriptedAdvisor());

            Assert.That(session.RequestAdvice(index).GetAwaiter().GetResult(), Is.Null);
        }

        [Test]
        public void Submit_ScoresRulingAgainstCaseAndAdvice()
        {
            CaseSession session = Session(new ScriptedAdvisor(), new AdvicePlan(new[] { FlawKind.MisreadFact, FlawKind.MisstatedLaw, FlawKind.None }));
            session.Start();

            DecisionLogEntry caught = session.Submit("bail.grant");
            DecisionLogEntry misled = session.Submit("evidence.exclude");
            DecisionLogEntry overrode = session.Submit("sentence.custody");

            Assert.That(caught.Quality, Is.EqualTo(OptionQuality.Sound));
            Assert.That(caught.Outcome, Is.EqualTo(AdviceOutcome.CaughtFlaw));
            Assert.That(caught.SessionId, Is.EqualTo("sess"));
            Assert.That(caught.CaseId, Is.EqualTo(TestCases.CaseId));
            Assert.That(caught.Record.SequenceIndex, Is.EqualTo(0));
            Assert.That(caught.AdviceError, Is.Null);
            Assert.That(misled.Quality, Is.EqualTo(OptionQuality.Unsound));
            Assert.That(misled.Outcome, Is.EqualTo(AdviceOutcome.MisledByFlaw));
            Assert.That(overrode.Quality, Is.EqualTo(OptionQuality.Defensible));
            Assert.That(overrode.Outcome, Is.EqualTo(AdviceOutcome.OverrodeSound));
            Assert.That(session.Entries, Is.EqualTo(new[] { caught, misled, overrode }));
            Assert.That(_sink.Records[1].GetString("type"), Is.EqualTo("decision"));
            Assert.That(_sink.Records[1].GetString("outcome"), Is.EqualTo("caught_flaw"));
            Assert.That(session.Summary.FlawsCaught, Is.EqualTo(1));
        }

        [Test]
        public void Submit_WithoutStart_StartsImplicitly()
        {
            CaseSession session = Session(new ScriptedAdvisor());

            DecisionLogEntry entry = session.Submit("bail.grant");

            Assert.That(_sink.Records[0].GetString("type"), Is.EqualTo("session_start"));
            Assert.That(entry.Outcome, Is.EqualTo(AdviceOutcome.TrustedSound));
        }

        [Test]
        public void Submit_RecordsStanceThenResetsIt()
        {
            CaseSession session = Session(new ScriptedAdvisor());
            session.Start();

            session.SetStance(AdviceStance.Challenged);
            Assert.That(session.CurrentStance, Is.EqualTo(AdviceStance.Challenged));
            DecisionLogEntry first = session.Submit("bail.grant");
            DecisionLogEntry second = session.Submit("evidence.admit");

            Assert.That(first.Stance, Is.EqualTo(AdviceStance.Challenged));
            Assert.That(_sink.Records[1].GetString("stance"), Is.EqualTo("challenged"));
            Assert.That(second.Stance, Is.EqualTo(AdviceStance.None));
            Assert.That(session.CurrentStance, Is.EqualTo(AdviceStance.None));
        }

        [Test]
        public void Submit_SecondsToDecide_RunFromPresentationOrPreviousRuling()
        {
            CaseSession session = Session(new ScriptedAdvisor());
            session.Start();

            _clock.Advance(5);
            session.MarkPresented();
            _clock.Advance(7);
            session.MarkPresented();
            _clock.Advance(1.5);
            DecisionLogEntry presented = session.Submit("bail.grant");

            _clock.Advance(4);
            DecisionLogEntry unpresented = session.Submit("evidence.admit");

            Assert.That(presented.SecondsToDecide, Is.EqualTo(8.5).Within(1e-9));
            Assert.That(presented.TimestampUtc, Is.EqualTo(_clock.Now.AddSeconds(-4)));
            Assert.That(unpresented.SecondsToDecide, Is.EqualTo(4).Within(1e-9));
        }

        [Test]
        public void Submit_FromStartWithoutPresentation_TimesFromStart()
        {
            CaseSession session = Session(new ScriptedAdvisor());
            session.Start();

            _clock.Advance(30);

            Assert.That(session.Submit("bail.grant").SecondsToDecide, Is.EqualTo(30).Within(1e-9));
        }

        [Test]
        public void Submit_LastRuling_WritesSessionEndAndRaisesCompleted()
        {
            CaseSession session = Session(new ScriptedAdvisor(), new AdvicePlan(new[] { FlawKind.MisreadFact, FlawKind.None, FlawKind.None }));
            var summaries = new List<SessionSummary>();
            session.Completed += summaries.Add;
            session.Start();

            session.Submit("bail.grant");
            session.Submit("evidence.admit");
            Assert.That(summaries, Is.Empty);
            _clock.Advance(2);
            session.Submit("sentence.community");

            Assert.That(session.IsComplete, Is.True);
            Assert.That(summaries.Count, Is.EqualTo(1));
            Assert.That(summaries[0].Rulings, Is.EqualTo(3));
            Assert.That(summaries[0].FlawsCaught, Is.EqualTo(1));
            Assert.That(_sink.Records.Count, Is.EqualTo(5));
            JsonValue end = _sink.Records[4];
            Assert.That(end.GetString("type"), Is.EqualTo("session_end"));
            Assert.That(end.GetString("schema"), Is.EqualTo(LogNames.Schema));
            Assert.That(end.GetString("sessionId"), Is.EqualTo("sess"));
            Assert.That(end.GetString("caseId"), Is.EqualTo(TestCases.CaseId));
            Assert.That(end.GetString("timestampUtc"), Is.EqualTo("2026-03-04T09:00:02.000Z"));
            Assert.That(end.Get("summary").GetNumber("rulings"), Is.EqualTo(3));
            Assert.That(end.Get("summary").GetNumber("soundRulings"), Is.EqualTo(3));
            Assert.That(end.Get("summary").GetNumber("flawDetectionRate"), Is.EqualTo(1));
        }

        [Test]
        public void Complete_CurrentStateIsEmptyAndInputsAreIgnored()
        {
            CaseSession session = CompletedSession();

            session.SetStance(AdviceStance.Accepted);
            session.MarkPresented();

            Assert.That(session.CurrentPoint, Is.Null);
            Assert.That(session.CurrentAdvice, Is.Null);
            Assert.That(session.CurrentAdviceState, Is.EqualTo(AdviceState.NotRequested));
            Assert.That(session.CurrentStance, Is.EqualTo(AdviceStance.None));
            Assert.That(session.CurrentIndex, Is.EqualTo(3));
        }

        [Test]
        public void Submit_AfterComplete_Throws()
        {
            CaseSession session = CompletedSession();
            int records = _sink.Records.Count;

            Assert.Throws<InvalidOperationException>(() => session.Submit("bail.grant"));
            Assert.That(_sink.Records.Count, Is.EqualTo(records));
        }

        [Test]
        public void Submit_InvalidOption_ThrowsAndRecordsNothing()
        {
            CaseSession session = Session(new ScriptedAdvisor());
            session.Start();

            Assert.Throws<ArgumentException>(() => session.Submit("evidence.admit"));
            Assert.Throws<ArgumentException>(() => session.Submit(null));

            Assert.That(session.CurrentIndex, Is.EqualTo(0));
            Assert.That(session.Entries, Is.Empty);
            Assert.That(_sink.Records.Count, Is.EqualTo(1));
        }

        [Test]
        public void AdviceFailure_MarksFailedAndLogsNoAdvice()
        {
            CaseSession session = Session(StubAdvisor.Failing(new AdvisorException("API down")));
            var changed = new List<int>();
            session.AdviceChanged += changed.Add;

            session.Start();

            Assert.That(session.CurrentAdviceState, Is.EqualTo(AdviceState.Failed));
            Assert.That(session.CurrentAdvice, Is.Null);
            Assert.That(session.AdviceErrorAt(0), Is.EqualTo("API down"));
            Assert.That(changed, Is.EqualTo(new[] { 0 }));

            session.SetStance(AdviceStance.Accepted);
            DecisionLogEntry entry = session.Submit("bail.grant");

            Assert.That(entry.Advice, Is.Null);
            Assert.That(entry.Outcome, Is.EqualTo(AdviceOutcome.NoAdvice));
            Assert.That(entry.Stance, Is.EqualTo(AdviceStance.None));
            Assert.That(entry.AdviceError, Is.EqualTo("API down"));
            Assert.That(_sink.Records[1].GetString("adviceError"), Is.EqualTo("API down"));
            Assert.That(_sink.Records[1].Get("advice").IsNull, Is.True);
        }

        [Test]
        public void AdvisorReturnsNull_MarksFailed()
        {
            CaseSession session = Session(StubAdvisor.Returning(null));
            session.Start();

            Assert.That(session.CurrentAdviceState, Is.EqualTo(AdviceState.Failed));
            Assert.That(session.Submit("bail.grant").AdviceError, Is.EqualTo("Advice was not ready when the ruling was made."));
        }

        [Test]
        public void AdvicePending_RulingIsLoggedWithoutAdvice()
        {
            var pending = new TaskCompletionSource<Advice>();
            CaseSession session = Session(new StubAdvisor((request, token) => pending.Task));
            session.Start();

            Assert.That(session.CurrentAdviceState, Is.EqualTo(AdviceState.Pending));
            Assert.That(session.CurrentAdvice, Is.Null);
            DecisionLogEntry entry = session.Submit("bail.grant");

            Assert.That(entry.Advice, Is.Null);
            Assert.That(entry.Outcome, Is.EqualTo(AdviceOutcome.NoAdvice));
            Assert.That(entry.AdviceError, Is.EqualTo("Advice was not ready when the ruling was made."));
        }

        [Test]
        public void Cancel_BeforeStart_FailsAdviceAsCancelled()
        {
            CaseSession session = Session(new ScriptedAdvisor());

            session.Cancel();
            session.Start();

            Assert.That(session.CurrentAdviceState, Is.EqualTo(AdviceState.Failed));
            Assert.That(session.AdviceErrorAt(0), Is.EqualTo("Advice request was cancelled."));
        }

        [Test]
        public void Cancel_PassesCancelledTokenToAdvisor()
        {
            CancellationToken seen = CancellationToken.None;
            CaseSession session = Session(new StubAdvisor((request, token) =>
            {
                seen = token;
                return Task.FromResult(ScriptedAdvisor.Build(request));
            }));

            session.Cancel();
            session.Start();

            Assert.That(seen.IsCancellationRequested, Is.True);
        }

        [Test]
        public void NoteAdvisorFallback_ReasonIsLoggedWithAdvice()
        {
            CaseSession session = Session(new ScriptedAdvisor());
            session.Start();

            session.NoteAdvisorFallback(0, "Claude failed; used scripted advice.");
            session.NoteAdvisorFallback(-1, "ignored");
            session.NoteAdvisorFallback(3, "ignored");
            DecisionLogEntry entry = session.Submit("bail.grant");

            Assert.That(entry.Advice, Is.Not.Null);
            Assert.That(entry.AdviceError, Is.EqualTo("Claude failed; used scripted advice."));
            Assert.That(session.AdviceErrorAt(-1), Is.Null);
            Assert.That(session.AdviceErrorAt(3), Is.Null);
            Assert.That(session.AdviceAt(-1), Is.Null);
            Assert.That(session.AdviceAt(3), Is.Null);
        }

        [Test]
        public void FallbackAdvisor_InSession_StillProducesAdvice()
        {
            var advisor = new FallbackAdvisor(StubAdvisor.Failing(new AdvisorException("offline")), new ScriptedAdvisor());
            CaseSession session = Session(advisor);
            advisor.PrimaryFailed += error => session.NoteAdvisorFallback(session.CurrentIndex, error.Message);

            session.Start();
            DecisionLogEntry entry = session.Submit("bail.grant");

            Assert.That(entry.Advice.Source, Is.EqualTo(AdviceSource.Scripted));
            Assert.That(entry.AdviceError, Is.EqualTo("offline"));
        }

        private CaseSession Session(IAdvisor advisor, AdvicePlan plan = null)
        {
            return new CaseSession(_case, plan, advisor, _sink, "scripted", _clock.Read, "sess");
        }

        private CaseSession CompletedSession()
        {
            CaseSession session = Session(new ScriptedAdvisor());
            session.Submit("bail.grant");
            session.Submit("evidence.admit");
            session.Submit("sentence.community");
            return session;
        }
    }
}
