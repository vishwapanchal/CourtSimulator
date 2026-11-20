using CourtSim.Core.Navigation;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    public sealed class MovementMathTests
    {
        private const float Epsilon = 1e-4f;

        [Test]
        public void PlanarVelocity_ForwardAtYawZero_PointsAlongPositiveZ()
        {
            PlanarVector v = MovementMath.PlanarVelocity(0f, 1f, 0f, 2f);

            Assert.That(v.X, Is.EqualTo(0f).Within(Epsilon));
            Assert.That(v.Z, Is.EqualTo(2f).Within(Epsilon));
        }

        [Test]
        public void PlanarVelocity_StrafeRightAtYawZero_PointsAlongPositiveX()
        {
            PlanarVector v = MovementMath.PlanarVelocity(1f, 0f, 0f, 1f);

            Assert.That(v.X, Is.EqualTo(1f).Within(Epsilon));
            Assert.That(v.Z, Is.EqualTo(0f).Within(Epsilon));
        }

        [Test]
        public void PlanarVelocity_ForwardAtYaw90_PointsAlongPositiveX()
        {
            PlanarVector v = MovementMath.PlanarVelocity(0f, 1f, 90f, 1f);

            Assert.That(v.X, Is.EqualTo(1f).Within(Epsilon));
            Assert.That(v.Z, Is.EqualTo(0f).Within(Epsilon));
        }

        [Test]
        public void PlanarVelocity_StrafeRightAtYaw90_PointsAlongNegativeZ()
        {
            PlanarVector v = MovementMath.PlanarVelocity(1f, 0f, 90f, 1f);

            Assert.That(v.X, Is.EqualTo(0f).Within(Epsilon));
            Assert.That(v.Z, Is.EqualTo(-1f).Within(Epsilon));
        }

        [Test]
        public void PlanarVelocity_Diagonal_IsNormalised()
        {
            PlanarVector v = MovementMath.PlanarVelocity(1f, 1f, 37f, 3f);

            Assert.That(v.Magnitude, Is.EqualTo(3f).Within(Epsilon));
        }

        [Test]
        public void PlanarVelocity_PartialInput_IsNotScaledUp()
        {
            PlanarVector v = MovementMath.PlanarVelocity(0f, 0.5f, 0f, 4f);

            Assert.That(v.Magnitude, Is.EqualTo(2f).Within(Epsilon));
        }

        [Test]
        public void ApplyLook_TurnsRightAndLooksUp()
        {
            MovementMath.ApplyLook(10f, 0f, 5f, 2f, 1f, false, 85f, out float yaw, out float pitch);

            Assert.That(yaw, Is.EqualTo(15f).Within(Epsilon));
            Assert.That(pitch, Is.EqualTo(-2f).Within(Epsilon), "Unity pitch is negative when looking up.");
        }

        [Test]
        public void ApplyLook_InvertY_FlipsVertical()
        {
            MovementMath.ApplyLook(0f, 0f, 0f, 2f, 1.5f, true, 85f, out _, out float pitch);

            Assert.That(pitch, Is.EqualTo(3f).Within(Epsilon));
        }

        [Test]
        public void ApplyLook_ClampsPitchAndWrapsYaw()
        {
            MovementMath.ApplyLook(355f, 80f, 10f, -100f, 1f, false, 85f, out float yaw, out float pitch);

            Assert.That(yaw, Is.EqualTo(5f).Within(Epsilon));
            Assert.That(pitch, Is.EqualTo(85f).Within(Epsilon));
        }

        [TestCase(0f, 0f)]
        [TestCase(360f, 0f)]
        [TestCase(-90f, 270f)]
        [TestCase(725f, 5f)]
        [TestCase(-0.00001f, 0f)]
        public void WrapAngle360_ReturnsValueInRange(float input, float expected)
        {
            float wrapped = MovementMath.WrapAngle360(input);

            Assert.That(wrapped, Is.EqualTo(expected).Within(1e-3f));
            Assert.That(wrapped, Is.GreaterThanOrEqualTo(0f).And.LessThan(360f));
        }

        [TestCase(100f, 85f, 85f)]
        [TestCase(-100f, 85f, -85f)]
        [TestCase(10f, 85f, 10f)]
        [TestCase(50f, 200f, 50f)]
        [TestCase(95f, 200f, 90f)]
        [TestCase(5f, -10f, 0f)]
        public void ClampPitch_RespectsLimit(float pitch, float limit, float expected)
        {
            Assert.That(MovementMath.ClampPitch(pitch, limit), Is.EqualTo(expected).Within(Epsilon));
        }

        [Test]
        public void Damp_MovesTowardTarget_WithoutOvershoot()
        {
            float result = MovementMath.Damp(0f, 10f, 5f, 0.1f);

            Assert.That(result, Is.GreaterThan(0f).And.LessThan(10f));
        }

        [Test]
        public void Damp_IsFrameRateIndependent()
        {
            float oneStep = MovementMath.Damp(0f, 10f, 8f, 0.2f);
            float twoSteps = MovementMath.Damp(MovementMath.Damp(0f, 10f, 8f, 0.1f), 10f, 8f, 0.1f);

            Assert.That(twoSteps, Is.EqualTo(oneStep).Within(Epsilon));
        }

        [Test]
        public void Damp_NonPositiveSharpness_Snaps()
        {
            Assert.That(MovementMath.Damp(0f, 10f, 0f, 0.1f), Is.EqualTo(10f));
        }

        [Test]
        public void Damp_ZeroDeltaTime_HoldsCurrent()
        {
            Assert.That(MovementMath.Damp(3f, 10f, 5f, 0f), Is.EqualTo(3f));
        }

        [Test]
        public void Damp_Planar_DampsEachAxis()
        {
            PlanarVector result = MovementMath.Damp(PlanarVector.Zero, new PlanarVector(4f, -4f), 0f, 0.1f);

            Assert.That(result, Is.EqualTo(new PlanarVector(4f, -4f)));
        }

        [Test]
        public void ApplyGravity_Grounded_PinsToStickVelocity()
        {
            Assert.That(MovementMath.ApplyGravity(-20f, true, 9.81f, 0.02f), Is.EqualTo(MovementMath.GroundedStickVelocity));
        }

        [Test]
        public void ApplyGravity_Airborne_Accelerates()
        {
            float v = MovementMath.ApplyGravity(0f, false, 10f, 0.5f);

            Assert.That(v, Is.EqualTo(-5f).Within(Epsilon));
        }

        [Test]
        public void ApplyGravity_GroundedButRising_IsNotPinned()
        {
            float v = MovementMath.ApplyGravity(3f, true, 10f, 0.1f);

            Assert.That(v, Is.EqualTo(2f).Within(Epsilon));
        }

        [Test]
        public void ApplyGravity_NegativeDeltaTime_IsIgnored()
        {
            Assert.That(MovementMath.ApplyGravity(1f, false, 10f, -1f), Is.EqualTo(1f));
        }

        [Test]
        public void PlanarVector_EqualityAndFormatting()
        {
            var a = new PlanarVector(1f, 2f);
            var b = new PlanarVector(1f, 2f);

            Assert.That(a.Equals((object)b), Is.True);
            Assert.That(a.Equals((object)"not a vector"), Is.False);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a.ToString(), Is.EqualTo("(1, 2)"));
        }
    }
}
