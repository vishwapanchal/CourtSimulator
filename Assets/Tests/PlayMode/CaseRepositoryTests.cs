using System.Collections.Generic;
using CourtSim.Core.Cases;
using CourtSim.Runtime.Cases;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace CourtSim.Runtime.Tests
{
    public sealed class CaseRepositoryTests
    {
        [Test]
        public void LoadAll_ShippedCases_AreSortedByDifficultyThenId()
        {
            IReadOnlyList<CaseFile> cases = CaseRepository.LoadAll();

            // 21 cases, seven at each difficulty, in order of difficulty and then id.
            Assert.That(cases.Count, Is.EqualTo(21));
            var perDifficulty = new int[4];
            for (int i = 0; i < cases.Count; i++)
            {
                perDifficulty[cases[i].Difficulty]++;
                if (i > 0)
                {
                    CaseFile previous = cases[i - 1];
                    bool ordered = previous.Difficulty < cases[i].Difficulty
                        || (previous.Difficulty == cases[i].Difficulty && string.CompareOrdinal(previous.Id, cases[i].Id) < 0);
                    Assert.That(ordered, Is.True, $"{previous.Id} should come before {cases[i].Id}");
                }
            }

            Assert.That(perDifficulty, Is.EqualTo(new[] { 0, 7, 7, 7 }));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LoadAll_ShippedCases_EachHaveThreeDecisionPointsAndAContestedExhibit()
        {
            foreach (CaseFile caseFile in CaseRepository.LoadAll())
            {
                Assert.That(caseFile.DecisionPoints.Count, Is.EqualTo(3), caseFile.Id);
                Assert.That(caseFile.Exhibits.Count, Is.GreaterThan(0), caseFile.Id);

                bool hasContestedExhibit = false;
                foreach (CaseDecisionPoint point in caseFile.DecisionPoints)
                {
                    if (point.ExhibitId != null)
                    {
                        Assert.That(caseFile.FindExhibit(point.ExhibitId), Is.Not.Null, caseFile.Id);
                        hasContestedExhibit = true;
                    }
                }

                Assert.That(hasContestedExhibit, Is.True, caseFile.Id);
            }
        }
    }
}
