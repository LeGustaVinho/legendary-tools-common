#if UNITY_EDITOR
using System;

namespace LegendaryTools.Chronos
{
    /// <summary>
    /// Editor-only bridge to access a live Chronos instance during Play Mode.
    /// Register it from your composition root to enable the editor tools.
    /// </summary>
    public static class ChronosEditorBridge
    {
        public static IChronos ChronosInstance { get; private set; }

        /// <summary>
        /// Registers a live Chronos instance so the editor window can control it during Play Mode.
        /// Call this once after creating Chronos in your composition root.
        /// </summary>
        public static void Register(IChronos chronos)
        {
            ChronosInstance = chronos;
        }

        /// <summary>
        /// Clears the registered instance.
        /// </summary>
        public static void Unregister(IChronos chronos)
        {
            if (ReferenceEquals(ChronosInstance, chronos))
                ChronosInstance = null;
        }
    }
}
#endif