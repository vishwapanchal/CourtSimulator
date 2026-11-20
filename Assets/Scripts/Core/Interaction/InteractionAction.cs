namespace CourtSim.Core.Interaction
{
    /// <summary>What happens when the player activates a courtroom fixture.</summary>
    public enum InteractionAction
    {
        /// <summary>The fixture is not interactive.</summary>
        None = 0,

        /// <summary>Open the next pending decision point (bail, admissibility, sentencing).</summary>
        OpenDecisionPoint = 1,

        /// <summary>Open the evidence viewer.</summary>
        OpenEvidence = 2,

        /// <summary>Show the AI assistant panel.</summary>
        ShowAssistant = 3,
    }
}
