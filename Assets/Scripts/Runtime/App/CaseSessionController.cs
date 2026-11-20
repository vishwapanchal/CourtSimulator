using System;
using System.Collections.Generic;
using System.IO;
using CourtSim.Core.Advising;
using CourtSim.Core.Cases;
using CourtSim.Core.Claude;
using CourtSim.Core.Logging;
using CourtSim.Core.Sessions;
using CourtSim.Runtime.Advising;
using CourtSim.Runtime.Cases;
using CourtSim.Runtime.UI;
using UnityEngine;

namespace CourtSim.Runtime.App
{
    /// <summary>
    /// Runs training sessions in the scene: picks a case, plans which advice is flawed, chooses the advisor
    /// (Claude when an API key is available, otherwise the authored offline advice), writes the decision log
    /// and hands the session to the <see cref="UIManager"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CaseSessionController : MonoBehaviour
    {
        [Header("Case")]
        [Tooltip("Case id to play. Empty: cycle through the loaded cases from easiest to hardest.")]
        [SerializeField] private string _caseId = string.Empty;
        [Tooltip("How many of the three decision points get deliberately flawed advice.")]
        [SerializeField, Range(0, 3)] private int _flawedAdviceCount = 1;
        [Tooltip("Random seed for the flaw plan. 0 picks a new plan each session.")]
        [SerializeField] private int _seed;

        [Header("Advisor")]
        [Tooltip("Use Claude when an API key is found. Offline advice is always the fallback.")]
        [SerializeField] private bool _useClaude = true;
        [SerializeField] private string _model = ClaudeClientOptions.DefaultModel;
        [Tooltip("low, medium, high, xhigh or max.")]
        [SerializeField] private string _effort = "medium";
        [Tooltip("Optional proxy base URL, so trainee machines never hold the key. Empty uses api.anthropic.com.")]
        [SerializeField] private string _apiBaseUrl = string.Empty;

        [Header("Logging")]
        [SerializeField] private bool _writeLogFile = true;

        private readonly MemoryLogSink _memoryLog = new MemoryLogSink();
        private IReadOnlyList<CaseFile> _cases = Array.Empty<CaseFile>();
        private UIManager _ui;
        private CaseSession _session;
        private FallbackAdvisor _fallbackAdvisor;
        private int _caseCursor = -1;
        private IAdvisor _advisorOverride;

        public CaseSession Session => _session;

        public IReadOnlyList<CaseFile> Cases => _cases;

        /// <summary>Path of the current session's JSON Lines log, or null if file logging is off.</summary>
        public string LogFilePath { get; private set; }

        /// <summary>Which advisor answers this session: the Claude model id, "scripted" (offline) or "custom".</summary>
        public string AdvisorLabel { get; private set; }

        public static string LogDirectory => Path.Combine(Application.persistentDataPath, "logs");

        public event Action<CaseSession> SessionStarted;

        /// <summary>Connects the controller to the UI. Called by <see cref="CourtSimBootstrap"/>.</summary>
        public void Initialize(UIManager ui)
        {
            if (_ui != null)
            {
                _ui.RestartRequested -= OnRestartRequested;
            }

            _ui = ui;
            if (_ui != null)
            {
                _ui.RestartRequested += OnRestartRequested;
                if (_session != null)
                {
                    _ui.AttachSession(_session);
                }
            }
        }

        /// <summary>Replaces the advisor for later sessions (tests, or a custom backend).</summary>
        public void UseAdvisor(IAdvisor advisor)
        {
            _advisorOverride = advisor;
        }

        /// <summary>Configures case selection before the first session starts.</summary>
        public void Configure(string caseId, int flawedAdviceCount, int seed, bool useClaude, bool writeLogFile)
        {
            _caseId = caseId ?? string.Empty;
            _flawedAdviceCount = Mathf.Clamp(flawedAdviceCount, 0, 3);
            _seed = seed;
            _useClaude = useClaude;
            _writeLogFile = writeLogFile;
        }

        /// <summary>Starts a new session on the next (or configured) case. Returns null if no valid case is available.</summary>
        public CaseSession StartNewSession()
        {
            EnsureCasesLoaded();
            CaseFile caseFile = PickCase();
            if (caseFile == null)
            {
                Debug.LogError("No valid case files were found under Resources/Cases; the decision flow uses placeholder content.");
                return null;
            }

            _session?.Cancel();
            var random = _seed != 0 ? new System.Random(_seed) : new System.Random();
            AdvicePlan plan = AdvicePlanner.Create(caseFile, _flawedAdviceCount, random);

            IAdvisor advisor = CreateAdvisor(out string label);
            AdvisorLabel = label;

            IDecisionLogSink sink = _memoryLog;
            LogFilePath = null;
            if (_writeLogFile)
            {
                string sessionId = Guid.NewGuid().ToString("N");
                LogFilePath = Path.Combine(LogDirectory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{caseFile.Id}-{sessionId.Substring(0, 8)}.jsonl");
                sink = new CompositeLogSink(_memoryLog, new JsonLinesFileSink(LogFilePath));
                _session = new CaseSession(caseFile, plan, advisor, sink, label, null, sessionId);
            }
            else
            {
                _session = new CaseSession(caseFile, plan, advisor, sink, label);
            }

            CaseSession session = _session;
            if (_fallbackAdvisor != null)
            {
                _fallbackAdvisor.PrimaryFailed += error =>
                {
                    if (session == _session && !session.IsComplete)
                    {
                        session.NoteAdvisorFallback(session.CurrentIndex, "Claude unavailable, used offline advice: " + error.Message);
                        Debug.LogWarning("Claude advice failed; using offline advice instead. " + error.Message);
                    }
                };
            }

            _session.Start();
            if (_ui != null)
            {
                _ui.AttachSession(_session);
            }

            SessionStarted?.Invoke(_session);
            return _session;
        }

        private void Start()
        {
            if (_session == null)
            {
                StartNewSession();
            }
        }

        private void OnDestroy()
        {
            _session?.Cancel();
            if (_ui != null)
            {
                _ui.RestartRequested -= OnRestartRequested;
            }
        }

        private void OnRestartRequested()
        {
            StartNewSession();
        }

        private void EnsureCasesLoaded()
        {
            if (_cases.Count == 0)
            {
                _cases = CaseRepository.LoadAll();
            }
        }

        private CaseFile PickCase()
        {
            if (_cases.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(_caseId))
            {
                foreach (CaseFile caseFile in _cases)
                {
                    if (caseFile.Id == _caseId)
                    {
                        return caseFile;
                    }
                }

                Debug.LogWarning($"Case '{_caseId}' was not found; playing the first available case instead.");
                return _cases[0];
            }

            _caseCursor = (_caseCursor + 1) % _cases.Count;
            return _cases[_caseCursor];
        }

        private IAdvisor CreateAdvisor(out string label)
        {
            _fallbackAdvisor = null;
            if (_advisorOverride != null)
            {
                label = "custom";
                return _advisorOverride;
            }

            var scripted = new ScriptedAdvisor();
            string apiKey = _useClaude ? ApiKeyLocator.Find() : null;
            if (string.IsNullOrEmpty(apiKey))
            {
                label = "scripted";
                return scripted;
            }

            var options = new ClaudeClientOptions
            {
                ApiKey = apiKey,
                Model = string.IsNullOrWhiteSpace(_model) ? ClaudeClientOptions.DefaultModel : _model.Trim(),
                Effort = string.IsNullOrWhiteSpace(_effort) ? "medium" : _effort.Trim(),
            };
            if (!string.IsNullOrWhiteSpace(_apiBaseUrl))
            {
                options.BaseUrl = _apiBaseUrl.Trim();
            }

            var claude = new ClaudeAdvisor(new ClaudeClient(options, new UnityWebRequestTransport()));
            _fallbackAdvisor = new FallbackAdvisor(claude, scripted);
            label = options.Model;
            return _fallbackAdvisor;
        }
    }
}
