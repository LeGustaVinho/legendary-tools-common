using LegendaryTools.FixedPoint;

namespace LegendaryTools.Deterministic.Samples
{
    /// <summary>Immutable rotation input prepared once and shared across entities/ticks under one frozen numeric profile.</summary>
    public readonly struct QuaternionRotationStep
    {
        public readonly int Degrees;
        public readonly FixedQuaternion Rotation;
        public readonly FixedStatus ConstructionStatus;
        public readonly ulong ProfileFingerprint;
        internal readonly bool IsPrepared;
        internal QuaternionRotationStep(int degrees, FixedQuaternion rotation, FixedStatus status, ulong fingerprint)
        { Degrees = degrees; Rotation = rotation; ConstructionStatus = status; ProfileFingerprint = fingerprint; IsPrepared = true; }
    }
}
