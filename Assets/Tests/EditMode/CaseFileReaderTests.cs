using System;
using System.IO;
using CourtSim.Core.Advising;
using CourtSim.Core.Cases;
using CourtSim.Core.Decisions;
using CourtSim.Core.Json;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    public sealed class CaseFileReaderTests
    {
        [TestCase("state-v-sahil-kadam")]
        [TestCase("state-v-mahesh-rathod")]
        [TestCase("state-v-kunal-bisht")]
        public void Read_ShippedCase_LoadsWithoutErrors(string caseId)
        {
            string path = TestCases.ShippedCasePath(caseId);
            Assert.That(File.Exists(path), Is.True, path);

            CaseReadResult result = CaseFileReader.Read(File.ReadAllText(path));

            Assert.That(result.Errors, Is.Empty, string.Join("\n", result.Errors));
            Assert.That(result.Success, Is.True);
            CaseFile caseFile = result.Case;
            Assert.That(caseFile.Id, Is.EqualTo(caseId));
            Assert.That(caseFile.DecisionPoints.Count, Is.EqualTo(3));
            for (int i = 0; i < 3; i++)
            {
                CaseDecisionPoint point = caseFile.DecisionPoints[i];
                Assert.That(point.Kind, Is.EqualTo(CaseFileReader.RequiredOrder[i]));
                Assert.That(point.FindAdvice(FlawKind.None), Is.Not.Null);
                Assert.That(point.AvailableFlaws().Count, Is.GreaterThan(0));
            }

            Assert.That(caseFile.FindExhibit(caseFile.DecisionPoints[1].ExhibitId), Is.Not.Null);
        }

        [Test]
        public void ShippedCaseIds_AllHaveFiles()
        {
            Assert.That(TestCases.ShippedCaseIds.Count, Is.EqualTo(21));
            foreach (string caseId in TestCases.ShippedCaseIds)
            {
                Assert.That(File.Exists(TestCases.ShippedCasePath(caseId)), Is.True, caseId);
            }
        }

        [Test]
        public void Read_TestCase_MapsEveryField()
        {
            CaseFile caseFile = TestCases.Load();

            Assert.That(caseFile.Id, Is.EqualTo(TestCases.CaseId));
            Assert.That(caseFile.Title, Is.EqualTo("State v. Alex Doe"));
            Assert.That(caseFile.Summary, Is.EqualTo("A test burglary."));
            Assert.That(caseFile.Difficulty, Is.EqualTo(2));
            Assert.That(caseFile.JurisdictionNote, Is.EqualTo("Fictional jurisdiction."));
            Assert.That(caseFile.Defendant.Name, Is.EqualTo("Alex Doe"));
            Assert.That(caseFile.Defendant.Age, Is.EqualTo(30));
            Assert.That(caseFile.Defendant.Background, Is.EqualTo("Works locally."));
            Assert.That(caseFile.Charges, Is.EqualTo(new[] { "Burglary" }));
            Assert.That(caseFile.Facts.Count, Is.EqualTo(2));
            Assert.That(caseFile.Facts[1].Id, Is.EqualTo("F2"));
            Assert.That(caseFile.Facts[1].Text, Is.EqualTo("Laptops were found in his car."));

            Exhibit exhibit = caseFile.FindExhibit("P-1");
            Assert.That(exhibit.Title, Is.EqualTo("CCTV recording"));
            Assert.That(exhibit.Description, Is.EqualTo("Footage of the rear door."));
            Assert.That(exhibit.TenderedBy, Is.EqualTo("Prosecution"));
            Assert.That(exhibit.Provenance, Is.EqualTo("Exported to a USB drive."));
            Assert.That(exhibit.Objection, Is.EqualTo("The chain of custody is broken."));
            Assert.That(caseFile.FindExhibit("P-9"), Is.Null);
        }

        [Test]
        public void Read_TestCase_MapsDecisionPoints()
        {
            CaseFile caseFile = TestCases.Load();
            CaseDecisionPoint bail = caseFile.DecisionPoints[0];
            CaseDecisionPoint evidence = caseFile.DecisionPoints[1];
            CaseDecisionPoint sentencing = caseFile.DecisionPoints[2];

            Assert.That(bail.Kind, Is.EqualTo(DecisionPointKind.Bail));
            Assert.That(bail.Definition.Title, Is.EqualTo("Bail application"));
            Assert.That(bail.Definition.Prompt, Is.EqualTo("The accused applies for bail."));
            Assert.That(bail.ProsecutionSubmission, Is.EqualTo("Prosecution says bail."));
            Assert.That(bail.DefenceSubmission, Is.EqualTo("Defence says bail."));
            Assert.That(bail.ExhibitId, Is.Null);
            Assert.That(evidence.ExhibitId, Is.EqualTo("P-1"));
            Assert.That(bail.Definition.Options.Count, Is.EqualTo(2));
            Assert.That(evidence.Definition.Options.Count, Is.EqualTo(3));

            Assert.That(bail.Assess("bail.grant").Quality, Is.EqualTo(OptionQuality.Sound));
            Assert.That(bail.Assess("bail.deny").Quality, Is.EqualTo(OptionQuality.Unsound));
            Assert.That(bail.Assess("bail.grant").Feedback, Is.EqualTo("Feedback for bail.grant."));
            Assert.That(evidence.Assess("evidence.admit_limited").Quality, Is.EqualTo(OptionQuality.Defensible));
            Assert.That(bail.Assess("nope"), Is.Null);
            Assert.That(bail.Assess(null), Is.Null);

            Assert.That(bail.FindFactor("ties").Text, Is.EqualTo("Factor ties."));
            Assert.That(bail.FindFactor("hash"), Is.Null);

            AdviceScript sound = bail.FindAdvice(FlawKind.None);
            Assert.That(sound.RecommendedOptionId, Is.EqualTo("bail.grant"));
            Assert.That(sound.Confidence, Is.EqualTo(AdviceConfidence.High));
            Assert.That(sound.CitedFactorIds, Is.EqualTo(new[] { "ties", "record" }));
            Assert.That(sound.Rationale, Is.EqualTo("I would rule bail.grant."));
            Assert.That(sound.FlawExplanation, Is.Empty);
            Assert.That(bail.FindAdvice(FlawKind.MisstatedLaw), Is.Null);
            Assert.That(bail.AvailableFlaws(), Is.EqualTo(new[] { FlawKind.MisreadFact }));
            Assert.That(evidence.AvailableFlaws(), Is.EqualTo(new[] { FlawKind.MisstatedLaw, FlawKind.OmittedFactor }));

            AdviceScript improper = sentencing.FindAdvice(FlawKind.ImproperFactor);
            Assert.That(improper.Confidence, Is.EqualTo(AdviceConfidence.Medium), "Missing confidence defaults to medium.");
            Assert.That(improper.FlawExplanation, Is.EqualTo("Relies on the accused's silence."));
        }

        [Test]
        public void CreateSequence_FollowsDecisionPointOrder()
        {
            DecisionSequence sequence = TestCases.Load().CreateSequence();

            Assert.That(sequence.Count, Is.EqualTo(3));
            Assert.That(sequence.Current.Kind, Is.EqualTo(DecisionPointKind.Bail));
            sequence.Record("bail.grant");
            Assert.That(sequence.Current.Kind, Is.EqualTo(DecisionPointKind.EvidenceAdmissibility));
        }

        [Test]
        public void Read_InvalidJson_ReturnsSingleParseError()
        {
            CaseReadResult result = CaseFileReader.Read("{\"schema\": ");

            Assert.That(result.Success, Is.False);
            Assert.That(result.Case, Is.Null);
            Assert.That(result.Errors.Count, Is.EqualTo(1));
            Assert.That(result.Errors[0], Does.StartWith("Invalid JSON: "));
            Assert.That(result.Errors[0], Does.Contain("at character"));
        }

        [Test]
        public void Read_NullText_ReturnsParseError()
        {
            CaseReadResult result = CaseFileReader.Read(null);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Errors[0], Does.StartWith("Invalid JSON: "));
        }

        [Test]
        public void Read_RootNotObject_ReportsRoot()
        {
            CaseReadResult result = CaseFileReader.Read("[]");

            Assert.That(result.Errors, Is.EqualTo(new[] { "$: must be a JSON object" }));
        }

        [Test]
        public void Read_WrongSchema_ReportsSchema()
        {
            JsonValue root = TestCases.BuildJson().Set("schema", "courtsim.case/2");

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "schema", "courtsim.case/1");
        }

        [Test]
        public void Read_MissingIdAndTitle_ReportsEach()
        {
            JsonValue root = TestCases.BuildJson().Set("id", "  ").Set("title", JsonValue.Null);

            CaseReadResult result = CaseFileReader.Read(root.ToJson());

            TestCases.AssertHasError(result, "id", "is required");
            TestCases.AssertHasError(result, "title", "is required");
        }

        [TestCase(0d)]
        [TestCase(4d)]
        [TestCase(2.5d)]
        public void Read_DifficultyOutOfRange_ReportsDifficulty(double difficulty)
        {
            JsonValue root = TestCases.BuildJson().Set("difficulty", difficulty);

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "difficulty", "integer from 1 to 3");
        }

        [Test]
        public void Read_MissingDefendant_ReportsDefendant()
        {
            JsonValue root = TestCases.BuildJson().Set("defendant", JsonValue.Null);

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "defendant", "must be an object");
        }

        [TestCase(5d)]
        [TestCase(30.5d)]
        [TestCase(200d)]
        public void Read_ImplausibleDefendantAge_ReportsAge(double age)
        {
            JsonValue root = TestCases.BuildJson();
            root.Get("defendant").Set("age", age);

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "defendant.age", "whole number");
        }

        [Test]
        public void Read_ChargesEmptyOrBlank_ReportsCharges()
        {
            JsonValue empty = TestCases.BuildJson().Set("charges", JsonValue.NewArray());
            JsonValue blank = TestCases.BuildJson().Set("charges", JsonValue.StringArray(new[] { " " }));
            JsonValue notArray = TestCases.BuildJson().Set("charges", "Burglary");
            JsonValue missing = TestCases.BuildJson().Set("charges", JsonValue.Null);

            TestCases.AssertHasError(CaseFileReader.Read(empty.ToJson()), "charges", "must not be empty");
            TestCases.AssertHasError(CaseFileReader.Read(blank.ToJson()), "charges[0]", "non-empty string");
            TestCases.AssertHasError(CaseFileReader.Read(notArray.ToJson()), "charges", "must be an array");
            TestCases.AssertHasError(CaseFileReader.Read(missing.ToJson()), "charges", "is required");
        }

        [Test]
        public void Read_FactNotObject_ReportsElement()
        {
            JsonValue root = TestCases.BuildJson().Set("facts", JsonValue.StringArray(new[] { "a fact" }));

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "facts[0]", "must be an object");
        }

        [Test]
        public void Read_DuplicateFactId_ReportsDuplicate()
        {
            JsonValue root = TestCases.BuildJson();
            root.Get("facts").Items[1].Set("id", "F1");

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "facts[1].id", "duplicates fact id 'F1'");
        }

        [Test]
        public void Read_DuplicateExhibitId_ReportsDuplicate()
        {
            JsonValue root = TestCases.BuildJson();
            root.Get("exhibits").Add(JsonValue.NewObject().Set("id", "P-1").Set("title", "Copy"));

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "exhibits[1].id", "duplicates exhibit id 'P-1'");
        }

        [Test]
        public void Read_ExhibitWithoutTitle_ReportsTitle()
        {
            JsonValue root = TestCases.BuildJson();
            root.Get("exhibits").Items[0].Set("title", string.Empty);

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "exhibits[0].title", "is required");
        }

        [Test]
        public void Read_WrongNumberOfDecisionPoints_ReportsCount()
        {
            JsonValue root = TestCases.BuildJson();
            JsonValue points = root.Get("decisionPoints");
            root.Set("decisionPoints", TestCases.ArrayOf(points.Items[0], points.Items[1]));

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "decisionPoints", "exactly 3");
        }

        [Test]
        public void Read_MissingDecisionPoints_ReportsRequired()
        {
            JsonValue root = TestCases.BuildJson().Set("decisionPoints", JsonValue.Null);

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "decisionPoints", "is required");
        }

        [Test]
        public void Read_DecisionPointNotObject_ReportsElement()
        {
            JsonValue root = TestCases.BuildJson();
            JsonValue points = root.Get("decisionPoints");
            root.Set("decisionPoints", TestCases.ArrayOf(JsonValue.Number(1), points.Items[1], points.Items[2]));

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "decisionPoints[0]", "must be an object");
        }

        [Test]
        public void Read_KindsOutOfOrder_ReportsExpectedKind()
        {
            JsonValue root = TestCases.BuildJson();
            JsonValue points = root.Get("decisionPoints");
            root.Set("decisionPoints", TestCases.ArrayOf(points.Items[2], points.Items[1], points.Items[0]));

            CaseReadResult result = CaseFileReader.Read(root.ToJson());

            TestCases.AssertHasError(result, "decisionPoints[0].kind", "must be 'bail'");
            TestCases.AssertHasError(result, "decisionPoints[2].kind", "must be 'sentencing'");
        }

        [Test]
        public void Read_UnknownKind_ReportsKind()
        {
            JsonValue root = TestCases.BuildJson();
            TestCases.PointAt(root, 2).Set("kind", "arraignment");

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "decisionPoints[2].kind", "'arraignment' is not one of");
        }

        [Test]
        public void Read_MissingPointTitleAndPrompt_ReportsEach()
        {
            JsonValue root = TestCases.BuildJson();
            TestCases.PointAt(root, 0).Set("title", JsonValue.Null).Set("prompt", string.Empty);

            CaseReadResult result = CaseFileReader.Read(root.ToJson());

            TestCases.AssertHasError(result, "decisionPoints[0].title", "is required");
            TestCases.AssertHasError(result, "decisionPoints[0].prompt", "is required");
        }

        [Test]
        public void Read_EvidencePointWithoutExhibit_ReportsExhibitRequired()
        {
            JsonValue root = TestCases.BuildJson();
            TestCases.PointAt(root, 1).Set("exhibitId", JsonValue.Null);

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "decisionPoints[1].exhibitId", "is required for an evidence_admissibility");
        }

        [Test]
        public void Read_UnknownExhibit_ReportsExhibitId()
        {
            JsonValue root = TestCases.BuildJson();
            TestCases.PointAt(root, 0).Set("exhibitId", "P-9");

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "decisionPoints[0].exhibitId", "unknown exhibit 'P-9'");
        }

        [Test]
        public void Read_SingleOption_ReportsAtLeastTwo()
        {
            JsonValue root = TestCases.BuildJson();
            JsonValue point = TestCases.PointAt(root, 0);
            point.Set("options", TestCases.ArrayOf(point.Get("options").Items[0]));

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "decisionPoints[0].options", "at least two options");
        }

        [Test]
        public void Read_NoSoundOption_ReportsMissingSound()
        {
            JsonValue root = TestCases.BuildJson();
            TestCases.OptionAt(root, 0, 0).Set("quality", "defensible");

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "decisionPoints[0].options", "at least one option with quality 'sound'");
        }

        [Test]
        public void Read_UnknownQuality_ReportsQuality()
        {
            JsonValue root = TestCases.BuildJson();
            TestCases.OptionAt(root, 0, 1).Set("quality", "terrible");

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "decisionPoints[0].options[1].quality", "'terrible' is not one of");
        }

        [Test]
        public void Read_DuplicateOptionId_ReportsDuplicate()
        {
            JsonValue root = TestCases.BuildJson();
            TestCases.OptionAt(root, 1, 2).Set("id", "evidence.admit");

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "decisionPoints[1].options[2].id", "duplicates option id 'evidence.admit'");
        }

        [Test]
        public void Read_OptionWithoutLabel_ReportsLabel()
        {
            JsonValue root = TestCases.BuildJson();
            TestCases.OptionAt(root, 1, 1).Set("label", JsonValue.Null);

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "decisionPoints[1].options[1].label", "is required");
        }

        [Test]
        public void Read_DuplicateFactorId_ReportsDuplicate()
        {
            JsonValue root = TestCases.BuildJson();
            TestCases.PointAt(root, 0).Get("factors").Items[2].Set("id", "ties");

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "decisionPoints[0].factors[2].id", "duplicates factor id 'ties'");
        }

        [Test]
        public void Read_MissingFactors_ReportsRequired()
        {
            JsonValue root = TestCases.BuildJson();
            TestCases.PointAt(root, 2).Set("factors", JsonValue.NewArray());

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "decisionPoints[2].factors", "must not be empty");
        }

        [Test]
        public void Read_AdviceRecommendsUnknownOption_ReportsRecommends()
        {
            JsonValue root = TestCases.BuildJson();
            TestCases.AdviceAt(root, 0, 0).Set("recommends", "bail.maybe");

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "decisionPoints[0].advice[0].recommends", "unknown option 'bail.maybe'");
        }

        [Test]
        public void Read_SoundAdviceRecommendsNonSoundOption_ReportsRecommends()
        {
            JsonValue root = TestCases.BuildJson();
            TestCases.AdviceAt(root, 0, 0).Set("recommends", "bail.deny");

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "decisionPoints[0].advice[0].recommends", "sound advice (flaw 'none') must recommend");
        }

        [Test]
        public void Read_FlawedAdviceRecommendsSoundOption_ReportsRecommends()
        {
            JsonValue root = TestCases.BuildJson();
            TestCases.AdviceAt(root, 1, 2).Set("recommends", "evidence.admit");

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "decisionPoints[1].advice[2].recommends", "flawed advice must recommend an option that is not graded 'sound'");
        }

        [Test]
        public void Read_FlawedAdviceWithoutExplanation_ReportsFlawExplanation()
        {
            JsonValue root = TestCases.BuildJson();
            TestCases.AdviceAt(root, 0, 1).Set("flawExplanation", "   ");
            TestCases.AdviceAt(root, 2, 1).Set("flawExplanation", JsonValue.Null);

            CaseReadResult result = CaseFileReader.Read(root.ToJson());

            TestCases.AssertHasError(result, "decisionPoints[0].advice[1].flawExplanation", "is required for flawed advice");
            TestCases.AssertHasError(result, "decisionPoints[2].advice[1].flawExplanation", "is required for flawed advice");
        }

        [Test]
        public void Read_AdviceCitesUnknownFactor_ReportsCites()
        {
            JsonValue root = TestCases.BuildJson();
            TestCases.AdviceAt(root, 0, 0).Set("cites", JsonValue.StringArray(new[] { "ties", "hash" }));

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "decisionPoints[0].advice[0].cites", "unknown factor 'hash'");
        }

        [Test]
        public void Read_AdviceWithoutCites_IsAllowed()
        {
            JsonValue root = TestCases.BuildJson();
            TestCases.AdviceAt(root, 0, 0).Set("cites", JsonValue.Null);

            CaseFile caseFile = TestCases.LoadJson(root.ToJson());

            Assert.That(caseFile.DecisionPoints[0].FindAdvice(FlawKind.None).CitedFactorIds, Is.Empty);
        }

        [Test]
        public void Read_UnknownFlawKind_ReportsFlaw()
        {
            JsonValue root = TestCases.BuildJson();
            TestCases.AdviceAt(root, 1, 1).Set("flaw", "lazy_reasoning");

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "decisionPoints[1].advice[1].flaw", "'lazy_reasoning' is not a known flaw kind");
        }

        [Test]
        public void Read_DuplicateAdviceFlaw_ReportsDuplicate()
        {
            JsonValue root = TestCases.BuildJson();
            TestCases.AdviceAt(root, 1, 2).Set("flaw", "misstated_law");

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "decisionPoints[1].advice[2].flaw", "duplicates advice for flaw 'misstated_law'");
        }

        [Test]
        public void Read_UnknownConfidence_ReportsConfidence()
        {
            JsonValue root = TestCases.BuildJson();
            TestCases.AdviceAt(root, 0, 0).Set("confidence", "certain");

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "decisionPoints[0].advice[0].confidence", "'certain' is not one of");
        }

        [Test]
        public void Read_AdviceWithoutRationale_ReportsRationale()
        {
            JsonValue root = TestCases.BuildJson();
            TestCases.AdviceAt(root, 0, 0).Set("rationale", string.Empty);

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "decisionPoints[0].advice[0].rationale", "is required");
        }

        [Test]
        public void Read_NoSoundAdvice_ReportsMissingSoundAdvice()
        {
            JsonValue root = TestCases.BuildJson();
            JsonValue point = TestCases.PointAt(root, 0);
            point.Set("advice", TestCases.ArrayOf(point.Get("advice").Items[1]));

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "decisionPoints[0].advice", "must include sound advice");
        }

        [Test]
        public void Read_NoFlawedAdvice_ReportsMissingFlawedVariant()
        {
            JsonValue root = TestCases.BuildJson();
            JsonValue point = TestCases.PointAt(root, 2);
            point.Set("advice", TestCases.ArrayOf(point.Get("advice").Items[0]));

            TestCases.AssertHasError(CaseFileReader.Read(root.ToJson()), "decisionPoints[2].advice", "at least one flawed variant");
        }

        [Test]
        public void Read_ManyProblems_ReportsAllOfThem()
        {
            JsonValue root = TestCases.BuildJson().Set("schema", "x").Set("difficulty", 9);
            TestCases.AdviceAt(root, 0, 0).Set("recommends", "bail.deny");
            TestCases.PointAt(root, 1).Set("exhibitId", JsonValue.Null);

            CaseReadResult result = CaseFileReader.Read(root.ToJson());

            Assert.That(result.Errors.Count, Is.GreaterThanOrEqualTo(4));
            TestCases.AssertHasError(result, "schema", "must be");
            TestCases.AssertHasError(result, "difficulty", "must be");
            TestCases.AssertHasError(result, "decisionPoints[0].advice[0].recommends", "sound advice");
            TestCases.AssertHasError(result, "decisionPoints[1].exhibitId", "is required");
        }

        [TestCase(DecisionPointKind.Bail, "bail")]
        [TestCase(DecisionPointKind.EvidenceAdmissibility, "evidence_admissibility")]
        [TestCase(DecisionPointKind.Sentencing, "sentencing")]
        public void KindWireName_RoundTrips(DecisionPointKind kind, string wireName)
        {
            Assert.That(CaseFileReader.KindToWireName(kind), Is.EqualTo(wireName));
            Assert.That(CaseFileReader.TryParseKind(wireName, out DecisionPointKind parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(kind));
        }

        [Test]
        public void KindWireName_UnknownValues_AreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CaseFileReader.KindToWireName((DecisionPointKind)99));
            Assert.That(CaseFileReader.TryParseKind("Bail", out _), Is.False);
            Assert.That(CaseFileReader.TryParseKind(null, out _), Is.False);
        }

        [TestCase("sound", OptionQuality.Sound)]
        [TestCase("defensible", OptionQuality.Defensible)]
        [TestCase("unsound", OptionQuality.Unsound)]
        public void TryParseQuality_KnownName_ReturnsQuality(string wireName, OptionQuality expected)
        {
            Assert.That(CaseFileReader.TryParseQuality(wireName, out OptionQuality quality), Is.True);
            Assert.That(quality, Is.EqualTo(expected));
        }

        [Test]
        public void TryParseQuality_UnknownName_ReturnsFalse()
        {
            Assert.That(CaseFileReader.TryParseQuality("Sound", out OptionQuality quality), Is.False);
            Assert.That(quality, Is.EqualTo(OptionQuality.Unsound));
        }

        [Test]
        public void Model_NullArguments_DefaultToEmpty()
        {
            var exhibit = new Exhibit("E", "T", null, null, null, null);
            var script = new AdviceScript(FlawKind.None, "a", AdviceConfidence.Low, null, null, null);
            var defendant = new Defendant("N", 40, null);
            var assessment = new OptionAssessment("a", OptionQuality.Sound, null);
            var caseFile = new CaseFile("id", "t", null, 1, null, defendant, null, null, null, null);

            Assert.That(exhibit.Description + exhibit.TenderedBy + exhibit.Provenance + exhibit.Objection, Is.Empty);
            Assert.That(script.Rationale + script.FlawExplanation, Is.Empty);
            Assert.That(script.CitedFactorIds, Is.Empty);
            Assert.That(defendant.Background, Is.Empty);
            Assert.That(assessment.Feedback, Is.Empty);
            Assert.That(caseFile.Summary + caseFile.JurisdictionNote, Is.Empty);
            Assert.That(caseFile.Charges, Is.Empty);
            Assert.That(caseFile.Facts, Is.Empty);
            Assert.That(caseFile.Exhibits, Is.Empty);
            Assert.That(caseFile.DecisionPoints, Is.Empty);
            Assert.Throws<ArgumentNullException>(() => new CaseDecisionPoint(null, null, null, null, null, null, null));
        }
    }
}
