using System.Collections.Generic;
using UnityEngine;

namespace CourtSim.Runtime.Player
{
    /// <summary>
    /// Keyboard and mouse input via Unity's built-in Input Manager, chosen so Phase 1 needs no extra
    /// packages. If the project is switched to the Input System package only, this source reports no
    /// input and logs a warning; add an Input System source at that point.
    /// </summary>
    public sealed class LegacyPlayerInputSource : IPlayerInputSource
    {
        public const KeyCode SprintKey = KeyCode.LeftShift;
        public const KeyCode InteractKey = KeyCode.E;
        public const KeyCode ToggleAssistantKey = KeyCode.Tab;
        public const KeyCode BackKey = KeyCode.Escape;

        private static readonly InputBindingInfo[] DefaultBindings =
        {
            new InputBindingInfo(PlayerAction.Move, "Move", "W A S D", false),
            new InputBindingInfo(PlayerAction.Look, "Look around", "Mouse", false),
            new InputBindingInfo(PlayerAction.Sprint, "Walk faster", "Shift", false),
            new InputBindingInfo(PlayerAction.Interact, "Interact", "E", false),
            new InputBindingInfo(PlayerAction.ToggleAssistant, "AI assistant", "Tab", true),
            new InputBindingInfo(PlayerAction.Back, "Menu / back", "Esc", true),
        };

#if !ENABLE_LEGACY_INPUT_MANAGER
        private static bool _warned;
#endif

        public LegacyPlayerInputSource()
        {
#if !ENABLE_LEGACY_INPUT_MANAGER
            if (!_warned)
            {
                _warned = true;
                Debug.LogWarning("LegacyPlayerInputSource needs 'Active Input Handling' set to 'Input Manager (Old)' or 'Both'.");
            }
#endif
        }

        public IReadOnlyList<InputBindingInfo> Bindings => DefaultBindings;

#if ENABLE_LEGACY_INPUT_MANAGER
        public Vector2 Move => Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1f);

        public Vector2 Look => new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));

        public bool SprintHeld => Input.GetKey(SprintKey) || Input.GetKey(KeyCode.RightShift);

        public bool InteractPressed => Input.GetKeyDown(InteractKey);

        public bool ToggleAssistantPressed => Input.GetKeyDown(ToggleAssistantKey);

        public bool BackPressed => Input.GetKeyDown(BackKey);
#else
        public Vector2 Move => Vector2.zero;

        public Vector2 Look => Vector2.zero;

        public bool SprintHeld => false;

        public bool InteractPressed => false;

        public bool ToggleAssistantPressed => false;

        public bool BackPressed => false;
#endif
    }
}
