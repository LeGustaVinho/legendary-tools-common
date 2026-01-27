using System;

namespace LegendaryTools.Chronos
{
    /// <summary>
    /// Result of a Chronos sync attempt.
    /// </summary>
    public readonly struct ChronosSyncResult
    {
        public readonly bool Success;
        public readonly bool IsTimeTrusted;
        public readonly bool SkippedDueToCooldown;

        public readonly ChronosSyncReason Reason;

        public readonly DateTime UtcNow;
        public readonly DateTime StoredUtcBefore;
        public readonly TimeSpan Elapsed;

        public readonly string ProviderName;

        public ChronosSyncResult(
            bool success,
            bool isTimeTrusted,
            bool skippedDueToCooldown,
            ChronosSyncReason reason,
            DateTime utcNow,
            DateTime storedUtcBefore,
            TimeSpan elapsed,
            string providerName)
        {
            Success = success;
            IsTimeTrusted = isTimeTrusted;
            SkippedDueToCooldown = skippedDueToCooldown;
            Reason = reason;
            UtcNow = utcNow;
            StoredUtcBefore = storedUtcBefore;
            Elapsed = elapsed;
            ProviderName = providerName;
        }

        public static ChronosSyncResult Failed(ChronosSyncReason reason, bool isTimeTrusted, string providerName = "")
        {
            return new ChronosSyncResult(
                false,
                isTimeTrusted,
                false,
                reason,
                default,
                default,
                TimeSpan.Zero,
                providerName ?? string.Empty);
        }

        public static ChronosSyncResult SkippedCooldown(ChronosSyncReason reason, bool isTimeTrusted,
            string providerName = "")
        {
            return new ChronosSyncResult(
                false,
                isTimeTrusted,
                true,
                reason,
                default,
                default,
                TimeSpan.Zero,
                providerName ?? string.Empty);
        }
    }
}