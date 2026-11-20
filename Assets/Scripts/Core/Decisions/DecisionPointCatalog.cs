using System;

namespace CourtSim.Core.Decisions
{
    /// <summary>
    /// Generic decision content used only when no case file loads (e.g. a broken Resources folder), so
    /// the courtroom stays playable. Real sessions take their decision points from case files.
    /// </summary>
    public static class DecisionPointCatalog
    {
        /// <summary>Returns the generic placeholder definition for <paramref name="kind"/>.</summary>
        public static DecisionPointDefinition GetPlaceholder(DecisionPointKind kind)
        {
            switch (kind)
            {
                case DecisionPointKind.Bail:
                    return new DecisionPointDefinition(
                        kind,
                        "Bail application",
                        "The accused applies for bail pending trial. Weigh flight risk, the seriousness of the charge and the risk to witnesses before you rule.",
                        new[]
                        {
                            new DecisionOption("bail.grant", "Grant bail"),
                            new DecisionOption("bail.grant_conditions", "Grant bail with conditions"),
                            new DecisionOption("bail.deny", "Refuse bail"),
                        });

                case DecisionPointKind.EvidenceAdmissibility:
                    return new DecisionPointDefinition(
                        kind,
                        "Admissibility of evidence",
                        "The prosecution seeks to tender an exhibit. The defence objects to how it was obtained. Decide whether the exhibit is admitted.",
                        new[]
                        {
                            new DecisionOption("evidence.admit", "Admit the exhibit"),
                            new DecisionOption("evidence.admit_limited", "Admit with a limiting direction"),
                            new DecisionOption("evidence.exclude", "Exclude the exhibit"),
                        });

                case DecisionPointKind.Sentencing:
                    return new DecisionPointDefinition(
                        kind,
                        "Sentencing",
                        "The accused has been convicted. Consider aggravating and mitigating factors and pass sentence.",
                        new[]
                        {
                            new DecisionOption("sentence.non_custodial", "Non-custodial sentence"),
                            new DecisionOption("sentence.custodial_lower", "Custodial, lower end of range"),
                            new DecisionOption("sentence.custodial_upper", "Custodial, upper end of range"),
                        });

                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown decision point kind.");
            }
        }

        /// <summary>Short label for HUD and progress displays.</summary>
        public static string ShortName(DecisionPointKind kind)
        {
            switch (kind)
            {
                case DecisionPointKind.Bail:
                    return "Bail";
                case DecisionPointKind.EvidenceAdmissibility:
                    return "Evidence";
                case DecisionPointKind.Sentencing:
                    return "Sentencing";
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown decision point kind.");
            }
        }
    }
}
