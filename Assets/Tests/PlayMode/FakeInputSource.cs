using System.Collections.Generic;
using CourtSim.Runtime.Player;
using UnityEngine;

namespace CourtSim.Runtime.Tests
{
    /// <summary>Scriptable input for PlayMode tests. "Pressed" flags stay set until the test clears them.</summary>
    internal sealed class FakeInputSource : IPlayerInputSource
    {
        private static readonly InputBindingInfo[] BindingList =
        {
            new InputBindingInfo(PlayerAction.Move, "Move", "Stick", true),
            new InputBindingInfo(PlayerAction.Interact, "Interact", "A", true),
            new InputBindingInfo(PlayerAction.Back, "Menu", "Start", false),
        };

        public Vector2 Move { get; set; }

        public Vector2 Look { get; set; }

        public bool SprintHeld { get; set; }

        public bool InteractPressed { get; set; }

        public bool ToggleAssistantPressed { get; set; }

        public bool BackPressed { get; set; }

        public IReadOnlyList<InputBindingInfo> Bindings => BindingList;
    }
}
