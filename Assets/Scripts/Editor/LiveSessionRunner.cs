using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CourtSim.Core.Adaptive;
using CourtSim.Core.Advising;
using CourtSim.Core.Cases;
using CourtSim.Core.Claude;
using CourtSim.Core.Logging;
using CourtSim.Core.Ollama;
using CourtSim.Core.Sessions;
using CourtSim.Runtime.Advising;
using CourtSim.Runtime.Cases;
using UnityEditor;
using UnityEngine;

namespace CourtSim.EditorTools
{
    /// <summary>
    /// Plays one case against a real language model from the command line (no fallback, so failures show)
    /// and writes a Markdown transcript plus the JSON Lines decision log. Used to verify live advice.
    /// </summary>
    /// <remarks>
    /// Run with <c>tools/unity.ps1 -Task live</c>. Arguments: <c>-liveBackend local|claude</c> (default local,
    /// the Ollama model), <c>-liveModel name</c>, <c>-liveCase id</c>, <c>-liveFlawed n</c>, <c>-liveSeed n</c>,
    /// <c>-liveCases a,b</c> with <c>-liveFlawed n,m</c> (several cases, one shared trainee), <c>-liveAdaptive on|off</c>,
    /// <c>-liveChoices follow,reject,sound</c> (one per decision point across all cases: follow the advice, pick anything but
    /// the advice, or pick the authored sound option) and <c>-liveOut folder</c>.
    /// </remarks>
    public static class LiveSessionRunner
    {
        private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(15);

        private static readonly List<CaseFile> Cases = new List<CaseFile>();
        private static int[] _flawedPerCase;
        private static int _caseIndex;
        private static int _decisionCount;
        private static string _model;
        private static System.Random _random;
        private static AdaptiveController _adaptation;
        private static CaseSession _session;
        private static IAdvisor _advisor;
        private static IAdvisor _primary;
        private static AdviceSource _expectedSource;
        private static string[] _choices;
        private static string _outFolder;
        private static StringBuilder _transcript;
        private static DateTime _startedAt;
        private static Task<Advice> _pending;

        /// <summary>Entry point for <c>-executeMethod</c>. Do not pass <c>-quit</c>: it exits when the session ends.</summary>
        public static void RunFromCommandLine()
        {
            try
            {
                Begin();
                EditorApplication.update += Tick;
            }
            catch (Exception ex)
            {
                Finish(false, ex.Message);
            }
        }

        private static void Begin()
        {
            // -liveCases a,b plays several cases in a row with one shared adaptive controller (one trainee).
            string[] caseIds = Argument("-liveCases", Argument("-liveCase", "state-v-sahil-kadam")).Split(',');
            string[] flawed = Argument("-liveFlawed", "1").Split(',');
            _random = new System.Random(int.Parse(Argument("-liveSeed", "11")));
            _choices = Argument("-liveChoices", "sound,sound,sound").Split(',');
            _outFolder = Argument("-liveOut", Path.Combine("Logs", "batch", "live"));
            Directory.CreateDirectory(_outFolder);

            IReadOnlyList<CaseFile> all = CaseRepository.LoadAll();
            _flawedPerCase = new int[caseIds.Length];
            for (int i = 0; i < caseIds.Length; i++)
            {
                CaseFile match = null;
                foreach (CaseFile candidate in all)
                {
                    if (candidate.Id == caseIds[i].Trim())
                    {
                        match = candidate;
                    }
                }

                Cases.Add(match ?? throw new InvalidOperationException($"Case '{caseIds[i]}' not found."));
                _flawedPerCase[i] = int.Parse(flawed[Math.Min(i, flawed.Length - 1)]);
            }

            _model = CreateAdvisor(Argument("-liveBackend", "local"), Argument("-liveModel", null));
            ((FallbackAdvisor)_advisor).PrimaryFailed += error => _session?.NoteAdvisorFallback(_session.CurrentIndex, error.Message);
            _adaptation = Argument("-liveAdaptive", "on") == "off" ? null : new AdaptiveController();
            _transcript = new StringBuilder();
            _transcript.Append("# Live session\n\nModel: `").Append(_model).Append("`, adaptive difficulty: ")
                .Append(_adaptation != null ? "on" : "off").Append(", started ").Append(DateTime.UtcNow.ToString("u")).Append("\n\n");
            _startedAt = DateTime.UtcNow;
            StartCase(0);
        }

