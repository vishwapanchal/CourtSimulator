using System;
using CourtSim.Core.Decisions;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    public sealed class DecisionTests
    {
        [Test]
        public void StandardSequence_OrdersBailEvidenceSentencing()
        {
            DecisionSequence sequence = DecisionSequence.CreateStandardPlaceholder();

            Assert.That(sequence.Count, Is.EqualTo(3));
            Assert.That(sequence.Current.Kind, Is.EqualTo(DecisionPointKind.Bail));
            sequence.Record(sequence.Current.Options[0].Id);
            Assert.That(sequence.Current.Kind, Is.EqualTo(DecisionPointKind.EvidenceAdmissibility));
            sequence.Record(sequence.Current.Options[0].Id);
            Assert.That(sequence.Current.Kind, Is.EqualTo(DecisionPointKind.Sentencing));
        }

        [Test]
        public void Record_StoresRulingsInOrder_AndCompletes()
        {
            DecisionSequence sequence = DecisionSequence.CreateStandardPlaceholder();

            DecisionRecord first = sequence.Record("bail.deny");
            sequence.Record("evidence.exclude");
            DecisionRecord last = sequence.Record("sentence.non_custodial");

            Assert.That(first.SequenceIndex, Is.EqualTo(0));
            Assert.That(first.Kind, Is.EqualTo(DecisionPointKind.Bail));
            Assert.That(first.OptionId, Is.EqualTo("bail.deny"));
            Assert.That(last.SequenceIndex, Is.EqualTo(2));
            Assert.That(sequence.IsComplete, Is.True);
            Assert.That(sequence.Current, Is.Null);
            Assert.That(sequence.CurrentIndex, Is.EqualTo(3));
            Assert.That(sequence.Records.Count, Is.EqualTo(3));
        }

        [Test]
        public void Record_RejectsOptionFromAnotherDecisionPoint()
        {
            DecisionSequence sequence = DecisionSequence.CreateStandardPlaceholder();

            Assert.Throws<ArgumentException>(() => sequence.Record("sentence.non_custodial"));
            Assert.That(sequence.CurrentIndex, Is.EqualTo(0));
        }

        [Test]
        public void Record_AfterCompletion_Throws()
        {
            var sequence = new DecisionSequence(new[] { DecisionPointCatalog.GetPlaceholder(DecisionPointKind.Bail) });
            sequence.Record("bail.grant");

            Assert.Throws<InvalidOperationException>(() => sequence.Record("bail.grant"));
        }

        [Test]
        public void Reset_ClearsRecords()
        {
            DecisionSequence sequence = DecisionSequence.CreateStandardPlaceholder();
            sequence.Record("bail.grant");

            sequence.Reset();

            Assert.That(sequence.CurrentIndex, Is.EqualTo(0));
            Assert.That(sequence.Records, Is.Empty);
        }

        [Test]
        public void Sequence_RejectsEmptyOrNullInput()
        {
            Assert.Throws<ArgumentNullException>(() => new DecisionSequence(null));
            Assert.Throws<ArgumentException>(() => new DecisionSequence(new DecisionPointDefinition[0]));
            Assert.Throws<ArgumentException>(() => new DecisionSequence(new DecisionPointDefinition[] { null }));
        }

        [TestCase(DecisionPointKind.Bail)]
        [TestCase(DecisionPointKind.EvidenceAdmissibility)]
        [TestCase(DecisionPointKind.Sentencing)]
        public void Catalog_ProvidesValidPlaceholderForEveryKind(DecisionPointKind kind)
        {
            DecisionPointDefinition definition = DecisionPointCatalog.GetPlaceholder(kind);

            Assert.That(definition.Kind, Is.EqualTo(kind));
            Assert.That(definition.Title, Is.Not.Empty);
            Assert.That(definition.Prompt, Is.Not.Empty);
            Assert.That(definition.Options.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(DecisionPointCatalog.ShortName(kind), Is.Not.Empty);
        }

        [Test]
        public void Catalog_RejectsUnknownKind()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DecisionPointCatalog.GetPlaceholder((DecisionPointKind)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => DecisionPointCatalog.ShortName((DecisionPointKind)99));
        }

        [Test]
        public void FindOption_ReturnsNullForUnknownId()
        {
            DecisionPointDefinition definition = DecisionPointCatalog.GetPlaceholder(DecisionPointKind.Bail);

            Assert.That(definition.FindOption("bail.grant").Label, Is.EqualTo("Grant bail"));
            Assert.That(definition.FindOption("nope"), Is.Null);
            Assert.That(definition.FindOption(null), Is.Null);
        }

        [Test]
        public void Definition_ValidatesInput()
        {
            var a = new DecisionOption("a", "A");
            var b = new DecisionOption("b", "B");

            Assert.Throws<ArgumentException>(() => new DecisionPointDefinition(DecisionPointKind.Bail, " ", "p", new[] { a, b }));
            Assert.Throws<ArgumentNullException>(() => new DecisionPointDefinition(DecisionPointKind.Bail, "t", "p", null));
            Assert.Throws<ArgumentException>(() => new DecisionPointDefinition(DecisionPointKind.Bail, "t", "p", new[] { a }));
            Assert.Throws<ArgumentException>(() => new DecisionPointDefinition(DecisionPointKind.Bail, "t", "p", new[] { a, a }));
            Assert.Throws<ArgumentException>(() => new DecisionPointDefinition(DecisionPointKind.Bail, "t", "p", new[] { a, null }));
            Assert.That(new DecisionPointDefinition(DecisionPointKind.Bail, "t", null, new[] { a, b }).Prompt, Is.Empty);
        }

        [Test]
        public void Option_ValidatesInput()
        {
            Assert.Throws<ArgumentException>(() => new DecisionOption("", "Label"));
            Assert.Throws<ArgumentException>(() => new DecisionOption("id", null));
        }
    }
}
