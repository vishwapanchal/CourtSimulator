using System;
using System.Threading;
using CourtSim.Core.Advising;
using CourtSim.Core.Cases;
using CourtSim.Core.Decisions;
using CourtSim.Core.Json;
using CourtSim.Core.Logging;
using CourtSim.Core.Ollama;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    public sealed class LocalModelAdvisorTests
    {
        private FakeTransport _transport;
        private CaseFile _case;

        [SetUp]
        public void SetUp()
        {
            _transport = new FakeTransport();
            _case = TestCases.Load();
        }

        [Test]
        public void Constructor_NullClient_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new LocalModelAdvisor(null));
        }

        [Test]
        public void GetAdvice_NullRequest_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Advisor().GetAdviceAsync(null, CancellationToken.None).GetAwaiter().GetResult());
        }

        [Test]
        public void GetAdvice_ValidReply_ReturnsLocalAdviceWithPlannedFlaw()
        {
            _transport.Enqueue(OllamaResponses.Ok(Reply("bail.deny", "  I would remand him.  ", new[] { "record" }, "low", " He never failed to appear. ")));
            LocalModelAdvisor advisor = Advisor();

            Advice advice = Get(advisor, new AdviceRequest(_case, 0, FlawKind.MisreadFact, null));

            Assert.That(advice.Source, Is.EqualTo(AdviceSource.LocalModel));
            Assert.That(advice.Kind, Is.EqualTo(DecisionPointKind.Bail));
            Assert.That(advice.RecommendedOptionId, Is.EqualTo("bail.deny"));
            Assert.That(advice.Flaw, Is.EqualTo(FlawKind.MisreadFact));
            Assert.That(advice.Rationale, Is.EqualTo("I would remand him."));
            Assert.That(advice.CitedFactorIds, Is.EqualTo(new[] { "record" }));
            Assert.That(advice.Confidence, Is.EqualTo(AdviceConfidence.Low));
            Assert.That(advice.FlawExplanation, Is.EqualTo("He never failed to appear."));
        }

        [Test]
        public void GetAdvice_ValidReply_SetsLastResponse()
        {
            _transport.Enqueue(OllamaResponses.Ok(Reply("bail.grant", "I would release him.", new[] { "ties" }, "high", string.Empty)));
            LocalModelAdvisor advisor = Advisor();

            Get(advisor, new AdviceRequest(_case, 0, FlawKind.None, null));

            Assert.That(advisor.LastResponse, Is.Not.Null);
            Assert.That(advisor.LastResponse.Model, Is.EqualTo("qwen3.5:0.8b"));
            Assert.That(advisor.LastResponse.PromptTokens, Is.EqualTo(812));
            Assert.That(advisor.LastResponse.OutputTokens, Is.EqualTo(96));
        }

        [Test]
        public void GetAdvice_DuplicateCitedIds_AreDeduplicated()
        {
            _transport.Enqueue(OllamaResponses.Ok(Reply("bail.grant", "I would release him.", new[] { "ties", "ties", "hash", "record", "ties" }, "medium", string.Empty)));

            Advice advice = Get(Advisor(), new AdviceRequest(_case, 0, FlawKind.None, null));

            Assert.That(advice.CitedFactorIds, Is.EqualTo(new[] { "ties", "record" }));
        }

        [Test]
        public void GetAdvice_Request_SendsPromptBuilderSystemPromptBriefAndSchema()
        {
            _transport.Enqueue(OllamaResponses.Ok(Reply("evidence.exclude", "I would exclude it.", new[] { "gap" }, "high", "Wrong test.")));
            var request = new AdviceRequest(_case, 1, FlawKind.MisstatedLaw, null);

            Get(Advisor(), request);

            JsonValue body = JsonReader.Parse(_transport.Requests[0].Body);
            JsonValue messages = body.Get("messages");
            Assert.That(messages.Items[0].GetString("role"), Is.EqualTo("system"));
            Assert.That(messages.Items[0].GetString("content"), Is.EqualTo(AdvicePromptBuilder.SystemPrompt));
            Assert.That(messages.Items[1].GetString("content"), Is.EqualTo(AdvicePromptBuilder.BuildBrief(request)));
            Assert.That(body.Get("format").ToJson(), Is.EqualTo(AdvicePromptBuilder.BuildSchema(request).ToJson()));
        }

        [Test]
        public void GetAdvice_WrongOption_ThrowsAdvisorException()
        {
            _transport.Enqueue(OllamaResponses.Ok(Reply("bail.grant", "I would release him.", new[] { "ties" }, "high", string.Empty)));
            LocalModelAdvisor advisor = Advisor();

            AdvisorException error = Assert.Throws<AdvisorException>(() => Get(advisor, new AdviceRequest(_case, 0, FlawKind.MisreadFact, null)));

            Assert.That(error.Message, Does.Contain("'bail.grant'"));
            Assert.That(error.Message, Does.Contain("'bail.deny'"));
            Assert.That(advisor.LastResponse, Is.Null);
        }

        [Test]
        public void GetAdvice_ServerError_PropagatesOllamaException()
        {
            _transport.Enqueue(OllamaResponses.Error(404, "model 'qwen3.5:0.8b' not found"));
            LocalModelAdvisor advisor = Advisor();

            OllamaException error = Assert.Throws<OllamaException>(() => Get(advisor, new AdviceRequest(_case, 0, FlawKind.None, null)));

            Assert.That(error.StatusCode, Is.EqualTo(404));
            Assert.That(advisor.LastResponse, Is.Null);
        }

        [Test]
        public void GetAdvice_NoAuthoredAdvice_ThrowsWithoutCallingServer()
        {
            Assert.Throws<AdvisorException>(() => Get(Advisor(), AdvisingTests.RequestWithoutAdvice()));
            Assert.That(_transport.Requests, Is.Empty);
        }

        [Test]
        public void Source_LocalModel_LogsAsLocal()
        {
            Assert.That(LogNames.Source(AdviceSource.LocalModel), Is.EqualTo("local"));
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

        private static Advice Get(LocalModelAdvisor advisor, AdviceRequest request)
        {
            return advisor.GetAdviceAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        }

        private LocalModelAdvisor Advisor()
        {
            return new LocalModelAdvisor(new OllamaClient(new OllamaClientOptions(), _transport));
        }
    }
}
