using System;

namespace CourtSim.Core.Presentation
{
    /// <summary>
    /// Makes every <see cref="SoundCue"/> from numbers alone (like <see cref="ClickSynth"/>), so the project ships no
    /// audio files. Each cue is a short recipe of three ingredients:
    /// <list type="bullet">
    /// <item><b>Sine tones</b> for pitched parts (the gavel's ring, the chime's notes, the drone's hum).</item>
    /// <item><b>Noise</b> (random numbers) for unpitched parts (the knock's crack, the rush of air).</item>
    /// <item><b>Envelopes</b> that shape loudness over time, mostly exponential decays: loud at once, then fading.</item>
    /// </list>
    /// Filters smooth the noise: a low-pass keeps the deep part (a rumble), a band-pass keeps a moving band (a whoosh).
    /// </summary>
    /// <remarks>
    /// The output is 44.1 kHz mono floats in [-1, 1]. Noise comes from a fixed seed per cue, so the same cue is
    /// always the same sound (the tests rely on this). Every cue is scaled to its own peak level, below 1, so none
    /// can clip, and the one-shots get a tiny fade at each end so they never start or stop with a "pop".
    /// </remarks>
    public static class SfxSynth
    {
        public const int DefaultSampleRate = 44100;

        /// <summary>Fade at the start of one-shot cues; short enough to keep the attack sharp.</summary>
        private const double FadeInSeconds = 0.0015;

        /// <summary>Fade at the end of one-shot cues, so the last sample is silent.</summary>
        private const double FadeOutSeconds = 0.012;

        private const double TwoPi = 2.0 * Math.PI;

        /// <summary>The length of <paramref name="cue"/> in seconds.</summary>
        public static double DurationSeconds(SoundCue cue)
        {
            switch (cue)
            {
                case SoundCue.Gavel:
                    return 0.6;
                case SoundCue.Impact:
                    return 3.0;
                case SoundCue.Whoosh:
                    return 1.1;
                case SoundCue.Riser:
                    return 2.2;
                case SoundCue.Shimmer:
                    return 2.6;
                case SoundCue.UiHover:
                    return 0.04;
                case SoundCue.UiClick:
                    return 0.07;
                case SoundCue.Drone:
                    return 8.0;
                default:
                    throw new ArgumentOutOfRangeException(nameof(cue), cue, null);
            }
        }

        /// <summary>True for cues meant to play on repeat (only the drone).</summary>
        public static bool IsLoop(SoundCue cue)
        {
            return cue == SoundCue.Drone;
        }

        /// <summary>Mono samples in [-1, 1] for <paramref name="cue"/>. The same cue always gives the same samples.</summary>
        public static float[] Generate(SoundCue cue, int sampleRate = DefaultSampleRate)
        {
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive.");
            }

            int count = Math.Max(1, (int)Math.Round(sampleRate * DurationSeconds(cue)));
            var buffer = new double[count];
            var random = new Random(9001 + ((int)cue * 101));
            double peak;
            switch (cue)
            {
                case SoundCue.Gavel:
                    // Two knocks: the second a little later and softer, like a real double tap.
                    AddKnock(buffer, sampleRate, 0.0, 1.0, random);
                    AddKnock(buffer, sampleRate, 0.17, 0.8, random);
                    peak = 0.9;
                    break;
                case SoundCue.Impact:
                    FillImpact(buffer, sampleRate, random);
                    peak = 0.95;
                    break;
                case SoundCue.Whoosh:
                    FillWhoosh(buffer, sampleRate, random);
                    peak = 0.75;
                    break;
                case SoundCue.Riser:
                    FillRiser(buffer, sampleRate, random);
                    peak = 0.7;
                    break;
                case SoundCue.Shimmer:
                    FillShimmer(buffer, sampleRate, random);
                    peak = 0.6;
                    break;
                case SoundCue.UiHover:
                    FillHover(buffer, sampleRate);
                    peak = 0.3;
                    break;
                case SoundCue.UiClick:
                    FillClick(buffer, sampleRate, random);
                    peak = 0.5;
                    break;
                case SoundCue.Drone:
                    FillDrone(buffer, sampleRate);
                    peak = 0.7;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(cue), cue, null);
            }

