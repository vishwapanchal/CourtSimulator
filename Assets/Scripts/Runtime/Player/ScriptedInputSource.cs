using System.Collections.Generic;
using UnityEngine;

namespace CourtSim.Runtime.Player
{
    /// <summary>
    /// Input driven by code instead of a keyboard and mouse. The demo recorder uses it to walk the "judge"
    /// through the courtroom exactly as a person would: same movement, same look, same interact button.
    /// </summary>
    public sealed class ScriptedInputSource : IPlayerInputSource
    {
        private static readonly InputBindingInfo[] BindingList =
        {
            new InputBindingInfo(PlayerAction.Move, "Move", "W A S D", false),
            new InputBindingInfo(PlayerAction.Look, "Look around", "Mouse", false),
            new InputBindingInfo(PlayerAction.Sprint, "Walk faster", "Shift", false),
            new InputBindingInfo(PlayerAction.Interact, "Interact", "E", true),
            new InputBindingInfo(PlayerAction.ToggleAssistant, "AI assistant", "Tab", true),
            new InputBindingInfo(PlayerAction.Back, "Menu / back", "Esc", true),
        };

        private bool _interact;
        private bool _toggleAssistant;
        private bool _back;

        public Vector2 Move { get; set; }

        public Vector2 Look { get; set; }

        public bool SprintHeld { get; set; }

        /// <summary>True for exactly one read after <see cref="PressInteract"/>, like a real key press.</summary>
        public bool InteractPressed => Consume(ref _interact);

        public bool ToggleAssistantPressed => Consume(ref _toggleAssistant);

        public bool BackPressed => Consume(ref _back);

        public IReadOnlyList<InputBindingInfo> Bindings => BindingList;

        public void PressInteract()
        {
            _interact = true;
        }

        public void PressToggleAssistant()
        {
            _toggleAssistant = true;
        }

        public void PressBack()
        {
            _back = true;
        }

        private static bool Consume(ref bool flag)
        {
            bool value = flag;
            flag = false;
            return value;
        }
    }
}
