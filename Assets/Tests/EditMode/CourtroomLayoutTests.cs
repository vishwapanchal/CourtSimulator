using System;
using System.Collections.Generic;
using CourtSim.Core.Courtroom;
using CourtSim.Core.Interaction;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    public sealed class CourtroomLayoutTests
    {
        private static readonly Float3 Room = new Float3(10f, 4f, 10f);

        [Test]
        public void DefaultLayout_IsValid()
        {
            IReadOnlyList<string> errors = CourtroomLayout.CreateDefault().Validate();

            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [Test]
        public void DefaultLayout_HasInteractiveKeyFixtures()
        {
            CourtroomLayout layout = CourtroomLayout.CreateDefault();

            Assert.That(layout.Find(FixtureKind.JudgeBench).Action, Is.EqualTo(InteractionAction.OpenDecisionPoint));
            Assert.That(layout.Find(FixtureKind.EvidenceDisplay).Action, Is.EqualTo(InteractionAction.OpenEvidence));
            Assert.That(layout.Find(FixtureKind.AiAssistantTerminal).Action, Is.EqualTo(InteractionAction.ShowAssistant));
            Assert.That(layout.Find(FixtureKind.WitnessStand).IsInteractive, Is.False);
        }

        [Test]
        public void DefaultLayout_BarLeavesWalkableGate()
        {
            CourtroomLayout layout = CourtroomLayout.CreateDefault();
            var gate = new FixtureSpec("gate", FixtureKind.GalleryBench, new Float3(0f, 0.5f, -3f), new Float3(1.2f, 1f, 0.5f));

            foreach (FixtureSpec fixture in layout.Fixtures)
            {
                if (fixture.Kind == FixtureKind.Bar)
                {
                    Assert.That(gate.FootprintOverlaps(fixture), Is.False, fixture.Id);
                }
            }
        }

        [Test]
        public void Validate_ReportsMissingRequiredKinds()
        {
            var layout = new CourtroomLayout(Room, new FixtureSpec[0]);

            IReadOnlyList<string> errors = layout.Validate();

            Assert.That(errors.Count, Is.EqualTo(CourtroomLayout.RequiredKinds.Count));
        }

        [Test]
        public void Validate_ReportsDuplicateIds()
        {
            var layout = new CourtroomLayout(Room, new[]
            {
                Box("dup", FixtureKind.Bar, -3f, 0f),
                Box("dup", FixtureKind.Bar, 3f, 0f),
            });

            AssertHasError(layout.Validate(), "Duplicate fixture id 'dup'");
        }

        [Test]
        public void Validate_ReportsOverlaps()
        {
            var layout = new CourtroomLayout(Room, new[]
            {
                Box("a", FixtureKind.Bar, 0f, 0f),
                Box("b", FixtureKind.Bar, 0.5f, 0f),
            });

            AssertHasError(layout.Validate(), "'a' and 'b' overlap");
        }

        [Test]
        public void Validate_AllowsTouchingFixtures()
        {
            var layout = new CourtroomLayout(Room, new[]
            {
                Box("a", FixtureKind.Bar, 0f, 0f),
                Box("b", FixtureKind.Bar, 1f, 0f),
            });

            AssertNoError(layout.Validate(), "overlap");
        }

        [Test]
        public void Validate_ReportsOutOfBounds()
        {
            var layout = new CourtroomLayout(Room, new[]
            {
                Box("outside", FixtureKind.Bar, 4.9f, 0f),
                new FixtureSpec("too_tall", FixtureKind.Bar, new Float3(0f, 3.9f, 3f), new Float3(1f, 1f, 1f)),
            });

            IReadOnlyList<string> errors = layout.Validate();

            AssertHasError(errors, "'outside' extends outside");
            AssertHasError(errors, "'too_tall' extends outside");
        }

        [Test]
        public void Validate_ReportsBlockedSpawn()
        {
            var layout = new CourtroomLayout(Room, new[]
            {
                new FixtureSpec("spawn", FixtureKind.PlayerSpawn, new Float3(0.7f, 0f, 0f), Float3.Zero),
                Box("bench", FixtureKind.GalleryBench, 0f, 0f),
            });

            AssertHasError(layout.Validate(), "spawn is blocked by 'bench'");
        }

        [Test]
        public void SpawnIsNotSolid_AndIgnoredByOverlapCheck()
        {
            var spawn = new FixtureSpec("spawn", FixtureKind.PlayerSpawn, Float3.Zero, Float3.Zero);

            Assert.That(spawn.IsSolid, Is.False);
            var layout = new CourtroomLayout(Room, new[] { spawn, new FixtureSpec("spawn2", FixtureKind.PlayerSpawn, Float3.Zero, Float3.Zero) });
            AssertNoError(layout.Validate(), "overlap");
        }

        [Test]
        public void Find_ReturnsNullWhenAbsent()
        {
            var layout = new CourtroomLayout(Room, new FixtureSpec[0]);

            Assert.That(layout.Find(FixtureKind.JudgeBench), Is.Null);
        }

        [Test]
        public void Layout_ValidatesConstructorInput()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CourtroomLayout(new Float3(0f, 1f, 1f), new FixtureSpec[0]));
            Assert.Throws<ArgumentNullException>(() => new CourtroomLayout(Room, null));
            Assert.Throws<ArgumentException>(() => new CourtroomLayout(Room, new FixtureSpec[] { null }));
        }

        [Test]
        public void FixtureSpec_ValidatesInput()
        {
            Assert.Throws<ArgumentException>(() => new FixtureSpec("", FixtureKind.Bar, Float3.Zero, Float3.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FixtureSpec("a", FixtureKind.Bar, Float3.Zero, new Float3(-1f, 1f, 1f)));
            Assert.Throws<ArgumentException>(() => new FixtureSpec("a", FixtureKind.Bar, Float3.Zero, Float3.Zero, 0f, InteractionAction.OpenEvidence));
            Assert.Throws<ArgumentNullException>(() => Box("a", FixtureKind.Bar, 0f, 0f).FootprintOverlaps(null));
        }

        [Test]
        public void FixtureSpec_ComputesBoundsAndContainment()
        {
            var fixture = new FixtureSpec("a", FixtureKind.Bar, new Float3(1f, 0.5f, 2f), new Float3(2f, 1f, 4f));

            Assert.That(fixture.Min, Is.EqualTo(new Float3(0f, 0f, 0f)));
            Assert.That(fixture.Max, Is.EqualTo(new Float3(2f, 1f, 4f)));
            Assert.That(fixture.FootprintContains(new Float3(1f, 99f, 3.9f)), Is.True);
            Assert.That(fixture.FootprintContains(new Float3(2.1f, 0f, 1f)), Is.False);
            Assert.That(fixture.Prompt, Is.Empty);
        }

        [Test]
        public void Float3_ArithmeticAndEquality()
        {
            var a = new Float3(1f, 2f, 3f);
            var b = new Float3(0.5f, 0.5f, 0.5f);

            Assert.That(a + b, Is.EqualTo(new Float3(1.5f, 2.5f, 3.5f)));
            Assert.That(a - b, Is.EqualTo(new Float3(0.5f, 1.5f, 2.5f)));
            Assert.That(a * 2f, Is.EqualTo(new Float3(2f, 4f, 6f)));
            Assert.That(a.Equals((object)new Float3(1f, 2f, 3f)), Is.True);
            Assert.That(a.Equals((object)null), Is.False);
            Assert.That(a.GetHashCode(), Is.EqualTo(new Float3(1f, 2f, 3f).GetHashCode()));
            Assert.That(a.ToString(), Is.EqualTo("(1, 2, 3)"));
        }

        private static void AssertHasError(IReadOnlyList<string> errors, string fragment)
        {
            Assert.That(CountContaining(errors, fragment), Is.GreaterThan(0), $"Expected an error containing \"{fragment}\". Got:\n{string.Join("\n", errors)}");
        }

        private static void AssertNoError(IReadOnlyList<string> errors, string fragment)
        {
            Assert.That(CountContaining(errors, fragment), Is.EqualTo(0), $"Unexpected error containing \"{fragment}\":\n{string.Join("\n", errors)}");
        }

        private static int CountContaining(IReadOnlyList<string> errors, string fragment)
        {
            int count = 0;
            foreach (string error in errors)
            {
                if (error.IndexOf(fragment, StringComparison.Ordinal) >= 0)
                {
                    count++;
                }
            }

            return count;
        }

        private static FixtureSpec Box(string id, FixtureKind kind, float x, float z)
        {
            return new FixtureSpec(id, kind, new Float3(x, 0.5f, z), new Float3(1f, 1f, 1f));
        }
    }
}
