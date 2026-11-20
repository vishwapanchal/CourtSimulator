using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CourtSim.Core.Adaptive;
using CourtSim.Core.Advising;
using CourtSim.Core.Cases;
using CourtSim.Core.Decisions;
using CourtSim.Core.Json;
using CourtSim.Core.Logging;

namespace CourtSim.Core.Sessions
{
    public enum AdviceState
    {
        NotRequested,
        Pending,
        Ready,
        Failed,
    }

    /// <summary>
    /// One trainee session on one case: walks the decision points, fetches advice ahead of time, scores each
    /// ruling against the case's ground truth and writes the decision log. Engine-independent; the Unity
    /// layer only presents it.
    /// </summary>
    public sealed class CaseSession
    {
        private readonly IAdvisor _advisor;
        private readonly IDecisionLogSink _sink;
        private readonly Func<DateTime> _clock;
        private readonly string _advisorLabel;
        private readonly Advice[] _advice;
        private readonly AdviceState[] _adviceStates;
        private readonly string[] _adviceErrors;
        private readonly ExplanationDetail[] _details;
        private readonly Task<Advice>[] _adviceTasks;
        private readonly List<DecisionLogEntry> _entries = new List<DecisionLogEntry>();
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private AdviceStance _stance;
        private DateTime _pointStartedAt;
        private DateTime? _presentedAt;
        private bool _started;
        private AdaptiveController _adaptive;
        private Random _random = new Random();

        /// <param name="advisorLabel">Recorded in the log, e.g. "claude-opus-5-5" or "scripted".</param>
        public CaseSession(CaseFile caseFile, AdvicePlan plan, IAdvisor advisor, IDecisionLogSink sink, string advisorLabel = "scripted", Func<DateTime> clock = null, string sessionId = null)
        {
            Case = caseFile ?? throw new ArgumentNullException(nameof(caseFile));
            Plan = plan ?? AdvicePlan.AllSound(caseFile.DecisionPoints.Count);
            _advisor = advisor ?? throw new ArgumentNullException(nameof(advisor));
            _sink = sink ?? new MemoryLogSink();
            _clock = clock ?? (() => DateTime.UtcNow);
            _advisorLabel = advisorLabel ?? "unknown";
            SessionId = string.IsNullOrEmpty(sessionId) ? Guid.NewGuid().ToString("N") : sessionId;
            Decisions = caseFile.CreateSequence();

            int count = caseFile.DecisionPoints.Count;
            _advice = new Advice[count];
            _adviceStates = new AdviceState[count];
            _adviceErrors = new string[count];
            _details = new ExplanationDetail[count];
            _adviceTasks = new Task<Advice>[count];
        }

        /// <summary>Raised (on the caller's synchronization context) when advice for a decision point changes state.</summary>
        public event Action<int> AdviceChanged;

        /// <summary>Raised with a plain-language note when the adaptive controller changes difficulty.</summary>
        public event Action<string> Adapted;

        /// <summary>Raised once, after the last ruling is logged.</summary>
        public event Action<SessionSummary> Completed;

        public string SessionId { get; }

        public CaseFile Case { get; }

        public AdvicePlan Plan { get; }

        public DecisionSequence Decisions { get; }

        public bool IsComplete => Decisions.IsComplete;

        public int CurrentIndex => Decisions.CurrentIndex;

        /// <summary>The pending decision point, or null once complete.</summary>
        public CaseDecisionPoint CurrentPoint => IsComplete ? null : Case.DecisionPoints[CurrentIndex];

        public AdviceState CurrentAdviceState => IsComplete ? AdviceState.NotRequested : _adviceStates[CurrentIndex];

        /// <summary>Advice for the pending point once <see cref="CurrentAdviceState"/> is Ready; otherwise null.</summary>
        public Advice CurrentAdvice => IsComplete ? null : _advice[CurrentIndex];

        public AdviceStance CurrentStance => _stance;

