using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CourtSim.Core.Adaptive;
using CourtSim.Core.Advising;
using CourtSim.Core.Cases;
using CourtSim.Core.Claude;
using CourtSim.Core.Json;
using CourtSim.Core.Logging;
using CourtSim.Core.Ollama;
using CourtSim.Core.Sessions;
using CourtSim.Runtime.Advising;
using CourtSim.Runtime.Cases;
using CourtSim.Runtime.UI;
using UnityEngine;

namespace CourtSim.Runtime.App
{
    /// <summary>Which language model writes the assistant's advice.</summary>
    public enum AdvisorBackend
    {
        /// <summary>A model running in Ollama on this machine: free, offline, no API key.</summary>
        LocalModel,

        /// <summary>Claude over the internet; needs ANTHROPIC_API_KEY.</summary>
        Claude,
    }

    /// <summary>
    /// Runs training sessions in the scene: picks a case, plans which advice is flawed, chooses the advisor
    /// (a local model by default, or Claude), writes the decision log and hands the session to the
    /// <see cref="UIManager"/>. Whichever model is used, the authored offline advice is the fallback.
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
        [Tooltip("Use a language model for advice. Off: always use the authored offline advice.")]
        [SerializeField] private bool _useModel = true;
        [SerializeField] private AdvisorBackend _backend = AdvisorBackend.LocalModel;
        [Tooltip("Seconds to wait for the model before showing the case file's own advice instead.")]
        [SerializeField, Min(1f)] private float _modelWaitSeconds = 25f;
        [Tooltip("Prepare advice for every decision point in the background when a session starts.")]
        [SerializeField] private bool _prefetchAdvice = true;
        [Tooltip("Save generated advice under persistentDataPath/advice-cache so cases seen before load instantly.")]
        [SerializeField] private bool _cacheAdviceOnDisk = true;

        [Header("Local model (Ollama)")]
        [SerializeField] private string _localModel = OllamaClientOptions.DefaultModel;
        [SerializeField] private string _ollamaUrl = OllamaClientOptions.DefaultBaseUrl;

        [Header("Claude")]
        [SerializeField] private string _model = ClaudeClientOptions.DefaultModel;
        [Tooltip("low, medium, high, xhigh or max.")]
        [SerializeField] private string _effort = "medium";
        [Tooltip("Optional proxy base URL, so trainee machines never hold the key. Empty uses api.anthropic.com.")]
        [SerializeField] private string _apiBaseUrl = string.Empty;

        [Header("Logging")]
        [Tooltip("Also saves the trainee's adaptive difficulty between sessions.")]
        [SerializeField] private bool _writeLogFile = true;

        [Header("Adaptive difficulty")]
        [Tooltip("Whose difficulty state to load and save. One file per trainee in persistentDataPath/trainees.")]
        [SerializeField] private string _traineeId = "default";
        [SerializeField] private bool _adaptive = true;

        private readonly MemoryLogSink _memoryLog = new MemoryLogSink();
        private IReadOnlyList<CaseFile> _cases = Array.Empty<CaseFile>();
        private UIManager _ui;
        private CaseSession _session;
        private FallbackAdvisor _fallbackAdvisor;
        private int _caseCursor = -1;
        private AdaptiveController _adaptation;
        private IAdvisor _advisorOverride;

        // Kept across sessions so advice generated (or being generated) for one session is reused by the next.
        private CachingAdvisor _cachingAdvisor;
        private CachingAdvisor _activeCache;
        private string _cachingSettings;
        private CancellationTokenSource _prefetchCancellation;
        private OllamaClient _ollama;
        private bool _gpuChecked;

        public CaseSession Session => _session;

        public IReadOnlyList<CaseFile> Cases => _cases;

        /// <summary>Path of the current session's JSON Lines log, or null if file logging is off.</summary>
        public string LogFilePath { get; private set; }

        /// <summary>Which advisor answers this session: the model id, "scripted" (offline) or "custom".</summary>
        public string AdvisorLabel { get; private set; }

        public static string LogDirectory => Path.Combine(Application.persistentDataPath, "logs");

        /// <summary>The trainee's adaptive difficulty state (loaded on the first session), or null if adaptation is off.</summary>
        public AdaptiveController Adaptation => _adaptation;

        private string TraineeFile => Path.Combine(Application.persistentDataPath, "trainees", (string.IsNullOrWhiteSpace(_traineeId) ? "default" : _traineeId.Trim()) + ".json");

        public event Action<CaseSession> SessionStarted;

