using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CourtSim.Core.Advising;
using CourtSim.Core.Cases;
using CourtSim.Core.Decisions;
using CourtSim.Core.Json;

namespace CourtSim.Core.Logging
{
    /// <summary>The trainee's explicit reaction to advice, via the assistant card's Accept/Challenge buttons.</summary>
    public enum AdviceStance
    {
        None,
        Accepted,
        Challenged,
    }

    /// <summary>How a ruling relates to the advice shown. The core signal for adaptive difficulty.</summary>
    public enum AdviceOutcome
    {
        /// <summary>No advice was available when the trainee ruled.</summary>
        NoAdvice,

        /// <summary>Sound advice, followed.</summary>
        TrustedSound,

        /// <summary>Sound advice, not followed.</summary>
        OverrodeSound,

        /// <summary>Flawed advice, followed: the trainee was misled.</summary>
        MisledByFlaw,

        /// <summary>Flawed advice, not followed: the trainee caught it.</summary>
        CaughtFlaw,
    }

    public static class LogNames
    {
        public const string Schema = "courtsim.log/1";

        public static string Quality(OptionQuality quality)
        {
            switch (quality)
            {
                case OptionQuality.Sound:
                    return "sound";
                case OptionQuality.Defensible:
                    return "defensible";
                default:
                    return "unsound";
            }
        }

        public static string Stance(AdviceStance stance)
        {
            switch (stance)
            {
                case AdviceStance.Accepted:
                    return "accepted";
                case AdviceStance.Challenged:
                    return "challenged";
                default:
                    return "none";
            }
        }

        public static string Outcome(AdviceOutcome outcome)
        {
            switch (outcome)
            {
                case AdviceOutcome.TrustedSound:
                    return "trusted_sound";
                case AdviceOutcome.OverrodeSound:
                    return "overrode_sound";
                case AdviceOutcome.MisledByFlaw:
                    return "misled_by_flaw";
                case AdviceOutcome.CaughtFlaw:
                    return "caught_flaw";
                default:
                    return "no_advice";
            }
        }

        /// <summary>Reverse of <see cref="Outcome"/>, for reading saved trainee state.</summary>
        public static bool TryParseOutcome(string name, out AdviceOutcome outcome)
        {
            foreach (AdviceOutcome candidate in new[] { AdviceOutcome.NoAdvice, AdviceOutcome.TrustedSound, AdviceOutcome.OverrodeSound, AdviceOutcome.MisledByFlaw, AdviceOutcome.CaughtFlaw })
            {
                if (Outcome(candidate) == name)
                {
                    outcome = candidate;
                    return true;
                }
            }

            outcome = AdviceOutcome.NoAdvice;
            return false;
        }

        public static string Source(AdviceSource source)
        {
            switch (source)
            {
                case AdviceSource.Claude:
                    return "claude";
                case AdviceSource.LocalModel:
                    return "local";
                default:
                    return "scripted";
            }
        }

