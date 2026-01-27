using UnityEngine;

namespace LegendaryTools.Chronos
{
    [CreateAssetMenu(fileName = "ChronosSecurityPolicy", menuName = "Tools/Chronos/ChronosSecurityPolicy")]
    public class ChronosSecurityPolicy : ScriptableObject
    {
        public ChronosSecurityMode Mode = ChronosSecurityMode.StrictRemoteOnly;

        [Tooltip(
            "Max accepted forward jump, in seconds, to cap suspicious leaps even with remote time (defense-in-depth).")]
        public int MaxForwardJumpSeconds = 60 * 60 * 24 * 30; // 30 days

        [Tooltip(
            "If true, invalid signature or rollback will disable offline progression (elapsed=0) and mark time untrusted.")]
        public bool FailClosedOnTamper = true;

        [Header("Offline fallback (SoftCap / GracePeriod)")]
        [Tooltip("Maximum offline elapsed granted when remote time is unavailable (seconds). Example: 2 hours = 7200.")]
        public int OfflineSoftCapSeconds = 2 * 60 * 60;

        [Tooltip("GracePeriod: number of consecutive remote failures tolerated before failing closed.")]
        public int GraceFailureLimit = 3;

        [Header("Rate limiting / cooldown")]
        [Tooltip("Minimum seconds between sync attempts to avoid spamming providers.")]
        public float MinSyncIntervalSeconds = 10f;
    }
}