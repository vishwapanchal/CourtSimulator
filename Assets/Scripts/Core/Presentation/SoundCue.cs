namespace CourtSim.Core.Presentation
{
    /// <summary>
    /// The game's sound effects. Each one is generated from numbers by <see cref="SfxSynth"/>, so the project
    /// ships no audio files. Play one with <c>SoundEffects.Play(SoundCue.Gavel)</c> (in CourtSim.Runtime.Audio).
    /// </summary>
    public enum SoundCue
    {
        /// <summary>Two sharp wooden knocks of the judge's gavel.</summary>
        Gavel,

        /// <summary>A deep cinematic boom for big moments, e.g. a title or a verdict.</summary>
        Impact,

        /// <summary>A rush of air, for transitions between screens or scenes.</summary>
        Whoosh,

        /// <summary>A tone that climbs for about two seconds, building tension before an impact.</summary>
        Riser,

        /// <summary>A soft, bright chime for titles and reveals.</summary>
        Shimmer,

        /// <summary>A very soft tick when the pointer moves onto a button.</summary>
        UiHover,

        /// <summary>A soft click when a button is pressed.</summary>
        UiClick,

        /// <summary>A low ambient pad that loops seamlessly (about eight seconds), for the menus.</summary>
        Drone,
    }
}
