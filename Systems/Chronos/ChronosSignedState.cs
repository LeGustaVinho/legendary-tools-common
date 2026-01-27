using System;

namespace LegendaryTools.Chronos
{
    /// <summary>
    /// DTO persisted to IPersistence, signed to detect tampering/rollback.
    /// </summary>
    [Serializable]
    public struct ChronosSignedState
    {
        public int Version;

        public bool IsFirstStart;

        /// <summary>
        /// Last recorded UTC in ISO 8601 roundtrip format ("o").
        /// </summary>
        public string LastRecordedUtcIso;

        public double LastUnscaledTimeAsDouble;

        public string NonceBase64;

        public string SignatureBase64;

        public bool TamperDetected;

        // v2 fields
        public int FailureCount;
        public string LastSyncUtcIso;
        public string LastProviderName;
    }
}