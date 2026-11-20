using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CourtSim.Core.Cases;
using CourtSim.Core.Claude;
using CourtSim.Core.Json;

namespace CourtSim.Core.Advising
{
    /// <summary>
    /// Generates case-specific advice with Claude. The planned flaw and the target ruling come from the case
    /// file; Claude writes the reasoning that carries them. Replies are validated against the case before use.
    /// </summary>
    public sealed class ClaudeAdvisor : IAdvisor
    {
        private readonly ClaudeClient _client;

        public ClaudeAdvisor(ClaudeClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>The last successful reply's metadata, for logging.</summary>
        public ClaudeMessageResponse LastResponse { get; private set; }

        public async Task<Advice> GetAdviceAsync(AdviceRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            AdviceScript target = request.TargetScript ?? throw new AdvisorException($"Case '{request.Case.Id}' has no authored advice for decision point {request.PointIndex}.");
            var message = new ClaudeMessageRequest(
                AdvicePromptBuilder.SystemPrompt,
                AdvicePromptBuilder.BuildBrief(request),
                AdvicePromptBuilder.BuildSchema(request));

            ClaudeMessageResponse response = await _client.CreateMessageAsync(message, cancellationToken);
            Advice advice = Parse(response.Text, request, target);
            LastResponse = response;
            return advice;
        }

        /// <summary>Validates Claude's JSON reply against the request. Throws <see cref="AdvisorException"/> if unusable.</summary>
        public static Advice Parse(string replyJson, AdviceRequest request, AdviceScript target)
        {
            if (!JsonReader.TryParse(replyJson, out JsonValue reply, out string error) || reply.Kind != JsonKind.Object)
            {
                throw new AdvisorException("Claude's reply was not a JSON object: " + error);
            }

            string recommended = reply.GetString("recommended_option_id");
            if (recommended != target.RecommendedOptionId)
            {
                throw new AdvisorException($"Claude recommended '{recommended}' instead of the planned '{target.RecommendedOptionId}'.");
            }

            string rationale = reply.GetString("rationale");
            if (string.IsNullOrWhiteSpace(rationale))
            {
                throw new AdvisorException("Claude's reply had no rationale.");
            }

            var cited = new List<string>();
            JsonValue citedJson = reply.Get("cited_factor_ids");
            if (citedJson != null)
            {
                foreach (JsonValue item in citedJson.Items)
                {
                    string id = item.AsString();
                    if (id != null && request.Point.FindFactor(id) != null && !cited.Contains(id))
                    {
                        cited.Add(id);
                    }
                }
            }

            if (!AdviceConfidences.TryParse(reply.GetString("confidence"), out AdviceConfidence confidence))
            {
                confidence = target.Confidence;
            }

            string explanation = reply.GetString("flaw_explanation", string.Empty);
            if (target.Flaw != FlawKind.None && string.IsNullOrWhiteSpace(explanation))
            {
                explanation = target.FlawExplanation;
            }
            else if (target.Flaw == FlawKind.None)
            {
                explanation = string.Empty;
            }

            return new Advice(request.Point.Kind, recommended, rationale.Trim(), cited, confidence, target.Flaw, explanation.Trim(), AdviceSource.Claude);
        }
    }
}
