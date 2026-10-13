using LegendaryTools.FixedPoint;

namespace LegendaryTools.Deterministic.Samples
{
    /// <summary>Reusable gameplay transition with integer input and explicit numeric status; independent of ECS/storage.</summary>
    public static class QuaternionSimulationTransition
    {
        public static bool TryRotate(in FixedQuaternion current, int degrees, out FixedQuaternion result, out FixedStatus status)
        {
            var step = Prepare(degrees, out _);
            return TryRotate(current, step, out result, out status);
        }
        public static QuaternionRotationStep Prepare(int degrees, out FixedStatus status)
        {
            Fixed64 angle = Fixed64.FromInt(degrees, out status);
            FixedQuaternion delta = FixedQuaternion.AngleAxis(angle, FixedVector3.up, out var construction);
            status |= construction;
            ulong fingerprint = FixedRuntime.IsInitialized ? FixedRuntime.Settings.QuaternionFingerprint : 0;
            return new QuaternionRotationStep(degrees, delta, status, fingerprint);
        }
        public static bool TryRotate(in FixedQuaternion current, in QuaternionRotationStep step, out FixedQuaternion result, out FixedStatus status)
        {
            status = step.ConstructionStatus;
            if (!step.IsPrepared) { result = FixedQuaternion.NaN; status |= FixedStatus.InvalidConfiguration; return false; }
            if (!FixedRuntime.IsInitialized) { result = FixedQuaternion.NaN; status |= FixedStatus.Uninitialized; return false; }
            if (step.ProfileFingerprint != FixedRuntime.Settings.QuaternionFingerprint) { result = FixedQuaternion.NaN; status |= FixedStatus.ProfileMismatch; return false; }
            result = FixedQuaternion.Multiply(current, step.Rotation, out var composition); status |= composition;
            if (status.IsSuccess() && FixedRuntime.Settings.QuaternionNormalization == FixedQuaternionNormalizationMode.None)
            { result = FixedQuaternion.Normalize(result, out var normalization); status |= normalization; }
            return status.IsSuccess();
        }
    }
}
