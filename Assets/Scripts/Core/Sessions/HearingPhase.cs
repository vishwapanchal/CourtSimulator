using CourtSim.Core.Decisions;

namespace CourtSim.Core.Sessions
{
    /// <summary>
    /// Names the three phases of a case, as shown on the title card before each hearing and in the final report.
    /// </summary>
    /// <remarks>
    /// In a real court these happen weeks or months apart: bail early on, evidence rulings during the trial and
    /// sentence only after a conviction. The simulator plays them one after another, so each title says where in
    /// the case the judge now is.
    /// </remarks>
    public static class HearingPhase
    {
        /// <summary>"PHASE 1 OF 3" and so on.</summary>
        public static string Number(int index, int count)
        {
            return $"PHASE {index + 1} OF {count}";
        }

        /// <summary>The big title, e.g. "BAIL HEARING".</summary>
        public static string Title(DecisionPointKind kind)
        {
            switch (kind)
            {
                case DecisionPointKind.Bail:
                    return "BAIL HEARING";
                case DecisionPointKind.EvidenceAdmissibility:
                    return "EVIDENCE HEARING";
                default:
                    return "SENTENCING HEARING";
            }
        }

        /// <summary>One line saying when this happens in a real case and what the judge must decide.</summary>
        public static string Stage(DecisionPointKind kind)
        {
            switch (kind)
            {
                case DecisionPointKind.Bail:
                    return "Before trial: should the accused be released while the case goes on?";
                case DecisionPointKind.EvidenceAdmissibility:
                    return "During the trial: may this piece of evidence be used?";
                default:
                    return "After conviction: what is the right sentence?";
            }
        }
    }
}
