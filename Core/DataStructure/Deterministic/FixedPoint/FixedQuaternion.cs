using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace LegendaryTools.FixedPoint
{
    /// <summary>Integer rotation with frozen FixedRuntime configuration. default is zero, not identity.</summary>
    [Serializable, StructLayout(LayoutKind.Sequential)]
    public partial struct FixedQuaternion : IEquatable<FixedQuaternion>, IFormattable
    {
        public const ushort AlgorithmVersion = 1;
        public Fixed64 x, y, z, w;
        public FixedQuaternion(Fixed64 x, Fixed64 y, Fixed64 z, Fixed64 w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        internal readonly FixedQuaternionBits Bits => new FixedQuaternionBits(x.Bits, y.Bits, z.Bits, w.Bits);
        internal static FixedQuaternion FromBits(FixedQuaternionBits value) => new FixedQuaternion(Fixed64.FromBits(value.X), Fixed64.FromBits(value.Y), Fixed64.FromBits(value.Z), Fixed64.FromBits(value.W));
        public static FixedQuaternion FromRaw(long x, long y, long z, long w) => new FixedQuaternion(Fixed64.FromRaw(x), Fixed64.FromRaw(y), Fixed64.FromRaw(z), Fixed64.FromRaw(w));
        public static FixedQuaternion identity => new FixedQuaternion(Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.One);
        public static FixedQuaternion NaN => new FixedQuaternion(Fixed64.NaN, Fixed64.NaN, Fixed64.NaN, Fixed64.NaN);
        public void Set(Fixed64 x, Fixed64 y, Fixed64 z, Fixed64 w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public Fixed64 this[int index]
        {
            readonly get { if (TryGetComponent(index, out Fixed64 value)) return value; throw new IndexOutOfRangeException("Invalid quaternion component."); }
            set { if (!TrySetComponent(index, value)) throw new IndexOutOfRangeException("Invalid quaternion component."); }
        }
        public readonly bool TryGetComponent(int index, out Fixed64 value)
        { switch (index) { case 0: value = x; return true; case 1: value = y; return true; case 2: value = z; return true; case 3: value = w; return true; default: value = default; return false; } }
        public bool TrySetComponent(int index, Fixed64 value)
        { switch (index) { case 0: x = value; return true; case 1: y = value; return true; case 2: z = value; return true; case 3: w = value; return true; default: return false; } }
        public readonly FixedQuaternion normalized => Normalize(this);
        public static bool IsFinite(in FixedQuaternion value) => Fixed64.IsFinite(value.x) && Fixed64.IsFinite(value.y) && Fixed64.IsFinite(value.z) && Fixed64.IsFinite(value.w);
        public static bool IsNaN(in FixedQuaternion value) => Fixed64.IsNaN(value.x) || Fixed64.IsNaN(value.y) || Fixed64.IsNaN(value.z) || Fixed64.IsNaN(value.w);
        public static FixedQuaternion Normalize(in FixedQuaternion value) => Normalize(value, out _);
        public static FixedQuaternion Normalize(in FixedQuaternion value, out FixedStatus status) => FromBits(FixedQuaternionKernel.Normalize(value.Bits, FixedRuntime.State, out status));
        public static bool TryNormalize(in FixedQuaternion value, out FixedQuaternion result, out FixedStatus status)
        { result = Normalize(value, out status); return status.IsSuccess(); }
        public void Normalize() => Normalize(out _);
        public void Normalize(out FixedStatus status) { FixedQuaternion result = Normalize(this, out status); if (status.IsSuccess()) this = result; }
        public bool TryNormalize(out FixedStatus status) { Normalize(out status); return status.IsSuccess(); }
        public static FixedQuaternion Multiply(in FixedQuaternion a, in FixedQuaternion b) => Multiply(a, b, out _);
        public static FixedQuaternion Multiply(in FixedQuaternion a, in FixedQuaternion b, out FixedStatus status) => FromBits(FixedQuaternionKernel.Multiply(a.Bits, b.Bits, FixedRuntime.State, out status));
        public static bool TryMultiply(in FixedQuaternion a, in FixedQuaternion b, out FixedQuaternion result, out FixedStatus status)
        { result = Multiply(a, b, out status); return status.IsSuccess(); }
        public static FixedVector3 Rotate(in FixedQuaternion rotation, in FixedVector3 point) => Rotate(rotation, point, out _);
        public static FixedVector3 Rotate(in FixedQuaternion rotation, in FixedVector3 point, out FixedStatus status) => FixedVector3.FromBits(FixedQuaternionKernel.Rotate(rotation.Bits, point.Bits, FixedRuntime.State, out status));
        public static bool TryRotate(in FixedQuaternion rotation, in FixedVector3 point, out FixedVector3 result, out FixedStatus status)
        { result = Rotate(rotation, point, out status); return status.IsSuccess(); }
        public static FixedQuaternion Inverse(in FixedQuaternion value) => Inverse(value, out _);
        public static FixedQuaternion Inverse(in FixedQuaternion value, out FixedStatus status) => FromBits(FixedQuaternionKernel.Inverse(value.Bits, FixedRuntime.State, out status));
        public static bool TryInverse(in FixedQuaternion value, out FixedQuaternion result, out FixedStatus status)
        { result = Inverse(value, out status); return status.IsSuccess(); }
        public static FixedQuaternion Conjugate(in FixedQuaternion value) => Conjugate(value, out _);
        public static FixedQuaternion Conjugate(in FixedQuaternion value, out FixedStatus status)
        { var a = Fixed64.Negate(value.x, out var sx); var b = Fixed64.Negate(value.y, out var sy); var c = Fixed64.Negate(value.z, out var sz); status = sx | sy | sz; return new FixedQuaternion(a, b, c, value.w); }
        public static FixedQuaternion Negate(in FixedQuaternion value) => Negate(value, out _);
        public static FixedQuaternion Negate(in FixedQuaternion value, out FixedStatus status)
        { var a = Conjugate(value, out status); a.w = Fixed64.Negate(value.w, out var sw); status |= sw; return a; }
        public static Fixed64 Dot(in FixedQuaternion a, in FixedQuaternion b) => Dot(a, b, out _);
        public static Fixed64 Dot(in FixedQuaternion a, in FixedQuaternion b, out FixedStatus status) => Fixed64.FromBits(FixedQuaternionKernel.Dot(a.Bits, b.Bits, FixedRuntime.State, out status));
        public static Fixed64 Angle(in FixedQuaternion a, in FixedQuaternion b) => Angle(a, b, out _);
        public static Fixed64 Angle(in FixedQuaternion a, in FixedQuaternion b, out FixedStatus status) => Fixed64.FromBits(FixedQuaternionKernel.Angle(a.Bits, b.Bits, true, FixedRuntime.State, out status));
        public static Fixed64 AngleRadians(in FixedQuaternion a, in FixedQuaternion b) => AngleRadians(a, b, out _);
        public static Fixed64 AngleRadians(in FixedQuaternion a, in FixedQuaternion b, out FixedStatus status) => Fixed64.FromBits(FixedQuaternionKernel.Angle(a.Bits, b.Bits, false, FixedRuntime.State, out status));
        public static bool IsNormalized(in FixedQuaternion value) => IsNormalized(value, Fixed64.Epsilon);
        public static bool IsNormalized(in FixedQuaternion value, Fixed64 tolerance)
            => IsFinite(value) && Fixed64.IsFinite(tolerance) && tolerance >= Fixed64.Zero && !(value.x == Fixed64.Zero && value.y == Fixed64.Zero && value.z == Fixed64.Zero && value.w == Fixed64.Zero) && FixedMath.Approximately(Dot(value, value), Fixed64.One, tolerance);
        public static bool TryDot(in FixedQuaternion a, in FixedQuaternion b, out Fixed64 result, out FixedStatus status) { result = Dot(a,b,out status); return status.IsSuccess(); }
        public static bool TryAngle(in FixedQuaternion a, in FixedQuaternion b, out Fixed64 result, out FixedStatus status) { result = Angle(a,b,out status); return status.IsSuccess(); }
        public static bool TryAngleRadians(in FixedQuaternion a, in FixedQuaternion b, out Fixed64 result, out FixedStatus status) { result = AngleRadians(a,b,out status); return status.IsSuccess(); }
        public static bool TryConjugate(in FixedQuaternion value, out FixedQuaternion result, out FixedStatus status) { result = Conjugate(value,out status); return status.IsSuccess(); }
        public static bool TryNegate(in FixedQuaternion value, out FixedQuaternion result, out FixedStatus status) { result = Negate(value,out status); return status.IsSuccess(); }
        public static bool ApproximatelyRotation(in FixedQuaternion a, in FixedQuaternion b, Fixed64 maxDegrees)
        { Fixed64 angle = Angle(a, b, out var status); return status.IsSuccess() && Fixed64.IsFinite(maxDegrees) && maxDegrees >= Fixed64.Zero && angle <= maxDegrees; }
        public static FixedQuaternion CanonicalizeSign(in FixedQuaternion value)
        {
            if (!IsFinite(value)) return NaN;
            Fixed64 first = value.w != Fixed64.Zero ? value.w : value.x != Fixed64.Zero ? value.x : value.y != Fixed64.Zero ? value.y : value.z;
            var q = first < Fixed64.Zero ? Negate(value) : value;
            if (q.x == Fixed64.Zero) q.x = Fixed64.Zero; if (q.y == Fixed64.Zero) q.y = Fixed64.Zero;
            if (q.z == Fixed64.Zero) q.z = Fixed64.Zero; if (q.w == Fixed64.Zero) q.w = Fixed64.Zero;
            return q;
        }
        public static FixedQuaternion Lerp(in FixedQuaternion a, in FixedQuaternion b, Fixed64 t) => Lerp(a, b, t, out _);
        public static FixedQuaternion Lerp(in FixedQuaternion a, in FixedQuaternion b, Fixed64 t, out FixedStatus status) => FromBits(FixedQuaternionKernel.Interpolate(a.Bits, b.Bits, t.Bits, true, false, FixedRuntime.State, out status));
        public static FixedQuaternion LerpUnclamped(in FixedQuaternion a, in FixedQuaternion b, Fixed64 t) => LerpUnclamped(a, b, t, out _);
        public static FixedQuaternion LerpUnclamped(in FixedQuaternion a, in FixedQuaternion b, Fixed64 t, out FixedStatus status) => FromBits(FixedQuaternionKernel.Interpolate(a.Bits, b.Bits, t.Bits, false, false, FixedRuntime.State, out status));
        public static FixedQuaternion Slerp(in FixedQuaternion a, in FixedQuaternion b, Fixed64 t) => Slerp(a, b, t, out _);
        public static FixedQuaternion Slerp(in FixedQuaternion a, in FixedQuaternion b, Fixed64 t, out FixedStatus status) => FromBits(FixedQuaternionKernel.Interpolate(a.Bits, b.Bits, t.Bits, true, true, FixedRuntime.State, out status));
        public static FixedQuaternion SlerpUnclamped(in FixedQuaternion a, in FixedQuaternion b, Fixed64 t) => SlerpUnclamped(a, b, t, out _);
        public static FixedQuaternion SlerpUnclamped(in FixedQuaternion a, in FixedQuaternion b, Fixed64 t, out FixedStatus status) => FromBits(FixedQuaternionKernel.Interpolate(a.Bits, b.Bits, t.Bits, false, true, FixedRuntime.State, out status));
        public static FixedQuaternion RotateTowards(in FixedQuaternion a, in FixedQuaternion b, Fixed64 maxDegreesDelta) => RotateTowards(a, b, maxDegreesDelta, out _);
        public static FixedQuaternion RotateTowards(in FixedQuaternion a, in FixedQuaternion b, Fixed64 maxDegreesDelta, out FixedStatus status) => FromBits(FixedQuaternionKernel.RotateTowards(a.Bits, b.Bits, maxDegreesDelta.Bits, true, FixedRuntime.State, out status));
        public static FixedQuaternion RotateTowardsRadians(in FixedQuaternion a, in FixedQuaternion b, Fixed64 maxRadiansDelta) => RotateTowardsRadians(a, b, maxRadiansDelta, out _);
        public static FixedQuaternion RotateTowardsRadians(in FixedQuaternion a, in FixedQuaternion b, Fixed64 maxRadiansDelta, out FixedStatus status) => FromBits(FixedQuaternionKernel.RotateTowards(a.Bits, b.Bits, maxRadiansDelta.Bits, false, FixedRuntime.State, out status));
        public static FixedQuaternion operator *(FixedQuaternion a, FixedQuaternion b) => Multiply(a, b);
        public static FixedVector3 operator *(FixedQuaternion rotation, FixedVector3 point) => Rotate(rotation, point);
        public static FixedQuaternion operator -(FixedQuaternion value) => Negate(value);
        public static bool operator ==(FixedQuaternion a, FixedQuaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
        public static bool operator !=(FixedQuaternion a, FixedQuaternion b) => !(a == b);
        public readonly bool Equals(FixedQuaternion other) => x.Equals(other.x) && y.Equals(other.y) && z.Equals(other.z) && w.Equals(other.w);
        public override readonly bool Equals(object obj) => obj is FixedQuaternion other && Equals(other);
        public override readonly int GetHashCode() { unchecked { int h = 17; h = h * 31 + x.GetHashCode(); h = h * 31 + y.GetHashCode(); h = h * 31 + z.GetHashCode(); return h * 31 + w.GetHashCode(); } }
        public override readonly string ToString() => ToString(null, CultureInfo.InvariantCulture);
        public readonly string ToString(string format) => ToString(format, CultureInfo.InvariantCulture);
        public readonly string ToString(string format, IFormatProvider provider)
        { provider = provider ?? CultureInfo.InvariantCulture; return string.Format(provider, "({0}, {1}, {2}, {3})", x.ToString(format, provider), y.ToString(format, provider), z.ToString(format, provider), w.ToString(format, provider)); }
    }
}