        public static string Timestamp(DateTime utc)
        {
            return utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>One ruling, with the advice that preceded it and how the trainee treated it.</summary>
    public sealed class DecisionLogEntry
    {
        public DecisionLogEntry(
            string sessionId,
            string caseId,
            DecisionRecord record,
            OptionQuality quality,
            Advice advice,
            AdviceStance stance,
            double secondsToDecide,
            DateTime timestampUtc,
            string adviceError = null)
        {
            SessionId = sessionId;
            CaseId = caseId;
            Record = record;
            Quality = quality;
            Advice = advice;
            Stance = stance;
            SecondsToDecide = Math.Max(0, secondsToDecide);
            TimestampUtc = timestampUtc;
            AdviceError = adviceError;
        }

        public string SessionId { get; }

        public string CaseId { get; }

        public DecisionRecord Record { get; }

        public OptionQuality Quality { get; }

        /// <summary>The advice available when the trainee ruled, or null.</summary>
        public Advice Advice { get; }

        public AdviceStance Stance { get; }

        public double SecondsToDecide { get; }

        public DateTime TimestampUtc { get; }

        /// <summary>Why advice was missing or came from the fallback, if it did.</summary>
        public string AdviceError { get; }

        public bool FollowedAdvice => Advice != null && Advice.RecommendedOptionId == Record.OptionId;

        public AdviceOutcome Outcome
        {
            get
            {
                if (Advice == null)
                {
                    return AdviceOutcome.NoAdvice;
                }

                if (Advice.IsFlawed)
                {
                    return FollowedAdvice ? AdviceOutcome.MisledByFlaw : AdviceOutcome.CaughtFlaw;
                }

                return FollowedAdvice ? AdviceOutcome.TrustedSound : AdviceOutcome.OverrodeSound;
            }
        }

        public JsonValue ToJson()
        {
            JsonValue json = JsonValue.NewObject()
                .Set("type", "decision")
                .Set("schema", LogNames.Schema)
                .Set("sessionId", SessionId)
                .Set("caseId", CaseId)
                .Set("timestampUtc", LogNames.Timestamp(TimestampUtc))
                .Set("index", Record.SequenceIndex)
                .Set("kind", CaseFileReader.KindToWireName(Record.Kind))
                .Set("optionId", Record.OptionId)
                .Set("quality", LogNames.Quality(Quality))
                .Set("secondsToDecide", Math.Round(SecondsToDecide, 2))
                .Set("stance", LogNames.Stance(Stance))
                .Set("outcome", LogNames.Outcome(Outcome));

            if (Advice != null)
            {
                json.Set("advice", JsonValue.NewObject()
                    .Set("recommendedOptionId", Advice.RecommendedOptionId)
                    .Set("flaw", FlawKinds.ToWireName(Advice.Flaw))
                    .Set("confidence", AdviceConfidences.ToWireName(Advice.Confidence))
                    .Set("source", LogNames.Source(Advice.Source))
                    .Set("followed", FollowedAdvice));
            }
            else
            {
                json.Set("advice", JsonValue.Null);
            }

            if (AdviceError != null)
            {
                json.Set("adviceError", AdviceError);
            }

            return json;
        }
    }

    /// <summary>Aggregate results of a session, for the debrief and for Phase 3's difficulty policy.</summary>
    public sealed class SessionSummary
    {
        private SessionSummary()
        {
        }

        public int Rulings { get; private set; }

        public int SoundRulings { get; private set; }

        public int DefensibleRulings { get; private set; }

        public int UnsoundRulings { get; private set; }

        public int FlawedAdviceSeen { get; private set; }

        public int FlawsCaught { get; private set; }

        public int TimesMisled { get; private set; }

        public int SoundAdviceSeen { get; private set; }

        public int SoundAdviceOverridden { get; private set; }

        /// <summary>Share of flawed advice the trainee did not follow, or null if none was shown.</summary>
        public double? FlawDetectionRate => FlawedAdviceSeen == 0 ? (double?)null : (double)FlawsCaught / FlawedAdviceSeen;

        /// <summary>
        /// Over-trust: flawed suggestions accepted ÷ flawed suggestions shown (null if none were shown).
        /// "Accepted" means the ruling matched the suggestion. High over-trust means the trainee follows the AI blindly.
        /// </summary>
        public double? OverTrust => FlawedAdviceSeen == 0 ? (double?)null : (double)TimesMisled / FlawedAdviceSeen;

        /// <summary>
        /// Under-trust: sound suggestions rejected ÷ sound suggestions shown (null if none were shown).
        /// High under-trust means the trainee dismisses good advice.
        /// </summary>
        public double? UnderTrust => SoundAdviceSeen == 0 ? (double?)null : (double)SoundAdviceOverridden / SoundAdviceSeen;

        public static SessionSummary From(IEnumerable<DecisionLogEntry> entries)
        {
            var summary = new SessionSummary();
            foreach (DecisionLogEntry entry in entries)
            {
                summary.Rulings++;
                switch (entry.Quality)
                {
                    case OptionQuality.Sound:
                        summary.SoundRulings++;
                        break;
                    case OptionQuality.Defensible:
                        summary.DefensibleRulings++;
                        break;
                    default:
                        summary.UnsoundRulings++;
                        break;
                }

                switch (entry.Outcome)
                {
                    case AdviceOutcome.CaughtFlaw:
                        summary.FlawedAdviceSeen++;
                        summary.FlawsCaught++;
                        break;
                    case AdviceOutcome.MisledByFlaw:
                        summary.FlawedAdviceSeen++;
                        summary.TimesMisled++;
                        break;
                    case AdviceOutcome.TrustedSound:
                        summary.SoundAdviceSeen++;
                        break;
                    case AdviceOutcome.OverrodeSound:
                        summary.SoundAdviceSeen++;
                        summary.SoundAdviceOverridden++;
                        break;
                }
            }

            return summary;
        }

        public JsonValue ToJson()
        {
            JsonValue json = JsonValue.NewObject()
                .Set("rulings", Rulings)
                .Set("soundRulings", SoundRulings)
                .Set("defensibleRulings", DefensibleRulings)
                .Set("unsoundRulings", UnsoundRulings)
                .Set("flawedAdviceSeen", FlawedAdviceSeen)
                .Set("flawsCaught", FlawsCaught)
                .Set("timesMisled", TimesMisled)
                .Set("soundAdviceSeen", SoundAdviceSeen)
                .Set("soundAdviceOverridden", SoundAdviceOverridden);
            json.Set("flawDetectionRate", Rate(FlawDetectionRate));
            json.Set("overTrust", Rate(OverTrust));
            json.Set("underTrust", Rate(UnderTrust));
            return json;
        }

        private static JsonValue Rate(double? value)
        {
            return value.HasValue ? JsonValue.Number(Math.Round(value.Value, 3)) : JsonValue.Null;
        }
    }

    /// <summary>Receives log records. Implementations must not throw for I/O problems they can survive.</summary>
    public interface IDecisionLogSink
    {
        void Write(JsonValue record);
    }

    /// <summary>Keeps records in memory. Used by tests and by the debrief screen.</summary>
    public sealed class MemoryLogSink : IDecisionLogSink
    {
        private readonly List<JsonValue> _records = new List<JsonValue>();

        public IReadOnlyList<JsonValue> Records => _records;

        public void Write(JsonValue record)
        {
            _records.Add(record);
        }
    }

    /// <summary>Appends one JSON object per line (JSON Lines), so a crashed session still leaves a readable log.</summary>
    public sealed class JsonLinesFileSink : IDecisionLogSink
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public JsonLinesFileSink(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A log file path is required.", nameof(path));
            }

            Path = path;
        }

        public string Path { get; }

        /// <summary>The last I/O error, if a write failed. Logging never interrupts a session.</summary>
        public Exception LastError { get; private set; }

        public void Write(JsonValue record)
        {
            try
            {
                string directory = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(Path, record.ToJson() + "\n", Utf8NoBom);
            }
            catch (IOException ex)
            {
                LastError = ex;
            }
            catch (UnauthorizedAccessException ex)
            {
                LastError = ex;
            }
        }
    }

    /// <summary>Writes to several sinks.</summary>
    public sealed class CompositeLogSink : IDecisionLogSink
    {
        private readonly IDecisionLogSink[] _sinks;

        public CompositeLogSink(params IDecisionLogSink[] sinks)
        {
            _sinks = sinks ?? Array.Empty<IDecisionLogSink>();
        }

        public void Write(JsonValue record)
        {
            foreach (IDecisionLogSink sink in _sinks)
            {
                sink?.Write(record);
            }
        }
    }
}
