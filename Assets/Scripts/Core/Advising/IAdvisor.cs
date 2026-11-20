using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CourtSim.Core.Cases;
using CourtSim.Core.Decisions;

namespace CourtSim.Core.Advising
{
    /// <summary>Everything an advisor needs to produce advice for one decision point.</summary>
    public sealed class AdviceRequest
    {
        public AdviceRequest(CaseFile caseFile, int pointIndex, FlawKind flaw, IReadOnlyList<DecisionRecord> priorRulings, ExplanationDetail detail = ExplanationDetail.Standard)
        {
            Case = caseFile ?? throw new ArgumentNullException(nameof(caseFile));
            if (pointIndex < 0 || pointIndex >= caseFile.DecisionPoints.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(pointIndex));
            }

            PointIndex = pointIndex;
            Flaw = flaw;
            PriorRulings = priorRulings ?? Array.Empty<DecisionRecord>();
            Detail = detail;
        }

        /// <summary>How much the advice should explain itself; raised by the adaptive controller.</summary>
        public ExplanationDetail Detail { get; }

        public CaseFile Case { get; }

        public int PointIndex { get; }

        public CaseDecisionPoint Point => Case.DecisionPoints[PointIndex];

        /// <summary>The flaw the advice must carry (None for sound advice).</summary>
        public FlawKind Flaw { get; }

        public IReadOnlyList<DecisionRecord> PriorRulings { get; }

        /// <summary>
        /// The authored script for <see cref="Flaw"/>, falling back to the sound script if the case has no
        /// variant for it. Its recommended option is the ruling every advisor must recommend.
        /// </summary>
        public AdviceScript TargetScript => Point.FindAdvice(Flaw) ?? Point.FindAdvice(FlawKind.None);
    }

    /// <summary>Produces advice. Implementations must be safe to call from Unity's main thread and must not block it.</summary>
    public interface IAdvisor
    {
        Task<Advice> GetAdviceAsync(AdviceRequest request, CancellationToken cancellationToken);
    }

    /// <summary>Raised when an advisor cannot produce valid advice.</summary>
    public sealed class AdvisorException : Exception
    {
        public AdvisorException(string message, Exception inner = null) : base(message, inner)
        {
        }
    }
}
