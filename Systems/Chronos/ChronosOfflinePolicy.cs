using System;
using UnityEngine;

namespace LegendaryTools.Chronos
{
    /// <summary>
    /// Encapsulates decisions and caps for offline fallback behavior.
    /// </summary>
    public static class ChronosOfflinePolicy
    {
        public static bool ShouldAllowFallback(ChronosSecurityPolicy policy, int failureCount)
        {
            if (policy == null)
                return false;

            switch (policy.Mode)
            {
                case ChronosSecurityMode.SoftCap:
                    return true;

                case ChronosSecurityMode.GracePeriod:
                    return failureCount <= Mathf.Max(0, policy.GraceFailureLimit);

                case ChronosSecurityMode.StrictRemoteOnly:
                default:
                    return false;
            }
        }

        public static TimeSpan ApplyForwardJumpCap(ChronosSecurityPolicy policy, DateTime storedBefore,
            ref DateTime utcNow, TimeSpan elapsed)
        {
            if (policy == null)
                return elapsed;

            int cap = Mathf.Max(0, policy.MaxForwardJumpSeconds);
            if (cap > 0 && elapsed.TotalSeconds > cap)
            {
                elapsed = TimeSpan.FromSeconds(cap);
                utcNow = storedBefore.AddSeconds(cap);
            }

            return elapsed;
        }

        public static ChronosTimeSample GetDeviceUtcNowSample()
        {
            DateTime utc = DateTime.UtcNow;
            if (utc.Kind != DateTimeKind.Utc)
                utc = DateTime.SpecifyKind(utc.ToUniversalTime(), DateTimeKind.Utc);

            return new ChronosTimeSample(true, utc, "LocalDeviceClock");
        }

        public static TimeSpan ApplyOfflineSoftCap(ChronosSecurityPolicy policy, DateTime storedBefore,
            ref DateTime utcNow, TimeSpan elapsed)
        {
            if (policy == null)
                return elapsed;

            int capSeconds = Mathf.Max(0, policy.OfflineSoftCapSeconds);
            if (capSeconds > 0 && elapsed.TotalSeconds > capSeconds)
            {
                elapsed = TimeSpan.FromSeconds(capSeconds);
                utcNow = storedBefore.AddSeconds(capSeconds);
            }

            return elapsed;
        }
    }
}