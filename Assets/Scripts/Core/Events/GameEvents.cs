using CourtSim.Core.Decisions;
using CourtSim.Core.Interaction;
using CourtSim.Core.UI;

namespace CourtSim.Core.Events
{
    /// <summary>Raised whenever gameplay input (movement, look, interaction) becomes blocked or unblocked by UI.</summary>
    public readonly struct GameplayInputBlockChanged
    {
        public GameplayInputBlockChanged(bool isBlocked)
        {
            IsBlocked = isBlocked;
        }

        public bool IsBlocked { get; }
    }

    /// <summary>Raised when the top-most UI screen changes. <see cref="ScreenId.None"/> means no screen is open.</summary>
    public readonly struct ScreenChanged
    {
        public ScreenChanged(ScreenId previous, ScreenId current)
        {
            Previous = previous;
            Current = current;
        }

        public ScreenId Previous { get; }

        public ScreenId Current { get; }
    }

    /// <summary>Raised when the object under the player's crosshair changes.</summary>
    public readonly struct InteractionFocusChanged
    {
        public InteractionFocusChanged(InteractionAction action, string prompt)
        {
            Action = action;
            Prompt = prompt;
        }

        /// <summary>The focused action, or <see cref="InteractionAction.None"/> when nothing is focused.</summary>
        public InteractionAction Action { get; }

        public string Prompt { get; }

        public bool HasFocus => Action != InteractionAction.None;
    }

    /// <summary>Raised when the player activates the focused interactable.</summary>
    public readonly struct InteractionRequested
    {
        public InteractionRequested(InteractionAction action)
        {
            Action = action;
        }

        public InteractionAction Action { get; }
    }

    /// <summary>Raised when the trainee commits a ruling at a decision point.</summary>
    public readonly struct DecisionSubmitted
    {
        public DecisionSubmitted(DecisionRecord record, string optionLabel)
        {
            Record = record;
            OptionLabel = optionLabel;
        }

        public DecisionRecord Record { get; }

        public string OptionLabel { get; }
    }
}
