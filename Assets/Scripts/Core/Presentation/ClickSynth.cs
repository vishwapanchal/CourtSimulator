using System;

namespace CourtSim.Core.Presentation
{
    /// <summary>
    /// Makes the soft keyboard "click" heard while the assistant's message types out, from numbers alone,
    /// so the project ships no audio files. A click is a short burst of noise mixed with a quick tone, both
    /// fading away exponentially; the seed varies the noise and nudges the pitch so clicks don't sound identical.
    /// </summary>
    public static class ClickSynth
    {
        public const int DefaultSampleRate = 44100;
        public const double DefaultDurationSeconds = 0.025;

        /// <summary>The click tone's centre frequency; each seed moves it by up to ±12%.</summary>
        private const double BaseToneHz = 1900.0;
        private const double PitchVariation = 0.12;

        /// <summary>Time for the loudness to fall to about a third (1/e). Short, so it sounds like a tap.</summary>
        private const double DecaySeconds = 0.004;
        private const float PeakLevel = 0.6f;

        /// <summary>How quickly the low-pass filter follows the noise (0..1); lower is duller.</summary>
        private const double NoiseSmoothing = 0.45;

        /// <summary>Mono samples in [-1, 1] for one click.</summary>
        /// <param name="seed">The same seed always gives the same click.</param>
        public static float[] Generate(int seed, int sampleRate = DefaultSampleRate, double durationSeconds = DefaultDurationSeconds)
        {
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive.");
            }

            if (durationSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Duration must be positive.");
            }

            var random = new Random(seed);
            int count = Math.Max(1, (int)Math.Round(sampleRate * durationSeconds));
            double toneHz = BaseToneHz * (1.0 + ((random.NextDouble() * 2.0) - 1.0) * PitchVariation);
            var samples = new float[count];

            // A one-pole low-pass filter softens the raw noise from a hiss into a duller "tick".
            double smoothed = 0.0;
            double peak = 0.0;
            for (int n = 0; n < count; n++)
            {
                double t = (double)n / sampleRate;
                double envelope = Math.Exp(-t / DecaySeconds);
                double noise = (random.NextDouble() * 2.0) - 1.0;
                smoothed += NoiseSmoothing * (noise - smoothed);
                double tone = Math.Sin(2.0 * Math.PI * toneHz * t);
                double value = envelope * ((0.65 * smoothed) + (0.35 * tone));
                samples[n] = (float)value;
                peak = Math.Max(peak, Math.Abs(value));
            }

            // Scale so every click has the same peak loudness, whatever the noise happened to be.
            if (peak > 0.0)
            {
                float gain = (float)(PeakLevel / peak);
                for (int n = 0; n < count; n++)
                {
                    samples[n] *= gain;
                }
            }

            return samples;
        }
    }
}
