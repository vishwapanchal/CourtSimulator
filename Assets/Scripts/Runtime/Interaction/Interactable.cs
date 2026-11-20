using CourtSim.Core.Interaction;
using UnityEngine;

namespace CourtSim.Runtime.Interaction
{
    /// <summary>
    /// Marks a collider as something the player can activate. The action is data, not behaviour:
    /// <see cref="InteractionProbe"/> publishes it and the UI decides what to open.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Interactable : MonoBehaviour
    {
        [SerializeField] private InteractionAction _action = InteractionAction.None;
        [Tooltip("Shown on the HUD while the player looks at this object.")]
        [SerializeField] private string _prompt = string.Empty;

        public InteractionAction Action => _action;

        public string Prompt => _prompt;

        public void Configure(InteractionAction action, string prompt)
        {
            _action = action;
            _prompt = prompt ?? string.Empty;
        }
    }
}