        private static void StartCase(int index)
        {
            _caseIndex = index;
            CaseFile caseFile = Cases[index];
            AdvicePlan plan = AdvicePlanner.Create(caseFile, _flawedPerCase[index], _random);
            string logPath = Path.Combine(_outFolder, $"live-{caseFile.Id}.jsonl");
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }

            _session = new CaseSession(caseFile, plan, _advisor, new JsonLinesFileSink(logPath), _model);
            if (_adaptation != null)
            {
                _session.UseAdaptation(_adaptation, _random);
                _session.Adapted += note =>
                {
                    _transcript.Append("> **Adaptation fired:** ").Append(note).Append(" State: `").Append(_adaptation.ToJson().ToJson()).Append("`\n\n");
                    Debug.Log("[live] ADAPTATION: " + note);
                };
            }

            _transcript.Append("# Case ").Append(index + 1).Append(": ").Append(caseFile.Title).Append("\n\nFlawed points planned: ").Append(plan.FlawedCount).Append("\n\n");
            _session.Start();
            _pending = _session.RequestAdvice(0);
            Debug.Log($"[live] Case {index + 1} of {Cases.Count} started: {caseFile.Id}.");
        }

        private static void Tick()
        {
            if (DateTime.UtcNow - _startedAt > Deadline)
            {
                Finish(false, "Timed out waiting for the language model.");
                return;
            }

            if (_pending == null || !_pending.IsCompleted)
            {
                return;
            }

            int index = _session.CurrentIndex;
            Advice advice = _session.CurrentAdvice;
            if (advice == null)
            {
                Finish(false, $"Decision {index + 1}: no advice ({_session.AdviceErrorAt(index) ?? "unknown error"}).");
                return;
            }

            // Like the game, a reply the model got wrong falls back to the authored advice; the transcript says so.
            string source = advice.Source == _expectedSource ? "model output" : "authored fallback: " + _session.AdviceErrorAt(index);

            CaseDecisionPoint point = _session.CurrentPoint;
            string choice = _decisionCount < _choices.Length ? _choices[_decisionCount].Trim() : "sound";
            _decisionCount++;
            string optionId = Choose(point, advice, choice);

            _transcript.Append("## ").Append(index + 1).Append(". ").Append(point.Definition.Title).Append("\n\n");
            _transcript.Append("- Planned flaw: `").Append(FlawKinds.ToWireName(advice.Flaw)).Append("`, explanation detail: `")
                .Append(_session.DetailAt(index) == ExplanationDetail.Detailed ? "detailed" : "standard").Append("`\n");
            _transcript.Append("- Model recommended: **").Append(Label(point, advice.RecommendedOptionId)).Append("** (confidence ")
                .Append(AdviceConfidences.ToWireName(advice.Confidence)).Append(")\n");
            _transcript.Append("- Rationale (").Append(source).Append("): ").Append(advice.Rationale.Replace("\n", " ")).Append('\n');
            if (advice.IsFlawed)
            {
                _transcript.Append("- Flaw explanation (debrief only): ").Append(advice.FlawExplanation).Append('\n');
            }

            if (advice.Source == _expectedSource)
            {
                _transcript.Append("- Response: ").Append(DescribeLastResponse()).Append('\n');
            }

            DecisionLogEntry entry = _session.Submit(optionId);
            _transcript.Append("- Choice policy `").Append(choice).Append("` → ruled **").Append(Label(point, optionId)).Append("** (")
                .Append(LogNames.Quality(entry.Quality)).Append("), outcome `").Append(LogNames.Outcome(entry.Outcome)).Append("`\n\n");
            Debug.Log($"[live] Decision {_decisionCount}: advice {advice.RecommendedOptionId} ({FlawKinds.ToWireName(advice.Flaw)}, {(_session.DetailAt(index) == ExplanationDetail.Detailed ? "detailed" : "standard")}), ruled {optionId} → {LogNames.Outcome(entry.Outcome)}");

            if (_session.IsComplete)
            {
                _transcript.Append("## Debrief\n\n").Append(Debrief.Headline(_session.Summary)).Append("\n\n```json\n")
                    .Append(_session.Summary.ToJson().ToJson()).Append("\n```\n\n");
                if (_caseIndex + 1 < Cases.Count)
                {
                    StartCase(_caseIndex + 1);
                    return;
                }

                Finish(true, null);
                return;
            }

            _pending = _session.RequestAdvice(_session.CurrentIndex);
        }
        /// <summary>Builds the advisor for the chosen backend and returns the model name.</summary>
        private static string CreateAdvisor(string backend, string model)
        {
            if (backend == "claude")
            {
                string apiKey = ApiKeyLocator.Find();
                if (string.IsNullOrEmpty(apiKey))
                {
                    throw new InvalidOperationException("ANTHROPIC_API_KEY is not set; the Claude backend needs it.");
                }

                var claude = new ClaudeClientOptions { ApiKey = apiKey, Model = model ?? ClaudeClientOptions.DefaultModel };
                _primary = new ClaudeAdvisor(new ClaudeClient(claude, new UnityWebRequestTransport()));
                _advisor = new FallbackAdvisor(_primary, new ScriptedAdvisor());
                _expectedSource = AdviceSource.Claude;
                return claude.Model;
            }

            var local = new OllamaClientOptions { Model = model ?? OllamaClientOptions.DefaultModel };
            _primary = new LocalModelAdvisor(new OllamaClient(local, new UnityWebRequestTransport()));
            _advisor = new FallbackAdvisor(_primary, new ScriptedAdvisor());
            _expectedSource = AdviceSource.LocalModel;
            return local.Model;
        }

