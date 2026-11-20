using System;
using System.Collections.Generic;
using CourtSim.Core.Advising;
using CourtSim.Core.Decisions;

namespace CourtSim.Core.Cases
{
    /// <summary>How the case author grades a ruling. This is the ground truth decision logs are scored against.</summary>
    public enum OptionQuality
    {
        /// <summary>The ruling the record and the law best support.</summary>
        Sound,

        /// <summary>A ruling a reasonable judge could make, but weaker than the sound one.</summary>
        Defensible,

        /// <summary>A ruling the record or the law does not support.</summary>
        Unsound,
    }

    public sealed class Defendant
    {
        public Defendant(string name, int age, string background)
        {
            Name = name;
            Age = age;
            Background = background ?? string.Empty;
        }

        public string Name { get; }

        public int Age { get; }

        public string Background { get; }
    }

    /// <summary>A numbered fact from the case record.</summary>
    public sealed class CaseFact
    {
        public CaseFact(string id, string text)
        {
            Id = id;
            Text = text;
        }

        public string Id { get; }

        public string Text { get; }
    }

    /// <summary>An item of evidence shown in the evidence viewer.</summary>
    public sealed class Exhibit
    {
        public Exhibit(string id, string title, string description, string tenderedBy, string provenance, string objection)
        {
            Id = id;
            Title = title;
            Description = description ?? string.Empty;
            TenderedBy = tenderedBy ?? string.Empty;
            Provenance = provenance ?? string.Empty;
            Objection = objection ?? string.Empty;
        }

        /// <summary>Exhibit number as read in court, e.g. "P-1".</summary>
        public string Id { get; }

        public string Title { get; }

        public string Description { get; }

        public string TenderedBy { get; }

        /// <summary>How the exhibit was obtained and handled.</summary>
        public string Provenance { get; }

        /// <summary>The defence's objection, if any.</summary>
        public string Objection { get; }
    }

    /// <summary>A consideration relevant to one decision point. Advice cites factors by id.</summary>
    public sealed class CaseFactor
    {
        public CaseFactor(string id, string text)
        {
            Id = id;
            Text = text;
        }

        public string Id { get; }

        public string Text { get; }
    }

    /// <summary>The author's grading of one option, with the feedback shown in the debrief.</summary>
    public sealed class OptionAssessment
    {
        public OptionAssessment(string optionId, OptionQuality quality, string feedback)
        {
            OptionId = optionId;
            Quality = quality;
            Feedback = feedback ?? string.Empty;
        }

        public string OptionId { get; }

        public OptionQuality Quality { get; }

        public string Feedback { get; }
    }

    /// <summary>
    /// Authored advice for one decision point: the sound advice, or one deliberately flawed variant.
    /// Used verbatim by the offline advisor and as the target ruling for generated advice.
    /// </summary>
    public sealed class AdviceScript
    {
        public AdviceScript(
            FlawKind flaw,
            string recommendedOptionId,
            AdviceConfidence confidence,
            string rationale,
            IReadOnlyList<string> citedFactorIds,
            string flawExplanation)
        {
            Flaw = flaw;
            RecommendedOptionId = recommendedOptionId;
            Confidence = confidence;
            Rationale = rationale ?? string.Empty;
            CitedFactorIds = citedFactorIds ?? Array.Empty<string>();
            FlawExplanation = flawExplanation ?? string.Empty;
        }

        public FlawKind Flaw { get; }

        public string RecommendedOptionId { get; }

        public AdviceConfidence Confidence { get; }

        public string Rationale { get; }

        public IReadOnlyList<string> CitedFactorIds { get; }

        /// <summary>Debrief text naming the flaw. Empty for sound advice.</summary>
        public string FlawExplanation { get; }
    }

    /// <summary>A decision point with everything the case author supplied around it.</summary>
    public sealed class CaseDecisionPoint
    {
        private readonly Dictionary<string, OptionAssessment> _assessments;

        public CaseDecisionPoint(
            DecisionPointDefinition definition,
            string prosecutionSubmission,
            string defenceSubmission,
            string exhibitId,
            IReadOnlyList<CaseFactor> factors,
            IReadOnlyList<OptionAssessment> assessments,
            IReadOnlyList<AdviceScript> advice)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            ProsecutionSubmission = prosecutionSubmission ?? string.Empty;
            DefenceSubmission = defenceSubmission ?? string.Empty;
            ExhibitId = string.IsNullOrEmpty(exhibitId) ? null : exhibitId;
            Factors = factors ?? Array.Empty<CaseFactor>();
            Assessments = assessments ?? Array.Empty<OptionAssessment>();
            Advice = advice ?? Array.Empty<AdviceScript>();
            _assessments = new Dictionary<string, OptionAssessment>(StringComparer.Ordinal);
            foreach (OptionAssessment assessment in Assessments)
            {
                _assessments[assessment.OptionId] = assessment;
            }
        }

