using System;
using System.Collections.Generic;

namespace CourtSim.Core.Presentation
{
    /// <summary>
    /// Writes 16-bit PCM .wav files, the reverse of <see cref="WavReader"/>. Used to join several pieces of speech
    /// (a long line is synthesised in parts) into one file for the voice cache.
    /// </summary>
    public static class WavWriter
    {
        private const int HeaderBytes = 44;

        /// <summary>The audio as a standard 44-byte-header RIFF/WAVE file with 16-bit samples.</summary>
        public static byte[] Write(WavData wav)
        {
            if (wav == null)
            {
                throw new ArgumentNullException(nameof(wav));
            }

            int dataBytes = wav.Samples.Length * 2;
            var bytes = new byte[HeaderBytes + dataBytes];
            WriteId(bytes, 0, "RIFF");
            WriteUInt32(bytes, 4, (uint)(HeaderBytes - 8 + dataBytes));
            WriteId(bytes, 8, "WAVE");
            WriteId(bytes, 12, "fmt ");
            WriteUInt32(bytes, 16, 16);
            WriteUInt16(bytes, 20, 1); // 1 = uncompressed PCM
            WriteUInt16(bytes, 22, wav.Channels);
            WriteUInt32(bytes, 24, (uint)wav.SampleRate);
            WriteUInt32(bytes, 28, (uint)(wav.SampleRate * wav.Channels * 2)); // bytes per second
            WriteUInt16(bytes, 32, wav.Channels * 2); // bytes per frame
            WriteUInt16(bytes, 34, 16);
            WriteId(bytes, 36, "data");
            WriteUInt32(bytes, 40, (uint)dataBytes);

            int offset = HeaderBytes;
            foreach (float sample in wav.Samples)
            {
                // Clamp first: a value just over 1 would otherwise wrap round to a loud click.
                float clamped = sample > 1f ? 1f : (sample < -1f ? -1f : sample);
                short value = (short)Math.Round(clamped * 32767f);
                bytes[offset] = (byte)(value & 0xFF);
                bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
                offset += 2;
            }

            return bytes;
        }

        /// <summary>
        /// Joins WAV files end to end into one. A single file is returned unchanged.
        /// </summary>
        /// <exception cref="ArgumentException">The list is empty.</exception>
        /// <exception cref="FormatException">A file is not a WAV, or the files have different sample rates or channel counts.</exception>
        public static byte[] Concatenate(IReadOnlyList<byte[]> wavFiles)
        {
            if (wavFiles == null || wavFiles.Count == 0)
            {
                throw new ArgumentException("At least one WAV file is needed.", nameof(wavFiles));
            }

            if (wavFiles.Count == 1)
            {
                return wavFiles[0];
            }

            var parts = new List<WavData>();
            int total = 0;
            foreach (byte[] file in wavFiles)
            {
                WavData part = WavReader.Read(file);
                if (parts.Count > 0 && (part.SampleRate != parts[0].SampleRate || part.Channels != parts[0].Channels))
                {
                    throw new FormatException("The WAV files to join have different sample rates or channel counts.");
                }

                parts.Add(part);
                total += part.Samples.Length;
            }

            var samples = new float[total];
            int position = 0;
            foreach (WavData part in parts)
            {
                Array.Copy(part.Samples, 0, samples, position, part.Samples.Length);
                position += part.Samples.Length;
            }

            return Write(new WavData(parts[0].SampleRate, parts[0].Channels, samples));
        }

        private static void WriteId(byte[] bytes, int offset, string id)
        {
            for (int i = 0; i < 4; i++)
            {
                bytes[offset + i] = (byte)id[i];
            }
        }

        private static void WriteUInt16(byte[] bytes, int offset, int value)
        {
            bytes[offset] = (byte)(value & 0xFF);
            bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        private static void WriteUInt32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)(value & 0xFF);
            bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
            bytes[offset + 2] = (byte)((value >> 16) & 0xFF);
            bytes[offset + 3] = (byte)((value >> 24) & 0xFF);
        }
    }
}
