using System;
using System.Collections.Generic;
using CourtSim.Core.Adaptive;
using CourtSim.Core.Advising;
using CourtSim.Core.Json;
using CourtSim.Core.Logging;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    public sealed class AdaptiveControllerTests
    {
        private const int Draws = 4000;

        private static readonly FlawKind[] AllFlaws =
        {
            FlawKind.MisreadFact,
            FlawKind.MisstatedLaw,
            FlawKind.ImproperFactor,
            FlawKind.OmittedFactor,
        };

        [Test]
        public void New_StartsAtFreshState()
        {
            var controller = new AdaptiveController();

            Assert.That(controller.Subtlety, Is.EqualTo(0));
            Assert.That(controller.DetailedExplanationPending, Is.False);
            Assert.That(controller.History, Is.Empty);
        }

        [Test]
        public void ObviousToSubtle_ListsFlawKindsInOrder()
        {
            Assert.That(AdaptiveController.ObviousToSubtle, Is.EqualTo(AllFlaws));
        }

        [Test]
        public void Record_TwoMisledInARow_RaisesSubtletyAndReturnsNote()
        {
            var controller = new AdaptiveController();

            string first = controller.Record(AdviceOutcome.MisledByFlaw);
            string second = controller.Record(AdviceOutcome.MisledByFlaw);

            Assert.That(first, Is.Null);
            Assert.That(second, Does.Contain("subtler").And.Contain("level 1"));
            Assert.That(controller.Subtlety, Is.EqualTo(1));
            Assert.That(controller.DetailedExplanationPending, Is.False);
        }

        [Test]
        public void Record_StreakResetsAfterFiring_NeedsTwoMoreToFireAgain()
        {
            var controller = new AdaptiveController();
            controller.Record(AdviceOutcome.MisledByFlaw);
            controller.Record(AdviceOutcome.MisledByFlaw);

            string third = controller.Record(AdviceOutcome.MisledByFlaw);
            Assert.That(third, Is.Null);
            Assert.That(controller.Subtlety, Is.EqualTo(1));

            string fourth = controller.Record(AdviceOutcome.MisledByFlaw);
            Assert.That(fourth, Does.Contain("level 2"));
            Assert.That(controller.Subtlety, Is.EqualTo(2));
        }

        [Test]
        public void Record_ManyMisled_SubtletyCapsAtMaxWithoutNote()
        {
            var controller = new AdaptiveController();
            var notes = new List<string>();

            for (int i = 0; i < 10; i++)
            {
                string note = controller.Record(AdviceOutcome.MisledByFlaw);
                if (note != null)
                {
                    notes.Add(note);
                }
            }

            Assert.That(controller.Subtlety, Is.EqualTo(AdaptiveController.MaxSubtlety));
            Assert.That(notes.Count, Is.EqualTo(3));
            Assert.That(notes[2], Does.Contain("level 3"));
        }

        [TestCase(AdviceOutcome.TrustedSound)]
        [TestCase(AdviceOutcome.CaughtFlaw)]
        [TestCase(AdviceOutcome.OverrodeSound)]
        public void Record_OtherOutcomeBetweenMisled_BreaksStreak(AdviceOutcome breaker)
        {
            var controller = new AdaptiveController();

            controller.Record(AdviceOutcome.MisledByFlaw);
            controller.Record(breaker);
            string note = controller.Record(AdviceOutcome.MisledByFlaw);

            Assert.That(note, Is.Null);
            Assert.That(controller.Subtlety, Is.EqualTo(0));
        }

        [Test]
        public void Record_NoAdviceBetweenMisled_NeitherExtendsNorBreaksStreak()
        {
            var controller = new AdaptiveController();

            controller.Record(AdviceOutcome.MisledByFlaw);
            Assert.That(controller.Record(AdviceOutcome.NoAdvice), Is.Null);
            Assert.That(controller.Record(AdviceOutcome.NoAdvice), Is.Null);
            string note = controller.Record(AdviceOutcome.MisledByFlaw);

            Assert.That(note, Is.Not.Null);
            Assert.That(controller.Subtlety, Is.EqualTo(1));
        }

        [Test]
        public void Record_TwoOverrodeInARow_SetsDetailedPendingAndReturnsNote()
        {
            var controller = new AdaptiveController();

            string first = controller.Record(AdviceOutcome.OverrodeSound);
            string second = controller.Record(AdviceOutcome.OverrodeSound);

            Assert.That(first, Is.Null);
            Assert.That(second, Does.Contain("detail"));
            Assert.That(controller.DetailedExplanationPending, Is.True);
            Assert.That(controller.Subtlety, Is.EqualTo(0));
        }

        [Test]
        public void Record_OverrodeStreakResetsAfterFiring()
        {
            var controller = new AdaptiveController();
            controller.Record(AdviceOutcome.OverrodeSound);
            controller.Record(AdviceOutcome.OverrodeSound);
            controller.TakeExplanationDetail();

            Assert.That(controller.Record(AdviceOutcome.OverrodeSound), Is.Null);
            Assert.That(controller.DetailedExplanationPending, Is.False);
            Assert.That(controller.Record(AdviceOutcome.OverrodeSound), Is.Not.Null);
            Assert.That(controller.DetailedExplanationPending, Is.True);
        }

        [TestCase(AdviceOutcome.TrustedSound)]
        [TestCase(AdviceOutcome.CaughtFlaw)]
        [TestCase(AdviceOutcome.MisledByFlaw)]
        public void Record_OtherOutcomeBetweenOverrode_BreaksStreak(AdviceOutcome breaker)
        {
            var controller = new AdaptiveController();

            controller.Record(AdviceOutcome.OverrodeSound);
            controller.Record(breaker);
            string note = controller.Record(AdviceOutcome.OverrodeSound);

            Assert.That(note, Is.Null);
            Assert.That(controller.DetailedExplanationPending, Is.False);
        }

        [Test]
        public void Record_NoAdviceBetweenOverrode_KeepsStreak()
        {
            var controller = new AdaptiveController();

            controller.Record(AdviceOutcome.OverrodeSound);
            controller.Record(AdviceOutcome.NoAdvice);
            string note = controller.Record(AdviceOutcome.OverrodeSound);

            Assert.That(note, Is.Not.Null);
            Assert.That(controller.DetailedExplanationPending, Is.True);
        }

        [Test]
        public void Record_NoAdviceOnly_NeverFires()
        {
            var controller = new AdaptiveController();

            for (int i = 0; i < 5; i++)
            {
                Assert.That(controller.Record(AdviceOutcome.NoAdvice), Is.Null);
            }

            Assert.That(controller.Subtlety, Is.EqualTo(0));
            Assert.That(controller.DetailedExplanationPending, Is.False);
        }

        [Test]
        public void Record_AppendsToHistoryOldestFirst()
        {
            var controller = new AdaptiveController();

            controller.Record(AdviceOutcome.TrustedSound);
            controller.Record(AdviceOutcome.CaughtFlaw);
            controller.Record(AdviceOutcome.NoAdvice);

            Assert.That(controller.History, Is.EqualTo(new[] { AdviceOutcome.TrustedSound, AdviceOutcome.CaughtFlaw, AdviceOutcome.NoAdvice }));
        }

        [Test]
        public void Record_MoreThanTwenty_HistoryKeepsLatestTwenty()
        {
            var controller = new AdaptiveController();
            controller.Record(AdviceOutcome.CaughtFlaw);
            controller.Record(AdviceOutcome.CaughtFlaw);

            for (int i = 0; i < 20; i++)
            {
                controller.Record(AdviceOutcome.TrustedSound);
            }

            Assert.That(controller.History.Count, Is.EqualTo(20));
            Assert.That(controller.History, Has.No.Member(AdviceOutcome.CaughtFlaw));

            controller.Record(AdviceOutcome.NoAdvice);
            Assert.That(controller.History.Count, Is.EqualTo(20));
            Assert.That(controller.History[19], Is.EqualTo(AdviceOutcome.NoAdvice));
        }

        [Test]
        public void ChooseFlaw_NullOrEmpty_ReturnsNone()
        {
            var controller = new AdaptiveController();

            Assert.That(controller.ChooseFlaw(null, new Random(1)), Is.EqualTo(FlawKind.None));
            Assert.That(controller.ChooseFlaw(new FlawKind[0], new Random(1)), Is.EqualTo(FlawKind.None));
        }

        [Test]
        public void ChooseFlaw_SingleKind_AlwaysReturnsIt()
        {
            AdaptiveController controller = ControllerAt(3);
            var random = new Random(7);

            for (int i = 0; i < 50; i++)
            {
                Assert.That(controller.ChooseFlaw(new[] { FlawKind.MisreadFact }, random), Is.EqualTo(FlawKind.MisreadFact));
            }
        }

        [Test]
        public void ChooseFlaw_SubtletyZero_PicksKindsUniformly()
        {
            Dictionary<FlawKind, int> counts = Draw(new AdaptiveController(), AllFlaws, 12345);

            foreach (FlawKind flaw in AllFlaws)
            {
                // Expected 1000 each; allow a wide band.
                Assert.That(counts[flaw], Is.InRange(850, 1150), flaw.ToString());
            }
        }

        [Test]
        public void ChooseFlaw_SubtletyOne_WeightsByRankPlusOne()
        {
            Dictionary<FlawKind, int> counts = Draw(ControllerAt(1), AllFlaws, 2024);

            // Weights 1:2:3:4 out of 10 → 400, 800, 1200, 1600.
            Assert.That(counts[FlawKind.MisreadFact], Is.InRange(320, 480));
            Assert.That(counts[FlawKind.MisstatedLaw], Is.InRange(680, 920));
            Assert.That(counts[FlawKind.ImproperFactor], Is.InRange(1060, 1340));
            Assert.That(counts[FlawKind.OmittedFactor], Is.InRange(1440, 1760));
        }

        [Test]
        public void ChooseFlaw_SubtletyThree_FavoursOmittedFactorOverMisreadFact()
        {
            Dictionary<FlawKind, int> counts = Draw(ControllerAt(3), AllFlaws, 99);

            // Weights 1:8:27:64 out of 100 → 40, 320, 1080, 2560.
            Assert.That(counts[FlawKind.OmittedFactor], Is.GreaterThan(counts[FlawKind.MisreadFact] * 10));
            Assert.That(counts[FlawKind.OmittedFactor], Is.InRange(2400, 2720));
            Assert.That(counts[FlawKind.MisreadFact], Is.LessThan(100));
            Assert.That(counts[FlawKind.ImproperFactor], Is.GreaterThan(counts[FlawKind.MisstatedLaw]));
        }

        [Test]
        public void ChooseFlaw_HigherSubtlety_ShiftsShareTowardSubtlerKinds()
        {
            int previous = -1;
            for (int level = 0; level <= AdaptiveController.MaxSubtlety; level++)
            {
                int omitted = Draw(ControllerAt(level), AllFlaws, 5)[FlawKind.OmittedFactor];
                Assert.That(omitted, Is.GreaterThan(previous), "level " + level);
                previous = omitted;
            }
        }

        [Test]
        public void ChooseFlaw_SubsetOfKinds_OnlyReturnsAvailableAndUsesRankWeights()
        {
            var available = new[] { FlawKind.OmittedFactor, FlawKind.MisstatedLaw };
            Dictionary<FlawKind, int> counts = Draw(ControllerAt(1), available, 31);

            // Weights 4:2 → 2667 vs 1333.
            Assert.That(counts.Keys, Is.EquivalentTo(available));
            Assert.That(counts[FlawKind.OmittedFactor], Is.InRange(2500, 2830));
        }

        [Test]
        public void ChooseFlaw_SameSeed_IsDeterministic()
        {
            AdaptiveController controller = ControllerAt(2);
            var a = new Random(42);
            var b = new Random(42);

            for (int i = 0; i < 100; i++)
            {
                Assert.That(controller.ChooseFlaw(AllFlaws, a), Is.EqualTo(controller.ChooseFlaw(AllFlaws, b)));
            }
        }

        [Test]
        public void ChooseFlaw_RollAtUpperBound_ReturnsLastAvailable()
        {
            AdaptiveController controller = ControllerAt(0);

            Assert.That(controller.ChooseFlaw(AllFlaws, new FixedRandom(1.0)), Is.EqualTo(FlawKind.OmittedFactor));
            Assert.That(controller.ChooseFlaw(AllFlaws, new FixedRandom(0.0)), Is.EqualTo(FlawKind.MisreadFact));
        }

        [Test]
        public void TakeExplanationDetail_ReturnsDetailedOnceThenStandard()
        {
            var controller = new AdaptiveController();
            Assert.That(controller.TakeExplanationDetail(), Is.EqualTo(ExplanationDetail.Standard));

            controller.Record(AdviceOutcome.OverrodeSound);
            controller.Record(AdviceOutcome.OverrodeSound);

            Assert.That(controller.TakeExplanationDetail(), Is.EqualTo(ExplanationDetail.Detailed));
            Assert.That(controller.DetailedExplanationPending, Is.False);
            Assert.That(controller.TakeExplanationDetail(), Is.EqualTo(ExplanationDetail.Standard));
        }

        [Test]
        public void ToJson_WritesStateAndHistoryNames()
        {
            var controller = new AdaptiveController();
            controller.Record(AdviceOutcome.MisledByFlaw);
            controller.Record(AdviceOutcome.MisledByFlaw);
            controller.Record(AdviceOutcome.OverrodeSound);
            controller.Record(AdviceOutcome.MisledByFlaw);

            JsonValue json = controller.ToJson();

            Assert.That(json.GetNumber("subtlety"), Is.EqualTo(1));
            Assert.That(json.Get("detailedExplanationPending").AsBool(), Is.False);
            Assert.That(json.GetNumber("acceptedFlawsInARow"), Is.EqualTo(1));
            Assert.That(json.GetNumber("rejectedSoundInARow"), Is.EqualTo(0));
            Assert.That(
                json.Get("history").ToJson(),
                Is.EqualTo("[\"misled_by_flaw\",\"misled_by_flaw\",\"overrode_sound\",\"misled_by_flaw\"]"));
        }

        [Test]
        public void FromJson_RoundTrip_RestoresStateAndStreaks()
        {
            var original = new AdaptiveController();
            original.Record(AdviceOutcome.MisledByFlaw);
            original.Record(AdviceOutcome.MisledByFlaw);
            original.Record(AdviceOutcome.OverrodeSound);
            original.Record(AdviceOutcome.OverrodeSound);
            original.Record(AdviceOutcome.TrustedSound);
            original.Record(AdviceOutcome.CaughtFlaw);
            original.Record(AdviceOutcome.NoAdvice);
            original.Record(AdviceOutcome.MisledByFlaw);

            AdaptiveController restored = AdaptiveController.FromJson(JsonReader.Parse(original.ToJson().ToJson()));

            Assert.That(restored.Subtlety, Is.EqualTo(1));
            Assert.That(restored.DetailedExplanationPending, Is.True);
            Assert.That(restored.History, Is.EqualTo(original.History));
            Assert.That(restored.ToJson().ToJson(), Is.EqualTo(original.ToJson().ToJson()));

            // The half-finished streak survives: one more misled ruling fires the rule.
            Assert.That(restored.Record(AdviceOutcome.MisledByFlaw), Is.Not.Null);
            Assert.That(restored.Subtlety, Is.EqualTo(2));
        }

        [Test]
        public void FromJson_RestoredRejectStreak_FiresOnNextOverride()
        {
            JsonValue json = JsonValue.NewObject().Set("rejectedSoundInARow", 1);

            AdaptiveController restored = AdaptiveController.FromJson(json);

            Assert.That(restored.Record(AdviceOutcome.OverrodeSound), Is.Not.Null);
            Assert.That(restored.DetailedExplanationPending, Is.True);
        }

        [Test]
        public void FromJson_NullOrNonObject_ReturnsFreshController()
        {
            foreach (JsonValue json in new[] { null, JsonValue.Null, JsonValue.Number(2), JsonValue.String("x"), JsonValue.NewArray() })
            {
                AdaptiveController controller = AdaptiveController.FromJson(json);
                Assert.That(controller, Is.Not.Null);
                Assert.That(controller.Subtlety, Is.EqualTo(0));
                Assert.That(controller.DetailedExplanationPending, Is.False);
                Assert.That(controller.History, Is.Empty);
            }
        }

        [Test]
        public void FromJson_EmptyObject_ReturnsFreshController()
        {
            AdaptiveController controller = AdaptiveController.FromJson(JsonValue.NewObject());

            Assert.That(controller.Subtlety, Is.EqualTo(0));
            Assert.That(controller.DetailedExplanationPending, Is.False);
            Assert.That(controller.History, Is.Empty);
            Assert.That(controller.Record(AdviceOutcome.MisledByFlaw), Is.Null);
        }

        [TestCase(-4, 0)]
        [TestCase(0, 0)]
        [TestCase(2, 2)]
        [TestCase(3, 3)]
        [TestCase(9, 3)]
        public void FromJson_Subtlety_IsClampedToRange(double saved, int expected)
        {
            AdaptiveController controller = AdaptiveController.FromJson(JsonValue.NewObject().Set("subtlety", saved));

            Assert.That(controller.Subtlety, Is.EqualTo(expected));
        }

        [Test]
        public void FromJson_NegativeStreaks_ClampToZero()
        {
            JsonValue json = JsonValue.NewObject()
                .Set("acceptedFlawsInARow", -5)
                .Set("rejectedSoundInARow", -5);

            AdaptiveController controller = AdaptiveController.FromJson(json);

            Assert.That(controller.Record(AdviceOutcome.MisledByFlaw), Is.Null);
            Assert.That(controller.Record(AdviceOutcome.OverrodeSound), Is.Null);
            Assert.That(controller.ToJson().GetNumber("acceptedFlawsInARow"), Is.EqualTo(0));
            Assert.That(controller.ToJson().GetNumber("rejectedSoundInARow"), Is.EqualTo(1));
        }

        [Test]
        public void FromJson_WrongTypes_FallBackToDefaults()
        {
            JsonValue json = JsonValue.NewObject()
                .Set("subtlety", "three")
                .Set("detailedExplanationPending", "yes")
                .Set("acceptedFlawsInARow", true)
                .Set("history", "misled_by_flaw");

            AdaptiveController controller = AdaptiveController.FromJson(json);

            Assert.That(controller.Subtlety, Is.EqualTo(0));
            Assert.That(controller.DetailedExplanationPending, Is.False);
            Assert.That(controller.History, Is.Empty);
        }

        [Test]
        public void FromJson_UnknownOrNonStringHistoryItems_AreSkipped()
        {
            JsonValue history = TestCases.ArrayOf(
                JsonValue.String("caught_flaw"),
                JsonValue.String("made_up"),
                JsonValue.Number(3),
                JsonValue.Null,
                JsonValue.String("no_advice"),
                JsonValue.String("TRUSTED_SOUND"));
            JsonValue json = JsonValue.NewObject().Set("detailedExplanationPending", true).Set("history", history);

            AdaptiveController controller = AdaptiveController.FromJson(json);

            Assert.That(controller.History, Is.EqualTo(new[] { AdviceOutcome.CaughtFlaw, AdviceOutcome.NoAdvice }));
            Assert.That(controller.DetailedExplanationPending, Is.True);
        }

        [Test]
        public void TryParseOutcome_EveryOutcome_RoundTripsThroughName()
        {
            foreach (AdviceOutcome outcome in new[]
            {
                AdviceOutcome.NoAdvice,
                AdviceOutcome.TrustedSound,
                AdviceOutcome.OverrodeSound,
                AdviceOutcome.MisledByFlaw,
                AdviceOutcome.CaughtFlaw,
            })
            {
                Assert.That(LogNames.TryParseOutcome(LogNames.Outcome(outcome), out AdviceOutcome parsed), Is.True);
                Assert.That(parsed, Is.EqualTo(outcome));
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("misled")]
        [TestCase("Misled_By_Flaw")]
        public void TryParseOutcome_UnknownName_ReturnsFalseAndNoAdvice(string name)
        {
            Assert.That(LogNames.TryParseOutcome(name, out AdviceOutcome parsed), Is.False);
            Assert.That(parsed, Is.EqualTo(AdviceOutcome.NoAdvice));
        }

        private static AdaptiveController ControllerAt(int subtlety)
        {
            return AdaptiveController.FromJson(JsonValue.NewObject().Set("subtlety", subtlety));
        }

        private static Dictionary<FlawKind, int> Draw(AdaptiveController controller, IReadOnlyList<FlawKind> available, int seed)
        {
            var random = new Random(seed);
            var counts = new Dictionary<FlawKind, int>();
            foreach (FlawKind flaw in available)
            {
                counts[flaw] = 0;
            }

            for (int i = 0; i < Draws; i++)
            {
                FlawKind chosen = controller.ChooseFlaw(available, random);
                Assert.That(counts.ContainsKey(chosen), Is.True, "Chose a flaw that was not available: " + chosen);
                counts[chosen]++;
            }

            return counts;
        }

        /// <summary>A Random whose NextDouble always returns the same value, to hit the edges of the roll.</summary>
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
