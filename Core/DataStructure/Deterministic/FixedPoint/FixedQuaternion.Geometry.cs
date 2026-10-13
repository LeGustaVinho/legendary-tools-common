namespace LegendaryTools.FixedPoint
{
    public partial struct FixedQuaternion
    {
        public FixedVector3 eulerAngles
        {
            readonly get => ToEulerAngles(this);
            set { TrySetEulerAngles(value, out _); }
        }
        public static FixedQuaternion Euler(Fixed64 x, Fixed64 y, Fixed64 z) => Euler(new FixedVector3(x, y, z));
        public static FixedQuaternion Euler(Fixed64 x, Fixed64 y, Fixed64 z, out FixedStatus status) => Euler(new FixedVector3(x, y, z), out status);
        public static FixedQuaternion Euler(in FixedVector3 angles) => Euler(angles, out _);
        public static FixedQuaternion Euler(in FixedVector3 angles, out FixedStatus status) => FromBits(FixedQuaternionKernel.Euler(angles.Bits, true, FixedRuntime.State, out status));
        public static bool TryEuler(in FixedVector3 angles, out FixedQuaternion result, out FixedStatus status) { result = Euler(angles, out status); return status.IsSuccess(); }
        public static FixedQuaternion EulerRadians(Fixed64 x, Fixed64 y, Fixed64 z) => EulerRadians(new FixedVector3(x, y, z));
        public static FixedQuaternion EulerRadians(Fixed64 x, Fixed64 y, Fixed64 z, out FixedStatus status) => EulerRadians(new FixedVector3(x,y,z),out status);
        public static FixedQuaternion EulerRadians(in FixedVector3 angles) => EulerRadians(angles, out _);
        public static FixedQuaternion EulerRadians(in FixedVector3 angles, out FixedStatus status) => FromBits(FixedQuaternionKernel.Euler(angles.Bits, false, FixedRuntime.State, out status));
        public static bool TryEulerRadians(in FixedVector3 angles, out FixedQuaternion result, out FixedStatus status) { result=EulerRadians(angles,out status); return status.IsSuccess(); }
        public static FixedVector3 ToEulerAngles(in FixedQuaternion value) => ToEulerAngles(value, out _);
        public static FixedVector3 ToEulerAngles(in FixedQuaternion value, out FixedStatus status) => FixedVector3.FromBits(FixedQuaternionKernel.EulerAngles(value.Bits, true, FixedRuntime.State, out status));
        public static FixedVector3 ToEulerRadians(in FixedQuaternion value) => ToEulerRadians(value, out _);
        public static FixedVector3 ToEulerRadians(in FixedQuaternion value, out FixedStatus status) => FixedVector3.FromBits(FixedQuaternionKernel.EulerAngles(value.Bits, false, FixedRuntime.State, out status));
        public static bool TryToEulerAngles(in FixedQuaternion value, out FixedVector3 result, out FixedStatus status) { result=ToEulerAngles(value,out status); return status.IsSuccess(); }
        public static bool TryToEulerRadians(in FixedQuaternion value, out FixedVector3 result, out FixedStatus status) { result=ToEulerRadians(value,out status); return status.IsSuccess(); }
        public bool TrySetEulerAngles(in FixedVector3 angles, out FixedStatus status) { FixedQuaternion result = Euler(angles, out status); if (status.IsSuccess()) this = result; return status.IsSuccess(); }
        public static FixedQuaternion AngleAxis(Fixed64 angle, in FixedVector3 axis) => AngleAxis(angle, axis, out _);
        public static FixedQuaternion AngleAxis(Fixed64 angle, in FixedVector3 axis, out FixedStatus status) => FromBits(FixedQuaternionKernel.AngleAxis(angle.Bits, axis.Bits, true, FixedRuntime.State, out status));
        public static bool TryAngleAxis(Fixed64 angle, in FixedVector3 axis, out FixedQuaternion result, out FixedStatus status) { result = AngleAxis(angle, axis, out status); return status.IsSuccess(); }
        public static FixedQuaternion AngleAxisRadians(Fixed64 angle, in FixedVector3 axis) => AngleAxisRadians(angle, axis, out _);
        public static FixedQuaternion AngleAxisRadians(Fixed64 angle, in FixedVector3 axis, out FixedStatus status) => FromBits(FixedQuaternionKernel.AngleAxis(angle.Bits, axis.Bits, false, FixedRuntime.State, out status));
        public static bool TryAngleAxisRadians(Fixed64 angle, in FixedVector3 axis, out FixedQuaternion result, out FixedStatus status) { result=AngleAxisRadians(angle,axis,out status); return status.IsSuccess(); }
        public static FixedQuaternion FromToRotation(in FixedVector3 fromDirection, in FixedVector3 toDirection) => FromToRotation(fromDirection, toDirection, out _);
        public static FixedQuaternion FromToRotation(in FixedVector3 fromDirection, in FixedVector3 toDirection, out FixedStatus status) => FromBits(FixedQuaternionKernel.FromTo(fromDirection.Bits, toDirection.Bits, FixedRuntime.State, out status));
        public static bool TryFromToRotation(in FixedVector3 fromDirection, in FixedVector3 toDirection, out FixedQuaternion result, out FixedStatus status) { result = FromToRotation(fromDirection, toDirection, out status); return status.IsSuccess(); }
        public void SetFromToRotation(in FixedVector3 fromDirection, in FixedVector3 toDirection) => TrySetFromToRotation(fromDirection, toDirection, out _);
        public void SetFromToRotation(in FixedVector3 fromDirection, in FixedVector3 toDirection, out FixedStatus status) => TrySetFromToRotation(fromDirection, toDirection, out status);
        public bool TrySetFromToRotation(in FixedVector3 fromDirection, in FixedVector3 toDirection, out FixedStatus status) { FixedQuaternion result = FromToRotation(fromDirection, toDirection, out status); if (status.IsSuccess()) this = result; return status.IsSuccess(); }
        public static FixedQuaternion LookRotation(in FixedVector3 forward) => LookRotation(forward, FixedVector3.up);
        public static FixedQuaternion LookRotation(in FixedVector3 forward, out FixedStatus status) => LookRotation(forward, FixedVector3.up, out status);
        public static FixedQuaternion LookRotation(in FixedVector3 forward, in FixedVector3 upwards) => LookRotation(forward, upwards, out _);
        public static FixedQuaternion LookRotation(in FixedVector3 forward, in FixedVector3 upwards, out FixedStatus status) => FromBits(FixedQuaternionKernel.LookRotation(forward.Bits, upwards.Bits, FixedRuntime.State, out status));
        public static bool TryLookRotation(in FixedVector3 forward, in FixedVector3 upwards, out FixedQuaternion result, out FixedStatus status) { result = LookRotation(forward, upwards, out status); return status.IsSuccess(); }
        public void SetLookRotation(in FixedVector3 forward) => TrySetLookRotation(forward, FixedVector3.up, out _);
        public void SetLookRotation(in FixedVector3 forward, in FixedVector3 upwards) => TrySetLookRotation(forward, upwards, out _);
        public void SetLookRotation(in FixedVector3 forward, in FixedVector3 upwards, out FixedStatus status) => TrySetLookRotation(forward, upwards, out status);
        public bool TrySetLookRotation(in FixedVector3 forward, in FixedVector3 upwards, out FixedStatus status) { FixedQuaternion result = LookRotation(forward, upwards, out status); if (status.IsSuccess()) this = result; return status.IsSuccess(); }
        public readonly void ToAngleAxis(out Fixed64 angle, out FixedVector3 axis) => ToAngleAxis(out angle, out axis, out _);
        public readonly void ToAngleAxis(out Fixed64 angle, out FixedVector3 axis, out FixedStatus status) { angle = Fixed64.FromBits(FixedQuaternionKernel.ToAngleAxis(Bits, true, FixedRuntime.State, out FixedVectorBits result, out status)); axis = FixedVector3.FromBits(result); }
        public readonly void ToAngleAxisRadians(out Fixed64 angle, out FixedVector3 axis) => ToAngleAxisRadians(out angle, out axis, out _);
        public readonly void ToAngleAxisRadians(out Fixed64 angle, out FixedVector3 axis, out FixedStatus status) { angle = Fixed64.FromBits(FixedQuaternionKernel.ToAngleAxis(Bits, false, FixedRuntime.State, out FixedVectorBits result, out status)); axis = FixedVector3.FromBits(result); }
        public readonly bool TryToAngleAxis(out Fixed64 angle, out FixedVector3 axis, out FixedStatus status) { ToAngleAxis(out angle, out axis, out status); return status.IsSuccess(); }
        public readonly bool TryToAngleAxisRadians(out Fixed64 angle, out FixedVector3 axis, out FixedStatus status) { ToAngleAxisRadians(out angle,out axis,out status); return status.IsSuccess(); }
        public static bool TryLerp(in FixedQuaternion a, in FixedQuaternion b, Fixed64 t, out FixedQuaternion result, out FixedStatus status) { result = Lerp(a, b, t, out status); return status.IsSuccess(); }
        public static bool TrySlerp(in FixedQuaternion a, in FixedQuaternion b, Fixed64 t, out FixedQuaternion result, out FixedStatus status) { result = Slerp(a, b, t, out status); return status.IsSuccess(); }
        public static bool TryLerpUnclamped(in FixedQuaternion a, in FixedQuaternion b, Fixed64 t, out FixedQuaternion result, out FixedStatus status) { result=LerpUnclamped(a,b,t,out status); return status.IsSuccess(); }
        public static bool TrySlerpUnclamped(in FixedQuaternion a, in FixedQuaternion b, Fixed64 t, out FixedQuaternion result, out FixedStatus status) { result=SlerpUnclamped(a,b,t,out status); return status.IsSuccess(); }
        public static bool TryRotateTowards(in FixedQuaternion a, in FixedQuaternion b, Fixed64 delta, out FixedQuaternion result, out FixedStatus status) { result = RotateTowards(a, b, delta, out status); return status.IsSuccess(); }
        public static bool TryRotateTowardsRadians(in FixedQuaternion a, in FixedQuaternion b, Fixed64 delta, out FixedQuaternion result, out FixedStatus status) { result=RotateTowardsRadians(a,b,delta,out status); return status.IsSuccess(); }
    }
}