        private static string DescribeLastResponse()
        {
            if (_primary is ClaudeAdvisor claude && claude.LastResponse != null)
            {
                ClaudeMessageResponse r = claude.LastResponse;
                return $"model `{r.Model}`, request id `{r.RequestId}`, tokens in/out {r.InputTokens}/{r.OutputTokens}";
            }

            if (_primary is LocalModelAdvisor local && local.LastResponse != null)
            {
                OllamaChatResponse r = local.LastResponse;
                return $"model `{r.Model}` (local), tokens in/out {r.PromptTokens}/{r.OutputTokens}, {r.TotalSeconds:F1} s";
            }

            return "n/a";
        }

        private static string Choose(CaseDecisionPoint point, Advice advice, string choice)
        {
            if (choice == "follow")
            {
                return advice.RecommendedOptionId;
            }

            string sound = null;
            string other = null;
            foreach (OptionAssessment assessment in point.Assessments)
            {
                if (assessment.Quality == OptionQuality.Sound && sound == null)
                {
                    sound = assessment.OptionId;
                }

                if (assessment.OptionId != advice.RecommendedOptionId && other == null)
                {
                    other = assessment.OptionId;
                }
            }

            if (choice == "reject")
            {
                // Prefer the sound option when it differs from the advice, so a rejected flaw is a correct ruling.
                return sound != null && sound != advice.RecommendedOptionId ? sound : other;
            }

            return sound ?? advice.RecommendedOptionId;
        }

        private static string Label(CaseDecisionPoint point, string optionId)
        {
            return point.Definition.FindOption(optionId)?.Label ?? optionId;
        }

        private static void Finish(bool success, string error)
        {
            EditorApplication.update -= Tick;
            if (_transcript != null)
            {
                if (!success)
                {
                    _transcript.Append("\n**FAILED:** ").Append(error).Append('\n');
                }

                string path = Path.Combine(_outFolder ?? Path.Combine("Logs", "batch", "live"), "live-transcript.md");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, _transcript.ToString(), new UTF8Encoding(false));
                Debug.Log("[live] Transcript written to " + Path.GetFullPath(path));
            }

            if (success)
            {
                Debug.Log("[live] Session complete.");
            }
            else
            {
                Debug.LogError("[live] " + error);
            }

            EditorApplication.Exit(success ? 0 : 1);
        }

        private static string Argument(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }

            return fallback;
        }
    }
}