            return Finish(buffer, sampleRate, peak, !IsLoop(cue));
        }

        // ------------------------------------------------------------------ recipes

        /// <summary>
        /// One wooden knock: a short crack of noise, a low "body" tone whose pitch drops quickly (the thud), and two
        /// higher rings that die away fast (the hard wood). Added on top of whatever is already in the buffer.
        /// </summary>
        private static void AddKnock(double[] buffer, int sampleRate, double startSeconds, double gain, Random random)
        {
            int start = (int)(startSeconds * sampleRate);
            int length = Math.Min(buffer.Length - start, (int)(0.3 * sampleRate));
            double phase = 0.0;
            double noiseLow = 0.0;
            for (int n = 0; n < length; n++)
            {
                double t = (double)n / sampleRate;
                double bodyHz = 180.0 + (160.0 * Math.Exp(-t / 0.01));
                phase += TwoPi * bodyHz / sampleRate;
                double body = Math.Sin(phase) * Math.Exp(-t / 0.045);
                double ring = (0.5 * Math.Sin(TwoPi * 640.0 * t) * Math.Exp(-t / 0.03))
                    + (0.3 * Math.Sin(TwoPi * 1520.0 * t) * Math.Exp(-t / 0.012));
                noiseLow += 0.5 * (Noise(random) - noiseLow);
                double crack = 0.9 * noiseLow * Math.Exp(-t / 0.004);
                buffer[start + n] += gain * (body + ring + crack);
            }
        }

        /// <summary>
        /// A deep boom: a sub-bass tone that falls from about 100 Hz to 38 Hz, a dull thump of low-passed noise at the
        /// start and a long low rumble underneath, gently saturated so it feels heavy rather than sharp.
        /// </summary>
        private static void FillImpact(double[] buffer, int sampleRate, Random random)
        {
            double phase = 0.0;
            double thump = 0.0;
            double rumble = 0.0;
            for (int n = 0; n < buffer.Length; n++)
            {
                double t = (double)n / sampleRate;
                double subHz = 38.0 + (62.0 * Math.Exp(-t / 0.18));
                phase += TwoPi * subHz / sampleRate;
                double sub = Math.Sin(phase) * Math.Exp(-t / 1.1);

                double noise = Noise(random);
                thump += 0.05 * (noise - thump);
                rumble += 0.008 * (noise - rumble);
                double crack = 0.3 * noise * Math.Exp(-t / 0.015);

                double value = sub
                    + (3.0 * thump * Math.Exp(-t / 0.12))
                    + (4.0 * rumble * Math.Exp(-t / 0.9))
                    + crack;
                buffer[n] = Math.Tanh(1.6 * value);
            }
        }

        /// <summary>
        /// A rush of air: noise through a band-pass filter whose centre sweeps up to about 3.5 kHz and back down,
        /// while the loudness swells and fades. The rising-then-falling brightness is what the ear hears as movement.
        /// </summary>
        private static void FillWhoosh(double[] buffer, int sampleRate, Random random)
        {
            var filter = new BandPass();
            for (int n = 0; n < buffer.Length; n++)
            {
                double u = (double)n / buffer.Length;
                double shape = Math.Sin(Math.PI * Math.Pow(u, 0.8));
                double centreHz = 300.0 + (3200.0 * Math.Pow(shape, 1.5));
                double air = filter.Process(Noise(random), centreHz, sampleRate, 0.8);
                buffer[n] = air * shape * shape;
            }
        }

        /// <summary>
        /// Tension that builds: two slightly detuned buzzy tones glide up about two and a half octaves, with hiss
        /// and a tremolo that both speed up, getting louder all the way to the end.
        /// </summary>
        private static void FillRiser(double[] buffer, int sampleRate, Random random)
        {
            double phaseA = 0.0;
            double phaseB = 0.0;
            double tremolo = 0.0;
            double noiseLow = 0.0;
            for (int n = 0; n < buffer.Length; n++)
            {
                double u = (double)n / buffer.Length;
                double hz = 90.0 * Math.Pow(2.0, 2.5 * u);
                phaseA += TwoPi * hz / sampleRate;
                phaseB += TwoPi * hz * 1.006 / sampleRate;
                tremolo += TwoPi * (4.0 + (14.0 * u)) / sampleRate;

                // A few harmonics at falling strength make a soft saw-like buzz instead of a pure whistle.
                double tone = 0.0;
                for (int k = 1; k <= 5; k++)
                {
                    tone += (Math.Sin(k * phaseA) + Math.Sin(k * phaseB)) / k;
                }

                double noise = Noise(random);
                noiseLow += 0.3 * (noise - noiseLow);
                double hiss = (noise - noiseLow) * u * u; // noise minus its low part = only the high hiss

                double loudness = Math.Pow(u, 2.2) * (0.8 + (0.2 * Math.Sin(tremolo)));
                buffer[n] = loudness * ((0.3 * tone) + (0.6 * hiss));
            }
        }

        /// <summary>
        /// A bright chime: five bell notes of a major chord, struck one after another, each with a quieter, slightly
        /// out-of-tune overtone (which is what makes a bell sound metallic) and a little air on top.
        /// </summary>
        private static void FillShimmer(double[] buffer, int sampleRate, Random random)
        {
            double[] notesHz = { 1046.5, 1318.5, 1568.0, 2093.0, 2637.0 };
            double noiseLow = 0.0;
            for (int n = 0; n < buffer.Length; n++)
            {
                double t = (double)n / sampleRate;
                double value = 0.0;
                for (int i = 0; i < notesHz.Length; i++)
                {
                    double local = t - (0.07 * i);
                    if (local < 0.0)
                    {
                        continue;
                    }

                    double strike = (1.0 - Math.Exp(-local / 0.004)) * Math.Exp(-local / 0.9);
                    double overtone = 0.3 * Math.Sin(TwoPi * notesHz[i] * 2.756 * local) * Math.Exp(-local / 0.25);
                    value += strike * (Math.Sin(TwoPi * notesHz[i] * local) + overtone) / (1.0 + (0.2 * i));
                }

                double noise = Noise(random);
                noiseLow += 0.4 * (noise - noiseLow);
                value += 0.08 * (noise - noiseLow) * Math.Exp(-t / 0.6);
                buffer[n] = value;
            }
        }

        /// <summary>A barely-there tick: a high tone that is gone in a few milliseconds.</summary>
        private static void FillHover(double[] buffer, int sampleRate)
        {
            for (int n = 0; n < buffer.Length; n++)
            {
                double t = (double)n / sampleRate;
                buffer[n] = Math.Sin(TwoPi * 2600.0 * t) * Math.Exp(-t / 0.007);
            }
        }

        /// <summary>A soft click: a short tone dropping in pitch, plus a tiny tap of noise at the very start.</summary>
        private static void FillClick(double[] buffer, int sampleRate, Random random)
        {
            double phase = 0.0;
            double noiseLow = 0.0;
            for (int n = 0; n < buffer.Length; n++)
            {
                double t = (double)n / sampleRate;
                double hz = 900.0 + (700.0 * Math.Exp(-t / 0.01));
                phase += TwoPi * hz / sampleRate;
                noiseLow += 0.45 * (Noise(random) - noiseLow);
                buffer[n] = (Math.Sin(phase) * Math.Exp(-t / 0.018)) + (0.5 * noiseLow * Math.Exp(-t / 0.003));
            }
        }

        /// <summary>
        /// A low ambient pad. To loop without a click, every frequency fits a whole number of cycles into the
        /// eight seconds (all are multiples of 1/8 Hz), so the end joins the start exactly. Pairs of tones a
        /// fraction of a hertz apart "beat" slowly against each other, which makes the pad feel alive.
        /// </summary>
        private static void FillDrone(double[] buffer, int sampleRate)
        {
            double[] hz = { 55.0, 55.125, 82.5, 110.0, 110.25, 165.0, 220.125 };
            double[] level = { 1.0, 0.8, 0.5, 0.35, 0.25, 0.15, 0.08 };
            for (int n = 0; n < buffer.Length; n++)
            {
                double t = (double)n / sampleRate;
                double value = 0.0;
                for (int i = 0; i < hz.Length; i++)
                {
                    value += level[i] * Math.Sin(TwoPi * hz[i] * t);
                }

                // A slow swell, two per loop, so the loop point is not audible either.
                buffer[n] = value * (0.85 + (0.15 * Math.Sin(TwoPi * 0.25 * t)));
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>A random number in [-1, 1).</summary>
        private static double Noise(Random random)
        {
            return (random.NextDouble() * 2.0) - 1.0;
        }

        /// <summary>Scales the sound to <paramref name="peakLevel"/>, adds the anti-pop fades and converts to floats.</summary>
        private static float[] Finish(double[] buffer, int sampleRate, double peakLevel, bool fadeEnds)
        {
            if (fadeEnds)
            {
                int fadeIn = Math.Min(buffer.Length, Math.Max(1, (int)(FadeInSeconds * sampleRate)));
                int fadeOut = Math.Min(buffer.Length, Math.Max(1, (int)(FadeOutSeconds * sampleRate)));
                for (int n = 0; n < fadeIn; n++)
                {
                    buffer[n] *= (double)n / fadeIn;
                }

                for (int n = 0; n < fadeOut; n++)
                {
                    buffer[buffer.Length - 1 - n] *= (double)n / fadeOut;
                }
            }

            double peak = 0.0;
            foreach (double value in buffer)
            {
                peak = Math.Max(peak, Math.Abs(value));
            }

            double gain = peak > 0.0 ? peakLevel / peak : 0.0;
            var samples = new float[buffer.Length];
            for (int n = 0; n < buffer.Length; n++)
            {
                samples[n] = (float)(buffer[n] * gain);
            }

            return samples;
        }

        /// <summary>
        /// A "state-variable" band-pass filter: it keeps the frequencies near a centre that may change every sample
        /// and removes the rest. Two running values (low and band) are all the memory it needs.
        /// </summary>
        private sealed class BandPass
        {
            private double _low;
            private double _band;

            /// <param name="damping">Lower is a narrower, more whistly band; around 0.8 sounds like air.</param>
            public double Process(double input, double centreHz, int sampleRate, double damping)
            {
                // Kept below a sixth of the sample rate, where this simple filter stays stable.
                double f = 2.0 * Math.Sin(Math.PI * Math.Min(centreHz, sampleRate / 6.0) / sampleRate);
                _low += f * _band;
                double high = input - _low - (damping * _band);
                _band += f * high;
                return _band;
            }
        }
    }
}
