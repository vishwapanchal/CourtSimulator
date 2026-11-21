using System;
using System.Collections.Generic;
using System.Threading;
using CourtSim.Core.Advising;
using CourtSim.Core.Cases;
using CourtSim.Core.Claude;
using CourtSim.Core.Decisions;
using CourtSim.Core.Json;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    public sealed class ClaudeAdvisorTests
    {
        private FakeTransport _transport;
        private DelayRecorder _delays;
        private CaseFile _case;

        [SetUp]
        public void SetUp()
        {
            _transport = new FakeTransport();
            _delays = new DelayRecorder();
            _case = TestCases.Load();
        }

        [Test]
        public void Constructor_NullClient_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ClaudeAdvisor(null));
        }

        [Test]
        public void GetAdvice_NullRequest_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Advisor().GetAdviceAsync(null, CancellationToken.None).GetAwaiter().GetResult());
        }

        [Test]
        public void GetAdvice_ValidReply_ReturnsClaudeAdviceWithPlannedFlaw()
        {
            _transport.Enqueue(ClaudeResponses.Ok(Reply("bail.deny", "  I would remand him.  ", new[] { "record" }, "low", " He never failed to appear. ")));
            ClaudeAdvisor advisor = Advisor();

            Advice advice = Get(advisor, new AdviceRequest(_case, 0, FlawKind.MisreadFact, null));

            Assert.That(advice.Source, Is.EqualTo(AdviceSource.Claude));
            Assert.That(advice.Kind, Is.EqualTo(DecisionPointKind.Bail));
            Assert.That(advice.RecommendedOptionId, Is.EqualTo("bail.deny"));
            Assert.That(advice.Flaw, Is.EqualTo(FlawKind.MisreadFact));
            Assert.That(advice.Rationale, Is.EqualTo("I would remand him."));
            Assert.That(advice.CitedFactorIds, Is.EqualTo(new[] { "record" }));
            Assert.That(advice.Confidence, Is.EqualTo(AdviceConfidence.Low));
            Assert.That(advice.FlawExplanation, Is.EqualTo("He never failed to appear."));
            Assert.That(advisor.LastResponse, Is.Not.Null);
            Assert.That(advisor.LastResponse.RequestId, Is.EqualTo("req_ok"));
        }

        [Test]
        public void GetAdvice_Request_SendsSystemPromptBriefAndPinnedSchema()
        {
            _transport.Enqueue(ClaudeResponses.Ok(Reply("evidence.exclude", "I would exclude it.", new[] { "gap" }, "high", "Wrong test.")));
            var request = new AdviceRequest(_case, 1, FlawKind.MisstatedLaw, null);

            Get(Advisor(), request);

            JsonValue body = JsonReader.Parse(_transport.Requests[0].Body);
            Assert.That(body.GetString("system"), Is.EqualTo(AdvicePromptBuilder.SystemPrompt));
            Assert.That(body.Get("messages").Items[0].GetString("content"), Is.EqualTo(AdvicePromptBuilder.BuildBrief(request)));
            JsonValue schema = body.Get("output_config").Get("format").Get("schema");
            Assert.That(schema.ToJson(), Is.EqualTo(AdvicePromptBuilder.BuildSchema(request).ToJson()));
            Assert.That(StringItems(schema.Get("properties").Get("recommended_option_id").Get("enum")), Is.EqualTo(new[] { "evidence.exclude" }));
        }

        [Test]
        public void GetAdvice_WrongOption_ThrowsAdvisorException()
        {
            _transport.Enqueue(ClaudeResponses.Ok(Reply("bail.grant", "I would release him.", new[] { "ties" }, "high", string.Empty)));
            ClaudeAdvisor advisor = Advisor();

            AdvisorException error = Assert.Throws<AdvisorException>(() => Get(advisor, new AdviceRequest(_case, 0, FlawKind.MisreadFact, null)));

            Assert.That(error.Message, Does.Contain("'bail.grant'"));
            Assert.That(error.Message, Does.Contain("'bail.deny'"));
            Assert.That(advisor.LastResponse, Is.Null);
        }

        [Test]
        public void GetAdvice_ApiError_PropagatesClaudeException()
        {
            _transport.Enqueue(ClaudeResponses.Error(400, "invalid_request_error", "bad"));

            ClaudeException error = Assert.Throws<ClaudeException>(() => Get(Advisor(), new AdviceRequest(_case, 0, FlawKind.None, null)));

            Assert.That(error.StatusCode, Is.EqualTo(400));
        }

        [Test]
        public void GetAdvice_NoAuthoredAdvice_ThrowsWithoutCallingApi()
        {
            Assert.Throws<AdvisorException>(() => Get(Advisor(), AdvisingTests.RequestWithoutAdvice()));
            Assert.That(_transport.Requests, Is.Empty);
        }

        [Test]
        public void Parse_UnknownAndDuplicateCitedIds_AreFiltered()
        {
            var request = new AdviceRequest(_case, 0, FlawKind.None, null);
            string reply = Reply("bail.grant", "I would release.", new[] { "ties", "hash", "ties", "record" }, "high", string.Empty);

            Advice advice = AdviceReply.Parse(reply, request, request.TargetScript, AdviceSource.Claude);

            Assert.That(advice.CitedFactorIds, Is.EqualTo(new[] { "ties", "record" }));
        }

        [Test]
        public void Parse_NonStringOrMissingCitations_AreIgnored()
        {
            var request = new AdviceRequest(_case, 0, FlawKind.None, null);
            JsonValue withNumbers = JsonValue.NewObject()
                .Set("recommended_option_id", "bail.grant")
                .Set("rationale", "r")
                .Set("cited_factor_ids", TestCases.ArrayOf(JsonValue.Number(1), JsonValue.String("ties")));
            JsonValue without = JsonValue.NewObject().Set("recommended_option_id", "bail.grant").Set("rationale", "r");

            Assert.That(AdviceReply.Parse(withNumbers.ToJson(), request, request.TargetScript, AdviceSource.Claude).CitedFactorIds, Is.EqualTo(new[] { "ties" }));
            Assert.That(AdviceReply.Parse(without.ToJson(), request, request.TargetScript, AdviceSource.Claude).CitedFactorIds, Is.Empty);
        }

        [Test]
        public void Parse_FlawedWithEmptyExplanation_FallsBackToScript()
        {
            var request = new AdviceRequest(_case, 1, FlawKind.OmittedFactor, null);

            Advice advice = AdviceReply.Parse(Reply("evidence.exclude", "I would exclude.", new[] { "gap" }, "medium", "   "), request, request.TargetScript, AdviceSource.Claude);

            Assert.That(advice.Flaw, Is.EqualTo(FlawKind.OmittedFactor));
            Assert.That(advice.FlawExplanation, Is.EqualTo("Ignores the hash match."));
        }

        [Test]
        public void Parse_FlawedRationaleAdmittingTheFlaw_Throws()
        {
            var request = new AdviceRequest(_case, 1, FlawKind.OmittedFactor, null);
            string reply = Reply("evidence.exclude", "I am applying a flawed rule and would exclude it.", new[] { "gap" }, "high", "x");

            Assert.Throws<AdvisorException>(() => AdviceReply.Parse(reply, request, request.TargetScript, AdviceSource.Claude));
        }

        [Test]
        public void Parse_SoundRationaleMentioningAMistake_IsAccepted()
        {
            var request = new AdviceRequest(_case, 0, FlawKind.None, null);
            string reply = Reply("bail.grant", "It would be a mistake to remand him.", new string[0], "high", string.Empty);

            Assert.That(AdviceReply.Parse(reply, request, request.TargetScript, AdviceSource.Claude).Rationale, Does.Contain("mistake"));
        }

        [TestCase("This rule is flawed.", true)]
        [TestCase("I incorrectly assume this.", true)]
        [TestCase("An Erroneous reading.", true)]
        [TestCase("I am misapplying the law here.", true)]
        [TestCase("I will proceed as if her record alone justifies release.", true)]
        [TestCase("I would grant bail on conditions.", false)]
        public void RevealsFlaw_DetectsGiveawayWords(string rationale, bool expected)
        {
            Assert.That(AdviceReply.RevealsFlaw(rationale), Is.EqualTo(expected));
        }

        [Test]
        public void Parse_FlawedWithoutExplanationMember_FallsBackToScript()
        {
            var request = new AdviceRequest(_case, 0, FlawKind.MisreadFact, null);
            JsonValue reply = JsonValue.NewObject().Set("recommended_option_id", "bail.deny").Set("rationale", "r");

            Assert.That(AdviceReply.Parse(reply.ToJson(), request, request.TargetScript, AdviceSource.Claude).FlawExplanation, Is.EqualTo("Invents a failure to appear."));
        }

        [Test]
        public void Parse_SoundTarget_DiscardsExplanation()
        {
            var request = new AdviceRequest(_case, 2, FlawKind.None, null);

            Advice advice = AdviceReply.Parse(Reply("sentence.community", "I would order.", new[] { "plea" }, "high", "Nothing wrong, honestly."), request, request.TargetScript, AdviceSource.Claude);

            Assert.That(advice.Flaw, Is.EqualTo(FlawKind.None));
            Assert.That(advice.FlawExplanation, Is.Empty);
        }

        [Test]
        public void Parse_UnknownConfidence_UsesScriptConfidence()
        {
            var request = new AdviceRequest(_case, 1, FlawKind.OmittedFactor, null);

            Advice advice = AdviceReply.Parse(Reply("evidence.exclude", "r", new string[0], "certain", "x"), request, request.TargetScript, AdviceSource.Claude);

            Assert.That(advice.Confidence, Is.EqualTo(AdviceConfidence.Low));
        }

        [TestCase("not json")]
        [TestCase("[]")]
        [TestCase("")]
        public void Parse_NotAnObject_ThrowsAdvisorException(string reply)
        {
            var request = new AdviceRequest(_case, 0, FlawKind.None, null);

            Assert.Throws<AdvisorException>(() => AdviceReply.Parse(reply, request, request.TargetScript, AdviceSource.Claude));
        }

        [TestCase("")]
        [TestCase("   ")]
        public void Parse_BlankRationale_ThrowsAdvisorException(string rationale)
        {
            var request = new AdviceRequest(_case, 0, FlawKind.None, null);

            AdvisorException error = Assert.Throws<AdvisorException>(() => AdviceReply.Parse(Reply("bail.grant", rationale, new string[0], "high", string.Empty), request, request.TargetScript, AdviceSource.Claude));

            Assert.That(error.Message, Does.Contain("no rationale"));
        }

        [Test]
        public void Parse_MissingRecommendation_ThrowsAdvisorException()
        {
            var request = new AdviceRequest(_case, 0, FlawKind.None, null);

            Assert.Throws<AdvisorException>(() => AdviceReply.Parse("{\"rationale\":\"r\"}", request, request.TargetScript, AdviceSource.Claude));
        }

        [Test]
        public void SystemPrompt_DescribesTheTask()
        {
            Assert.That(AdvicePromptBuilder.SystemPrompt, Does.Contain("flaw_explanation"));
            Assert.That(AdvicePromptBuilder.SystemPrompt, Does.Contain("fictional"));
            Assert.That(AdvicePromptBuilder.SystemPrompt, Does.Contain("Bharatiya Nyaya Sanhita"));
            Assert.That(AdvicePromptBuilder.SystemPrompt, Does.Contain("Never invent a section number"));
        }

        [Test]
        public void BuildBrief_FlawedRequest_ContainsCaseOptionsFactorsAndFlawInstruction()
        {
            string brief = AdvicePromptBuilder.BuildBrief(new AdviceRequest(_case, 0, FlawKind.MisreadFact, null));

            Assert.That(brief, Does.Contain("Title: State v. Alex Doe"));
            Assert.That(brief, Does.Contain("Accused: Alex Doe, age 30. Works locally."));
            Assert.That(brief, Does.Contain("Charges: Burglary"));
            Assert.That(brief, Does.Contain("Summary: A test burglary."));
            Assert.That(brief, Does.Contain("- [F1] The door was forced."));
            Assert.That(brief, Does.Contain("- bail.grant: Release on conditions"));
            Assert.That(brief, Does.Contain("- bail.deny: Remand in custody"));
            Assert.That(brief, Does.Contain("- ties: Factor ties."));
            Assert.That(brief, Does.Contain("- silence: Factor silence."));
            Assert.That(brief, Does.Contain("Prosecution submits: Prosecution says bail."));
            Assert.That(brief, Does.Contain("Defence submits: Defence says bail."));
            Assert.That(brief, Does.Contain("Recommend option: bail.deny"));
            Assert.That(brief, Does.Contain("Advice type: flawed. " + FlawKinds.Describe(FlawKind.MisreadFact)));
            Assert.That(brief, Does.Contain("The specific flaw to embed: Invents a failure to appear."));
            Assert.That(brief, Does.Not.Contain("Exhibit "));
            Assert.That(brief, Does.Not.Contain("<earlier_rulings>"));
        }

        [Test]
        public void BuildBrief_SoundRequest_HasNoFlawInstruction()
        {
            string brief = AdvicePromptBuilder.BuildBrief(new AdviceRequest(_case, 0, FlawKind.None, null));

            Assert.That(brief, Does.Contain("Recommend option: bail.grant"));
            Assert.That(brief, Does.Contain("Advice type: sound."));
            Assert.That(brief, Does.Not.Contain("flawed"));
            Assert.That(brief, Does.Not.Contain("flaw to embed"));
            Assert.That(brief, Does.Not.Contain("Invents a failure to appear."));
        }

        [Test]
        public void BuildBrief_UnauthoredFlaw_FallsBackToSoundInstruction()
        {
            string brief = AdvicePromptBuilder.BuildBrief(new AdviceRequest(_case, 0, FlawKind.ImproperFactor, null));

            Assert.That(brief, Does.Contain("Advice type: sound."));
            Assert.That(brief, Does.Contain("Recommend option: bail.grant"));
        }

        [Test]
        public void BuildBrief_EvidencePoint_DescribesExhibit()
        {
            string brief = AdvicePromptBuilder.BuildBrief(new AdviceRequest(_case, 1, FlawKind.None, null));

            Assert.That(brief, Does.Contain("Exhibit P-1: CCTV recording. Footage of the rear door."));
            Assert.That(brief, Does.Contain("Provenance: Exported to a USB drive."));
            Assert.That(brief, Does.Contain("Defence objection: The chain of custody is broken."));
        }

        [Test]
        public void BuildBrief_PriorRulings_ListsLabels()
        {
            var prior = new List<DecisionRecord>
            {
                new DecisionRecord(0, DecisionPointKind.Bail, "bail.deny"),
                new DecisionRecord(1, DecisionPointKind.EvidenceAdmissibility, "evidence.unknown"),
                new DecisionRecord(7, DecisionPointKind.Sentencing, "sentence.custody"),
            };

            string brief = AdvicePromptBuilder.BuildBrief(new AdviceRequest(_case, 2, FlawKind.None, prior));

            Assert.That(brief, Does.Contain("<earlier_rulings>"));
            Assert.That(brief, Does.Contain("- Bail application: Remand in custody"));
            Assert.That(brief, Does.Contain("- Admissibility of the CCTV: evidence.unknown"));
            Assert.That(brief, Does.Contain("- Sentencing: sentence.custody"));
        }

        [Test]
        public void BuildBrief_SparseCase_OmitsEmptySections()
        {
            CaseFile full = _case;
            var caseFile = new CaseFile("s", "Sparse", null, 1, null, null, null, null, null, full.DecisionPoints);

            string brief = AdvicePromptBuilder.BuildBrief(new AdviceRequest(caseFile, 1, FlawKind.None, null));

            Assert.That(brief, Does.Not.Contain("Accused:"));
            Assert.That(brief, Does.Not.Contain("Summary:"));
            Assert.That(brief, Does.Not.Contain("Exhibit "));
        }

        [Test]
        public void BuildSchema_PinsRecommendationAndFactorIds()
        {
            JsonValue schema = AdvicePromptBuilder.BuildSchema(new AdviceRequest(_case, 1, FlawKind.OmittedFactor, null));

            Assert.That(schema.GetString("type"), Is.EqualTo("object"));
            Assert.That(schema.Get("additionalProperties").AsBool(), Is.False);
            JsonValue properties = schema.Get("properties");
            Assert.That(StringItems(properties.Get("recommended_option_id").Get("enum")), Is.EqualTo(new[] { "evidence.exclude" }));
            Assert.That(StringItems(properties.Get("cited_factor_ids").Get("items").Get("enum")), Is.EqualTo(new[] { "hash", "gap" }));
            Assert.That(StringItems(properties.Get("confidence").Get("enum")), Is.EqualTo(new[] { "low", "medium", "high" }));
            Assert.That(StringItems(schema.Get("required")), Is.EquivalentTo(new[] { "recommended_option_id", "rationale", "cited_factor_ids", "confidence", "flaw_explanation" }));
        }

        [Test]
        public void BuildSchema_SoundRequest_PinsSoundOption()
        {
            JsonValue schema = AdvicePromptBuilder.BuildSchema(new AdviceRequest(_case, 1, FlawKind.None, null));

            Assert.That(StringItems(schema.Get("properties").Get("recommended_option_id").Get("enum")), Is.EqualTo(new[] { "evidence.admit" }));
        }

        private static string Reply(string option, string rationale, string[] cited, string confidence, string explanation)
        {
            return JsonValue.NewObject()
                .Set("recommended_option_id", option)
                .Set("rationale", rationale)
                .Set("cited_factor_ids", JsonValue.StringArray(cited))
                .Set("confidence", confidence)
                .Set("flaw_explanation", explanation)
                .ToJson();
        }

        private static List<string> StringItems(JsonValue array)
        {
            var items = new List<string>();
            foreach (JsonValue item in array.Items)
            {
                items.Add(item.AsString());
            }

            return items;
        }

        private static Advice Get(ClaudeAdvisor advisor, AdviceRequest request)
        {
            return advisor.GetAdviceAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        }

        private ClaudeAdvisor Advisor()
        {
            var client = new ClaudeClient(new ClaudeClientOptions { ApiKey = "sk-test" }, _transport, _delays.Delay, new Random(1));
            return new ClaudeAdvisor(client);
        }
    }
}
