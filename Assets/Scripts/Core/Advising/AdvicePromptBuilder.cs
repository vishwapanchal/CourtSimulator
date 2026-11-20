using System.Collections.Generic;
using System.Text;
using CourtSim.Core.Cases;
using CourtSim.Core.Decisions;
using CourtSim.Core.Json;

namespace CourtSim.Core.Advising
{
    /// <summary>Builds the system prompt, the per-request brief and the output schema for <see cref="ClaudeAdvisor"/>.</summary>
    public static class AdvicePromptBuilder
    {
        /// <summary>Identical for every request, so it stays cacheable.</summary>
        public const string SystemPrompt =
            "You are the AI judicial assistant in CourtSim, a training simulation for trainee judges. The cases are fictional "
            + "and set in a fictional common-law jurisdiction; nothing you write is legal advice to a real person.\n\n"
            + "At each decision point you suggest a ruling to the presiding judge. Write the rationale in two to four sentences, "
            + "in the first person (\"I would...\"), and tie it to the specific facts and submissions you rely on. Write plainly, "
            + "as a careful judicial clerk would.\n\n"
            + "The simulation trains judges to scrutinise AI advice, so some suggestions are deliberately flawed. Each request "
            + "says whether your advice is sound or which flaw it must carry, and names the ruling to recommend.\n"
            + "- Sound advice applies the correct law to an accurate reading of the record. Set flaw_explanation to an empty string.\n"
            + "- Flawed advice embeds exactly the flaw described: plausible and subtle, the kind of mistake a competent but "
            + "hurried assistant makes. Don't exaggerate it, don't hint in the rationale that the advice may be wrong, and don't "
            + "hedge more than you would for sound advice. Explain the flaw in flaw_explanation; the judge sees that only in "
            + "the debrief, after ruling.\n\n"
            + "Recommend exactly the option id the request gives. Cite only factor ids from the request's list, and only those "
            + "your rationale actually relies on.";

        public static string BuildBrief(AdviceRequest request)
        {
            CaseFile caseFile = request.Case;
            CaseDecisionPoint point = request.Point;
            AdviceScript target = request.TargetScript;
            var brief = new StringBuilder();

            brief.Append("<case>\n");
            brief.Append("Title: ").Append(caseFile.Title).Append('\n');
            if (caseFile.Defendant != null)
            {
                brief.Append("Accused: ").Append(caseFile.Defendant.Name).Append(", age ").Append(caseFile.Defendant.Age);
                if (caseFile.Defendant.Background.Length > 0)
                {
                    brief.Append(". ").Append(caseFile.Defendant.Background);
                }

                brief.Append('\n');
            }

            brief.Append("Charges: ").Append(string.Join("; ", caseFile.Charges)).Append('\n');
            if (caseFile.Summary.Length > 0)
            {
                brief.Append("Summary: ").Append(caseFile.Summary).Append('\n');
            }

            brief.Append("Record:\n");
            foreach (CaseFact fact in caseFile.Facts)
            {
                brief.Append("- [").Append(fact.Id).Append("] ").Append(fact.Text).Append('\n');
            }

            Exhibit exhibit = point.ExhibitId != null ? caseFile.FindExhibit(point.ExhibitId) : null;
            if (exhibit != null)
            {
                brief.Append("Exhibit ").Append(exhibit.Id).Append(": ").Append(exhibit.Title).Append(". ").Append(exhibit.Description).Append('\n');
                AppendIfPresent(brief, "Provenance", exhibit.Provenance);
                AppendIfPresent(brief, "Defence objection", exhibit.Objection);
            }

            brief.Append("</case>\n\n");

            if (request.PriorRulings.Count > 0)
            {
                brief.Append("<earlier_rulings>\n");
                foreach (DecisionRecord record in request.PriorRulings)
                {
                    CaseDecisionPoint earlier = record.SequenceIndex < caseFile.DecisionPoints.Count ? caseFile.DecisionPoints[record.SequenceIndex] : null;
                    DecisionOption option = earlier?.Definition.FindOption(record.OptionId);
                    brief.Append("- ").Append(earlier?.Definition.Title ?? record.Kind.ToString()).Append(": ").Append(option?.Label ?? record.OptionId).Append('\n');
                }

                brief.Append("</earlier_rulings>\n\n");
            }

            brief.Append("<decision_point>\n");
            brief.Append(point.Definition.Title).Append(". ").Append(point.Definition.Prompt).Append('\n');
            AppendIfPresent(brief, "Prosecution submits", point.ProsecutionSubmission);
            AppendIfPresent(brief, "Defence submits", point.DefenceSubmission);
            brief.Append("Options:\n");
            foreach (DecisionOption option in point.Definition.Options)
            {
                brief.Append("- ").Append(option.Id).Append(": ").Append(option.Label).Append('\n');
            }

            brief.Append("Factors you may cite:\n");
            foreach (CaseFactor factor in point.Factors)
            {
                brief.Append("- ").Append(factor.Id).Append(": ").Append(factor.Text).Append('\n');
            }

            brief.Append("</decision_point>\n\n");

            brief.Append("<instructions>\n");
            brief.Append("Recommend option: ").Append(target.RecommendedOptionId).Append('\n');
            if (request.Flaw == FlawKind.None || target.Flaw == FlawKind.None)
            {
                brief.Append("Advice type: sound.\n");
            }
            else
            {
                brief.Append("Advice type: flawed. ").Append(FlawKinds.Describe(target.Flaw)).Append('\n');
                brief.Append("The specific flaw to embed: ").Append(target.FlawExplanation).Append('\n');
            }

            brief.Append("</instructions>");
            return brief.ToString();
        }

        /// <summary>Output schema: the recommended option is pinned to the target, cited factors to the point's ids.</summary>
        public static JsonValue BuildSchema(AdviceRequest request)
        {
            var factorIds = new List<string>();
            foreach (CaseFactor factor in request.Point.Factors)
            {
                factorIds.Add(factor.Id);
            }

            JsonValue properties = JsonValue.NewObject()
                .Set("recommended_option_id", JsonValue.NewObject().Set("type", "string").Set("enum", JsonValue.StringArray(new[] { request.TargetScript.RecommendedOptionId })))
                .Set("rationale", JsonValue.NewObject().Set("type", "string").Set("description", "Two to four sentences addressed to the judge."))
                .Set("cited_factor_ids", JsonValue.NewObject()
                    .Set("type", "array")
                    .Set("items", JsonValue.NewObject().Set("type", "string").Set("enum", JsonValue.StringArray(factorIds))))
                .Set("confidence", JsonValue.NewObject().Set("type", "string").Set("enum", JsonValue.StringArray(new[] { "low", "medium", "high" })))
                .Set("flaw_explanation", JsonValue.NewObject().Set("type", "string").Set("description", "Debrief explanation of the flaw; empty for sound advice."));

            return JsonValue.NewObject()
                .Set("type", "object")
                .Set("properties", properties)
                .Set("required", JsonValue.StringArray(new[] { "recommended_option_id", "rationale", "cited_factor_ids", "confidence", "flaw_explanation" }))
                .Set("additionalProperties", false);
        }

        private static void AppendIfPresent(StringBuilder brief, string label, string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                brief.Append(label).Append(": ").Append(text).Append('\n');
            }
        }
    }
}
