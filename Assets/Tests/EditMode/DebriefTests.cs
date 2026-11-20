using System.Collections.Generic;
using CourtSim.Core.Advising;
using CourtSim.Core.Cases;
using CourtSim.Core.Logging;
using CourtSim.Core.Sessions;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    public sealed class DebriefTests
    {
        [Test]
        public void Build_CompletedSession_DescribesEveryRuling()
        {
            CaseSession session = Run(new[] { FlawKind.MisreadFact, FlawKind.MisstatedLaw, FlawKind.None }, "bail.grant", "evidence.exclude", "sentence.custody");

            IReadOnlyList<DebriefItem> items = Debrief.Build(session);

            Assert.That(items.Count, Is.EqualTo(3));

            DebriefItem caught = items[0];
            Assert.That(caught.Title, Is.EqualTo("Bail application"));
            Assert.That(caught.RulingLabel, Is.EqualTo("Release on conditions"));
            Assert.That(caught.Quality, Is.EqualTo(OptionQuality.Sound));
            Assert.That(caught.Feedback, Is.EqualTo("Feedback for bail.grant."));
            Assert.That(caught.Outcome, Is.EqualTo(AdviceOutcome.CaughtFlaw));
            Assert.That(caught.AdviceLabel, Is.EqualTo("Remand in custody"));
            Assert.That(caught.AdviceFlaw, Is.EqualTo(FlawKind.MisreadFact));
            Assert.That(caught.FlawExplanation, Is.EqualTo("Invents a failure to appear."));
            Assert.That(caught.SoundLabel, Is.EqualTo("Release on conditions"));

            DebriefItem misled = items[1];
            Assert.That(misled.Outcome, Is.EqualTo(AdviceOutcome.MisledByFlaw));
            Assert.That(misled.Quality, Is.EqualTo(OptionQuality.Unsound));
            Assert.That(misled.RulingLabel, Is.EqualTo("Exclude the recording"));
            Assert.That(misled.AdviceFlaw, Is.EqualTo(FlawKind.MisstatedLaw));
            Assert.That(misled.SoundLabel, Is.EqualTo("Admit the recording"));

            DebriefItem overrode = items[2];
            Assert.That(overrode.Outcome, Is.EqualTo(AdviceOutcome.OverrodeSound));
            Assert.That(overrode.Quality, Is.EqualTo(OptionQuality.Defensible));
            Assert.That(overrode.AdviceLabel, Is.EqualTo("Community order"));
            Assert.That(overrode.AdviceFlaw, Is.EqualTo(FlawKind.None));
            Assert.That(overrode.FlawExplanation, Is.Empty);
            Assert.That(overrode.SoundLabel, Is.EqualTo("Community order"));
        }

        [Test]
        public void Build_PartialSession_OnlyIncludesRulingsMade()
        {
            CaseSession session = Run(new[] { FlawKind.None, FlawKind.None, FlawKind.None }, "bail.grant");

            Assert.That(Debrief.Build(session).Count, Is.EqualTo(1));
        }

        [Test]
        public void Build_NoAdvice_LeavesAdviceFieldsEmpty()
        {
            var session = new CaseSession(TestCases.Load(), null, StubAdvisor.Failing(new AdvisorException("down")), new MemoryLogSink());
            session.Submit("bail.deny");

            DebriefItem item = Debrief.Build(session)[0];

            Assert.That(item.Outcome, Is.EqualTo(AdviceOutcome.NoAdvice));
            Assert.That(item.AdviceLabel, Is.Null);
            Assert.That(item.AdviceFlaw, Is.EqualTo(FlawKind.None));
            Assert.That(item.FlawExplanation, Is.Null);
            Assert.That(Debrief.DescribeOutcome(item), Is.EqualTo("No advice was available for this ruling."));
        }

        [Test]
        public void DescribeOutcome_EachOutcome_NamesTheSuggestion()
        {
            IReadOnlyList<DebriefItem> flawed = Debrief.Build(Run(new[] { FlawKind.MisreadFact, FlawKind.MisstatedLaw, FlawKind.None }, "bail.grant", "evidence.exclude", "sentence.custody"));
            IReadOnlyList<DebriefItem> sound = Debrief.Build(Run(new[] { FlawKind.None, FlawKind.None, FlawKind.None }, "bail.grant"));

            Assert.That(Debrief.DescribeOutcome(flawed[0]), Is.EqualTo("You did not follow the assistant's flawed suggestion (Remand in custody). Well caught."));
            Assert.That(Debrief.DescribeOutcome(flawed[1]), Is.EqualTo("You followed the assistant's suggestion (Exclude the recording), but it was deliberately flawed."));
            Assert.That(Debrief.DescribeOutcome(flawed[2]), Is.EqualTo("The assistant's suggestion (Community order) was sound, and you departed from it."));
            Assert.That(Debrief.DescribeOutcome(sound[0]), Is.EqualTo("You followed the assistant's suggestion (Release on conditions), which was sound."));
        }

        [Test]
        public void QualityLabel_MapsEveryQuality()
        {
            Assert.That(Debrief.QualityLabel(OptionQuality.Sound), Is.EqualTo("Sound"));
            Assert.That(Debrief.QualityLabel(OptionQuality.Defensible), Is.EqualTo("Defensible"));
            Assert.That(Debrief.QualityLabel(OptionQuality.Unsound), Is.EqualTo("Unsound"));
        }

        [Test]
        public void Headline_MixedSession_ReportsSoundDefensibleAndCatches()
        {
            CaseSession session = Run(new[] { FlawKind.MisreadFact, FlawKind.MisstatedLaw, FlawKind.None }, "bail.grant", "evidence.exclude", "sentence.custody");

            Assert.That(Debrief.Headline(session.Summary), Is.EqualTo("1 of 3 rulings were sound, 1 defensible. You caught 1 of 2 flawed suggestions."));
        }

        [Test]
        public void Headline_OneFlaw_UsesSingular()
        {
            CaseSession session = Run(new[] { FlawKind.MisreadFact, FlawKind.None, FlawKind.None }, "bail.deny", "evidence.admit", "sentence.community");

            Assert.That(Debrief.Headline(session.Summary), Is.EqualTo("2 of 3 rulings were sound. You caught 0 of 1 flawed suggestion."));
        }

        [Test]
        public void Headline_NoFlaws_SaysEverySuggestionWasSound()
        {
            CaseSession session = Run(new[] { FlawKind.None, FlawKind.None, FlawKind.None }, "bail.grant", "evidence.admit", "sentence.community");

            Assert.That(Debrief.Headline(session.Summary), Is.EqualTo("3 of 3 rulings were sound. Every suggestion you saw this session was sound."));
        }

        [Test]
        public void Headline_NoAdviceShown_SaysSo()
        {
            var session = new CaseSession(TestCases.Load(), null, StubAdvisor.Failing(new System.InvalidOperationException("offline")), new MemoryLogSink());
            session.Start();
            foreach (string ruling in new[] { "bail.grant", "evidence.admit", "sentence.community" })
            {
                session.Submit(ruling);
            }

            Assert.That(Debrief.Headline(session.Summary), Is.EqualTo("3 of 3 rulings were sound. No advice was shown this session."));
        }

        private static CaseSession Run(FlawKind[] plan, params string[] rulings)
        {
            var session = new CaseSession(TestCases.Load(), new AdvicePlan(plan), new ScriptedAdvisor(), new MemoryLogSink());
            session.Start();
            foreach (string ruling in rulings)
            {
                session.Submit(ruling);
            }

            return session;
        }
    }
}
