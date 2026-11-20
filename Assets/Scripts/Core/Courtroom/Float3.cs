using System;

namespace CourtSim.Core.Courtroom
{
    /// <summary>Engine-independent 3D vector (metres, Y up) so layout data stays testable outside Unity.</summary>
    public readonly struct Float3 : IEquatable<Float3>
    {
        public static readonly Float3 Zero = new Float3(0f, 0f, 0f);

        public Float3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }

        public float Y { get; }

        public float Z { get; }

        public static Float3 operator +(Float3 a, Float3 b)
        {
            return new Float3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        public static Float3 operator -(Float3 a, Float3 b)
        {
            return new Float3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }

        public static Float3 operator *(Float3 a, float scale)
        {
            return new Float3(a.X * scale, a.Y * scale, a.Z * scale);
        }

        public bool Equals(Float3 other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        }

        public override bool Equals(object obj)
        {
            return obj is Float3 other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Z);
        }

        public override string ToString()
        {
            return $"({X:0.##}, {Y:0.##}, {Z:0.##})";
        }
    }
}
