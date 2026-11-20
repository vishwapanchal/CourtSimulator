using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CourtSim.Core.Advising;
using CourtSim.Core.Cases;
using CourtSim.Core.Claude;
using CourtSim.Core.Logging;
using CourtSim.Core.Sessions;
using CourtSim.Runtime.Advising;
using CourtSim.Runtime.Cases;
using UnityEditor;
using UnityEngine;

namespace CourtSim.EditorTools
{
    /// <summary>
    /// Plays one case against the real Claude API from the command line (no fallback, so failures show)
    /// and writes a Markdown transcript plus the JSON Lines decision log. Used to verify live advice.
    /// </summary>
    /// <remarks>
    /// Run with <c>tools/unity.ps1 -Task live</c>. Arguments: <c>-liveCase id</c>, <c>-liveFlawed n</c>,
    /// <c>-liveSeed n</c>, <c>-liveChoices follow,reject,sound</c> (one per decision point: follow the advice,
    /// pick anything but the advice, or pick the authored sound option) and <c>-liveOut folder</c>.
    /// </remarks>
    public static class LiveSessionRunner
    {
        private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(8);

        private static CaseSession _session;
        private static ClaudeAdvisor _advisor;
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
            string apiKey = ApiKeyLocator.Find();
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("ANTHROPIC_API_KEY is not set; a live session needs it.");
            }

            string caseId = Argument("-liveCase", "hale-burglary");
            int flawed = int.Parse(Argument("-liveFlawed", "1"));
            int seed = int.Parse(Argument("-liveSeed", "11"));
            _choices = Argument("-liveChoices", "sound,sound,sound").Split(',');
            _outFolder = Argument("-liveOut", Path.Combine("Logs", "batch", "live"));
            Directory.CreateDirectory(_outFolder);

            CaseFile caseFile = null;
            foreach (CaseFile candidate in CaseRepository.LoadAll())
            {
                if (candidate.Id == caseId)
                {
                    caseFile = candidate;
                }
            }

            if (caseFile == null)
            {
                throw new InvalidOperationException($"Case '{caseId}' not found.");
            }

            var options = new ClaudeClientOptions { ApiKey = apiKey };
            _advisor = new ClaudeAdvisor(new ClaudeClient(options, new UnityWebRequestTransport()));
            AdvicePlan plan = AdvicePlanner.Create(caseFile, flawed, new System.Random(seed));
            string logPath = Path.Combine(_outFolder, $"live-{caseFile.Id}.jsonl");
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }

            _session = new CaseSession(caseFile, plan, _advisor, new JsonLinesFileSink(logPath), options.Model);
            _transcript = new StringBuilder();
            _transcript.Append("# Live session: ").Append(caseFile.Title).Append("\n\n");
            _transcript.Append("Model: `").Append(options.Model).Append("`, flawed points planned: ").Append(plan.FlawedCount)
                .Append(", started ").Append(DateTime.UtcNow.ToString("u")).Append("\n\n");
            _startedAt = DateTime.UtcNow;
            _session.Start();
            _pending = _session.RequestAdvice(0);
            Debug.Log($"[live] Session started on {caseFile.Id}; waiting for advice 1 of {caseFile.DecisionPoints.Count}.");
        }

        private static void Tick()
        {
            if (DateTime.UtcNow - _startedAt > Deadline)
            {
                Finish(false, "Timed out waiting for the Claude API.");
                return;
            }

            if (_pending == null || !_pending.IsCompleted)
            {
                return;
            }

            int index = _session.CurrentIndex;
            Advice advice = _session.CurrentAdvice;
            if (advice == null || advice.Source != AdviceSource.Claude)
            {
                Finish(false, $"Decision {index + 1}: no live advice ({_session.AdviceErrorAt(index) ?? "unknown error"}).");
                return;
            }

            CaseDecisionPoint point = _session.CurrentPoint;
            string choice = index < _choices.Length ? _choices[index].Trim() : "sound";
            string optionId = Choose(point, advice, choice);
            ClaudeMessageResponse response = _advisor.LastResponse;

            _transcript.Append("## ").Append(index + 1).Append(". ").Append(point.Definition.Title).Append("\n\n");
            _transcript.Append("- Planned flaw: `").Append(FlawKinds.ToWireName(advice.Flaw)).Append("`\n");
            _transcript.Append("- Claude recommended: **").Append(Label(point, advice.RecommendedOptionId)).Append("** (confidence ")
                .Append(AdviceConfidences.ToWireName(advice.Confidence)).Append(")\n");
            _transcript.Append("- Rationale (model output): ").Append(advice.Rationale).Append('\n');
            if (advice.IsFlawed)
            {
                _transcript.Append("- Flaw explanation (debrief only): ").Append(advice.FlawExplanation).Append('\n');
            }

            if (response != null)
            {
                _transcript.Append("- Response: model `").Append(response.Model).Append("`, request id `").Append(response.RequestId)
                    .Append("`, tokens in/out ").Append(response.InputTokens).Append('/').Append(response.OutputTokens).Append('\n');
            }

            DecisionLogEntry entry = _session.Submit(optionId);
            _transcript.Append("- Choice policy `").Append(choice).Append("` → ruled **").Append(Label(point, optionId)).Append("** (")
                .Append(LogNames.Quality(entry.Quality)).Append("), outcome `").Append(LogNames.Outcome(entry.Outcome)).Append("`\n\n");
            Debug.Log($"[live] Decision {index + 1}: advice {advice.RecommendedOptionId} ({FlawKinds.ToWireName(advice.Flaw)}), ruled {optionId} → {LogNames.Outcome(entry.Outcome)}");

            if (_session.IsComplete)
            {
                _transcript.Append("## Debrief\n\n").Append(Debrief.Headline(_session.Summary)).Append("\n\n```json\n")
                    .Append(_session.Summary.ToJson().ToJson()).Append("\n```\n");
                Finish(true, null);
                return;
            }

            _pending = _session.RequestAdvice(_session.CurrentIndex);
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
