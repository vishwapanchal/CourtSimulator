using System;

namespace CourtSim.Core.Presentation
{
    /// <summary>Decoded audio: interleaved samples in [-1, 1] (left, right, left, right… for stereo).</summary>
    public sealed class WavData
    {
        public WavData(int sampleRate, int channels, float[] samples)
        {
            SampleRate = sampleRate;
            Channels = channels;
            Samples = samples ?? throw new ArgumentNullException(nameof(samples));
        }

        public int SampleRate { get; }

        public int Channels { get; }

        public float[] Samples { get; }

        /// <summary>Samples per channel, which is what Unity's AudioClip calls its length.</summary>
        public int FrameCount => Channels > 0 ? Samples.Length / Channels : 0;
    }

    /// <summary>
    /// Reads a .wav file (RIFF/WAVE, uncompressed PCM, 8- or 16-bit, mono or stereo) such as the one the
    /// Windows speech synthesiser writes. A WAV file is a list of chunks: "fmt " says how the sound is stored,
    /// "data" holds the samples, and any other chunk (e.g. "LIST" metadata) is skipped.
    /// </summary>
    public static class WavReader
    {
        private const int FormatPcm = 1;
        private const int FormatExtensible = 0xFFFE;

        /// <exception cref="ArgumentNullException"><paramref name="bytes"/> is null.</exception>
        /// <exception cref="FormatException">The bytes are not a WAV file, or use a format this reader doesn't support.</exception>
        public static WavData Read(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            if (bytes.Length < 12 || !HasId(bytes, 0, "RIFF") || !HasId(bytes, 8, "WAVE"))
            {
                throw new FormatException("Not a WAV file: the RIFF/WAVE header is missing.");
            }

            int channels = 0;
            int sampleRate = 0;
            int bitsPerSample = 0;
            bool haveFormat = false;
            int position = 12;
            while (position + 8 <= bytes.Length)
            {
                long declared = ReadUInt32(bytes, position + 4);
                int bodyStart = position + 8;
                int available = bytes.Length - bodyStart;

                // Some writers leave the size unset (or too large) for a still-open stream; read what is there.
                int size = declared > available ? available : (int)declared;

                if (HasId(bytes, position, "fmt "))
                {
                    if (size < 16)
                    {
                        throw new FormatException("WAV 'fmt ' chunk is too short.");
                    }

                    int format = ReadUInt16(bytes, bodyStart);
                    channels = ReadUInt16(bytes, bodyStart + 2);
                    sampleRate = (int)ReadUInt32(bytes, bodyStart + 4);
                    bitsPerSample = ReadUInt16(bytes, bodyStart + 14);
                    if (format == FormatExtensible && size >= 26)
                    {
                        // The real format code is the first two bytes of the sub-format GUID.
                        format = ReadUInt16(bytes, bodyStart + 24);
                    }

                    if (format != FormatPcm)
                    {
                        throw new FormatException($"Unsupported WAV format code {format}: only uncompressed PCM is supported.");
                    }

                    if (channels < 1 || channels > 2)
                    {
                        throw new FormatException($"Unsupported WAV channel count {channels}: only mono and stereo are supported.");
                    }

                    if (bitsPerSample != 8 && bitsPerSample != 16)
                    {
                        throw new FormatException($"Unsupported WAV bit depth {bitsPerSample}: only 8- and 16-bit are supported.");
                    }

                    if (sampleRate <= 0)
                    {
                        throw new FormatException("WAV sample rate must be positive.");
                    }

                    haveFormat = true;
                }
                else if (HasId(bytes, position, "data"))
                {
                    if (!haveFormat)
                    {
                        throw new FormatException("WAV 'data' chunk comes before the 'fmt ' chunk.");
                    }

                    return new WavData(sampleRate, channels, Decode(bytes, bodyStart, size, bitsPerSample, channels));
                }

                // Chunks are padded to an even number of bytes.
                position = bodyStart + size + (size % 2);
            }

            throw new FormatException(haveFormat ? "WAV file has no 'data' chunk." : "WAV file has no 'fmt ' chunk.");
        }

        private static float[] Decode(byte[] bytes, int start, int size, int bitsPerSample, int channels)
        {
            int bytesPerSample = bitsPerSample / 8;

            // Only whole frames (one sample for every channel) are kept.
            int frameBytes = bytesPerSample * channels;
            int count = (size / frameBytes) * channels;
            var samples = new float[count];
            for (int n = 0; n < count; n++)
            {
                int offset = start + (n * bytesPerSample);
                if (bitsPerSample == 8)
                {
                    // 8-bit WAV is unsigned: 128 is silence.
                    samples[n] = (bytes[offset] - 128) / 128f;
                }
                else
                {
                    short value = (short)(bytes[offset] | (bytes[offset + 1] << 8));
                    samples[n] = value / 32768f;
                }
            }

            return samples;
        }

        private static bool HasId(byte[] bytes, int offset, string id)
        {
            if (offset + 4 > bytes.Length)
            {
                return false;
            }

            for (int i = 0; i < 4; i++)
            {
                if (bytes[offset + i] != id[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static int ReadUInt16(byte[] bytes, int offset)
        {
            return bytes[offset] | (bytes[offset + 1] << 8);
        }

        private static long ReadUInt32(byte[] bytes, int offset)
        {
            return (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));
        }
    }
}
