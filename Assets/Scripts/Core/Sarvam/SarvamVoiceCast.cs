using System;
using System.Text;

namespace CourtSim.Core.Sarvam
{
    /// <summary>Who is speaking. Each role gets its own Sarvam voice.</summary>
    public enum SarvamVoiceRole
    {
        /// <summary>The AI assistant in the judge's ear.</summary>
        Assistant,

        /// <summary>The Public Prosecutor.</summary>
        Prosecutor,

        /// <summary>The defence advocate.</summary>
        Defence,

        /// <summary>The bench clerk who calls the matter.</summary>
        Clerk,
    }

    /// <summary>One Sarvam speaker: the bulbul voice name and whether it is a female voice.</summary>
    /// <remarks>The gender matters twice: the translation uses matching verb forms, and the offline fallback
    /// voice picks a Windows voice of the same gender.</remarks>
    public sealed class SarvamVoice
    {
        public SarvamVoice(string speaker, bool isFemale)
        {
            Speaker = speaker ?? throw new ArgumentNullException(nameof(speaker));
            IsFemale = isFemale;
        }

        /// <summary>The bulbul:v3 speaker name, e.g. "roopa".</summary>
        public string Speaker { get; }

        public bool IsFemale { get; }

        /// <summary>"Female" or "Male", as Sarvam's translate endpoint expects.</summary>
        public string Gender => IsFemale ? "Female" : "Male";
    }

    /// <summary>
    /// Chooses the voices for one case. The assistant is always "roopa", so the trainee always recognises it.
    /// The prosecutor, defence and clerk change from case to case, so each case sounds like a different courtroom.
    /// </summary>
    /// <remarks>
    /// <para>The choice must be the same every time the same case is played (otherwise the voice cache would
    /// miss and a replay would sound different). So it is worked out from the case id with the FNV-1a hash,
    /// which always gives the same number for the same text. <c>string.GetHashCode</c> is not used because .NET
    /// may give a different value on every run.</para>
    /// <para>The hash is treated like a number written in "mixed digits": the remainder after dividing by 10
    /// picks the prosecutor, the next digit picks the clerk, and so on. Already-used speakers are skipped, so
    /// the prosecutor, clerk and a male defence advocate are always three different people.</para>
    /// </remarks>
    public sealed class SarvamVoiceCast
    {
        /// <summary>The assistant's fixed voice. Reserved: no courtroom character uses it.</summary>
        public const string AssistantSpeaker = "roopa";

        /// <summary>FNV-1a starting value and multiplier, fixed by the FNV specification.</summary>
        private const uint FnvOffsetBasis = 2166136261;
        private const uint FnvPrime = 16777619;

        /// <summary>Male bulbul:v3 speakers for the courtroom characters.</summary>
        public static readonly string[] MaleSpeakers =
        {
            "anand", "vijay", "gokul", "rahul", "shubh", "aditya", "kabir", "rohan", "varun", "mohit",
        };

        /// <summary>Female bulbul:v3 speakers for the courtroom characters ("roopa" is left out on purpose).</summary>
        public static readonly string[] FemaleSpeakers =
        {
            "kavitha", "priya", "kavya", "ritu", "shruti", "neha", "pooja", "ishita", "rupali",
        };

        /// <summary>The voices used when no case is known (e.g. the menu): the original fixed cast.</summary>
        public static readonly SarvamVoiceCast Default = new SarvamVoiceCast(
            new SarvamVoice("anand", false),
            new SarvamVoice("kavitha", true),
            new SarvamVoice("gokul", false));

        private SarvamVoiceCast(SarvamVoice prosecutor, SarvamVoice defence, SarvamVoice clerk)
        {
            Assistant = new SarvamVoice(AssistantSpeaker, true);
            Prosecutor = prosecutor;
            Defence = defence;
            Clerk = clerk;
        }

        public SarvamVoice Assistant { get; }

        public SarvamVoice Prosecutor { get; }

        public SarvamVoice Defence { get; }

        public SarvamVoice Clerk { get; }

        /// <summary>The cast for a case. The same id always gives the same cast; null or blank gives <see cref="Default"/>.</summary>
        public static SarvamVoiceCast For(string caseId)
        {
            if (string.IsNullOrWhiteSpace(caseId))
            {
                return Default;
            }

            uint hash = Fnv1a(caseId);

            // Prosecutor: any male speaker.
            int prosecutor = (int)(hash % (uint)MaleSpeakers.Length);
            hash /= (uint)MaleSpeakers.Length;

            // Clerk: any male speaker except the prosecutor.
            int clerk = PickSkipping(ref hash, MaleSpeakers.Length, prosecutor, -1);

            // Defence: a coin toss for gender, then a speaker nobody else in this case uses.
            bool defenceFemale = hash % 2 == 0;
            hash /= 2;
            SarvamVoice defence = defenceFemale
                ? new SarvamVoice(FemaleSpeakers[(int)(hash % (uint)FemaleSpeakers.Length)], true)
                : new SarvamVoice(MaleSpeakers[PickSkipping(ref hash, MaleSpeakers.Length, prosecutor, clerk)], false);

            return new SarvamVoiceCast(
                new SarvamVoice(MaleSpeakers[prosecutor], false),
                defence,
                new SarvamVoice(MaleSpeakers[clerk], false));
        }

        /// <summary>
        /// 32-bit FNV-1a hash of the text's UTF-8 bytes: start from a fixed "offset basis", then for every byte
        /// XOR it in and multiply by the FNV prime. Simple, fast and identical on every machine and run.
        /// </summary>
        public static uint Fnv1a(string text)
        {
            uint hash = FnvOffsetBasis;
            foreach (byte b in Encoding.UTF8.GetBytes(text ?? string.Empty))
            {
                unchecked
                {
                    hash ^= b;
                    hash *= FnvPrime;
                }
            }

            return hash;
        }

        /// <summary>The voice for <paramref name="role"/>.</summary>
        public SarvamVoice Get(SarvamVoiceRole role)
        {
            switch (role)
            {
                case SarvamVoiceRole.Prosecutor:
                    return Prosecutor;
                case SarvamVoiceRole.Defence:
                    return Defence;
                case SarvamVoiceRole.Clerk:
                    return Clerk;
                default:
                    return Assistant;
            }
        }

        /// <summary>
        /// Picks an index in [0, count) that is not <paramref name="skipA"/> or <paramref name="skipB"/> (-1 = none),
        /// using the next "digit" of the hash. Counting only the free slots keeps every free speaker equally likely.
        /// </summary>
        private static int PickSkipping(ref uint hash, int count, int skipA, int skipB)
        {
            int skipped = (skipA >= 0 ? 1 : 0) + (skipB >= 0 && skipB != skipA ? 1 : 0);
            int free = count - skipped;
            int slot = (int)(hash % (uint)free);
            hash /= (uint)free;
            for (int index = 0; index < count; index++)
            {
                if (index == skipA || index == skipB)
                {
                    continue;
                }

                if (slot == 0)
                {
                    return index;
                }

                slot--;
            }

            // Unreachable while count > skipped; kept so the compiler sees every path return.
            return 0;
        }
    }
}
