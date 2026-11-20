using System;

namespace CourtSim.Core.Navigation
{
    /// <summary>A horizontal (XZ-plane) vector in Unity's left-handed, Y-up world space.</summary>
    public readonly struct PlanarVector : IEquatable<PlanarVector>
    {
        public static readonly PlanarVector Zero = new PlanarVector(0f, 0f);

        public PlanarVector(float x, float z)
        {
            X = x;
            Z = z;
        }

        public float X { get; }

        public float Z { get; }

        public float Magnitude => MathF.Sqrt((X * X) + (Z * Z));

        public bool Equals(PlanarVector other)
        {
            return X.Equals(other.X) && Z.Equals(other.Z);
        }

        public override bool Equals(object obj)
        {
            return obj is PlanarVector other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Z);
        }

        public override string ToString()
        {
            return $"({X:0.###}, {Z:0.###})";
        }
    }

    /// <summary>
    /// Engine-independent movement math shared by every locomotion mode (desktop now, VR later).
    /// All angles are in degrees; yaw 0 faces +Z and positive yaw turns clockwise seen from above.
    /// </summary>
    public static class MovementMath
    {
        /// <summary>Default vertical look limit; just short of straight up/down to avoid gimbal flip.</summary>
        public const float DefaultPitchLimit = 85f;

        /// <summary>Downward speed applied while grounded so the controller stays snapped to floors and ramps.</summary>
        public const float GroundedStickVelocity = -2f;

        private const float DegToRad = MathF.PI / 180f;

        /// <summary>
        /// Converts move input (x = strafe right, y = forward) into a world-space velocity for a body
        /// facing <paramref name="yawDegrees"/>. Input longer than 1 is normalised so diagonals are not faster.
        /// </summary>
        public static PlanarVector PlanarVelocity(float inputX, float inputY, float yawDegrees, float speed)
        {
            float length = MathF.Sqrt((inputX * inputX) + (inputY * inputY));
            if (length > 1f)
            {
                inputX /= length;
                inputY /= length;
            }

            float yaw = yawDegrees * DegToRad;
            float sin = MathF.Sin(yaw);
            float cos = MathF.Cos(yaw);

            // forward = (sin, cos), right = (cos, -sin) in XZ.
            float x = (inputX * cos) + (inputY * sin);
            float z = (-inputX * sin) + (inputY * cos);
            return new PlanarVector(x * speed, z * speed);
        }

        /// <summary>Applies a look delta and returns the new yaw (wrapped to [0, 360)) and clamped pitch.</summary>
        /// <param name="deltaX">Horizontal look input; positive turns right.</param>
        /// <param name="deltaY">Vertical look input; positive looks up unless <paramref name="invertY"/>.</param>
        public static void ApplyLook(
            float yaw,
            float pitch,
            float deltaX,
            float deltaY,
            float sensitivity,
            bool invertY,
            float pitchLimit,
            out float newYaw,
            out float newPitch)
        {
            float vertical = invertY ? deltaY : -deltaY;
            newYaw = WrapAngle360(yaw + (deltaX * sensitivity));
            newPitch = ClampPitch(pitch + (vertical * sensitivity), pitchLimit);
        }

        /// <summary>Clamps <paramref name="pitch"/> to ±<paramref name="limit"/> (limit itself clamped to [0, 90]).</summary>
        public static float ClampPitch(float pitch, float limit = DefaultPitchLimit)
        {
            float safeLimit = Math.Clamp(limit, 0f, 90f);
            return Math.Clamp(pitch, -safeLimit, safeLimit);
        }

        /// <summary>Wraps any angle into [0, 360).</summary>
        public static float WrapAngle360(float degrees)
        {
            float wrapped = degrees % 360f;
            if (wrapped < 0f)
            {
                wrapped += 360f;
            }

            // -0.00001 % 360 + 360 can round to exactly 360.
            return wrapped >= 360f ? 0f : wrapped;
        }

        /// <summary>
        /// Frame-rate independent exponential smoothing toward <paramref name="target"/>.
        /// A non-positive <paramref name="sharpness"/> snaps to the target immediately.
        /// </summary>
        public static float Damp(float current, float target, float sharpness, float deltaTime)
        {
            if (sharpness <= 0f)
            {
                return target;
            }

            if (deltaTime <= 0f)
            {
                return current;
            }

            float t = 1f - MathF.Exp(-sharpness * deltaTime);
            return current + ((target - current) * t);
        }

        /// <summary>Component-wise <see cref="Damp(float, float, float, float)"/> for planar velocities.</summary>
        public static PlanarVector Damp(PlanarVector current, PlanarVector target, float sharpness, float deltaTime)
        {
            return new PlanarVector(
                Damp(current.X, target.X, sharpness, deltaTime),
                Damp(current.Z, target.Z, sharpness, deltaTime));
        }

        /// <summary>
        /// Integrates gravity. While grounded and not moving upward, the result is pinned to
        /// <see cref="GroundedStickVelocity"/> so velocity does not accumulate while standing still.
        /// </summary>
        /// <param name="gravity">Gravity magnitude in m/s² (positive pulls down).</param>
        public static float ApplyGravity(float verticalVelocity, bool isGrounded, float gravity, float deltaTime)
        {
            if (isGrounded && verticalVelocity <= 0f)
            {
                return GroundedStickVelocity;
            }

            return verticalVelocity - (gravity * Math.Max(deltaTime, 0f));
        }
    }
}
