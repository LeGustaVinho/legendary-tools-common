#if !FIXEDPOINT_STANDALONE
using Unity.Burst;
#endif

namespace LegendaryTools.FixedPoint
{
    public static class FixedRuntime
    {
#if FIXEDPOINT_STANDALONE
        private static FixedRuntimeState StandaloneState;
        private static ref FixedRuntimeState Data => ref StandaloneState;
#else
        private struct StorageKey { }
        private static readonly SharedStatic<FixedRuntimeState> Storage = SharedStatic<FixedRuntimeState>.GetOrCreate<StorageKey>();
        private static ref FixedRuntimeState Data => ref Storage.Data;
#endif

        // Keep managed synchronization in a different type so its object initializer is never reached by Burst.
        private static class ManagedInitialization { internal static readonly object Gate = new object(); }

        public static bool IsInitialized => Data.IsInitialized;
        public static FixedSettings Settings => Data.Settings;
        internal static FixedRuntimeState State => Data;

        public static void Initialize() => Initialize(FixedSettings.Default);
        public static void Initialize(FixedSettings settings)
        {
            TryInitialize(settings, out FixedStatus status);
            status.ThrowIfFailure();
        }

        public static bool TryInitialize(FixedSettings settings, out FixedStatus status)
        {
            if (!settings.IsValid) { status = FixedStatus.InvalidConfiguration; return false; }
            lock (ManagedInitialization.Gate)
            {
                if (Data.IsInitialized)
                {
                    status = Data.Settings.Equals(settings) ? FixedStatus.None : FixedStatus.ConfigurationFrozen;
                    return status.IsSuccess();
                }
                Data = new FixedRuntimeState(settings);
                status = FixedStatus.None;
                return true;
            }
        }
    }

    internal readonly struct FixedRuntimeState
    {
        internal readonly FixedSettings Settings;
        internal readonly ulong Scale32;
        internal readonly ulong Scale64;
        internal readonly bool IsInitialized;

        internal FixedRuntimeState(FixedSettings settings)
        {
            Settings = settings;
            Scale32 = PowerOfTen(settings.DecimalPlaces32);
            Scale64 = PowerOfTen(settings.DecimalPlaces64);
            IsInitialized = settings.IsValid;
        }

        internal static ulong PowerOfTen(int digits)
        {
            ulong value = 1;
            for (int i = 0; i < digits; i++) value *= 10;
            return value;
        }
    }
}