        /// <summary>Connects the controller to the UI. Called by <see cref="CourtSimBootstrap"/>.</summary>
        public void Initialize(UIManager ui)
        {
            if (_ui != null)
            {
                _ui.RestartRequested -= OnRestartRequested;
                _ui.CaseChosen -= OnCaseChosen;
            }

            _ui = ui;
            if (_ui != null)
            {
                _ui.RestartRequested += OnRestartRequested;
                _ui.CaseChosen += OnCaseChosen;
                EnsureCasesLoaded();
                _ui.SetCaseCatalog(_cases);
                if (_session != null)
                {
                    _ui.AttachSession(_session);
                }
            }
        }

        /// <summary>Starts a session on the case the player picked.</summary>
        public CaseSession StartCase(string caseId)
        {
            _caseId = caseId ?? string.Empty;
            return StartNewSession();
        }

        /// <summary>Replaces the advisor for later sessions (tests, or a custom backend).</summary>
        public void UseAdvisor(IAdvisor advisor)
        {
            _advisorOverride = advisor;
        }

        /// <summary>Configures case selection before the first session starts.</summary>
        /// <param name="useModel">False forces the authored offline advice (used by tests).</param>
        public void Configure(string caseId, int flawedAdviceCount, int seed, bool useModel, bool writeLogFile)
        {
            _caseId = caseId ?? string.Empty;
            _flawedAdviceCount = Mathf.Clamp(flawedAdviceCount, 0, 3);
            _seed = seed;
            _useModel = useModel;
            _writeLogFile = writeLogFile;
        }

        /// <summary>Selects whose adaptive state is loaded and saved. Call before the first session.</summary>
        public void UseTrainee(string traineeId)
        {
            _traineeId = traineeId;
            _adaptation = null;
        }

        /// <summary>Where this trainee's adaptive state is saved.</summary>
        public string TraineeFilePath => TraineeFile;

