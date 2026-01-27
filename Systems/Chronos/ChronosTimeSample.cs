using System;

namespace LegendaryTools.Chronos
{
    public readonly struct ChronosTimeSample
    {
        public readonly bool Success;
        public readonly DateTime UtcNow;
        public readonly string ProviderName;

        public ChronosTimeSample(bool success, DateTime utcNow, string providerName)
        {
            Success = success;
            UtcNow = utcNow;
            ProviderName = providerName ?? string.Empty;
        }

        public static ChronosTimeSample Failed()
        {
            return new ChronosTimeSample(false, default, string.Empty);
        }
    }
}