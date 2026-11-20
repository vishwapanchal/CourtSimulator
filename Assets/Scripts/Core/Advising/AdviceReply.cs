using System.Collections.Generic;
using CourtSim.Core.Cases;
using CourtSim.Core.Decisions;
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

            // Small models sometimes give the game away ("I am applying a flawed rule"). Such advice would teach
            // nothing, so it is rejected and the authored advice is used instead.
            if (target.Flaw != FlawKind.None && RevealsFlaw(rationale))
            {
                throw new AdvisorException("The model's flawed rationale openly admits the flaw.");
            }

            // Trainees should read plain language, not internal ids such as "bail.grant" or "early_plea".
            string leakedId = FindLeakedId(rationale, request);
            if (leakedId != null)
            {
                throw new AdvisorException($"The model's rationale contains the internal id '{leakedId}'.");
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

        /// <summary>
        /// The first option or factor id written in the rationale, or null. Only ids with "." or "_" are checked,
        /// because plain-word ids like "ties" are ordinary English.
        /// </summary>
        public static string FindLeakedId(string rationale, AdviceRequest request)
        {
            var ids = new List<string>();
            foreach (DecisionOption option in request.Point.Definition.Options)
            {
                ids.Add(option.Id);
            }

            foreach (CaseFactor factor in request.Point.Factors)
            {
                ids.Add(factor.Id);
            }

            foreach (string id in ids)
            {
                if ((id.Contains(".") || id.Contains("_")) && rationale.Contains(id))
                {
                    return id;
                }
            }

            return null;
        }

        /// <summary>True if the rationale names its own flaw, e.g. "flawed", "I am misapplying" or "incorrectly".</summary>
        public static bool RevealsFlaw(string rationale)
        {
            string text = rationale.ToLowerInvariant();
            foreach (string giveaway in new[] { "flaw", "mistake", "incorrect", "erroneous", "misappl", "misstat", "misread", "wrongly", "proceed as if", "deliberately wrong" })
            {
                if (text.Contains(giveaway))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
