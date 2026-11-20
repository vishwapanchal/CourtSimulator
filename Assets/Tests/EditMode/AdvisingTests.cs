using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CourtSim.Core.Advising;
using CourtSim.Core.Cases;
using CourtSim.Core.Decisions;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    public sealed class AdvisingTests
    {
        [TestCase(FlawKind.None, "none")]
        [TestCase(FlawKind.MisstatedLaw, "misstated_law")]
        [TestCase(FlawKind.MisreadFact, "misread_fact")]
        [TestCase(FlawKind.OmittedFactor, "omitted_factor")]
        [TestCase(FlawKind.ImproperFactor, "improper_factor")]
        public void FlawKindWireName_RoundTrips(FlawKind kind, string wireName)
        {
            Assert.That(FlawKinds.ToWireName(kind), Is.EqualTo(wireName));
            Assert.That(FlawKinds.TryParse(wireName, out FlawKind parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(kind));
            Assert.That(FlawKinds.Describe(kind), Is.Not.Empty);
        }

        [Test]
        public void FlawKinds_All_ListsEveryFlawButNone()
        {
            Assert.That(FlawKinds.All, Is.EqualTo(new[] { FlawKind.MisstatedLaw, FlawKind.MisreadFact, FlawKind.OmittedFactor, FlawKind.ImproperFactor }));
        }

        [Test]
        public void FlawKinds_UnknownValues_AreRejected()
        {
            Assert.That(FlawKinds.TryParse("Misstated_Law", out FlawKind kind), Is.False);
            Assert.That(kind, Is.EqualTo(FlawKind.None));
            Assert.That(FlawKinds.TryParse(null, out _), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => FlawKinds.ToWireName((FlawKind)42));
            Assert.Throws<ArgumentOutOfRangeException>(() => FlawKinds.Describe((FlawKind)42));
        }

        [TestCase(AdviceConfidence.Low, "low")]
        [TestCase(AdviceConfidence.Medium, "medium")]
        [TestCase(AdviceConfidence.High, "high")]
        public void ConfidenceWireName_RoundTrips(AdviceConfidence confidence, string wireName)
        {
            Assert.That(AdviceConfidences.ToWireName(confidence), Is.EqualTo(wireName));
            Assert.That(AdviceConfidences.TryParse(wireName, out AdviceConfidence parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(confidence));
        }

        [Test]
        public void Confidences_UnknownValues_AreRejected()
        {
            Assert.That(AdviceConfidences.TryParse("HIGH", out AdviceConfidence confidence), Is.False);
            Assert.That(confidence, Is.EqualTo(AdviceConfidence.Medium));
            Assert.Throws<ArgumentOutOfRangeException>(() => AdviceConfidences.ToWireName((AdviceConfidence)42));
        }

        [Test]
        public void Advice_WithoutOption_Throws()
        {
            Assert.Throws<ArgumentException>(() => new Advice(DecisionPointKind.Bail, "", "r", null, AdviceConfidence.Low, FlawKind.None, null, AdviceSource.Scripted));
            Assert.Throws<ArgumentException>(() => new Advice(DecisionPointKind.Bail, null, "r", null, AdviceConfidence.Low, FlawKind.None, null, AdviceSource.Scripted));
        }

        [Test]
        public void Advice_NullTexts_DefaultToEmptyAndFlagFlaw()
        {
            var sound = new Advice(DecisionPointKind.Bail, "a", null, null, AdviceConfidence.Low, FlawKind.None, null, AdviceSource.Scripted);
            var flawed = new Advice(DecisionPointKind.Bail, "a", "r", new[] { "f" }, AdviceConfidence.High, FlawKind.MisreadFact, "x", AdviceSource.Claude);

            Assert.That(sound.Rationale, Is.Empty);
            Assert.That(sound.FlawExplanation, Is.Empty);
            Assert.That(sound.CitedFactorIds, Is.Empty);
            Assert.That(sound.IsFlawed, Is.False);
            Assert.That(flawed.IsFlawed, Is.True);
            Assert.That(flawed.Source, Is.EqualTo(AdviceSource.Claude));
            Assert.That(flawed.Kind, Is.EqualTo(DecisionPointKind.Bail));
        }

        [Test]
        public void AdviceRequest_InvalidArguments_Throw()
        {
            CaseFile caseFile = TestCases.Load();

            Assert.Throws<ArgumentNullException>(() => new AdviceRequest(null, 0, FlawKind.None, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AdviceRequest(caseFile, -1, FlawKind.None, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AdviceRequest(caseFile, 3, FlawKind.None, null));
        }

        [Test]
        public void AdviceRequest_TargetScript_UsesPlannedFlaw()
        {
            var request = new AdviceRequest(TestCases.Load(), 1, FlawKind.OmittedFactor, null);

            Assert.That(request.Point.Kind, Is.EqualTo(DecisionPointKind.EvidenceAdmissibility));
            Assert.That(request.TargetScript.Flaw, Is.EqualTo(FlawKind.OmittedFactor));
            Assert.That(request.PriorRulings, Is.Empty);
        }

        [Test]
        public void AdviceRequest_UnauthoredFlaw_FallsBackToSoundScript()
        {
            var request = new AdviceRequest(TestCases.Load(), 0, FlawKind.MisstatedLaw, null);

            Assert.That(request.TargetScript.Flaw, Is.EqualTo(FlawKind.None));
            Assert.That(request.TargetScript.RecommendedOptionId, Is.EqualTo("bail.grant"));
        }

        [Test]
        public void AdvicePlan_NullFlaws_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new AdvicePlan(null));
        }

        [Test]
        public void AdvicePlan_FlawAt_ReturnsNoneOutOfRange()
        {
            var plan = new AdvicePlan(new[] { FlawKind.None, FlawKind.MisreadFact });

            Assert.That(plan.Count, Is.EqualTo(2));
            Assert.That(plan.FlawAt(1), Is.EqualTo(FlawKind.MisreadFact));
            Assert.That(plan.FlawAt(-1), Is.EqualTo(FlawKind.None));
            Assert.That(plan.FlawAt(2), Is.EqualTo(FlawKind.None));
            Assert.That(plan.FlawedCount, Is.EqualTo(1));
        }

        [Test]
        public void AdvicePlan_AllSound_HasNoFlaws()
        {
            AdvicePlan plan = AdvicePlan.AllSound(3);

            Assert.That(plan.Count, Is.EqualTo(3));
            Assert.That(plan.FlawedCount, Is.EqualTo(0));
        }

        [Test]
        public void Create_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => AdvicePlanner.Create(null, 1, new Random(1)));
            Assert.Throws<ArgumentNullException>(() => AdvicePlanner.Create(TestCases.Load(), 1, null));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void Create_FlawedCount_FlawsExactlyThatManyPoints(int flawedCount)
        {
            CaseFile caseFile = TestCases.Load();
            for (int seed = 0; seed < 50; seed++)
            {
                AdvicePlan plan = AdvicePlanner.Create(caseFile, flawedCount, new Random(seed));

                Assert.That(plan.Count, Is.EqualTo(3));
                Assert.That(plan.FlawedCount, Is.EqualTo(flawedCount));
                for (int i = 0; i < plan.Count; i++)
                {
                    FlawKind flaw = plan.FlawAt(i);
                    Assert.That(flaw == FlawKind.None || ContainsFlaw(caseFile.DecisionPoints[i].AvailableFlaws(), flaw), Is.True, $"seed {seed}, point {i}: {flaw}");
                }
            }
        }

        [TestCase(-5, 0)]
        [TestCase(4, 3)]
        [TestCase(100, 3)]
        public void Create_FlawedCountOutOfRange_IsClamped(int requested, int expected)
        {
            AdvicePlan plan = AdvicePlanner.Create(TestCases.Load(), requested, new Random(3));

            Assert.That(plan.FlawedCount, Is.EqualTo(expected));
        }

        [Test]
        public void Create_PointsWithoutFlawedVariants_AreNeverFlawed()
        {
            CaseFile full = TestCases.Load();
            CaseDecisionPoint original = full.DecisionPoints[1];
            var soundOnly = new CaseDecisionPoint(
                original.Definition,
                null,
                null,
                original.ExhibitId,
                original.Factors,
                original.Assessments,
                new[] { original.FindAdvice(FlawKind.None) });
            var caseFile = new CaseFile("c", "t", null, 1, null, full.Defendant, null, null, full.Exhibits, new[] { full.DecisionPoints[0], soundOnly, full.DecisionPoints[2] });

            for (int seed = 0; seed < 30; seed++)
            {
                AdvicePlan plan = AdvicePlanner.Create(caseFile, 3, new Random(seed));

                Assert.That(plan.FlawedCount, Is.EqualTo(2));
                Assert.That(plan.FlawAt(1), Is.EqualTo(FlawKind.None));
            }
        }

        [Test]
        public void Create_SameSeed_IsDeterministic()
        {
            CaseFile caseFile = TestCases.Load();
            for (int seed = 0; seed < 20; seed++)
            {
                AdvicePlan first = AdvicePlanner.Create(caseFile, 2, new Random(seed));
                AdvicePlan second = AdvicePlanner.Create(caseFile, 2, new Random(seed));

                for (int i = 0; i < 3; i++)
                {
                    Assert.That(second.FlawAt(i), Is.EqualTo(first.FlawAt(i)));
                }
            }
        }

        [Test]
        public void Create_DifferentSeeds_VaryTheChoice()
        {
            CaseFile caseFile = TestCases.Load();
            var flawedIndices = new HashSet<int>();
            for (int seed = 0; seed < 50; seed++)
            {
                AdvicePlan plan = AdvicePlanner.Create(caseFile, 1, new Random(seed));
                for (int i = 0; i < 3; i++)
                {
                    if (plan.FlawAt(i) != FlawKind.None)
                    {
                        flawedIndices.Add(i);
                    }
                }
            }

            Assert.That(flawedIndices.Count, Is.EqualTo(3), "Every point should be chosen for at least one seed.");
        }

        [Test]
        public void Create_ZeroWeight_ExcludesKindWherePossible()
        {
            CaseFile caseFile = TestCases.Load();
            var weights = new Dictionary<FlawKind, double> { { FlawKind.MisstatedLaw, 0 } };
            for (int seed = 0; seed < 100; seed++)
            {
                AdvicePlan plan = AdvicePlanner.Create(caseFile, 3, new Random(seed), weights);

                Assert.That(plan.FlawAt(1), Is.EqualTo(FlawKind.OmittedFactor));
            }
        }

        [Test]
        public void Create_NegativeWeight_IsTreatedAsZero()
        {
            CaseFile caseFile = TestCases.Load();
            var weights = new Dictionary<FlawKind, double> { { FlawKind.OmittedFactor, -3 } };
            for (int seed = 0; seed < 100; seed++)
            {
                Assert.That(AdvicePlanner.Create(caseFile, 3, new Random(seed), weights).FlawAt(1), Is.EqualTo(FlawKind.MisstatedLaw));
            }
        }

        [Test]
        public void Create_AllWeightsZero_StillPicksAnAuthoredFlaw()
        {
            CaseFile caseFile = TestCases.Load();
            var weights = new Dictionary<FlawKind, double>();
            foreach (FlawKind kind in FlawKinds.All)
            {
                weights[kind] = 0;
            }

            for (int seed = 0; seed < 30; seed++)
            {
                AdvicePlan plan = AdvicePlanner.Create(caseFile, 3, new Random(seed), weights);

                Assert.That(plan.FlawedCount, Is.EqualTo(3));
                Assert.That(plan.FlawAt(0), Is.EqualTo(FlawKind.MisreadFact));
                Assert.That(ContainsFlaw(caseFile.DecisionPoints[1].AvailableFlaws(), plan.FlawAt(1)), Is.True);
            }
        }

        [Test]
        public void Create_HeavyWeight_DominatesTheDraw()
        {
            CaseFile caseFile = TestCases.Load();
            var weights = new Dictionary<FlawKind, double> { { FlawKind.MisstatedLaw, 99 } };
            int misstated = 0;
            const int runs = 200;
            for (int seed = 0; seed < runs; seed++)
            {
                if (AdvicePlanner.Create(caseFile, 3, new Random(seed), weights).FlawAt(1) == FlawKind.MisstatedLaw)
                {
                    misstated++;
                }
            }

            Assert.That(misstated, Is.GreaterThan(runs * 9 / 10));
        }

        [Test]
        public void ScriptedAdvisor_ReturnsAuthoredAdviceForPlannedFlaw()
        {
            var request = new AdviceRequest(TestCases.Load(), 1, FlawKind.MisstatedLaw, null);

            Advice advice = new ScriptedAdvisor().GetAdviceAsync(request, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(advice.Kind, Is.EqualTo(DecisionPointKind.EvidenceAdmissibility));
            Assert.That(advice.RecommendedOptionId, Is.EqualTo("evidence.exclude"));
            Assert.That(advice.Flaw, Is.EqualTo(FlawKind.MisstatedLaw));
            Assert.That(advice.FlawExplanation, Is.EqualTo("Treats an unbroken chain as a precondition."));
            Assert.That(advice.Confidence, Is.EqualTo(AdviceConfidence.High));
            Assert.That(advice.CitedFactorIds, Is.EqualTo(new[] { "gap" }));
            Assert.That(advice.Rationale, Is.EqualTo("I would rule evidence.exclude."));
            Assert.That(advice.Source, Is.EqualTo(AdviceSource.Scripted));
        }

        [Test]
        public void ScriptedAdvisor_CancelledToken_Throws()
        {
            var request = new AdviceRequest(TestCases.Load(), 0, FlawKind.None, null);
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();

                Assert.Catch<OperationCanceledException>(() => new ScriptedAdvisor().GetAdviceAsync(request, cancellation.Token));
            }
        }

        [Test]
        public void ScriptedAdvisor_NoAuthoredAdvice_ThrowsAdvisorException()
        {
            AdviceRequest request = RequestWithoutAdvice();

            AdvisorException error = Assert.Throws<AdvisorException>(() => ScriptedAdvisor.Build(request));

            Assert.That(error.Message, Does.Contain("no authored advice"));
        }

        [Test]
        public void FallbackAdvisor_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new FallbackAdvisor(null, new ScriptedAdvisor()));
            Assert.Throws<ArgumentNullException>(() => new FallbackAdvisor(new ScriptedAdvisor(), null));
        }

        [Test]
        public void FallbackAdvisor_PrimarySucceeds_ReturnsPrimaryAdvice()
        {
            var expected = new Advice(DecisionPointKind.Bail, "bail.grant", "Primary.", null, AdviceConfidence.High, FlawKind.None, null, AdviceSource.Claude);
            StubAdvisor fallback = StubAdvisor.Failing(new InvalidOperationException("must not be called"));
            var advisor = new FallbackAdvisor(StubAdvisor.Returning(expected), fallback);
            int failures = 0;
            advisor.PrimaryFailed += error => failures++;

            Advice advice = advisor.GetAdviceAsync(new AdviceRequest(TestCases.Load(), 0, FlawKind.None, null), CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(advice, Is.SameAs(expected));
            Assert.That(fallback.Requests, Is.Empty);
            Assert.That(failures, Is.EqualTo(0));
            Assert.That(advisor.LastFailure, Is.Null);
        }

        [Test]
        public void FallbackAdvisor_PrimaryFaults_UsesFallbackAndRaisesPrimaryFailed()
        {
            var failure = new AdvisorException("network down");
            var advisor = new FallbackAdvisor(StubAdvisor.Failing(failure), new ScriptedAdvisor());
            var raised = new List<Exception>();
            advisor.PrimaryFailed += raised.Add;

            Advice advice = advisor.GetAdviceAsync(new AdviceRequest(TestCases.Load(), 0, FlawKind.MisreadFact, null), CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(advice.Source, Is.EqualTo(AdviceSource.Scripted));
            Assert.That(advice.Flaw, Is.EqualTo(FlawKind.MisreadFact));
            Assert.That(raised, Is.EqualTo(new Exception[] { failure }));
            Assert.That(advisor.LastFailure, Is.SameAs(failure));
        }

        [Test]
        public void FallbackAdvisor_PrimaryThrowsSynchronously_UsesFallback()
        {
            var primary = new StubAdvisor((request, token) => throw new InvalidOperationException("sync"));
            var advisor = new FallbackAdvisor(primary, new ScriptedAdvisor());

            Advice advice = advisor.GetAdviceAsync(new AdviceRequest(TestCases.Load(), 2, FlawKind.None, null), CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(advice.RecommendedOptionId, Is.EqualTo("sentence.community"));
            Assert.That(advisor.LastFailure.Message, Is.EqualTo("sync"));
        }

        [Test]
        public void FallbackAdvisor_PrimaryCancelledWithoutRequest_StillFallsBack()
        {
            var advisor = new FallbackAdvisor(StubAdvisor.Failing(new OperationCanceledException("timeout")), new ScriptedAdvisor());

            Advice advice = advisor.GetAdviceAsync(new AdviceRequest(TestCases.Load(), 0, FlawKind.None, null), CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(advice, Is.Not.Null);
            Assert.That(advisor.LastFailure, Is.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void FallbackAdvisor_CallerCancelled_RethrowsWithoutFallback()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                StubAdvisor fallback = StubAdvisor.Returning(null);
                var primary = new StubAdvisor((request, token) => Task.FromException<Advice>(new OperationCanceledException(token)));
                var advisor = new FallbackAdvisor(primary, fallback);
                int failures = 0;
                advisor.PrimaryFailed += error => failures++;
                var request = new AdviceRequest(TestCases.Load(), 0, FlawKind.None, null);

                Assert.Catch<OperationCanceledException>(() => advisor.GetAdviceAsync(request, cancellation.Token).GetAwaiter().GetResult());
                Assert.That(fallback.Requests, Is.Empty);
                Assert.That(failures, Is.EqualTo(0));
                Assert.That(advisor.LastFailure, Is.Null);
            }
        }

        internal static AdviceRequest RequestWithoutAdvice()
        {
            CaseFile full = TestCases.Load();
            CaseDecisionPoint original = full.DecisionPoints[0];
            var bare = new CaseDecisionPoint(original.Definition, null, null, null, original.Factors, original.Assessments, null);
            var caseFile = new CaseFile("bare", "Bare", null, 1, null, full.Defendant, null, null, null, new[] { bare });
            return new AdviceRequest(caseFile, 0, FlawKind.None, null);
        }

        private static bool ContainsFlaw(IReadOnlyList<FlawKind> flaws, FlawKind flaw)
        {
            foreach (FlawKind candidate in flaws)
            {
                if (candidate == flaw)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
