using System;
using System.Collections.Generic;
using CourtSim.Core.Interaction;

namespace CourtSim.Core.Courtroom
{
    /// <summary>
    /// Data description of a courtroom: room dimensions plus every fixture's footprint.
    /// Builders turn this into geometry; <see cref="Validate"/> catches broken layouts before they are built.
    /// </summary>
    public sealed class CourtroomLayout
    {
        /// <summary>Fixtures every layout must contain for the training flow to work.</summary>
        public static readonly IReadOnlyList<FixtureKind> RequiredKinds = new[]
        {
            FixtureKind.PlayerSpawn,
            FixtureKind.JudgeBench,
            FixtureKind.WitnessStand,
            FixtureKind.EvidenceDisplay,
            FixtureKind.AiAssistantTerminal,
        };

        /// <summary>Minimum free radius around the spawn point; slightly larger than the player capsule.</summary>
        public const float SpawnClearance = 0.4f;

        private const float BoundsTolerance = 0.001f;

        private readonly FixtureSpec[] _fixtures;

        public CourtroomLayout(Float3 roomSize, IEnumerable<FixtureSpec> fixtures)
        {
            if (roomSize.X <= 0f || roomSize.Y <= 0f || roomSize.Z <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(roomSize), roomSize, "Room dimensions must be positive.");
            }

            if (fixtures == null)
            {
                throw new ArgumentNullException(nameof(fixtures));
            }

            RoomSize = roomSize;
            _fixtures = new List<FixtureSpec>(fixtures).ToArray();
            foreach (FixtureSpec fixture in _fixtures)
            {
                if (fixture == null)
                {
                    throw new ArgumentException("Fixtures must not contain null.", nameof(fixtures));
                }
            }
        }

        /// <summary>Interior width (X), height (Y) and depth (Z) in metres.</summary>
        public Float3 RoomSize { get; }

        public IReadOnlyList<FixtureSpec> Fixtures => _fixtures;

        /// <summary>Returns the first fixture of <paramref name="kind"/>, or null.</summary>
        public FixtureSpec Find(FixtureKind kind)
        {
            foreach (FixtureSpec fixture in _fixtures)
            {
                if (fixture.Kind == kind)
                {
                    return fixture;
                }
            }

            return null;
        }

