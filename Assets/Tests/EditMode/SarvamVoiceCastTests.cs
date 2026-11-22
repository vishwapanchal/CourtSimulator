using System;
using System.Collections.Generic;
using System.Linq;
using CourtSim.Core.Sarvam;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    public sealed class SarvamVoiceCastTests
    {
        private static readonly string[] SampleIds = Enumerable.Range(1, 60).Select(i => "case-" + i.ToString("00")).ToArray();

        [Test]
        public void Fnv1a_MatchesPublishedTestVectors()
        {
            // Reference values from the FNV specification (32-bit FNV-1a).
            Assert.That(SarvamVoiceCast.Fnv1a(string.Empty), Is.EqualTo(2166136261u));
            Assert.That(SarvamVoiceCast.Fnv1a("a"), Is.EqualTo(0xE40C292Cu));
            Assert.That(SarvamVoiceCast.Fnv1a("foobar"), Is.EqualTo(0xBF9CF968u));
            Assert.That(SarvamVoiceCast.Fnv1a(null), Is.EqualTo(2166136261u));
        }

        [Test]
        public void For_SameId_GivesSameCast()
        {
            SarvamVoiceCast first = SarvamVoiceCast.For("bail-theft-01");
            SarvamVoiceCast second = SarvamVoiceCast.For("bail-theft-01");

            foreach (SarvamVoiceRole role in AllRoles())
            {
                Assert.That(second.Get(role).Speaker, Is.EqualTo(first.Get(role).Speaker));
                Assert.That(second.Get(role).IsFemale, Is.EqualTo(first.Get(role).IsFemale));
            }
        }

        [Test]
        public void For_BlankId_GivesDefaultCast()
        {
            Assert.That(SarvamVoiceCast.For(null), Is.SameAs(SarvamVoiceCast.Default));
            Assert.That(SarvamVoiceCast.For("  "), Is.SameAs(SarvamVoiceCast.Default));
            Assert.That(SarvamVoiceCast.Default.Prosecutor.Speaker, Is.EqualTo("anand"));
            Assert.That(SarvamVoiceCast.Default.Defence.Speaker, Is.EqualTo("kavitha"));
            Assert.That(SarvamVoiceCast.Default.Defence.IsFemale, Is.True);
            Assert.That(SarvamVoiceCast.Default.Clerk.Speaker, Is.EqualTo("gokul"));
        }

        [Test]
        public void For_AssistantIsAlwaysRoopa()
        {
            foreach (string id in SampleIds.Concat(new string[] { null }))
            {
                SarvamVoice assistant = SarvamVoiceCast.For(id).Get(SarvamVoiceRole.Assistant);
                Assert.That(assistant.Speaker, Is.EqualTo("roopa"));
                Assert.That(assistant.IsFemale, Is.True);
                Assert.That(assistant.Gender, Is.EqualTo("Female"));
            }
        }

        [Test]
        public void For_EveryCase_HasDistinctSpeakersFromTheRightPools()
        {
            foreach (string id in SampleIds)
            {
                SarvamVoiceCast cast = SarvamVoiceCast.For(id);
                var speakers = new[] { cast.Assistant.Speaker, cast.Prosecutor.Speaker, cast.Defence.Speaker, cast.Clerk.Speaker };

                Assert.That(speakers.Distinct().Count(), Is.EqualTo(4), id);
                Assert.That(SarvamVoiceCast.MaleSpeakers, Does.Contain(cast.Prosecutor.Speaker), id);
                Assert.That(SarvamVoiceCast.MaleSpeakers, Does.Contain(cast.Clerk.Speaker), id);
                Assert.That(cast.Prosecutor.IsFemale || cast.Clerk.IsFemale, Is.False, id);
                string[] defencePool = cast.Defence.IsFemale ? SarvamVoiceCast.FemaleSpeakers : SarvamVoiceCast.MaleSpeakers;
                Assert.That(defencePool, Does.Contain(cast.Defence.Speaker), id);
                Assert.That(cast.Defence.Gender, Is.EqualTo(cast.Defence.IsFemale ? "Female" : "Male"));
            }
        }

        [Test]
        public void For_DifferentIds_VaryVoicesAndDefenceGender()
        {
            List<SarvamVoiceCast> casts = SampleIds.Select(SarvamVoiceCast.For).ToList();

            Assert.That(casts.Select(c => c.Prosecutor.Speaker).Distinct().Count(), Is.GreaterThan(3));
            Assert.That(casts.Select(c => c.Clerk.Speaker).Distinct().Count(), Is.GreaterThan(3));
            Assert.That(casts.Select(c => c.Defence.Speaker).Distinct().Count(), Is.GreaterThan(3));
            Assert.That(casts.Any(c => c.Defence.IsFemale), Is.True);
            Assert.That(casts.Any(c => !c.Defence.IsFemale), Is.True);
        }

        [Test]
        public void Pools_AreValidAndRoopaIsReserved()
        {
            Assert.That(SarvamVoiceCast.FemaleSpeakers, Does.Not.Contain(SarvamVoiceCast.AssistantSpeaker));
            Assert.That(SarvamVoiceCast.MaleSpeakers, Does.Not.Contain(SarvamVoiceCast.AssistantSpeaker));
            Assert.That(SarvamVoiceCast.MaleSpeakers.Distinct().Count(), Is.EqualTo(SarvamVoiceCast.MaleSpeakers.Length));
            Assert.That(SarvamVoiceCast.FemaleSpeakers.Distinct().Count(), Is.EqualTo(SarvamVoiceCast.FemaleSpeakers.Length));
        }

        [Test]
        public void Get_ReturnsTheVoiceForEachRole()
        {
            SarvamVoiceCast cast = SarvamVoiceCast.For("case-07");

            Assert.That(cast.Get(SarvamVoiceRole.Prosecutor), Is.SameAs(cast.Prosecutor));
            Assert.That(cast.Get(SarvamVoiceRole.Defence), Is.SameAs(cast.Defence));
            Assert.That(cast.Get(SarvamVoiceRole.Clerk), Is.SameAs(cast.Clerk));
            Assert.That(cast.Get(SarvamVoiceRole.Assistant), Is.SameAs(cast.Assistant));
        }

        [Test]
        public void SarvamVoice_NullSpeaker_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new SarvamVoice(null, false));
        }

        private static IEnumerable<SarvamVoiceRole> AllRoles()
        {
            return Enum.GetValues(typeof(SarvamVoiceRole)).Cast<SarvamVoiceRole>();
        }
    }
}
