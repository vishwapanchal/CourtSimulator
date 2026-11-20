using System.Collections.Generic;
using CourtSim.Core.Cases;
using CourtSim.Core.Json;

namespace CourtSim.Core.Advising
{
    /// <summary>
    /// Turns a language model's JSON reply (shape: <see cref="AdvicePromptBuilder.BuildSchema"/>) into
    /// <see cref="Advice"/>, checking it against the case. Shared by the Claude and local-model advisors so
    /// both obey exactly the same rules.
    /// </summary>
    public static class AdviceReply
    {
        /// <summary>Validates the reply. Throws <see cref="AdvisorException"/> if it can't be used.</summary>
        public static Advice Parse(string replyJson, AdviceRequest request, AdviceScript target, AdviceSource source)
        {
            if (!JsonReader.TryParse(replyJson, out JsonValue reply, out string error) || reply.Kind != JsonKind.Object)
            {
                throw new AdvisorException("The model's reply was not a JSON object: " + error);
            }

            // The planned ruling is ground truth; a model that recommends anything else is not used.
            string recommended = reply.GetString("recommended_option_id");
            if (recommended != target.RecommendedOptionId)
            {
                throw new AdvisorException($"The model recommended '{recommended}' instead of the planned '{target.RecommendedOptionId}'.");
            }

            string rationale = reply.GetString("rationale");
            if (string.IsNullOrWhiteSpace(rationale))
            {
                throw new AdvisorException("The model's reply had no rationale.");
            }

            // Keep only factor ids that exist at this decision point, without duplicates.
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

            // The debrief must always explain a flaw, so fall back to the author's explanation if the model gave none.
            string explanation = reply.GetString("flaw_explanation", string.Empty);
            if (target.Flaw != FlawKind.None && string.IsNullOrWhiteSpace(explanation))
            {
                explanation = target.FlawExplanation;
            }
            else if (target.Flaw == FlawKind.None)
            {
                explanation = string.Empty;
            }

            return new Advice(request.Point.Kind, recommended, rationale.Trim(), cited, confidence, target.Flaw, explanation.Trim(), source);
        }
    }
}