        public IReadOnlyList<DecisionLogEntry> Entries => _entries;

        public SessionSummary Summary => SessionSummary.From(_entries);

        /// <summary>
        /// Lets <paramref name="controller"/> choose each flaw kind and explanation detail, and feeds it every
        /// ruling. Call before <see cref="Start"/>. The plan still decides <em>which</em> points are flawed.
        /// </summary>
        public void UseAdaptation(AdaptiveController controller, Random random = null)
        {
            _adaptive = controller;
            _random = random ?? _random;
        }

        /// <summary>The trainee's adaptive state, or null if adaptation is off.</summary>
        public AdaptiveController Adaptation => _adaptive;

        /// <summary>The explanation detail used for decision point <paramref name="index"/>.</summary>
        public ExplanationDetail DetailAt(int index)
        {
            return index >= 0 && index < _details.Length ? _details[index] : ExplanationDetail.Standard;
        }

        /// <summary>Logs the session start and begins fetching advice for the first decision point.</summary>
        public void Start()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _pointStartedAt = _clock();
            var plan = new List<string>();
            for (int i = 0; i < Plan.Count; i++)
            {
                // With adaptation on, the controller picks the kind when the advice is requested; each decision record has it.
                FlawKind planned = Plan.FlawAt(i);
                plan.Add(_adaptive != null && planned != FlawKind.None ? "flawed" : FlawKinds.ToWireName(planned));
            }

            _sink.Write(JsonValue.NewObject()
                .Set("type", "session_start")
                .Set("schema", LogNames.Schema)
                .Set("sessionId", SessionId)
                .Set("caseId", Case.Id)
                .Set("caseTitle", Case.Title)
                .Set("difficulty", Case.Difficulty)
                .Set("advisor", _advisorLabel)
                .Set("plan", JsonValue.StringArray(plan))
                .Set("timestampUtc", LogNames.Timestamp(_clock())));

            RequestAdvice(0);
        }

        /// <summary>Starts (once) the advice request for decision point <paramref name="index"/> and returns its task.</summary>
        public Task<Advice> RequestAdvice(int index)
        {
            if (index < 0 || index >= _adviceTasks.Length)
            {
                return Task.FromResult<Advice>(null);
            }

            if (_adviceTasks[index] == null)
            {
                _adviceStates[index] = AdviceState.Pending;
                _adviceTasks[index] = LoadAdviceAsync(index, _cancellation.Token);
            }

            return _adviceTasks[index];
        }

        /// <summary>Call when the pending decision is first shown; the decision timer runs from then.</summary>
        public void MarkPresented()
        {
            if (!IsComplete && _presentedAt == null)
            {
                _presentedAt = _clock();
            }
        }

        /// <summary>Records the trainee's Accept/Challenge reaction to the current advice.</summary>
        public void SetStance(AdviceStance stance)
        {
            if (!IsComplete)
            {
                _stance = stance;
            }
        }

