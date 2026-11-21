using System;
using System.Collections.Generic;
using System.Text;
using CourtSim.Core.Presentation;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    public sealed class RichTextTypingTests
    {
        private const string Advice = "<b>Suggested ruling: Grant bail</b>\nReasons here.\n<i>Confidence: high</i>";

        [Test]
        public void VisibleLength_IgnoresTags()
        {
            Assert.That(RichTextTyping.VisibleLength("<b>ab</b>c<i>d</i>"), Is.EqualTo(4));
            Assert.That(RichTextTyping.VisibleLength(Advice), Is.EqualTo(RichTextTyping.StripTags(Advice).Length));
        }

        [Test]
        public void VisibleLength_NullOrEmpty_IsZero()
        {
            Assert.That(RichTextTyping.VisibleLength(null), Is.EqualTo(0));
            Assert.That(RichTextTyping.VisibleLength(string.Empty), Is.EqualTo(0));
        }

        [Test]
        public void Prefix_CutInsideBold_ClosesTheTag()
        {
            Assert.That(RichTextTyping.Prefix("<b>Hello</b> world", 3), Is.EqualTo("<b>Hel</b>"));
        }

        [Test]
        public void Prefix_NestedTags_ClosesInReverseOrder()
        {
            Assert.That(RichTextTyping.Prefix("<b>a<i>bc</i>d</b>", 2), Is.EqualTo("<b>a<i>b</i></b>"));
        }

        [Test]
        public void Prefix_ValueTag_ClosesWithItsName()
        {
            Assert.That(RichTextTyping.Prefix("<color=#ff0000>red</color>", 1), Is.EqualTo("<color=#ff0000>r</color>"));
        }

        [Test]
        public void Prefix_AtTagBoundary_IncludesClosingTagButNotNextOpening()
        {
            Assert.That(RichTextTyping.Prefix("<b>ab</b><i>cd</i>", 2), Is.EqualTo("<b>ab</b>"));
        }

        [Test]
        public void Prefix_Zero_IsEmpty()
        {
            Assert.That(RichTextTyping.Prefix(Advice, 0), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Prefix_BeyondLength_ReturnsWholeText()
        {
            Assert.That(RichTextTyping.Prefix(Advice, 10000), Is.EqualTo(Advice));
            Assert.That(RichTextTyping.Prefix(Advice, RichTextTyping.VisibleLength(Advice)), Is.EqualTo(Advice));
        }

        [Test]
        public void Prefix_NoTags_IsPlainSubstring()
        {
            Assert.That(RichTextTyping.Prefix("plain text", 5), Is.EqualTo("plain"));
            Assert.That(RichTextTyping.VisibleLength("plain text"), Is.EqualTo(10));
        }

        [Test]
        public void Prefix_NullOrNegative_IsEmpty()
        {
            Assert.That(RichTextTyping.Prefix(null, 3), Is.EqualTo(string.Empty));
            Assert.That(RichTextTyping.Prefix("abc", -1), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Prefix_UnknownOrUnclosedAngleBracket_CountsAsText()
        {
            Assert.That(RichTextTyping.VisibleLength("a<x>b"), Is.EqualTo(5));
            Assert.That(RichTextTyping.VisibleLength("1 < 2"), Is.EqualTo(5));
            Assert.That(RichTextTyping.Prefix("a<x>b", 3), Is.EqualTo("a<x"));
        }

        [Test]
        public void Prefix_EveryLength_KeepsTagsBalancedAndCountsVisibleCharacters()
        {
            int length = RichTextTyping.VisibleLength(Advice);
            for (int n = 0; n <= length; n++)
            {
                string prefix = RichTextTyping.Prefix(Advice, n);
                Assert.That(RichTextTyping.VisibleLength(prefix), Is.EqualTo(n), prefix);
                Assert.That(IsBalanced(prefix), Is.True, prefix);
            }
        }

        [Test]
        public void PrefixWithHiddenRest_KeepsWholeTextAndBalance()
        {
            string partial = RichTextTyping.PrefixWithHiddenRest("<b>Hello</b> world", 3);

            Assert.That(partial, Is.EqualTo("<b>Hel</b><color=#00000000><b>lo</b> world</color>"));
            Assert.That(RichTextTyping.StripTags(partial), Is.EqualTo("Hello world"));
            Assert.That(RichTextTyping.PrefixWithHiddenRest("<b>Hi</b>", 2), Is.EqualTo("<b>Hi</b>"));
        }

        [Test]
        public void StripTags_RemovesOnlyRichTextTags()
        {
            Assert.That(RichTextTyping.StripTags("<b>Bold</b> and <i>it</i> 1 < 2"), Is.EqualTo("Bold and it 1 < 2"));
            Assert.That(RichTextTyping.StripTags(null), Is.EqualTo(string.Empty));
        }

        private static bool IsBalanced(string text)
        {
            var open = new Stack<string>();
            int i = 0;
            while (i < text.Length)
            {
                int end = text[i] == '<' ? text.IndexOf('>', i) : -1;
                if (end < 0)
                {
                    i++;
                    continue;
                }

                string tag = text.Substring(i + 1, end - i - 1);
                if (tag.StartsWith("/", StringComparison.Ordinal))
                {
                    if (open.Count == 0 || open.Pop() != tag.Substring(1))
                    {
                        return false;
                    }
                }
                else
                {
                    int equals = tag.IndexOf('=');
                    open.Push(equals >= 0 ? tag.Substring(0, equals) : tag);
                }

                i = end + 1;
            }

            return open.Count == 0;
        }
    }

    public sealed class ClickSynthTests
    {
        [Test]
        public void Generate_Defaults_Is25MillisecondsAt44100Hz()
        {
            float[] click = ClickSynth.Generate(1);

            Assert.That(click.Length, Is.EqualTo((int)Math.Round(44100 * 0.025)));
        }

        [Test]
        public void Generate_CustomRate_ScalesLength()
        {
            Assert.That(ClickSynth.Generate(1, 22050, 0.02).Length, Is.EqualTo(441));
        }

        [Test]
        public void Generate_SamplesStayInRange()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                foreach (float sample in ClickSynth.Generate(seed))
                {
                    Assert.That(sample, Is.InRange(-1f, 1f));
                }
            }
        }

        [Test]
        public void Generate_DecaysOverTime()
        {
            float[] click = ClickSynth.Generate(7);
            int quarter = click.Length / 4;

            Assert.That(Rms(click, 0, quarter), Is.GreaterThan(10 * Rms(click, click.Length - quarter, quarter)));
            Assert.That(Rms(click, 0, quarter), Is.GreaterThan(0.05));
        }

        [Test]
        public void Generate_SameSeed_IsIdentical_DifferentSeed_Differs()
        {
            Assert.That(ClickSynth.Generate(42), Is.EqualTo(ClickSynth.Generate(42)));
            Assert.That(ClickSynth.Generate(42), Is.Not.EqualTo(ClickSynth.Generate(43)));
        }

        [Test]
        public void Generate_BadArguments_Throw()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ClickSynth.Generate(1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ClickSynth.Generate(1, 44100, 0));
        }

        private static double Rms(float[] samples, int start, int count)
        {
            double sum = 0;
            for (int i = start; i < start + count; i++)
            {
                sum += samples[i] * samples[i];
            }

            return Math.Sqrt(sum / count);
        }
    }

    public sealed class WavReaderTests
    {
        [Test]
        public void Read_16BitMono_DecodesSamples()
        {
            byte[] wav = BuildWav(1, 22050, 16, Pcm16(0, 16384, -32768, 32767));

            WavData data = WavReader.Read(wav);

            Assert.That(data.SampleRate, Is.EqualTo(22050));
            Assert.That(data.Channels, Is.EqualTo(1));
            Assert.That(data.FrameCount, Is.EqualTo(4));
            Assert.That(data.Samples[0], Is.EqualTo(0f));
            Assert.That(data.Samples[1], Is.EqualTo(0.5f).Within(1e-4));
            Assert.That(data.Samples[2], Is.EqualTo(-1f));
            Assert.That(data.Samples[3], Is.EqualTo(1f).Within(1e-4));
        }

        [Test]
        public void Read_16BitStereo_KeepsInterleavedOrder()
        {
            byte[] wav = BuildWav(2, 44100, 16, Pcm16(16384, -16384, 8192, -8192));

            WavData data = WavReader.Read(wav);

            Assert.That(data.Channels, Is.EqualTo(2));
            Assert.That(data.FrameCount, Is.EqualTo(2));
            Assert.That(data.Samples[0], Is.EqualTo(0.5f).Within(1e-4));
            Assert.That(data.Samples[1], Is.EqualTo(-0.5f).Within(1e-4));
            Assert.That(data.Samples[3], Is.EqualTo(-0.25f).Within(1e-4));
        }

        [Test]
        public void Read_8Bit_CentresOn128()
        {
            byte[] wav = BuildWav(1, 8000, 8, new byte[] { 128, 0, 255, 192 });

            WavData data = WavReader.Read(wav);

            Assert.That(data.Samples[0], Is.EqualTo(0f));
            Assert.That(data.Samples[1], Is.EqualTo(-1f));
            Assert.That(data.Samples[2], Is.EqualTo(127f / 128f).Within(1e-4));
            Assert.That(data.Samples[3], Is.EqualTo(0.5f).Within(1e-4));
        }

        [Test]
        public void Read_ExtraChunkBeforeData_IsSkipped()
        {
            // An odd-sized "LIST" chunk also checks the pad byte is skipped.
            byte[] wav = BuildWav(1, 16000, 16, Pcm16(100, 200), Chunk("LIST", new byte[] { 1, 2, 3 }));

            WavData data = WavReader.Read(wav);

            Assert.That(data.FrameCount, Is.EqualTo(2));
            Assert.That(data.Samples[1], Is.EqualTo(200 / 32768f).Within(1e-6));
        }

        [Test]
        public void Read_OversizedDataLength_ReadsWhatIsPresent()
        {
            byte[] wav = BuildWav(1, 16000, 16, Pcm16(1, 2, 3));
            int dataSizeOffset = wav.Length - 6 - 4;
            wav[dataSizeOffset] = 0xFF;
            wav[dataSizeOffset + 1] = 0xFF;
            wav[dataSizeOffset + 2] = 0xFF;
            wav[dataSizeOffset + 3] = 0x7F;

            Assert.That(WavReader.Read(wav).FrameCount, Is.EqualTo(3));
        }

        [Test]
        public void Read_ExtensiblePcm_IsAccepted()
        {
            byte[] fmt = FormatBody(0xFFFE, 1, 16000, 16, 26);
            fmt[24] = 1;
            byte[] wav = Riff(Chunk("fmt ", fmt), Chunk("data", Pcm16(5)));

            Assert.That(WavReader.Read(wav).Samples.Length, Is.EqualTo(1));
        }

        [Test]
        public void Read_BadHeader_Throws()
        {
            byte[] wav = BuildWav(1, 16000, 16, Pcm16(1));
            wav[0] = (byte)'X';

            FormatException error = Assert.Throws<FormatException>(() => WavReader.Read(wav));
            Assert.That(error.Message, Does.Contain("RIFF"));
            Assert.Throws<FormatException>(() => WavReader.Read(new byte[4]));
            Assert.Throws<ArgumentNullException>(() => WavReader.Read(null));
        }

        [Test]
        public void Read_UnsupportedFormats_ThrowClearly()
        {
            byte[] floatWav = Riff(Chunk("fmt ", FormatBody(3, 1, 16000, 32, 16)), Chunk("data", new byte[4]));
            byte[] wideWav = BuildWav(1, 16000, 24, new byte[3]);
            byte[] surround = BuildWav(6, 16000, 16, new byte[12]);

            Assert.That(Assert.Throws<FormatException>(() => WavReader.Read(floatWav)).Message, Does.Contain("PCM"));
            Assert.That(Assert.Throws<FormatException>(() => WavReader.Read(wideWav)).Message, Does.Contain("bit depth"));
            Assert.That(Assert.Throws<FormatException>(() => WavReader.Read(surround)).Message, Does.Contain("channel"));
        }

        [Test]
        public void Read_MissingChunks_Throw()
        {
            byte[] noData = Riff(Chunk("fmt ", FormatBody(1, 1, 16000, 16, 16)));
            byte[] dataFirst = Riff(Chunk("data", Pcm16(1)), Chunk("fmt ", FormatBody(1, 1, 16000, 16, 16)));
            byte[] shortFmt = Riff(Chunk("fmt ", new byte[8]));
            byte[] noFmt = Riff(Chunk("LIST", new byte[2]));

            Assert.That(Assert.Throws<FormatException>(() => WavReader.Read(noData)).Message, Does.Contain("no 'data'"));
            Assert.That(Assert.Throws<FormatException>(() => WavReader.Read(dataFirst)).Message, Does.Contain("before"));
            Assert.That(Assert.Throws<FormatException>(() => WavReader.Read(shortFmt)).Message, Does.Contain("too short"));
            Assert.That(Assert.Throws<FormatException>(() => WavReader.Read(noFmt)).Message, Does.Contain("no 'fmt '"));
        }

        private static byte[] BuildWav(int channels, int sampleRate, int bits, byte[] data, byte[] extraChunk = null)
        {
            var chunks = new List<byte[]> { Chunk("fmt ", FormatBody(1, channels, sampleRate, bits, 16)) };
            if (extraChunk != null)
            {
                chunks.Add(extraChunk);
            }

            chunks.Add(Chunk("data", data));
            return Riff(chunks.ToArray());
        }

        private static byte[] FormatBody(int format, int channels, int sampleRate, int bits, int size)
        {
            var body = new byte[size];
            WriteUInt16(body, 0, format);
            WriteUInt16(body, 2, channels);
            WriteUInt32(body, 4, sampleRate);
            WriteUInt32(body, 8, sampleRate * channels * bits / 8);
            WriteUInt16(body, 12, channels * bits / 8);
            WriteUInt16(body, 14, bits);
            return body;
        }

        private static byte[] Chunk(string id, byte[] body)
        {
            var chunk = new byte[8 + body.Length + (body.Length % 2)];
            Encoding.ASCII.GetBytes(id).CopyTo(chunk, 0);
            WriteUInt32(chunk, 4, body.Length);
            body.CopyTo(chunk, 8);
            return chunk;
        }

        private static byte[] Riff(params byte[][] chunks)
        {
            var bytes = new List<byte>();
            bytes.AddRange(Encoding.ASCII.GetBytes("RIFF"));
            bytes.AddRange(new byte[4]);
            bytes.AddRange(Encoding.ASCII.GetBytes("WAVE"));
            foreach (byte[] chunk in chunks)
            {
                bytes.AddRange(chunk);
            }

            byte[] result = bytes.ToArray();
            WriteUInt32(result, 4, result.Length - 8);
            return result;
        }

        private static byte[] Pcm16(params short[] samples)
        {
            var bytes = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                WriteUInt16(bytes, i * 2, (ushort)samples[i]);
            }

            return bytes;
        }

        private static void WriteUInt16(byte[] bytes, int offset, int value)
        {
            bytes[offset] = (byte)(value & 0xFF);
            bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        private static void WriteUInt32(byte[] bytes, int offset, int value)
        {
            WriteUInt16(bytes, offset, value & 0xFFFF);
            WriteUInt16(bytes, offset + 2, (value >> 16) & 0xFFFF);
        }
    }
}
