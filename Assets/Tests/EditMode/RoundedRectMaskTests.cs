using System;
using CourtSim.Core.UI;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    public sealed class RoundedRectMaskTests
    {
        [Test]
        public void Coverage_CenterIsFullyCovered()
        {
            Assert.That(RoundedRectMask.Coverage(16, 16, 32, 32, 8f), Is.EqualTo(1f));
        }

        [Test]
        public void Coverage_CornerPixelIsTransparent_WhenRounded()
        {
            Assert.That(RoundedRectMask.Coverage(0, 0, 32, 32, 8f), Is.EqualTo(0f));
        }

        [Test]
        public void Coverage_CornerPixelIsCovered_WhenRadiusZero()
        {
            Assert.That(RoundedRectMask.Coverage(0, 0, 32, 32, 0f), Is.EqualTo(1f));
        }

        [Test]
        public void Coverage_EdgeMidpointIsCovered()
        {
            Assert.That(RoundedRectMask.Coverage(0, 16, 32, 32, 8f), Is.EqualTo(1f));
        }

        [Test]
        public void Coverage_IsSymmetric()
        {
            for (int i = 0; i < 32; i++)
            {
                Assert.That(
                    RoundedRectMask.Coverage(i, 3, 32, 32, 10f),
                    Is.EqualTo(RoundedRectMask.Coverage(31 - i, 28, 32, 32, 10f)).Within(1e-5f));
            }
        }

        [Test]
        public void Coverage_ProducesAntiAliasedEdge()
        {
            float[] mask = RoundedRectMask.BuildFill(32, 32, 10f);
            bool hasPartial = Array.Exists(mask, value => value > 0f && value < 1f);

            Assert.That(hasPartial, Is.True);
        }

        [Test]
        public void Coverage_OversizedRadius_IsClampedToCircle()
        {
            // A 32x32 square with a huge radius becomes a circle: corners empty, centre full.
            Assert.That(RoundedRectMask.Coverage(1, 1, 32, 32, 500f), Is.EqualTo(0f));
            Assert.That(RoundedRectMask.Coverage(16, 16, 32, 32, 500f), Is.EqualTo(1f));
        }

        [Test]
        public void Coverage_InsetLargerThanShape_IsEmpty()
        {
            Assert.That(RoundedRectMask.Coverage(8, 8, 16, 16, 4f, 9f), Is.EqualTo(0f));
        }

        [Test]
        public void Coverage_RejectsNonPositiveDimensions()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RoundedRectMask.Coverage(0, 0, 0, 10, 2f));
            Assert.Throws<ArgumentOutOfRangeException>(() => RoundedRectMask.Coverage(0, 0, 10, -1, 2f));
        }

        [Test]
        public void Outline_IsHollow()
        {
            Assert.That(RoundedRectMask.OutlineCoverage(16, 16, 32, 32, 8f, 2f), Is.EqualTo(0f));
            Assert.That(RoundedRectMask.OutlineCoverage(0, 16, 32, 32, 8f, 2f), Is.EqualTo(1f));
        }

        [Test]
        public void BuildOutline_HasExpectedSizeAndHollowCentre()
        {
            float[] mask = RoundedRectMask.BuildOutline(20, 10, 4f, 1.5f);

            Assert.That(mask.Length, Is.EqualTo(200));
            Assert.That(mask[(5 * 20) + 10], Is.EqualTo(0f));
        }
    }
}
