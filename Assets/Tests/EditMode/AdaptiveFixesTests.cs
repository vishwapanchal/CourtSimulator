using CourtSim.Core.Adaptive;
using CourtSim.Core.Advising;
using CourtSim.Core.Cases;
using CourtSim.Core.Json;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    /// <summary>Regression tests for the leaked-id guard and the saved-state fixes found in review.</summary>
    public sealed class AdaptiveFixesTests
    {
        [Test]
        public void FindLeakedId_RationaleWithOptionId_ReturnsIt()
        {
            var request = new AdviceRequest(TestCases.Load(), 0, FlawKind.None, null);

            Assert.That(AdviceReply.FindLeakedId("I would order bail.grant here.", request), Is.EqualTo("bail.grant"));
        }

        [Test]
        public void FindLeakedId_PlainWordsOnly_ReturnsNull()
        {
            var request = new AdviceRequest(TestCases.Load(), 0, FlawKind.None, null);

            Assert.That(AdviceReply.FindLeakedId("His ties and his record support release.", request), Is.Null);
        }

        [Test]
        public void Parse_RationaleWithOptionId_Throws()
        {
            var request = new AdviceRequest(TestCases.Load(), 0, FlawKind.None, null);
            string reply = JsonValue.NewObject()
                .Set("recommended_option_id", request.TargetScript.RecommendedOptionId)
                .Set("rationale", "I recommend " + request.TargetScript.RecommendedOptionId + ".")
                .ToJson();

            Assert.Throws<AdvisorException>(() => AdviceReply.Parse(reply, request, request.TargetScript, AdviceSource.LocalModel));
        }

        [Test]
        public void FromJson_LongHistory_KeepsNewestTwenty()
        {
            JsonValue history = JsonValue.NewArray();
            for (int i = 0; i < 30; i++)
            {
                history.Add(JsonValue.String(i < 10 ? "caught_flaw" : "trusted_sound"));
            }

            AdaptiveController controller = AdaptiveController.FromJson(JsonValue.NewObject().Set("history", history));

            Assert.That(controller.History.Count, Is.EqualTo(20));
            Assert.That(controller.History, Has.None.EqualTo(Logging.AdviceOutcome.CaughtFlaw));
        }

        [Test]
        public void FromJson_HugeNumbers_AreClampedNotOverflowed()
        {
            AdaptiveController controller = AdaptiveController.FromJson(JsonValue.NewObject()
                .Set("subtlety", 1e20)
                .Set("acceptedFlawsInARow", 1e20));

            Assert.That(controller.Subtlety, Is.EqualTo(AdaptiveController.MaxSubtlety));
            Assert.That(controller.Record(Logging.AdviceOutcome.MisledByFlaw), Is.Null, "a clamped streak of 1 plus one more fires only at max subtlety, which returns no note");
        }

        [Test]
        public void SessionStart_WithAdaptation_LogsFlawedPointsWithoutAKind()
        {
            CaseFile caseFile = TestCases.Load();
            var sink = new Logging.MemoryLogSink();
            var session = new Sessions.CaseSession(caseFile, new AdvicePlan(new[] { FlawKind.MisreadFact, FlawKind.None, FlawKind.None }), new ScriptedAdvisor(), sink);
            session.UseAdaptation(new AdaptiveController(), new System.Random(1));

            session.Start();

            JsonValue plan = sink.Records[0].Get("plan");
            Assert.That(plan.Items[0].AsString(), Is.EqualTo("flawed"));
            Assert.That(plan.Items[1].AsString(), Is.EqualTo("none"));
        }
    }
}
