using System;
using System.Collections.Generic;
using System.IO;
using CourtSim.Core.Cases;
using CourtSim.Core.Json;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    /// <summary>
    /// A small, valid <c>courtsim.case/1</c> case built in code, plus access to the shipped case files.
    /// Tests mutate the <see cref="JsonValue"/> tree from <see cref="BuildJson"/> to produce invalid variants.
    /// </summary>
    internal static class TestCases
    {
        public const string CaseId = "test-case";

        public static readonly IReadOnlyList<string> ShippedCaseIds = new[] { "state-v-sahil-kadam", "state-v-mahesh-rathod", "state-v-kunal-bisht", "state-v-arif-mulla", "state-v-basavaraj-angadi", "state-v-darshan-hegde", "state-v-girish-rao", "state-v-joseph-varghese", "state-v-kiran-biradar", "state-v-lokesh-naik", "state-v-manjunath-patil", "state-v-manjunath-wadeyar", "state-v-naveen-kulkarni", "state-v-naveen-upadhya", "state-v-pradeep-lamani", "state-v-prakash-shetty", "state-v-ramesh-gaonkar", "state-v-rohan-desai", "state-v-santosh-talwar", "state-v-shivanand-ingale", "state-v-shivappa-doddamani" };

        /// <summary>
        /// Point 0 (bail) has flaw misread_fact; point 1 (evidence, exhibit P-1) has misstated_law and omitted_factor;
        /// point 2 (sentencing) has improper_factor, whose advice omits "confidence".
        /// </summary>
        public static JsonValue BuildJson()
        {
            JsonValue bail = Point(
                "bail",
                "Bail application",
                "The accused applies for bail.",
                null,
                ArrayOf(Factor("ties"), Factor("record"), Factor("silence")),
                ArrayOf(
                    Option("bail.grant", "Release on conditions", "sound"),
                    Option("bail.deny", "Remand in custody", "unsound")),
                ArrayOf(
                    AdviceJson("none", "bail.grant", "high", new[] { "ties", "record" }, string.Empty),
                    AdviceJson("misread_fact", "bail.deny", "medium", new[] { "record" }, "Invents a failure to appear.")));

            JsonValue evidence = Point(
                "evidence_admissibility",
                "Admissibility of the CCTV",
                "The defence objects to exhibit P-1.",
                "P-1",
                ArrayOf(Factor("hash"), Factor("gap")),
                ArrayOf(
                    Option("evidence.admit", "Admit the recording", "sound"),
                    Option("evidence.admit_limited", "Admit with a caution", "defensible"),
                    Option("evidence.exclude", "Exclude the recording", "unsound")),
                ArrayOf(
                    AdviceJson("none", "evidence.admit", "high", new[] { "hash" }, string.Empty),
                    AdviceJson("misstated_law", "evidence.exclude", "high", new[] { "gap" }, "Treats an unbroken chain as a precondition."),
                    AdviceJson("omitted_factor", "evidence.exclude", "low", new[] { "gap" }, "Ignores the hash match.")));

            JsonValue sentencing = Point(
                "sentencing",
                "Sentence",
                "The accused pleaded guilty.",
                null,
                ArrayOf(Factor("plea"), Factor("planning")),
                ArrayOf(
                    Option("sentence.community", "Community order", "sound"),
                    Option("sentence.custody", "Short custody", "defensible")),
                ArrayOf(
                    AdviceJson("none", "sentence.community", "medium", new[] { "plea" }, string.Empty),
                    AdviceJson("improper_factor", "sentence.custody", null, new[] { "planning" }, "Relies on the accused's silence.")));

            return JsonValue.NewObject()
                .Set("schema", CaseFile.SchemaId)
                .Set("id", CaseId)
                .Set("title", "State v. Alex Doe")
                .Set("summary", "A test burglary.")
                .Set("difficulty", 2)
                .Set("jurisdictionNote", "Fictional jurisdiction.")
                .Set("defendant", JsonValue.NewObject().Set("name", "Alex Doe").Set("age", 30).Set("background", "Works locally."))
                .Set("charges", JsonValue.StringArray(new[] { "Burglary" }))
                .Set("facts", ArrayOf(
                    JsonValue.NewObject().Set("id", "F1").Set("text", "The door was forced."),
                    JsonValue.NewObject().Set("id", "F2").Set("text", "Laptops were found in his car.")))
                .Set("exhibits", ArrayOf(JsonValue.NewObject()
                    .Set("id", "P-1")
                    .Set("title", "CCTV recording")
                    .Set("description", "Footage of the rear door.")
                    .Set("tenderedBy", "Prosecution")
                    .Set("provenance", "Exported to a USB drive.")
                    .Set("objection", "The chain of custody is broken.")))
                .Set("decisionPoints", ArrayOf(bail, evidence, sentencing));
        }

        public static string ValidJson()
        {
            return BuildJson().ToJson();
        }

        /// <summary>The valid test case, parsed. Fails the test if it does not load.</summary>
        public static CaseFile Load()
        {
            return LoadJson(ValidJson());
        }

        public static CaseFile LoadJson(string json)
        {
            CaseReadResult result = CaseFileReader.Read(json);
            if (!result.Success)
            {
                Assert.Fail("Test case did not load:\n" + string.Join("\n", result.Errors));
            }

            return result.Case;
        }

        public static JsonValue PointAt(JsonValue root, int index)
        {
            return root.Get("decisionPoints").Items[index];
        }

        public static JsonValue AdviceAt(JsonValue root, int pointIndex, int adviceIndex)
        {
            return PointAt(root, pointIndex).Get("advice").Items[adviceIndex];
        }

        public static JsonValue OptionAt(JsonValue root, int pointIndex, int optionIndex)
        {
            return PointAt(root, pointIndex).Get("options").Items[optionIndex];
        }

        /// <summary>An array holding <paramref name="items"/>.</summary>
        public static JsonValue ArrayOf(params JsonValue[] items)
        {
            JsonValue array = JsonValue.NewArray();
            foreach (JsonValue item in items)
            {
                array.Add(item);
            }

            return array;
        }

        /// <summary>Asserts that some error starts with <paramref name="path"/> and contains <paramref name="fragment"/>.</summary>
        public static void AssertHasError(CaseReadResult result, string path, string fragment)
        {
            Assert.That(result.Success, Is.False, "Expected the case to be rejected.");
            Assert.That(result.Case, Is.Null);
            foreach (string error in result.Errors)
            {
                if (error.StartsWith(path + ":", StringComparison.Ordinal) && error.Contains(fragment))
                {
                    return;
                }
            }

            Assert.Fail($"No error at '{path}' containing '{fragment}'. Errors:\n{string.Join("\n", result.Errors)}");
        }

        /// <summary>
        /// The Unity project root: the nearest ancestor with <c>Assets/Resources/Cases</c>, searched from the test
        /// directory (dotnet test runs from bin/) and from the working directory (Unity runs from the project root).
        /// </summary>
        public static string FindProjectRoot()
        {
            foreach (string start in new[] { TestContext.CurrentContext.TestDirectory, Directory.GetCurrentDirectory() })
            {
                string directory = start;
                while (!string.IsNullOrEmpty(directory))
                {
                    if (Directory.Exists(Path.Combine(directory, "Assets", "Resources", "Cases")))
                    {
                        return directory;
                    }

                    directory = Path.GetDirectoryName(directory);
                }
            }

            Assert.Fail("Could not find the project root (a folder containing Assets/Resources/Cases).");
            return null;
        }

        public static string ShippedCasePath(string caseId)
        {
            return Path.Combine(FindProjectRoot(), "Assets", "Resources", "Cases", caseId + ".json");
        }

        private static JsonValue Point(string kind, string title, string prompt, string exhibitId, JsonValue factors, JsonValue options, JsonValue advice)
        {
            JsonValue point = JsonValue.NewObject()
                .Set("kind", kind)
                .Set("title", title)
                .Set("prompt", prompt);
            if (exhibitId != null)
            {
                point.Set("exhibitId", exhibitId);
            }

            return point
                .Set("submissions", JsonValue.NewObject().Set("prosecution", "Prosecution says " + kind + ".").Set("defence", "Defence says " + kind + "."))
                .Set("factors", factors)
                .Set("options", options)
                .Set("advice", advice);
        }

        private static JsonValue Factor(string id)
        {
            return JsonValue.NewObject().Set("id", id).Set("text", "Factor " + id + ".");
        }

        private static JsonValue Option(string id, string label, string quality)
        {
            return JsonValue.NewObject()
                .Set("id", id)
                .Set("label", label)
                .Set("quality", quality)
                .Set("feedback", "Feedback for " + id + ".");
        }

        private static JsonValue AdviceJson(string flaw, string recommends, string confidence, string[] cites, string flawExplanation)
        {
            JsonValue advice = JsonValue.NewObject()
                .Set("flaw", flaw)
                .Set("recommends", recommends);
            if (confidence != null)
            {
                advice.Set("confidence", confidence);
            }

            return advice
                .Set("rationale", "I would rule " + recommends + ".")
                .Set("cites", JsonValue.StringArray(cites))
                .Set("flawExplanation", flawExplanation);
        }
    }
}
