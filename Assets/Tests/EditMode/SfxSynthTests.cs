using System;
using CourtSim.Core.Presentation;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    public sealed class SfxSynthTests
    {
        private static readonly SoundCue[] AllCues = (SoundCue[])Enum.GetValues(typeof(SoundCue));

        [Test]
        public void Generate_EveryCue_HasItsStatedLength()
        {
            foreach (SoundCue cue in AllCues)
            {
                int expected = (int)Math.Round(SfxSynth.DefaultSampleRate * SfxSynth.DurationSeconds(cue));
                Assert.That(SfxSynth.Generate(cue).Length, Is.EqualTo(expected), cue.ToString());
            }
        }

        [Test]
        public void Generate_CustomRate_ScalesLength()
        {
            Assert.That(SfxSynth.Generate(SoundCue.Gavel, 22050).Length, Is.EqualTo((int)Math.Round(22050 * SfxSynth.DurationSeconds(SoundCue.Gavel))));
        }

        [Test]
        public void Generate_EveryCue_NeverClips()
        {
            foreach (SoundCue cue in AllCues)
            {
                Assert.That(Peak(SfxSynth.Generate(cue)), Is.LessThanOrEqualTo(1f), cue.ToString());
            }
        }

        [Test]
        public void Generate_EveryCue_IsAudible()
        {
            foreach (SoundCue cue in AllCues)
            {
                float[] samples = SfxSynth.Generate(cue);
                Assert.That(Peak(samples), Is.GreaterThan(0.2f), cue.ToString());
                Assert.That(Rms(samples), Is.GreaterThan(0.01), cue.ToString());
            }
        }

        [Test]
        public void Generate_SameCue_IsIdenticalEveryTime()
        {
            foreach (SoundCue cue in AllCues)
            {
                Assert.That(SfxSynth.Generate(cue), Is.EqualTo(SfxSynth.Generate(cue)), cue.ToString());
            }
        }

        [Test]
        public void Generate_OneShots_StartAndEndSilent()
        {
            foreach (SoundCue cue in AllCues)
            {
                if (SfxSynth.IsLoop(cue))
                {
                    continue;
                }

                float[] samples = SfxSynth.Generate(cue);
                Assert.That(samples[0], Is.EqualTo(0f), cue.ToString());
                Assert.That(Math.Abs(samples[samples.Length - 1]), Is.LessThan(0.01f), cue.ToString());
            }
        }

        [Test]
        public void Generate_Drone_LoopsWithoutAJump()
        {
            float[] drone = SfxSynth.Generate(SoundCue.Drone);

            // Wrapping from the last sample to the first must be no bigger a step than the pad's normal movement.
            Assert.That(SfxSynth.IsLoop(SoundCue.Drone), Is.True);
            Assert.That(Math.Abs(drone[0] - drone[drone.Length - 1]), Is.LessThan(0.02f));
        }

        [Test]
        public void Generate_BadSampleRate_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SfxSynth.Generate(SoundCue.Gavel, 0));
        }

        private static float Peak(float[] samples)
        {
            float peak = 0f;
            foreach (float sample in samples)
            {
                peak = Math.Max(peak, Math.Abs(sample));
            }

            return peak;
        }

        private static double Rms(float[] samples)
        {
            double sum = 0.0;
            foreach (float sample in samples)
            {
                sum += sample * sample;
            }

            return Math.Sqrt(sum / samples.Length);
        }
    }
}
