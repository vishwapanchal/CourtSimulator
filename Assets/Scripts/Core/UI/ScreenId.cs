namespace CourtSim.Core.UI
{
    /// <summary>Identifies a modal UI screen managed by the screen stack.</summary>
    public enum ScreenId
    {
        /// <summary>No screen; gameplay has focus.</summary>
        None = 0,
        MainMenu = 1,
        Pause = 2,
        Controls = 3,
        DecisionPoint = 4,
        Evidence = 5,

        /// <summary>Choose a case to play, grouped by difficulty.</summary>
        CaseSelect = 6,
    }
}