        /// <summary>Checks the layout for problems. Returns an empty list when it is valid.</summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            ValidateIds(errors);
            ValidateRequiredKinds(errors);
            ValidateBounds(errors);
            ValidateOverlaps(errors);
            ValidateSpawn(errors);
            return errors;
        }

        /// <summary>
        /// A 14 m × 20 m common-law style courtroom: judge's bench at the far (+Z) end, counsel
        /// tables in the well, a bar with a central gate, and the public gallery at the rear.
        /// </summary>
        public static CourtroomLayout CreateDefault()
        {
            var fixtures = new[]
            {
                new FixtureSpec("spawn", FixtureKind.PlayerSpawn, new Float3(0f, 0f, -8.5f), Float3.Zero),

                new FixtureSpec(
                    "judge_bench", FixtureKind.JudgeBench, new Float3(0f, 0.8f, 8f), new Float3(5f, 1.6f, 2.4f), 180f,
                    InteractionAction.OpenDecisionPoint, "Approach the bench"),
                new FixtureSpec("clerk_desk", FixtureKind.ClerkDesk, new Float3(0f, 0.45f, 5.6f), new Float3(3f, 0.9f, 1f), 180f),
                new FixtureSpec("witness_stand", FixtureKind.WitnessStand, new Float3(4.5f, 0.6f, 6.5f), new Float3(1.6f, 1.2f, 1.6f), 270f),
                new FixtureSpec(
                    "evidence_display", FixtureKind.EvidenceDisplay, new Float3(-6.75f, 1.8f, 4f), new Float3(0.15f, 1.6f, 2.6f), 90f,
                    InteractionAction.OpenEvidence, "Examine the evidence"),
                new FixtureSpec(
                    "ai_terminal", FixtureKind.AiAssistantTerminal, new Float3(-5.6f, 0.6f, 1.5f), new Float3(1f, 1.2f, 0.8f), 90f,
                    InteractionAction.ShowAssistant, "Consult the AI assistant"),

                new FixtureSpec("prosecution_table", FixtureKind.ProsecutionTable, new Float3(2.5f, 0.38f, 2f), new Float3(3f, 0.76f, 1.2f)),
                new FixtureSpec("defense_table", FixtureKind.DefenseTable, new Float3(-2.5f, 0.38f, 2f), new Float3(3f, 0.76f, 1.2f)),
                new FixtureSpec("accused_dock", FixtureKind.AccusedDock, new Float3(5f, 0.55f, -1.5f), new Float3(1.8f, 1.1f, 2f), 270f),

                new FixtureSpec("bar_left", FixtureKind.Bar, new Float3(-3.9f, 0.5f, -3f), new Float3(6.2f, 1f, 0.1f)),
                new FixtureSpec("bar_right", FixtureKind.Bar, new Float3(3.9f, 0.5f, -3f), new Float3(6.2f, 1f, 0.1f)),

                new FixtureSpec("gallery_front_left", FixtureKind.GalleryBench, new Float3(-3.75f, 0.25f, -5f), new Float3(4.5f, 0.5f, 0.6f)),
                new FixtureSpec("gallery_front_right", FixtureKind.GalleryBench, new Float3(3.75f, 0.25f, -5f), new Float3(4.5f, 0.5f, 0.6f)),
                new FixtureSpec("gallery_rear_left", FixtureKind.GalleryBench, new Float3(-3.75f, 0.25f, -6.6f), new Float3(4.5f, 0.5f, 0.6f)),
                new FixtureSpec("gallery_rear_right", FixtureKind.GalleryBench, new Float3(3.75f, 0.25f, -6.6f), new Float3(4.5f, 0.5f, 0.6f)),
            };

            return new CourtroomLayout(new Float3(14f, 5f, 20f), fixtures);
        }

        private void ValidateIds(List<string> errors)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (FixtureSpec fixture in _fixtures)
            {
                if (!seen.Add(fixture.Id))
                {
                    errors.Add($"Duplicate fixture id '{fixture.Id}'.");
                }
            }
        }

        private void ValidateRequiredKinds(List<string> errors)
        {
            foreach (FixtureKind kind in RequiredKinds)
            {
                if (Find(kind) == null)
                {
                    errors.Add($"Missing required fixture: {kind}.");
                }
            }
        }

        private void ValidateBounds(List<string> errors)
        {
            float halfX = (RoomSize.X * 0.5f) + BoundsTolerance;
            float halfZ = (RoomSize.Z * 0.5f) + BoundsTolerance;
            float maxY = RoomSize.Y + BoundsTolerance;

            foreach (FixtureSpec fixture in _fixtures)
            {
                Float3 min = fixture.Min;
                Float3 max = fixture.Max;
                bool inside = min.X >= -halfX && max.X <= halfX
                    && min.Z >= -halfZ && max.Z <= halfZ
                    && min.Y >= -BoundsTolerance && max.Y <= maxY;
                if (!inside)
                {
                    errors.Add($"Fixture '{fixture.Id}' extends outside the room.");
                }
            }
        }

        private void ValidateOverlaps(List<string> errors)
        {
            for (int i = 0; i < _fixtures.Length; i++)
            {
                if (!_fixtures[i].IsSolid)
                {
                    continue;
                }

                for (int j = i + 1; j < _fixtures.Length; j++)
                {
                    if (_fixtures[j].IsSolid && _fixtures[i].FootprintOverlaps(_fixtures[j]))
                    {
                        errors.Add($"Fixtures '{_fixtures[i].Id}' and '{_fixtures[j].Id}' overlap.");
                    }
                }
            }
        }

        private void ValidateSpawn(List<string> errors)
        {
            FixtureSpec spawn = Find(FixtureKind.PlayerSpawn);
            if (spawn == null)
            {
                return;
            }

            var clearance = new FixtureSpec(
                "spawn_clearance",
                FixtureKind.GalleryBench,
                spawn.Center,
                new Float3(SpawnClearance * 2f, 0f, SpawnClearance * 2f));

            foreach (FixtureSpec fixture in _fixtures)
            {
                if (fixture.IsSolid && clearance.FootprintOverlaps(fixture, 0f))
                {
                    errors.Add($"Player spawn is blocked by '{fixture.Id}'.");
                }
            }
        }
    }
}
