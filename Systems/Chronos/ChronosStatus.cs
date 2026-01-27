using System;

namespace LegendaryTools.Chronos
{
    /// <summary>
    /// Runtime status snapshot of Chronos.
    /// </summary>
    public readonly struct ChronosStatus
    {
        public readonly bool IsInitialized;
        public readonly bool IsTimeTrusted;
        public readonly bool TamperDetected;

        public readonly DateTime LastSyncUtc;
        public readonly ChronosSyncResult LastSyncResult;

        public readonly int FailureCount;
        public readonly string LastProviderName;

        public ChronosStatus(
            bool isInitialized,
            bool isTimeTrusted,
            bool tamperDetected,
            DateTime lastSyncUtc,
            ChronosSyncResult lastSyncResult,
            int failureCount,
            string lastProviderName)
        {
            IsInitialized = isInitialized;
            IsTimeTrusted = isTimeTrusted;
            TamperDetected = tamperDetected;
            LastSyncUtc = lastSyncUtc;
            LastSyncResult = lastSyncResult;
            FailureCount = failureCount;
            LastProviderName = lastProviderName;
        }
    }
}