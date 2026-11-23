using CourtSim.Core.Decisions;
using CourtSim.Core.Sessions;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    public class HearingPhaseTests
    {
        [Test]
        public void Number_IsOneBased()
        {
            Assert.That(HearingPhase.Number(0, 3), Is.EqualTo("PHASE 1 OF 3"));
            Assert.That(HearingPhase.Number(2, 3), Is.EqualTo("PHASE 3 OF 3"));
        }

        [TestCase(DecisionPointKind.Bail, "BAIL HEARING", "Before trial")]
        [TestCase(DecisionPointKind.EvidenceAdmissibility, "EVIDENCE HEARING", "During the trial")]
        [TestCase(DecisionPointKind.Sentencing, "SENTENCING HEARING", "After conviction")]
        public void EachKind_HasTitleAndStage(DecisionPointKind kind, string title, string stageStart)
        {
            Assert.That(HearingPhase.Title(kind), Is.EqualTo(title));
            Assert.That(HearingPhase.Stage(kind), Does.StartWith(stageStart));
        }
    }
}