        /// <summary>Records a ruling for the pending decision point, logs it, and prefetches the next advice.</summary>
        /// <exception cref="InvalidOperationException">The session is complete.</exception>
        /// <exception cref="ArgumentException"><paramref name="optionId"/> is not an option of the pending point.</exception>
        public DecisionLogEntry Submit(string optionId)
        {
            Start();
            int index = CurrentIndex;
            CaseDecisionPoint point = CurrentPoint ?? throw new InvalidOperationException("Every decision point has already been ruled on.");
            OptionAssessment assessment = point.Assess(optionId);
            DecisionRecord record = Decisions.Record(optionId);

            DateTime now = _clock();
            Advice advice = _adviceStates[index] == AdviceState.Ready ? _advice[index] : null;
            string adviceError = _adviceErrors[index] ?? (advice == null ? "Advice was not ready when the ruling was made." : null);
            var entry = new DecisionLogEntry(
                SessionId,
                Case.Id,
                record,
                assessment != null ? assessment.Quality : OptionQuality.Unsound,
                advice,
                advice != null ? _stance : AdviceStance.None,
                (now - (_presentedAt ?? _pointStartedAt)).TotalSeconds,
                now,
                adviceError);
            _entries.Add(entry);
            JsonValue logRecord = entry.ToJson();
            if (advice != null)
            {
                logRecord.Get("advice").Set("detail", _details[index] == ExplanationDetail.Detailed ? "detailed" : "standard");
            }

            _sink.Write(logRecord);

            // The adaptive controller learns from every ruling and may change the difficulty for what follows.
            string adaptation = _adaptive?.Record(entry.Outcome);
            if (adaptation != null)
            {
                _sink.Write(JsonValue.NewObject()
                    .Set("type", "adaptation")
                    .Set("schema", LogNames.Schema)
                    .Set("sessionId", SessionId)
                    .Set("afterIndex", index)
                    .Set("note", adaptation)
                    .Set("state", _adaptive.ToJson())
                    .Set("timestampUtc", LogNames.Timestamp(now)));
                Adapted?.Invoke(adaptation);
            }

            _stance = AdviceStance.None;
            _presentedAt = null;
            _pointStartedAt = now;

            if (IsComplete)
            {
                SessionSummary summary = Summary;
                _sink.Write(JsonValue.NewObject()
                    .Set("type", "session_end")
                    .Set("schema", LogNames.Schema)
                    .Set("sessionId", SessionId)
                    .Set("caseId", Case.Id)
                    .Set("timestampUtc", LogNames.Timestamp(now))
                    .Set("summary", summary.ToJson()));
                Completed?.Invoke(summary);
            }
            else
            {
                RequestAdvice(CurrentIndex);
            }

            return entry;
        }

        /// <summary>Cancels outstanding advice requests. The session can't fetch advice afterwards.</summary>
        public void Cancel()
        {
            _cancellation.Cancel();
        }

        /// <summary>The advice error for <paramref name="index"/> (including fallback reasons), or null.</summary>
        public string AdviceErrorAt(int index)
        {
            return index >= 0 && index < _adviceErrors.Length ? _adviceErrors[index] : null;
        }

        public Advice AdviceAt(int index)
        {
            return index >= 0 && index < _advice.Length ? _advice[index] : null;
        }

        /// <summary>Records why the primary advisor failed for a point that still got (fallback) advice.</summary>
        public void NoteAdvisorFallback(int index, string reason)
        {
            if (index >= 0 && index < _adviceErrors.Length)
            {
                _adviceErrors[index] = reason;
            }
        }

        private async Task<Advice> LoadAdviceAsync(int index, CancellationToken cancellationToken)
        {
            FlawKind flaw = Plan.FlawAt(index);
            if (_adaptive != null && flaw != FlawKind.None)
            {
                // The plan says this point is flawed; the controller picks how subtle the flaw is.
                flaw = _adaptive.ChooseFlaw(Case.DecisionPoints[index].AvailableFlaws(), _random);
            }

            _details[index] = _adaptive != null ? _adaptive.TakeExplanationDetail() : ExplanationDetail.Standard;
            var request = new AdviceRequest(Case, index, flaw, new List<DecisionRecord>(Decisions.Records), _details[index]);
            try
            {
                Advice advice = await _advisor.GetAdviceAsync(request, cancellationToken);
                _advice[index] = advice;
                _adviceStates[index] = advice != null ? AdviceState.Ready : AdviceState.Failed;
            }
            catch (OperationCanceledException)
            {
                _adviceStates[index] = AdviceState.Failed;
                _adviceErrors[index] = "Advice request was cancelled.";
            }
            catch (Exception ex)
            {
                _adviceStates[index] = AdviceState.Failed;
                _adviceErrors[index] = ex.Message;
            }

            AdviceChanged?.Invoke(index);
            return _advice[index];
        }
    }
}