        /// <summary>Chooses the language model used when <c>useModel</c> is on.</summary>
        public void UseBackend(AdvisorBackend backend)
        {
            _backend = backend;
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
                        session.NoteAdvisorFallback(session.CurrentIndex, "AI model unavailable, used offline advice: " + error.Message);
                        Debug.LogWarning("[advice] AI advice failed or was too slow; showing the case file's scripted advice instead. " + error.Message);
                    }
                };
            }

            if (_adaptive)
            {
                _adaptation = _adaptation ?? LoadAdaptation();
                _session.UseAdaptation(_adaptation, random);
                _session.Adapted += OnAdapted;
                _session.Completed += _ => SaveAdaptation();
            }

            _session.Start();
            StartPrefetch(_session);
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

        private void OnAdapted(string note)
        {
            Debug.Log("[adaptive] " + note);
            if (_ui != null)
            {
                _ui.ShowToast(note);
            }

            SaveAdaptation();
        }

        // Trainee state is a small JSON file; it is only written when file logging is on, so tests leave no files.
        private AdaptiveController LoadAdaptation()
        {
            try
            {
                if (_writeLogFile && File.Exists(TraineeFile) && JsonReader.TryParse(File.ReadAllText(TraineeFile), out JsonValue json, out _))
                {
                    return AdaptiveController.FromJson(json);
                }
            }
            catch (IOException ex)
            {
                Debug.LogWarning("Could not read trainee state; starting fresh. " + ex.Message);
            }

            return new AdaptiveController();
        }

        private void SaveAdaptation()
        {
            if (!_writeLogFile || _adaptation == null)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(TraineeFile));
                File.WriteAllText(TraineeFile, _adaptation.ToJson().ToJson());
            }
            catch (IOException ex)
            {
                Debug.LogWarning("Could not save trainee state. " + ex.Message);
            }
        }

        private void OnDestroy()
        {
            SaveAdaptation();
            _session?.Cancel();
            _prefetchCancellation?.Cancel();
            _cachingAdvisor?.Dispose();
            if (_ui != null)
            {
                _ui.RestartRequested -= OnRestartRequested;
                _ui.CaseChosen -= OnCaseChosen;
            }
        }

        private void OnCaseChosen(string caseId)
        {
            StartCase(caseId);
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

        // Prepares advice for every decision point, one after another, so it is waiting when the judge rules.
        // The session's own requests go through the same CachingAdvisor and pick up these replies.
        private async void StartPrefetch(CaseSession session)
        {
            _prefetchCancellation?.Cancel();
            _prefetchCancellation = null;
            CachingAdvisor advisor = _activeCache;
            if (!_prefetchAdvice || advisor == null || session == null)
            {
                return;
            }

            var cancellation = new CancellationTokenSource();
            _prefetchCancellation = cancellation;
            IReadOnlyList<AdviceRequest> requests = AdvicePrefetch.PlanRequests(session.Case, session.Plan, session.Adaptation, session.DetailAt);
            DateTime started = DateTime.UtcNow;
            try
            {
                int prepared = await AdvicePrefetch.RunAsync(
                    advisor,
                    requests,
                    cancellation.Token,
                    (request, error) => Debug.LogWarning($"[advice] Could not prepare advice for decision point {request.PointIndex + 1}: {error.Message}"));
                if (!cancellation.IsCancellationRequested)
                {
                    Debug.Log($"[advice] Prepared {prepared} of {requests.Count} suggestions for '{session.Case.Id}' in {(DateTime.UtcNow - started).TotalSeconds:0.0} s.");
                }
            }
            catch (Exception ex)
            {
                // async void: an escaped exception would only reach the console, so report it plainly here.
                Debug.LogWarning("[advice] Preparing advice stopped: " + ex.Message);
            }
        }

        // One CachingAdvisor per model configuration, kept across sessions; changing model or server starts a fresh one.
        private IAdvisor Cached(IAdvisor modelAdvisor, string model, string settings)
        {
            if (_cachingAdvisor == null || _cachingSettings != settings)
            {
                _prefetchCancellation?.Cancel();
                _cachingAdvisor?.Dispose();

                // Like the trainee file, the cache is only written when file logging is on, so tests leave no files.
                IAdviceStore store = _cacheAdviceOnDisk && _writeLogFile ? new FileAdviceStore(FileAdviceStore.DefaultDirectory) : null;
                _cachingAdvisor = new CachingAdvisor(modelAdvisor, model, store);
                _cachingSettings = settings;
                _cachingAdvisor.CacheHit += key => Debug.Log("[advice] Loaded from cache: " + key);
                _cachingAdvisor.Generated += (key, advice) =>
                {
                    Debug.Log("[advice] Generated by the model and cached: " + key);
                    CheckGpuOnce();
                };
            }

            _activeCache = _cachingAdvisor;

            // A slow or silent model must never leave the panel empty: after the wait, the scripted advice is shown.
            return new TimeoutAdvisor(_cachingAdvisor, TimeSpan.FromSeconds(Mathf.Max(1f, _modelWaitSeconds)));
        }

        // Ollama sometimes loads the model into system memory instead of the GPU, which makes advice ten times
        // slower. Once the model has answered (so it is loaded), ask Ollama where it put it.
        private async void CheckGpuOnce()
        {
            if (_gpuChecked || _ollama == null)
            {
                return;
            }

            _gpuChecked = true;
            try
            {
                OllamaModelStatus status = await _ollama.GetModelStatusAsync(CancellationToken.None);
                if (status.Warning != null)
                {
                    Debug.LogWarning("[advice] " + status.Warning);
                }
                else if (status.Placement == ModelPlacement.Gpu)
                {
                    Debug.Log($"[advice] Ollama has {status.Name} on the GPU ({status.VramBytes / (1024 * 1024)} MB in GPU memory).");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[advice] Could not check whether Ollama is using the GPU: " + ex.Message);
            }
        }

        private IAdvisor CreateAdvisor(out string label)
        {
            _fallbackAdvisor = null;
            _ollama = null;
            _activeCache = null;
            if (_advisorOverride != null)
            {
                label = "custom";
                return _advisorOverride;
            }

            var scripted = new ScriptedAdvisor();
            if (!_useModel)
            {
                label = "scripted";
                return scripted;
            }

            if (_backend == AdvisorBackend.LocalModel)
            {
                var local = new OllamaClientOptions
                {
                    Model = string.IsNullOrWhiteSpace(_localModel) ? OllamaClientOptions.DefaultModel : _localModel.Trim(),
                    BaseUrl = string.IsNullOrWhiteSpace(_ollamaUrl) ? OllamaClientOptions.DefaultBaseUrl : _ollamaUrl.Trim(),
                };

                // If Ollama isn't running, each request fails fast and the offline advice is shown instead.
                _ollama = new OllamaClient(local, new UnityWebRequestTransport());
                IAdvisor localAdvisor = Cached(new LocalModelAdvisor(_ollama), local.Model, "ollama|" + local.BaseUrl + "|" + local.Model);
                _fallbackAdvisor = new FallbackAdvisor(localAdvisor, scripted);
                label = local.Model;
                return _fallbackAdvisor;
            }

            string apiKey = ApiKeyLocator.Find();
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogWarning("Claude was selected but ANTHROPIC_API_KEY is not set; using offline advice.");
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
            IAdvisor cachedClaude = Cached(claude, options.Model, "claude|" + options.BaseUrl + "|" + options.Model + "|" + options.Effort);
            _fallbackAdvisor = new FallbackAdvisor(cachedClaude, scripted);
            label = options.Model;
            return _fallbackAdvisor;
        }
    }
}
