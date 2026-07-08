using UnityEngine;

namespace SFP.Gameplay
{
    // Modal console UIs (helm, ballast, sonar, ...) acquire keyboard focus while open so
    // PlayerController ignores movement keys — W/S at the helm sets throttle, not walking.
    public static class ConsoleFocus
    {
        static object _owner;

        public static bool IsLocked => _owner != null;

        public static void Acquire(object owner) => _owner = owner;

        public static void Release(object owner)
        {
            if (ReferenceEquals(_owner, owner)) _owner = null;
        }

        // Statics survive play sessions when domain reload is disabled; start clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => _owner = null;
    }
}
