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

            var ids = new List<string>();
            foreach (CaseFile caseFile in cases)
            {
                ids.Add(caseFile.Id);
            }

            Assert.That(ids, Is.EqualTo(new[] { "hale-burglary", "raman-assault", "okafor-supply" }));
            Assert.That(cases[0].Difficulty, Is.EqualTo(1));
            Assert.That(cases[1].Difficulty, Is.EqualTo(2));
            Assert.That(cases[2].Difficulty, Is.EqualTo(3));
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
