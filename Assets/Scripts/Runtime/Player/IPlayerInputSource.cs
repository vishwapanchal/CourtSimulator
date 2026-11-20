using System.Collections.Generic;
using UnityEngine;

namespace CourtSim.Runtime.Player
{
    /// <summary>Logical player actions, independent of device.</summary>
    public enum PlayerAction
    {
        Move,
        Look,
        Sprint,
        Interact,
        ToggleAssistant,
        Back,
    }

    /// <summary>Human-readable description of how an action is triggered, for help screens and HUD hints.</summary>
    public readonly struct InputBindingInfo
    {
        public InputBindingInfo(PlayerAction action, string displayName, string keys, bool showInHud)
        {
            Action = action;
            DisplayName = displayName;
            Keys = keys;
            ShowInHud = showInHud;
        }

        public PlayerAction Action { get; }

        public string DisplayName { get; }

        public string Keys { get; }

        public bool ShowInHud { get; }
    }

    /// <summary>
    /// Device-agnostic player input. Desktop uses <see cref="LegacyPlayerInputSource"/>; a VR source
    /// (XR Interaction Toolkit) can implement this later without touching movement or UI code.
    /// </summary>
    public interface IPlayerInputSource
    {
        /// <summary>Strafe (x) and forward (y), each in [-1, 1].</summary>
        Vector2 Move { get; }

        /// <summary>Look delta for this frame (already frame-scaled by the device).</summary>
        Vector2 Look { get; }

        bool SprintHeld { get; }

        /// <summary>True only on the frame the interact control was pressed.</summary>
        bool InteractPressed { get; }

        /// <summary>True only on the frame the assistant toggle was pressed.</summary>
        bool ToggleAssistantPressed { get; }

        /// <summary>True only on the frame the back/menu control was pressed.</summary>
        bool BackPressed { get; }

        IReadOnlyList<InputBindingInfo> Bindings { get; }
    }

    public static class PlayerInputSourceExtensions
    {
        /// <summary>The display keys for <paramref name="action"/>, or "?" if the source does not bind it.</summary>
        public static string KeyLabel(this IPlayerInputSource source, PlayerAction action)
        {
            foreach (InputBindingInfo binding in source.Bindings)
            {
                if (binding.Action == action)
                {
                    return binding.Keys;
                }
            }

            return "?";
        }
    }
}
