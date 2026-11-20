using CourtSim.Core.Events;
using UnityEngine;

namespace CourtSim.Runtime.App
{
    /// <summary>
    /// Process-wide services shared by runtime systems. Kept deliberately tiny: anything with
    /// scene lifetime belongs on a component, not here.
    /// </summary>
    public static class GameServices
    {
        /// <summary>The application event bus. Handler exceptions are logged rather than propagated.</summary>
        public static EventBus Events { get; private set; } = CreateBus();

        // Runs before any scene loads on every play session, so static state is clean even when
        // "Enter Play Mode Options" disables domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Events = CreateBus();
        }

        private static EventBus CreateBus()
        {
            return new EventBus { HandlerException = Debug.LogException };
        }
    }
}
