namespace CourtSim.Core.Courtroom
{
    /// <summary>The role a piece of courtroom furniture plays. Drives both visuals and validation.</summary>
    public enum FixtureKind
    {
        /// <summary>Where the trainee appears. Not solid and not rendered.</summary>
        PlayerSpawn = 0,
        JudgeBench = 1,
        ClerkDesk = 2,
        WitnessStand = 3,
        ProsecutionTable = 4,
        DefenseTable = 5,
        AccusedDock = 6,
        EvidenceDisplay = 7,
        AiAssistantTerminal = 8,

        /// <summary>Railing separating the well of the court from the public gallery.</summary>
        Bar = 9,
        GalleryBench = 10,
    }
}