        public DecisionPointDefinition Definition { get; }

        public DecisionPointKind Kind => Definition.Kind;

        public string ProsecutionSubmission { get; }

        public string DefenceSubmission { get; }

        /// <summary>The exhibit this ruling concerns, or null.</summary>
        public string ExhibitId { get; }

        public IReadOnlyList<CaseFactor> Factors { get; }

        public IReadOnlyList<OptionAssessment> Assessments { get; }

        public IReadOnlyList<AdviceScript> Advice { get; }

        /// <summary>The assessment for <paramref name="optionId"/>, or null if the option is unknown.</summary>
        public OptionAssessment Assess(string optionId)
        {
            return optionId != null && _assessments.TryGetValue(optionId, out OptionAssessment assessment) ? assessment : null;
        }

        /// <summary>The authored advice with flaw <paramref name="flaw"/>, or null.</summary>
        public AdviceScript FindAdvice(FlawKind flaw)
        {
            foreach (AdviceScript script in Advice)
            {
                if (script.Flaw == flaw)
                {
                    return script;
                }
            }

            return null;
        }

        /// <summary>Flaw kinds this point has authored advice for, excluding <see cref="FlawKind.None"/>.</summary>
        public IReadOnlyList<FlawKind> AvailableFlaws()
        {
            var flaws = new List<FlawKind>();
            foreach (AdviceScript script in Advice)
            {
                if (script.Flaw != FlawKind.None && !flaws.Contains(script.Flaw))
                {
                    flaws.Add(script.Flaw);
                }
            }

            return flaws;
        }

        public CaseFactor FindFactor(string id)
        {
            foreach (CaseFactor factor in Factors)
            {
                if (factor.Id == id)
                {
                    return factor;
                }
            }

            return null;
        }
    }

    /// <summary>One training case: the record, the exhibits and the three decision points, as loaded from JSON.</summary>
    public sealed class CaseFile
    {
        public const string SchemaId = "courtsim.case/1";

        public CaseFile(
            string id,
            string title,
            string summary,
            int difficulty,
            string jurisdictionNote,
            Defendant defendant,
            IReadOnlyList<string> charges,
            IReadOnlyList<CaseFact> facts,
            IReadOnlyList<Exhibit> exhibits,
            IReadOnlyList<CaseDecisionPoint> decisionPoints)
        {
            Id = id;
            Title = title;
            Summary = summary ?? string.Empty;
            Difficulty = difficulty;
            JurisdictionNote = jurisdictionNote ?? string.Empty;
            Defendant = defendant;
            Charges = charges ?? Array.Empty<string>();
            Facts = facts ?? Array.Empty<CaseFact>();
            Exhibits = exhibits ?? Array.Empty<Exhibit>();
            DecisionPoints = decisionPoints ?? Array.Empty<CaseDecisionPoint>();
        }

        public string Id { get; }

        public string Title { get; }

        public string Summary { get; }

        /// <summary>1 (introductory) to 3 (advanced). Input to adaptive case selection.</summary>
        public int Difficulty { get; }

        /// <summary>States that the jurisdiction is fictional; shown with the case brief.</summary>
        public string JurisdictionNote { get; }

        public Defendant Defendant { get; }

        public IReadOnlyList<string> Charges { get; }

        public IReadOnlyList<CaseFact> Facts { get; }

        public IReadOnlyList<Exhibit> Exhibits { get; }

        public IReadOnlyList<CaseDecisionPoint> DecisionPoints { get; }

        public Exhibit FindExhibit(string id)
        {
            foreach (Exhibit exhibit in Exhibits)
            {
                if (exhibit.Id == id)
                {
                    return exhibit;
                }
            }

            return null;
        }

        /// <summary>A fresh sequence over this case's decision points, in order.</summary>
        public DecisionSequence CreateSequence()
        {
            var definitions = new List<DecisionPointDefinition>();
            foreach (CaseDecisionPoint point in DecisionPoints)
            {
                definitions.Add(point.Definition);
            }

            return new DecisionSequence(definitions);
        }
    }
}
