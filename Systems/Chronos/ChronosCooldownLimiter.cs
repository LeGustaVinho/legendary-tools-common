using System;

namespace LegendaryTools.Chronos
{
    /// <summary>
    /// Prevents spamming time providers by enforcing a minimum interval between sync attempts.
    /// </summary>
    public sealed class ChronosCooldownLimiter
    {
        private double lastAttemptUnscaled;

        public bool ShouldSkip(double nowUnscaled, float minIntervalSeconds)
        {
            if (minIntervalSeconds <= 0f)
                return false;

            double delta = nowUnscaled - lastAttemptUnscaled;
            if (delta < minIntervalSeconds)
                return true;

            lastAttemptUnscaled = nowUnscaled;
            return false;
        }

        public void Reset()
        {
            lastAttemptUnscaled = 0;
        }
    }
}