using System;
using CourtSim.Core.Interaction;

namespace CourtSim.Core.Courtroom
{
    /// <summary>
    /// An axis-aligned box describing one fixture's footprint in room space.
    /// The room origin is the floor centre; +Z points from the public gallery toward the judge.
    /// </summary>
    public sealed class FixtureSpec
    {
        public FixtureSpec(
            string id,
            FixtureKind kind,
            Float3 center,
            Float3 size,
            float facingYawDegrees = 0f,
            InteractionAction action = InteractionAction.None,
            string prompt = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Fixture id must not be empty.", nameof(id));
            }

            if (size.X < 0f || size.Y < 0f || size.Z < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(size), size, "Fixture size must not be negative.");
            }

            if (action != InteractionAction.None && string.IsNullOrWhiteSpace(prompt))
            {
                throw new ArgumentException("Interactive fixtures need a prompt.", nameof(prompt));
            }

            Id = id;
            Kind = kind;
            Center = center;
            Size = size;
            FacingYawDegrees = facingYawDegrees;
            Action = action;
            Prompt = prompt ?? string.Empty;
        }

        public string Id { get; }

        public FixtureKind Kind { get; }

        /// <summary>Box centre in metres.</summary>
        public Float3 Center { get; }

        /// <summary>Box extents (full width, height, depth) in metres.</summary>
        public Float3 Size { get; }

        /// <summary>Direction the fixture's front faces; used for decoration and spawn orientation.</summary>
        public float FacingYawDegrees { get; }

        public InteractionAction Action { get; }

        public string Prompt { get; }

        /// <summary>Solid fixtures block movement and must not overlap each other.</summary>
        public bool IsSolid => Kind != FixtureKind.PlayerSpawn;

        public bool IsInteractive => Action != InteractionAction.None;

        public Float3 Min => Center - (Size * 0.5f);

        public Float3 Max => Center + (Size * 0.5f);

        /// <summary>True when the XZ footprints overlap by more than <paramref name="tolerance"/> metres.</summary>
        public bool FootprintOverlaps(FixtureSpec other, float tolerance = 0.01f)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            float overlapX = Math.Min(Max.X, other.Max.X) - Math.Max(Min.X, other.Min.X);
            float overlapZ = Math.Min(Max.Z, other.Max.Z) - Math.Max(Min.Z, other.Min.Z);
            return overlapX > tolerance && overlapZ > tolerance;
        }

        /// <summary>True when the point lies inside the XZ footprint (inclusive).</summary>
        public bool FootprintContains(Float3 point)
        {
            return point.X >= Min.X && point.X <= Max.X && point.Z >= Min.Z && point.Z <= Max.Z;
        }
    }
}
