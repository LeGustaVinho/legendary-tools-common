namespace LegendaryTools.Chronos
{
    public enum ChronosSecurityMode
    {
        /// <summary>
        /// Only remote-trusted time can advance offline progress.
        /// If remote time is unavailable, offline elapsed is 0.
        /// </summary>
        StrictRemoteOnly = 0,

        /// <summary>
        /// If remote time is unavailable, allow offline progression using device clock,
        /// but cap the maximum granted elapsed time (untrusted).
        /// </summary>
        SoftCap = 1,

        /// <summary>
        /// Tolerate a configured number of consecutive remote failures.
        /// During the grace period, behaves like SoftCap (untrusted + capped).
        /// After exceeding the limit, behaves like StrictRemoteOnly (fail closed).
        /// </summary>
        GracePeriod = 2
    }
}