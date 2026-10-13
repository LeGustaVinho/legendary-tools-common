#if !FIXEDPOINT_STANDALONE
using Unity.Burst;
#endif

namespace LegendaryTools.FixedPoint
{
    /// <summary>Initialize after FixedRuntime and before using bounds or scheduling jobs. Never reset during a simulation.</summary>
    public static class FixedBoundsRuntime
    {
#if FIXEDPOINT_STANDALONE
        private static FixedBoundsRuntimeState StandaloneState;
        private static ref FixedBoundsRuntimeState Data => ref StandaloneState;
#else
        private struct StorageKey { }
        private static readonly SharedStatic<FixedBoundsRuntimeState> Storage = SharedStatic<FixedBoundsRuntimeState>.GetOrCreate<StorageKey>();
        private static ref FixedBoundsRuntimeState Data => ref Storage.Data;
#endif
        private static class ManagedInitialization { internal static readonly object Gate = new object(); }
        public static bool IsInitialized => Data.IsInitialized;
        public static FixedBoundsSettings Settings => Data.Settings;
        internal static FixedBoundsRuntimeState State => Data;
        public static void Initialize() => Initialize(FixedBoundsSettings.Default);
        public static void Initialize(FixedBoundsSettings settings)
        { TryInitialize(settings, out FixedStatus status); status.ThrowIfFailure(); }
        public static bool TryInitialize(FixedBoundsSettings settings, out FixedStatus status)
        {
            if (!FixedRuntime.IsInitialized) { status = FixedStatus.Uninitialized; return false; }
            if (!settings.IsValid) { status = FixedStatus.InvalidConfiguration; return false; }
            lock (ManagedInitialization.Gate)
            {
                if (Data.IsInitialized)
                { status = Data.Settings.Equals(settings) ? FixedStatus.None : FixedStatus.ConfigurationFrozen; return status.IsSuccess(); }
                Data = new FixedBoundsRuntimeState(settings);
                status = FixedStatus.None;
                return true;
            }
        }
    }

    internal readonly struct FixedBoundsRuntimeState
    {
        internal readonly FixedBoundsSettings Settings;
        internal readonly bool IsInitialized;
        internal FixedBoundsRuntimeState(FixedBoundsSettings settings)
        { Settings = settings; IsInitialized = settings.IsValid; }
    }
}
