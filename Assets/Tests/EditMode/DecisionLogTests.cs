using System;
using System.IO;
using CourtSim.Core.Advising;
using CourtSim.Core.Cases;
using CourtSim.Core.Decisions;
using CourtSim.Core.Json;
using CourtSim.Core.Logging;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    public sealed class DecisionLogTests
    {
        private static readonly DateTime Timestamp = new DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc);

        private string _tempDirectory;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "courtsim-tests-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        [TestCase(FlawKind.None, true, AdviceOutcome.TrustedSound)]
        [TestCase(FlawKind.None, false, AdviceOutcome.OverrodeSound)]
        [TestCase(FlawKind.MisreadFact, true, AdviceOutcome.MisledByFlaw)]
        [TestCase(FlawKind.MisstatedLaw, false, AdviceOutcome.CaughtFlaw)]
        public void Outcome_AdviceAndRuling_MapsToMatrix(FlawKind flaw, bool followed, AdviceOutcome expected)
        {
            DecisionLogEntry entry = Entry(followed ? "bail.deny" : "bail.grant", OptionQuality.Sound, AdviceFor("bail.deny", flaw));

            Assert.That(entry.FollowedAdvice, Is.EqualTo(followed));
            Assert.That(entry.Outcome, Is.EqualTo(expected));
        }

        [Test]
        public void Outcome_NoAdvice_IsNoAdvice()
        {
            DecisionLogEntry entry = Entry("bail.grant", OptionQuality.Sound, null);

            Assert.That(entry.FollowedAdvice, Is.False);
            Assert.That(entry.Outcome, Is.EqualTo(AdviceOutcome.NoAdvice));
        }

        [Test]
        public void Entry_NegativeSeconds_ClampToZero()
        {
            var entry = new DecisionLogEntry("s", "c", new DecisionRecord(0, DecisionPointKind.Bail, "a"), OptionQuality.Sound, null, AdviceStance.None, -4, Timestamp);

            Assert.That(entry.SecondsToDecide, Is.EqualTo(0));
            Assert.That(entry.AdviceError, Is.Null);
        }

        [Test]
        public void ToJson_WithAdvice_WritesEveryField()
        {
            var entry = new DecisionLogEntry(
                "session-1",
                "case-1",
                new DecisionRecord(1, DecisionPointKind.EvidenceAdmissibility, "evidence.exclude"),
                OptionQuality.Unsound,
                AdviceFor("evidence.exclude", FlawKind.MisstatedLaw, AdviceSource.Claude),
                AdviceStance.Accepted,
                12.3456,
                Timestamp,
                "fallback used");

            JsonValue json = JsonReader.Parse(entry.ToJson().ToJson());

            Assert.That(json.GetString("type"), Is.EqualTo("decision"));
            Assert.That(json.GetString("schema"), Is.EqualTo("courtsim.log/1"));
            Assert.That(json.GetString("sessionId"), Is.EqualTo("session-1"));
            Assert.That(json.GetString("caseId"), Is.EqualTo("case-1"));
            Assert.That(json.GetString("timestampUtc"), Is.EqualTo("2026-01-02T03:04:05.678Z"));
            Assert.That(json.GetNumber("index"), Is.EqualTo(1));
            Assert.That(json.GetString("kind"), Is.EqualTo("evidence_admissibility"));
            Assert.That(json.GetString("optionId"), Is.EqualTo("evidence.exclude"));
            Assert.That(json.GetString("quality"), Is.EqualTo("unsound"));
            Assert.That(json.GetNumber("secondsToDecide"), Is.EqualTo(12.35));
            Assert.That(json.GetString("stance"), Is.EqualTo("accepted"));
            Assert.That(json.GetString("outcome"), Is.EqualTo("misled_by_flaw"));
            Assert.That(json.GetString("adviceError"), Is.EqualTo("fallback used"));
            JsonValue advice = json.Get("advice");
            Assert.That(advice.GetString("recommendedOptionId"), Is.EqualTo("evidence.exclude"));
            Assert.That(advice.GetString("flaw"), Is.EqualTo("misstated_law"));
            Assert.That(advice.GetString("confidence"), Is.EqualTo("medium"));
            Assert.That(advice.GetString("source"), Is.EqualTo("claude"));
            Assert.That(advice.Get("followed").AsBool(), Is.True);
        }

        [Test]
        public void ToJson_WithoutAdvice_WritesNullAdviceAndNoError()
        {
            JsonValue json = Entry("bail.grant", OptionQuality.Defensible, null).ToJson();

            Assert.That(json.Get("advice").Kind, Is.EqualTo(JsonKind.Null));
            Assert.That(json.Has("adviceError"), Is.False);
            Assert.That(json.GetString("outcome"), Is.EqualTo("no_advice"));
            Assert.That(json.GetString("quality"), Is.EqualTo("defensible"));
            Assert.That(json.GetString("stance"), Is.EqualTo("none"));
        }

        [Test]
        public void LogNames_MapEveryValue()
        {
            Assert.That(LogNames.Quality(OptionQuality.Sound), Is.EqualTo("sound"));
            Assert.That(LogNames.Quality(OptionQuality.Defensible), Is.EqualTo("defensible"));
            Assert.That(LogNames.Quality(OptionQuality.Unsound), Is.EqualTo("unsound"));
            Assert.That(LogNames.Stance(AdviceStance.None), Is.EqualTo("none"));
            Assert.That(LogNames.Stance(AdviceStance.Accepted), Is.EqualTo("accepted"));
            Assert.That(LogNames.Stance(AdviceStance.Challenged), Is.EqualTo("challenged"));
            Assert.That(LogNames.Outcome(AdviceOutcome.NoAdvice), Is.EqualTo("no_advice"));
            Assert.That(LogNames.Outcome(AdviceOutcome.TrustedSound), Is.EqualTo("trusted_sound"));
            Assert.That(LogNames.Outcome(AdviceOutcome.OverrodeSound), Is.EqualTo("overrode_sound"));
            Assert.That(LogNames.Outcome(AdviceOutcome.MisledByFlaw), Is.EqualTo("misled_by_flaw"));
            Assert.That(LogNames.Outcome(AdviceOutcome.CaughtFlaw), Is.EqualTo("caught_flaw"));
            Assert.That(LogNames.Source(AdviceSource.Claude), Is.EqualTo("claude"));
            Assert.That(LogNames.Source(AdviceSource.Scripted), Is.EqualTo("scripted"));
        }

        [Test]
        public void Timestamp_LocalTime_IsConvertedToUtc()
        {
            DateTime local = Timestamp.ToLocalTime();

            Assert.That(LogNames.Timestamp(local), Is.EqualTo("2026-01-02T03:04:05.678Z"));
        }

        [Test]
        public void Summary_NoEntries_IsEmptyWithNullDetectionRate()
        {
            SessionSummary summary = SessionSummary.From(new DecisionLogEntry[0]);

            Assert.That(summary.Rulings, Is.EqualTo(0));
            Assert.That(summary.FlawDetectionRate, Is.Null);
            Assert.That(summary.ToJson().Get("flawDetectionRate").Kind, Is.EqualTo(JsonKind.Null));
        }

        [Test]
        public void Summary_MixedEntries_CountsEachCategory()
        {
            var entries = new[]
            {
                Entry("bail.deny", OptionQuality.Sound, AdviceFor("bail.deny", FlawKind.None)),
                Entry("bail.grant", OptionQuality.Defensible, AdviceFor("bail.deny", FlawKind.None)),
                Entry("bail.grant", OptionQuality.Sound, AdviceFor("bail.deny", FlawKind.MisreadFact)),
                Entry("bail.grant", OptionQuality.Sound, AdviceFor("bail.deny", FlawKind.OmittedFactor)),
                Entry("bail.deny", OptionQuality.Unsound, AdviceFor("bail.deny", FlawKind.ImproperFactor)),
                Entry("bail.deny", OptionQuality.Unsound, null),
            };

            SessionSummary summary = SessionSummary.From(entries);

            Assert.That(summary.Rulings, Is.EqualTo(6));
            Assert.That(summary.SoundRulings, Is.EqualTo(3));
            Assert.That(summary.DefensibleRulings, Is.EqualTo(1));
            Assert.That(summary.UnsoundRulings, Is.EqualTo(2));
            Assert.That(summary.SoundAdviceSeen, Is.EqualTo(2));
            Assert.That(summary.SoundAdviceOverridden, Is.EqualTo(1));
            Assert.That(summary.FlawedAdviceSeen, Is.EqualTo(3));
            Assert.That(summary.FlawsCaught, Is.EqualTo(2));
            Assert.That(summary.TimesMisled, Is.EqualTo(1));
            Assert.That(summary.FlawDetectionRate, Is.EqualTo(2.0 / 3.0).Within(1e-9));

            JsonValue json = summary.ToJson();
            Assert.That(json.GetNumber("rulings"), Is.EqualTo(6));
            Assert.That(json.GetNumber("soundRulings"), Is.EqualTo(3));
            Assert.That(json.GetNumber("defensibleRulings"), Is.EqualTo(1));
            Assert.That(json.GetNumber("unsoundRulings"), Is.EqualTo(2));
            Assert.That(json.GetNumber("flawedAdviceSeen"), Is.EqualTo(3));
            Assert.That(json.GetNumber("flawsCaught"), Is.EqualTo(2));
            Assert.That(json.GetNumber("timesMisled"), Is.EqualTo(1));
            Assert.That(json.GetNumber("soundAdviceSeen"), Is.EqualTo(2));
            Assert.That(json.GetNumber("soundAdviceOverridden"), Is.EqualTo(1));
            Assert.That(json.GetNumber("flawDetectionRate"), Is.EqualTo(0.667));
        }

        [Test]
        public void Summary_AllFlawsCaught_HasDetectionRateOne()
        {
            SessionSummary summary = SessionSummary.From(new[] { Entry("bail.grant", OptionQuality.Sound, AdviceFor("bail.deny", FlawKind.MisreadFact)) });

            Assert.That(summary.FlawDetectionRate, Is.EqualTo(1.0));
        }

        [Test]
        public void MemoryLogSink_KeepsRecordsInOrder()
        {
            var sink = new MemoryLogSink();
            JsonValue first = JsonValue.NewObject().Set("n", 1);
            JsonValue second = JsonValue.NewObject().Set("n", 2);

            sink.Write(first);
            sink.Write(second);

            Assert.That(sink.Records, Is.EqualTo(new[] { first, second }));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        public void JsonLinesFileSink_BlankPath_Throws(string path)
        {
            Assert.Throws<ArgumentException>(() => new JsonLinesFileSink(path));
        }

        [Test]
        public void JsonLinesFileSink_Write_AppendsOneJsonObjectPerLine()
        {
            string path = Path.Combine(_tempDirectory, "nested", "log.jsonl");
            var sink = new JsonLinesFileSink(path);

            sink.Write(JsonValue.NewObject().Set("n", 1).Set("text", "café\nline"));
            sink.Write(JsonValue.NewObject().Set("n", 2));

            Assert.That(sink.Path, Is.EqualTo(path));
            Assert.That(sink.LastError, Is.Null);
            string content = File.ReadAllText(path);
            Assert.That(content, Does.EndWith("\n"));
            string[] lines = content.TrimEnd('\n').Split('\n');
            Assert.That(lines.Length, Is.EqualTo(2));
            Assert.That(JsonReader.Parse(lines[0]).GetString("text"), Is.EqualTo("café\nline"));
            Assert.That(JsonReader.Parse(lines[1]).GetNumber("n"), Is.EqualTo(2));
            byte[] bytes = File.ReadAllBytes(path);
            Assert.That(bytes[0], Is.EqualTo((byte)'{'), "No byte-order mark.");
        }

        [Test]
        public void JsonLinesFileSink_UnwritablePath_RecordsErrorWithoutThrowing()
        {
            Directory.CreateDirectory(_tempDirectory);
            string blocker = Path.Combine(_tempDirectory, "blocker");
            File.WriteAllText(blocker, "not a directory");
            var sink = new JsonLinesFileSink(Path.Combine(blocker, "log.jsonl"));

            Assert.DoesNotThrow(() => sink.Write(JsonValue.NewObject().Set("n", 1)));

            Assert.That(sink.LastError, Is.Not.Null);
        }

        [Test]
        public void JsonLinesFileSink_PathIsDirectory_RecordsErrorWithoutThrowing()
        {
            Directory.CreateDirectory(_tempDirectory);
            var sink = new JsonLinesFileSink(_tempDirectory);

            Assert.DoesNotThrow(() => sink.Write(JsonValue.NewObject().Set("n", 1)));

            Assert.That(sink.LastError, Is.Not.Null);
        }

        [Test]
        public void CompositeLogSink_WritesToEverySinkAndSkipsNull()
        {
            var first = new MemoryLogSink();
            var second = new MemoryLogSink();
            var composite = new CompositeLogSink(first, null, second);
            JsonValue record = JsonValue.NewObject().Set("n", 1);

            composite.Write(record);

            Assert.That(first.Records, Is.EqualTo(new[] { record }));
            Assert.That(second.Records, Is.EqualTo(new[] { record }));
        }

        [Test]
        public void CompositeLogSink_NullOrEmpty_IsNoOp()
        {
            Assert.DoesNotThrow(() => new CompositeLogSink(null).Write(JsonValue.Null));
            Assert.DoesNotThrow(() => new CompositeLogSink().Write(JsonValue.Null));
        }

        private static Advice AdviceFor(string optionId, FlawKind flaw, AdviceSource source = AdviceSource.Scripted)
        {
            return new Advice(DecisionPointKind.Bail, optionId, "r", null, AdviceConfidence.Medium, flaw, flaw == FlawKind.None ? null : "x", source);
        }

        private static DecisionLogEntry Entry(string optionId, OptionQuality quality, Advice advice)
        {
            var record = new DecisionRecord(0, DecisionPointKind.Bail, optionId);
            return new DecisionLogEntry("s", "c", record, quality, advice, AdviceStance.None, 1, Timestamp);
        }
    }
}
