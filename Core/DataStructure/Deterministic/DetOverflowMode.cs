using System;
using System.Threading;

namespace DeterministicFixedPoint
{
    public enum DetOverflowMode
    {
        Wrap = 0,
        Saturate = 1
    }

    /// <summary>
    /// Global deterministic configuration.
    /// Must be initialized before first use if you want a non-default mode.
    /// Once any deterministic type is used, the config becomes locked and cannot change.
    /// </summary>
    public static class DetConfig
    {
        // Optional compile-time override:
        // Define DET_OVERFLOW_SATURATE to force saturate everywhere.
        // Define DET_OVERFLOW_WRAP to force wrap everywhere.
        // If neither is defined, default is Wrap, but can be set at runtime (until locked).

#if DET_OVERFLOW_SATURATE && DET_OVERFLOW_WRAP
#error Define only one of DET_OVERFLOW_SATURATE or DET_OVERFLOW_WRAP, not both.
#endif

#if DET_OVERFLOW_SATURATE
        private const bool kCompileTimeFixed = true;
        private const DetOverflowMode kCompileTimeMode = DetOverflowMode.Saturate;
#elif DET_OVERFLOW_WRAP
        private const bool kCompileTimeFixed = true;
        private const DetOverflowMode kCompileTimeMode = DetOverflowMode.Wrap;
#else
        private const bool kCompileTimeFixed = false;
        private const DetOverflowMode kCompileTimeMode = DetOverflowMode.Wrap;
#endif

        private static int sLocked; // 0 = false, 1 = true
        private static DetOverflowMode sMode = kCompileTimeMode;

        /// <summary>
        /// Current overflow mode. Reading this locks the config.
        /// </summary>
        public static DetOverflowMode Mode
        {
            get
            {
                Touch();
                return kCompileTimeFixed ? kCompileTimeMode : sMode;
            }
        }

        /// <summary>
        /// Initialize overflow mode. Must be called before first use (before config locks).
        /// If compile-time mode is set, this will throw.
        /// </summary>
        public static void Initialize(DetOverflowMode mode)
        {
            if (kCompileTimeFixed)
                throw new InvalidOperationException(
                    "DetConfig is compile-time fixed and cannot be initialized at runtime.");

            if (Volatile.Read(ref sLocked) != 0)
                throw new InvalidOperationException("DetConfig is locked after first use and cannot be changed.");

            sMode = mode;
        }

        /// <summary>
        /// Marks the deterministic system as used and locks configuration.
        /// Call this from any public API that constitutes "first use".
        /// </summary>
        public static void Touch()
        {
            Interlocked.Exchange(ref sLocked, 1);
        }

        /// <summary>
        /// Explicitly lock the config (alias of Touch).
        /// </summary>
        public static void Lock()
        {
            Touch();
        }

        internal static bool IsLocked => Volatile.Read(ref sLocked) != 0;
    }
}