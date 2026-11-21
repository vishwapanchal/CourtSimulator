using CourtSim.Core.Advising;
using CourtSim.Core.Logging;
using CourtSim.Core.Sessions;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    /// <summary>Over-trust and under-trust scores shown on the debrief (Phase C).</summary>
    public sealed class TrustScoreTests
    {
        [Test]
        public void OverTrust_FollowedOneOfTwoFlaws_IsHalf()
        {
            // Plan: two flawed points and one sound one. Follow the first flaw, reject the second, follow the sound advice.
            SessionSummary summary = Run(new[] { FlawKind.MisreadFact, FlawKind.MisstatedLaw, FlawKind.None }, true, false, true);

            Assert.That(summary.OverTrust, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(summary.UnderTrust, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(Debrief.OverTrustPercent(summary), Is.EqualTo("50%"));
            Assert.That(Debrief.OverTrustLine(summary), Does.StartWith("You accepted 1 of 2 flawed suggestions: high over-trust"));
            Assert.That(Debrief.UnderTrustLine(summary), Does.StartWith("You rejected 0 of 1 sound suggestion: low under-trust"));
        }

        [Test]
        public void UnderTrust_RejectedAllSoundAdvice_IsHundredPercent()
        {
            SessionSummary summary = Run(new[] { FlawKind.None, FlawKind.None, FlawKind.None }, false, false, false);

            Assert.That(summary.UnderTrust, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(summary.OverTrust, Is.Null);
            Assert.That(Debrief.UnderTrustPercent(summary), Is.EqualTo("100%"));
            Assert.That(Debrief.OverTrustPercent(summary), Is.EqualTo("–"));
            Assert.That(Debrief.OverTrustLine(summary), Does.Contain("could not be measured"));
        }

        [Test]
        public void NoAdvice_BothScoresUnmeasured()
        {
            var session = new CaseSession(TestCases.Load(), null, StubAdvisor.Failing(new System.InvalidOperationException("offline")), new MemoryLogSink());
            session.Start();
            foreach (string ruling in new[] { "bail.grant", "evidence.admit", "sentence.community" })
            {
                session.Submit(ruling);
            }

            Assert.That(session.Summary.OverTrust, Is.Null);
            Assert.That(session.Summary.UnderTrust, Is.Null);
            Assert.That(Debrief.UnderTrustLine(session.Summary), Does.Contain("could not be measured"));
        }

        [TestCase(0.0, "low")]
        [TestCase(0.24, "low")]
        [TestCase(0.25, "moderate")]
        [TestCase(0.49, "moderate")]
        [TestCase(0.5, "high")]
        [TestCase(1.0, "high")]
        public void Band_Thresholds(double rate, string expected)
        {
            Assert.That(Debrief.Band(rate), Is.EqualTo(expected));
        }

        [Test]
        public void SummaryJson_IncludesBothScores()
        {
            SessionSummary summary = Run(new[] { FlawKind.MisreadFact, FlawKind.None, FlawKind.None }, true, false, true);

            Assert.That(summary.ToJson().GetNumber("overTrust"), Is.EqualTo(1.0));
            Assert.That(summary.ToJson().GetNumber("underTrust"), Is.EqualTo(0.5));
        }

        /// <summary>Plays the test case with scripted advice; each flag says whether that ruling follows the advice.</summary>
        private static SessionSummary Run(FlawKind[] plan, params bool[] follow)
        {
            var session = new CaseSession(TestCases.Load(), new AdvicePlan(plan), new ScriptedAdvisor(), new MemoryLogSink());
            session.Start();
            for (int i = 0; i < follow.Length; i++)
            {
                string advised = session.CurrentAdvice.RecommendedOptionId;
                string choice = advised;
                if (!follow[i])
                {
                    foreach (var option in session.CurrentPoint.Definition.Options)
                    {
                        if (option.Id != advised)
                        {
                            choice = option.Id;
                            break;
                        }
                    }
                }

                session.Submit(choice);
            }

            return session.Summary;
        }
    }
}
