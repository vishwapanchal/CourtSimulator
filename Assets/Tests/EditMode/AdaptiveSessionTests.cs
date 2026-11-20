using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CourtSim.Core.Adaptive;
using CourtSim.Core.Advising;
using CourtSim.Core.Cases;
using CourtSim.Core.Json;
using CourtSim.Core.Logging;
using CourtSim.Core.Sessions;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    public sealed class AdaptiveSessionTests
    {
        private CaseFile _case;
        private MemoryLogSink _sink;
        private FakeClock _clock;

        [SetUp]
        public void SetUp()
        {
            _case = TestCases.Load();
            _sink = new MemoryLogSink();
            _clock = new FakeClock();
        }

        [Test]
        public void AdviceRequest_DefaultDetail_IsStandard()
        {
            var request = new AdviceRequest(_case, 0, FlawKind.None, null);

            Assert.That(request.Detail, Is.EqualTo(ExplanationDetail.Standard));
        }

        [Test]
        public void ScriptedAdvisor_Standard_ReturnsAuthoredRationale()
        {
            Advice advice = ScriptedAdvisor.Build(new AdviceRequest(_case, 0, FlawKind.None, null, ExplanationDetail.Standard));

            Assert.That(advice.Rationale, Is.EqualTo("I would rule bail.grant."));
        }

        [Test]
        public void ScriptedAdvisor_Detailed_AppendsEachCitedFactorText()
        {
            Advice advice = ScriptedAdvisor.Build(new AdviceRequest(_case, 0, FlawKind.None, null, ExplanationDetail.Detailed));

            Assert.That(advice.Rationale, Is.EqualTo("I would rule bail.grant.\n\nWhat I relied on:\n- Factor ties.\n- Factor record."));
            Assert.That(advice.RecommendedOptionId, Is.EqualTo("bail.grant"));
            Assert.That(advice.Flaw, Is.EqualTo(FlawKind.None));
        }

        [Test]
        public void ScriptedAdvisor_DetailedFlawed_KeepsFlawAndListsFactors()
        {
            Advice advice = ScriptedAdvisor.Build(new AdviceRequest(_case, 2, FlawKind.ImproperFactor, null, ExplanationDetail.Detailed));

            Assert.That(advice.Flaw, Is.EqualTo(FlawKind.ImproperFactor));
            Assert.That(advice.Rationale, Does.StartWith("I would rule sentence.custody.\n\nWhat I relied on:"));
            Assert.That(advice.Rationale, Does.EndWith("\n- Factor planning."));
        }

        [Test]
        public void BuildBrief_Detailed_AddsDetailedExplanationLine()
        {
            string detailed = AdvicePromptBuilder.BuildBrief(new AdviceRequest(_case, 0, FlawKind.None, null, ExplanationDetail.Detailed));
            string standard = AdvicePromptBuilder.BuildBrief(new AdviceRequest(_case, 0, FlawKind.None, null));

            Assert.That(detailed, Does.Contain("Explanation: detailed"));
            Assert.That(detailed.IndexOf("Explanation: detailed", StringComparison.Ordinal), Is.LessThan(detailed.IndexOf("</instructions>", StringComparison.Ordinal)));
            Assert.That(standard, Does.Not.Contain("Explanation: detailed"));
        }

        [Test]
        public void Session_WithoutAdaptation_UsesPlanFlawAndStandardDetail()
        {
            StubAdvisor advisor = Scripted();
            CaseSession session = Session(advisor, new AdvicePlan(new[] { FlawKind.MisreadFact, FlawKind.None, FlawKind.None }));

            session.Start();
            session.Submit("bail.grant");

            Assert.That(session.Adaptation, Is.Null);
            Assert.That(advisor.Requests[0].Flaw, Is.EqualTo(FlawKind.MisreadFact));
            Assert.That(advisor.Requests[0].Detail, Is.EqualTo(ExplanationDetail.Standard));
            Assert.That(session.DetailAt(0), Is.EqualTo(ExplanationDetail.Standard));
            Assert.That(Decision(0).Get("advice").GetString("detail"), Is.EqualTo("standard"));
            Assert.That(Records("adaptation"), Is.Empty);
        }

        [Test]
        public void UseAdaptation_ExposesController()
        {
            var controller = new AdaptiveController();
            CaseSession session = Session(new ScriptedAdvisor());

            session.UseAdaptation(controller);

            Assert.That(session.Adaptation, Is.SameAs(controller));
        }

        [Test]
        public void FlawedPoint_WithAdaptation_FlawKindComesFromController()
        {
            StubAdvisor advisor = Scripted();
            // The plan names misread_fact for point 1, which it does not have; the controller picks from what is authored.
            CaseSession session = Session(advisor, new AdvicePlan(new[] { FlawKind.None, FlawKind.MisreadFact, FlawKind.None }));
            session.UseAdaptation(new AdaptiveController(), new FixedRandom(0.99));

            session.Start();
            session.Submit("bail.grant");

            Assert.That(advisor.Requests[0].Flaw, Is.EqualTo(FlawKind.None));
            Assert.That(advisor.Requests[1].Flaw, Is.EqualTo(FlawKind.OmittedFactor));
            Assert.That(session.CurrentAdvice.Flaw, Is.EqualTo(FlawKind.OmittedFactor));
        }

        [Test]
        public void FlawedPoint_WithAdaptation_LowRollPicksFirstAuthoredFlaw()
        {
            StubAdvisor advisor = Scripted();
            CaseSession session = Session(advisor, new AdvicePlan(new[] { FlawKind.None, FlawKind.OmittedFactor, FlawKind.None }));
            session.UseAdaptation(new AdaptiveController(), new FixedRandom(0.0));

            session.Start();
            session.Submit("bail.grant");

            Assert.That(advisor.Requests[1].Flaw, Is.EqualTo(FlawKind.MisstatedLaw));
        }

        [Test]
        public void FlawedPoint_HighSubtletySeeded_MostlyPicksSubtlerAuthoredFlaw()
        {
            var controller = AdaptiveController.FromJson(JsonValue.NewObject().Set("subtlety", 3));
            var random = new Random(17);
            int omitted = 0;
            for (int i = 0; i < 200; i++)
            {
                StubAdvisor advisor = Scripted();
                CaseSession session = Session(advisor, new AdvicePlan(new[] { FlawKind.None, FlawKind.MisstatedLaw, FlawKind.None }));
                session.UseAdaptation(controller, random);
                session.RequestAdvice(1);
                if (advisor.Requests[0].Flaw == FlawKind.OmittedFactor)
                {
                    omitted++;
                }
            }

            // Ranks 1 and 3 at level 3: weights 8 and 64, so about 89% omitted_factor.
            Assert.That(omitted, Is.InRange(160, 195));
        }

        [Test]
        public void UseAdaptation_PendingDetail_FirstAdviceIsDetailedThenStandard()
        {
            StubAdvisor advisor = Scripted();
            CaseSession session = Session(advisor);
            session.UseAdaptation(PendingDetailController());

            session.Start();
            Assert.That(session.CurrentAdvice.Rationale, Does.Contain("What I relied on:"));
            session.Submit("bail.grant");

            Assert.That(advisor.Requests[0].Detail, Is.EqualTo(ExplanationDetail.Detailed));
            Assert.That(advisor.Requests[1].Detail, Is.EqualTo(ExplanationDetail.Standard));
            Assert.That(session.DetailAt(0), Is.EqualTo(ExplanationDetail.Detailed));
            Assert.That(session.DetailAt(1), Is.EqualTo(ExplanationDetail.Standard));
            Assert.That(session.CurrentAdvice.Rationale, Does.Not.Contain("What I relied on:"));
            Assert.That(Decision(0).Get("advice").GetString("detail"), Is.EqualTo("detailed"));
        }

        [TestCase(-1)]
        [TestCase(3)]
        public void DetailAt_OutOfRange_IsStandard(int index)
        {
            CaseSession session = Session(new ScriptedAdvisor());
            session.UseAdaptation(PendingDetailController());
            session.Start();

            Assert.That(session.DetailAt(index), Is.EqualTo(ExplanationDetail.Standard));
        }

        [Test]
        public void Submit_WithAdaptation_FeedsEveryOutcomeToController()
        {
            var controller = new AdaptiveController();
            CaseSession session = Session(Scripted(), new AdvicePlan(new[] { FlawKind.MisreadFact, FlawKind.None, FlawKind.None }));
            session.UseAdaptation(controller, new Random(3));

            session.Start();
            session.Submit("bail.grant");
            session.Submit("evidence.admit");
            session.Submit("sentence.custody");

            Assert.That(controller.History, Is.EqualTo(new[] { AdviceOutcome.CaughtFlaw, AdviceOutcome.TrustedSound, AdviceOutcome.OverrodeSound }));
            Assert.That(Records("adaptation"), Is.Empty);
        }

        [Test]
        public void Submit_AdviceFailed_RecordsNoAdviceAndOmitsDetail()
        {
            var controller = new AdaptiveController();
            CaseSession session = Session(StubAdvisor.Failing(new AdvisorException("down")));
            session.UseAdaptation(controller);

            session.Start();
            session.Submit("bail.grant");

            Assert.That(controller.History, Is.EqualTo(new[] { AdviceOutcome.NoAdvice }));
            Assert.That(Decision(0).Get("advice").IsNull, Is.True);
        }

        [Test]
        public void Submit_TwoOverrides_WritesAdaptationRecordRaisesEventAndDetailsNextAdvice()
        {
            StubAdvisor advisor = Scripted();
            var controller = new AdaptiveController();
            CaseSession session = Session(advisor);
            session.UseAdaptation(controller);
            var notes = new List<string>();
            session.Adapted += notes.Add;

            session.Start();
            session.Submit("bail.deny");
            Assert.That(notes, Is.Empty);
            _clock.Advance(3);
            session.Submit("evidence.exclude");

            Assert.That(notes.Count, Is.EqualTo(1));
            Assert.That(notes[0], Does.Contain("explain its reasoning in detail"));

            List<JsonValue> adaptations = Records("adaptation");
            Assert.That(adaptations.Count, Is.EqualTo(1));
            JsonValue record = adaptations[0];
            Assert.That(record.GetString("schema"), Is.EqualTo(LogNames.Schema));
            Assert.That(record.GetString("sessionId"), Is.EqualTo("sess"));
            Assert.That(record.GetNumber("afterIndex"), Is.EqualTo(1));
            Assert.That(record.GetString("note"), Is.EqualTo(notes[0]));
            Assert.That(record.GetString("timestampUtc"), Is.EqualTo("2026-03-04T09:00:03.000Z"));
            Assert.That(record.Get("state").Get("detailedExplanationPending").AsBool(), Is.True);
            Assert.That(record.Get("state").Get("history").ToJson(), Is.EqualTo("[\"overrode_sound\",\"overrode_sound\"]"));

            // The adaptation record follows the decision it reacts to, and precedes the next one.
            Assert.That(_sink.Records[3], Is.SameAs(record));
            Assert.That(_sink.Records[2].GetString("type"), Is.EqualTo("decision"));
            Assert.That(_sink.Records[2].GetString("outcome"), Is.EqualTo("overrode_sound"));

            Assert.That(advisor.Requests[2].Detail, Is.EqualTo(ExplanationDetail.Detailed));
            Assert.That(session.DetailAt(2), Is.EqualTo(ExplanationDetail.Detailed));
            Assert.That(session.CurrentAdvice.Rationale, Is.EqualTo("I would rule sentence.community.\n\nWhat I relied on:\n- Factor plea."));
            Assert.That(controller.DetailedExplanationPending, Is.False);

            session.Submit("sentence.community");
            Assert.That(Decision(2).Get("advice").GetString("detail"), Is.EqualTo("detailed"));
            Assert.That(Decision(1).Get("advice").GetString("detail"), Is.EqualTo("standard"));
        }

        [Test]
        public void TwoSessionsSharingController_SubtletyRisesThenDetailedAdviceFollowsRejections()
        {
            var controller = new AdaptiveController();
            var random = new Random(2026);
            var notes = new List<string>();

            // Session one: the trainee follows two flawed suggestions in a row.
            StubAdvisor firstAdvisor = Scripted();
            var first = new CaseSession(
                _case,
                new AdvicePlan(new[] { FlawKind.MisreadFact, FlawKind.MisstatedLaw, FlawKind.None }),
                firstAdvisor,
                _sink,
                "scripted",
                _clock.Read,
                "first");
            first.UseAdaptation(controller, random);
            first.Adapted += notes.Add;
            first.Start();

            Assert.That(first.CurrentAdvice.IsFlawed, Is.True);
            Assert.That(first.Submit(first.CurrentAdvice.RecommendedOptionId).Outcome, Is.EqualTo(AdviceOutcome.MisledByFlaw));
            Assert.That(first.CurrentAdvice.IsFlawed, Is.True);
            Assert.That(first.Submit(first.CurrentAdvice.RecommendedOptionId).Outcome, Is.EqualTo(AdviceOutcome.MisledByFlaw));
            Assert.That(controller.Subtlety, Is.EqualTo(1));
            Assert.That(first.Submit("sentence.community").Outcome, Is.EqualTo(AdviceOutcome.TrustedSound));
            Assert.That(first.IsComplete, Is.True);

            // Session two: the trainee rejects two sound suggestions in a row.
            StubAdvisor secondAdvisor = Scripted();
            var second = new CaseSession(_case, AdvicePlan.AllSound(3), secondAdvisor, _sink, "scripted", _clock.Read, "second");
            second.UseAdaptation(controller, random);
            second.Adapted += notes.Add;
            second.Start();

            Assert.That(second.CurrentAdvice.Rationale, Does.Not.Contain("What I relied on:"));
            Assert.That(second.Submit("bail.deny").Outcome, Is.EqualTo(AdviceOutcome.OverrodeSound));
            Assert.That(second.Submit("evidence.admit_limited").Outcome, Is.EqualTo(AdviceOutcome.OverrodeSound));

            Assert.That(secondAdvisor.Requests[2].Detail, Is.EqualTo(ExplanationDetail.Detailed));
            Assert.That(second.CurrentAdvice.Rationale, Does.Contain("What I relied on:"));
            Assert.That(second.DetailAt(2), Is.EqualTo(ExplanationDetail.Detailed));
            Assert.That(controller.Subtlety, Is.EqualTo(1));
            second.Submit("sentence.community");

            Assert.That(notes.Count, Is.EqualTo(2));
            List<JsonValue> adaptations = Records("adaptation");
            Assert.That(adaptations.Count, Is.EqualTo(2));
            Assert.That(adaptations[0].GetString("sessionId"), Is.EqualTo("first"));
            Assert.That(adaptations[0].GetNumber("afterIndex"), Is.EqualTo(1));
            Assert.That(adaptations[0].Get("state").GetNumber("subtlety"), Is.EqualTo(1));
            Assert.That(adaptations[0].GetString("note"), Does.Contain("subtler"));
            Assert.That(adaptations[1].GetString("sessionId"), Is.EqualTo("second"));
            Assert.That(adaptations[1].GetNumber("afterIndex"), Is.EqualTo(1));
            Assert.That(adaptations[1].Get("state").Get("detailedExplanationPending").AsBool(), Is.True);
            Assert.That(adaptations[1].Get("state").Get("history").Items.Count, Is.EqualTo(5));

            JsonValue lastDecision = Records("decision")[5];
            Assert.That(lastDecision.GetString("sessionId"), Is.EqualTo("second"));
            Assert.That(lastDecision.Get("advice").GetString("detail"), Is.EqualTo("detailed"));
            Assert.That(Records("session_end").Count, Is.EqualTo(2));
        }

        private static StubAdvisor Scripted()
        {
            return new StubAdvisor((request, token) => Task.FromResult(ScriptedAdvisor.Build(request)));
        }

        private static AdaptiveController PendingDetailController()
        {
            return AdaptiveController.FromJson(JsonValue.NewObject().Set("detailedExplanationPending", true));
        }

        private CaseSession Session(IAdvisor advisor, AdvicePlan plan = null)
        {
            return new CaseSession(_case, plan, advisor, _sink, "scripted", _clock.Read, "sess");
        }

        private List<JsonValue> Records(string type)
        {
            var records = new List<JsonValue>();
            foreach (JsonValue record in _sink.Records)
            {
                if (record.GetString("type") == type)
                {
                    records.Add(record);
                }
            }

            return records;
        }

        private JsonValue Decision(int index)
        {
            return Records("decision")[index];
        }

        /// <summary>A Random whose NextDouble always returns the same value.</summary>
        private sealed class FixedRandom : Random
        {
            private readonly double _value;

            public FixedRandom(double value)
            {
                _value = value;
            }

            public override double NextDouble()
            {
                return _value;
            }
        }
    }
}
