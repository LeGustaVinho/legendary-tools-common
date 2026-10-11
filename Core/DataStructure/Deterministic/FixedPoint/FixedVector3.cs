using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace LegendaryTools.FixedPoint
{
    /// <summary>Deterministic 3D vector. Components use the frozen FixedRuntime settings.</summary>
    [Serializable, StructLayout(LayoutKind.Sequential)]
    public struct FixedVector3 : IEquatable<FixedVector3>, IFormattable
    {
        public Fixed64 x;
        public Fixed64 y;
        public Fixed64 z;
        public FixedVector3(Fixed64 x, Fixed64 y, Fixed64 z) { this.x = x; this.y = y; this.z = z; }
        internal readonly FixedVectorBits Bits => new FixedVectorBits(x.Bits, y.Bits, z.Bits);
        internal static FixedVector3 FromBits(FixedVectorBits value) => new FixedVector3(Fixed64.FromBits(value.X), Fixed64.FromBits(value.Y), Fixed64.FromBits(value.Z));
        public static FixedVector3 FromRaw(long x, long y, long z) => new FixedVector3(Fixed64.FromRaw(x), Fixed64.FromRaw(y), Fixed64.FromRaw(z));
        public void Set(Fixed64 x, Fixed64 y, Fixed64 z) { this.x = x; this.y = y; this.z = z; }
        public Fixed64 this[int index]
        {
            readonly get { if (TryGetComponent(index, out Fixed64 value)) return value; throw new IndexOutOfRangeException("Invalid vector component."); }
            set { if (!TrySetComponent(index, value)) throw new IndexOutOfRangeException("Invalid vector component."); }
        }
        public readonly bool TryGetComponent(int index, out Fixed64 value)
        {
            switch (index) { case 0: value = x; return true; case 1: value = y; return true; case 2: value = z; return true; default: value = default; return false; }
        }
        public bool TrySetComponent(int index, Fixed64 value)
        {
            switch (index) { case 0: x = value; return true; case 1: y = value; return true; case 2: z = value; return true; default: return false; }
        }
        public readonly Fixed64 magnitude => Magnitude(this, out _);
        public readonly Fixed64 sqrMagnitude => SqrMagnitude(this, out _);
        public readonly FixedVector3 normalized => Normalize(this, out _);
        public static Fixed64 Magnitude(in FixedVector3 value, out FixedStatus status) => Fixed64.FromBits(FixedVectorKernel.Magnitude(value.Bits, 3, false, FixedRuntime.State, out status));
        public static Fixed64 SqrMagnitude(in FixedVector3 value, out FixedStatus status) => Fixed64.FromBits(FixedVectorKernel.Magnitude(value.Bits, 3, true, FixedRuntime.State, out status));
        public void Normalize() => Normalize(out _);
        public void Normalize(out FixedStatus status) { this = Normalize(this, out status); }
        public static FixedVector3 Normalize(in FixedVector3 value) => Normalize(value, out _);
        public static FixedVector3 Normalize(in FixedVector3 value, out FixedStatus status) => FromBits(FixedVectorKernel.Normalize(value.Bits, 3, FixedRuntime.State, out status));
        public static bool TryNormalize(in FixedVector3 value, out FixedVector3 result, out FixedStatus status)
        { result = Normalize(value, out status); return status.IsSuccess(); }
        public static bool IsFinite(in FixedVector3 value) => Fixed64.IsFinite(value.x) && Fixed64.IsFinite(value.y) && Fixed64.IsFinite(value.z);
        public static bool IsNaN(in FixedVector3 value) => Fixed64.IsNaN(value.x) || Fixed64.IsNaN(value.y) || Fixed64.IsNaN(value.z);
        public static FixedVector3 zero => default;
        public static FixedVector3 one => new FixedVector3(Fixed64.One, Fixed64.One, Fixed64.One);
        public static FixedVector3 positiveInfinity => new FixedVector3(Fixed64.PositiveInfinity, Fixed64.PositiveInfinity, Fixed64.PositiveInfinity);
        public static FixedVector3 negativeInfinity => new FixedVector3(Fixed64.NegativeInfinity, Fixed64.NegativeInfinity, Fixed64.NegativeInfinity);
        public static FixedVector3 NaN => new FixedVector3(Fixed64.NaN, Fixed64.NaN, Fixed64.NaN);
        public static FixedVector3 right => new FixedVector3(Fixed64.One, Fixed64.Zero, Fixed64.Zero);
        public static FixedVector3 left => new FixedVector3(-Fixed64.One, Fixed64.Zero, Fixed64.Zero);
        public static FixedVector3 up => new FixedVector3(Fixed64.Zero, Fixed64.One, Fixed64.Zero);
        public static FixedVector3 down => new FixedVector3(Fixed64.Zero, -Fixed64.One, Fixed64.Zero);
        public static FixedVector3 forward => new FixedVector3(Fixed64.Zero, Fixed64.Zero, Fixed64.One);
        public static FixedVector3 back => new FixedVector3(Fixed64.Zero, Fixed64.Zero, -Fixed64.One);
        public static FixedVector3 Add(in FixedVector3 a, in FixedVector3 b) => Add(a, b, out _);
        public static FixedVector3 Add(in FixedVector3 a, in FixedVector3 b, out FixedStatus status) => FromBits(FixedVectorKernel.Binary(a.Bits, b.Bits, 3, FixedBinary.Add, FixedRuntime.State, out status));
        public static FixedVector3 Subtract(in FixedVector3 a, in FixedVector3 b) => Subtract(a, b, out _);
        public static FixedVector3 Subtract(in FixedVector3 a, in FixedVector3 b, out FixedStatus status) => FromBits(FixedVectorKernel.Binary(a.Bits, b.Bits, 3, FixedBinary.Subtract, FixedRuntime.State, out status));
        public static FixedVector3 Scale(in FixedVector3 a, in FixedVector3 b) => Scale(a, b, out _);
        public static FixedVector3 Scale(in FixedVector3 a, in FixedVector3 b, out FixedStatus status) => FromBits(FixedVectorKernel.Binary(a.Bits, b.Bits, 3, FixedBinary.Multiply, FixedRuntime.State, out status));
        public void Scale(FixedVector3 scale) { this = Scale(this, scale); }
        public void Scale(FixedVector3 scale, out FixedStatus status) { this = Scale(this, scale, out status); }
        public static FixedVector3 Negate(in FixedVector3 value, out FixedStatus status) => FromBits(FixedVectorKernel.Negate(value.Bits, 3, FixedRuntime.State, out status));
        public static FixedVector3 Multiply(in FixedVector3 value, Fixed64 scalar) => Multiply(value, scalar, out _);
        public static FixedVector3 Multiply(in FixedVector3 value, Fixed64 scalar, out FixedStatus status) => FromBits(FixedVectorKernel.Binary(value.Bits, new FixedVectorBits(scalar.Bits, scalar.Bits, scalar.Bits), 3, FixedBinary.Multiply, FixedRuntime.State, out status));
        public static FixedVector3 Divide(in FixedVector3 value, Fixed64 scalar) => Divide(value, scalar, out _);
        public static FixedVector3 Divide(in FixedVector3 value, Fixed64 scalar, out FixedStatus status) => FromBits(FixedVectorKernel.Binary(value.Bits, new FixedVectorBits(scalar.Bits, scalar.Bits, scalar.Bits), 3, FixedBinary.Divide, FixedRuntime.State, out status));
        public static bool TryDivide(in FixedVector3 value, Fixed64 scalar, out FixedVector3 result, out FixedStatus status)
        { result = Divide(value, scalar, out status); return status.IsSuccess(); }
        public static Fixed64 Dot(in FixedVector3 a, in FixedVector3 b) => Dot(a, b, out _);
        public static Fixed64 Dot(in FixedVector3 a, in FixedVector3 b, out FixedStatus status) => Fixed64.FromBits(FixedVectorKernel.Dot(a.Bits, b.Bits, 3, FixedRuntime.State, out status));
        public static Fixed64 Distance(in FixedVector3 a, in FixedVector3 b) => Distance(a, b, out _);
        public static Fixed64 Distance(in FixedVector3 a, in FixedVector3 b, out FixedStatus status) => Fixed64.FromBits(FixedVectorKernel.Distance(a.Bits, b.Bits, 3, FixedRuntime.State, out status));
        public static FixedVector3 Min(in FixedVector3 a, in FixedVector3 b) => new FixedVector3(FixedMath.Min(a.x, b.x), FixedMath.Min(a.y, b.y), FixedMath.Min(a.z, b.z));
        public static FixedVector3 Max(in FixedVector3 a, in FixedVector3 b) => new FixedVector3(FixedMath.Max(a.x, b.x), FixedMath.Max(a.y, b.y), FixedMath.Max(a.z, b.z));
        public static FixedVector3 Lerp(in FixedVector3 a, in FixedVector3 b, Fixed64 t) => Lerp(a, b, t, out _);
        public static FixedVector3 Lerp(in FixedVector3 a, in FixedVector3 b, Fixed64 t, out FixedStatus status) => FromBits(FixedVectorKernel.Lerp(a.Bits, b.Bits, t.Bits, 3, true, FixedRuntime.State, out status));
        public static FixedVector3 LerpUnclamped(in FixedVector3 a, in FixedVector3 b, Fixed64 t) => LerpUnclamped(a, b, t, out _);
        public static FixedVector3 LerpUnclamped(in FixedVector3 a, in FixedVector3 b, Fixed64 t, out FixedStatus status) => FromBits(FixedVectorKernel.Lerp(a.Bits, b.Bits, t.Bits, 3, false, FixedRuntime.State, out status));
        public static FixedVector3 MoveTowards(in FixedVector3 current, in FixedVector3 target, Fixed64 maxDistanceDelta) => MoveTowards(current, target, maxDistanceDelta, out _);
        public static FixedVector3 MoveTowards(in FixedVector3 current, in FixedVector3 target, Fixed64 maxDistanceDelta, out FixedStatus status) => FromBits(FixedVectorKernel.MoveTowards(current.Bits, target.Bits, maxDistanceDelta.Bits, 3, FixedRuntime.State, out status));
        public static FixedVector3 ClampMagnitude(in FixedVector3 vector, Fixed64 maxLength) => ClampMagnitude(vector, maxLength, out _);
        public static FixedVector3 ClampMagnitude(in FixedVector3 vector, Fixed64 maxLength, out FixedStatus status) => FromBits(FixedVectorKernel.ClampMagnitude(vector.Bits, maxLength.Bits, 3, FixedRuntime.State, out status));
        public static FixedVector3 Reflect(in FixedVector3 direction, in FixedVector3 normal) => Reflect(direction, normal, out _);
        public static FixedVector3 Reflect(in FixedVector3 direction, in FixedVector3 normal, out FixedStatus status) => FromBits(FixedVectorKernel.Reflect(direction.Bits, normal.Bits, 3, FixedRuntime.State, out status));
        public static Fixed64 Angle(in FixedVector3 from, in FixedVector3 to) => Angle(from, to, out _);
        public static Fixed64 Angle(in FixedVector3 from, in FixedVector3 to, out FixedStatus status) => Fixed64.FromBits(FixedVectorKernel.Angle(from.Bits, to.Bits, default, 3, false, FixedRuntime.State, out status));
        public static bool Approximately(in FixedVector3 a, in FixedVector3 b) => Approximately(a, b, Fixed64.Epsilon);
        public static bool Approximately(in FixedVector3 a, in FixedVector3 b, Fixed64 tolerance) => FixedMath.Approximately(a.x, b.x, tolerance) && FixedMath.Approximately(a.y, b.y, tolerance) && FixedMath.Approximately(a.z, b.z, tolerance);
        /// <summary>deltaTime is supplied by the deterministic simulation clock; maxSpeed may be +Infinity.</summary>
        public static FixedVector3 SmoothDamp(in FixedVector3 current, in FixedVector3 target, ref FixedVector3 currentVelocity, Fixed64 smoothTime, Fixed64 maxSpeed, Fixed64 deltaTime) => SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime, out _);
        public static FixedVector3 SmoothDamp(in FixedVector3 current, in FixedVector3 target, ref FixedVector3 currentVelocity, Fixed64 smoothTime, Fixed64 maxSpeed, Fixed64 deltaTime, out FixedStatus status)
        {
            FixedVectorBits result = FixedVectorKernel.SmoothDamp(current.Bits, target.Bits, currentVelocity.Bits, smoothTime.Bits, maxSpeed.Bits, deltaTime.Bits, 3, FixedRuntime.State, out FixedVectorBits velocity, out status);
            if (status.IsSuccess()) currentVelocity = FromBits(velocity);
            return FromBits(result);
        }
        public static bool TrySmoothDamp(in FixedVector3 current, in FixedVector3 target, ref FixedVector3 currentVelocity, Fixed64 smoothTime, Fixed64 maxSpeed, Fixed64 deltaTime, out FixedVector3 result, out FixedStatus status)
        { result = SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime, out status); return status.IsSuccess(); }
        public FixedVector3(Fixed64 x, Fixed64 y) : this(x, y, Fixed64.Zero) { }
        public static FixedVector3 Cross(in FixedVector3 a, in FixedVector3 b) => Cross(a, b, out _);
        public static FixedVector3 Cross(in FixedVector3 a, in FixedVector3 b, out FixedStatus status) => FromBits(FixedVectorKernel.Cross(a.Bits, b.Bits, FixedRuntime.State, out status));
        public static Fixed64 SignedAngle(in FixedVector3 from, in FixedVector3 to, in FixedVector3 axis) => SignedAngle(from, to, axis, out _);
        public static Fixed64 SignedAngle(in FixedVector3 from, in FixedVector3 to, in FixedVector3 axis, out FixedStatus status) => Fixed64.FromBits(FixedVectorKernel.Angle(from.Bits, to.Bits, axis.Bits, 3, true, FixedRuntime.State, out status));
        public static FixedVector3 Project(in FixedVector3 vector, in FixedVector3 normal) => Project(vector, normal, out _);
        public static FixedVector3 Project(in FixedVector3 vector, in FixedVector3 normal, out FixedStatus status) => FromBits(FixedVectorKernel.Project(vector.Bits, normal.Bits, false, FixedRuntime.State, out status));
        public static FixedVector3 ProjectOnPlane(in FixedVector3 vector, in FixedVector3 normal) => ProjectOnPlane(vector, normal, out _);
        public static FixedVector3 ProjectOnPlane(in FixedVector3 vector, in FixedVector3 normal, out FixedStatus status) => FromBits(FixedVectorKernel.Project(vector.Bits, normal.Bits, true, FixedRuntime.State, out status));
        public static FixedVector3 Slerp(in FixedVector3 a, in FixedVector3 b, Fixed64 t) => Slerp(a, b, t, out _);
        public static FixedVector3 Slerp(in FixedVector3 a, in FixedVector3 b, Fixed64 t, out FixedStatus status) => FromBits(FixedVectorKernel.Slerp(a.Bits, b.Bits, t.Bits, true, FixedRuntime.State, out status));
        public static FixedVector3 SlerpUnclamped(in FixedVector3 a, in FixedVector3 b, Fixed64 t) => SlerpUnclamped(a, b, t, out _);
        public static FixedVector3 SlerpUnclamped(in FixedVector3 a, in FixedVector3 b, Fixed64 t, out FixedStatus status) => FromBits(FixedVectorKernel.Slerp(a.Bits, b.Bits, t.Bits, false, FixedRuntime.State, out status));
        public static FixedVector3 RotateTowards(in FixedVector3 current, in FixedVector3 target, Fixed64 maxRadiansDelta, Fixed64 maxMagnitudeDelta) => RotateTowards(current, target, maxRadiansDelta, maxMagnitudeDelta, out _);
        public static FixedVector3 RotateTowards(in FixedVector3 current, in FixedVector3 target, Fixed64 maxRadiansDelta, Fixed64 maxMagnitudeDelta, out FixedStatus status) => FromBits(FixedVectorKernel.RotateTowards(current.Bits, target.Bits, maxRadiansDelta.Bits, maxMagnitudeDelta.Bits, FixedRuntime.State, out status));
        public static void OrthoNormalize(ref FixedVector3 normal, ref FixedVector3 tangent) => TryOrthoNormalize(ref normal, ref tangent, out _);
        public static void OrthoNormalize(ref FixedVector3 normal, ref FixedVector3 tangent, out FixedStatus status) => TryOrthoNormalize(ref normal, ref tangent, out status);
        public static bool TryOrthoNormalize(ref FixedVector3 normal, ref FixedVector3 tangent, out FixedStatus status)
        {
            bool success = FixedVectorKernel.OrthoNormalize(normal.Bits, tangent.Bits, default, false, FixedRuntime.State, out FixedVectorBits n, out FixedVectorBits t, out _, out status);
            if (success) { normal = FromBits(n); tangent = FromBits(t); } return success;
        }
        public static void OrthoNormalize(ref FixedVector3 normal, ref FixedVector3 tangent, ref FixedVector3 binormal) => TryOrthoNormalize(ref normal, ref tangent, ref binormal, out _);
        public static void OrthoNormalize(ref FixedVector3 normal, ref FixedVector3 tangent, ref FixedVector3 binormal, out FixedStatus status) => TryOrthoNormalize(ref normal, ref tangent, ref binormal, out status);
        public static bool TryOrthoNormalize(ref FixedVector3 normal, ref FixedVector3 tangent, ref FixedVector3 binormal, out FixedStatus status)
        {
            bool success = FixedVectorKernel.OrthoNormalize(normal.Bits, tangent.Bits, binormal.Bits, true, FixedRuntime.State, out FixedVectorBits n, out FixedVectorBits t, out FixedVectorBits b, out status);
            if (success) { normal = FromBits(n); tangent = FromBits(t); binormal = FromBits(b); } return success;
        }
        public static FixedVector3 operator +(FixedVector3 value) => value;
        public static FixedVector3 operator -(FixedVector3 value) => Negate(value, out _);
        public static FixedVector3 operator +(FixedVector3 a, FixedVector3 b) => Add(a, b);
        public static FixedVector3 operator -(FixedVector3 a, FixedVector3 b) => Subtract(a, b);
        public static FixedVector3 operator *(FixedVector3 value, Fixed64 scalar) => Multiply(value, scalar);
        public static FixedVector3 operator *(Fixed64 scalar, FixedVector3 value) => Multiply(value, scalar);
        public static FixedVector3 operator /(FixedVector3 value, Fixed64 scalar) => Divide(value, scalar);
        public static bool operator ==(FixedVector3 a, FixedVector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
        public static bool operator !=(FixedVector3 a, FixedVector3 b) => !(a == b);
        public readonly bool Equals(FixedVector3 other) => x.Equals(other.x) && y.Equals(other.y) && z.Equals(other.z);
        public override readonly bool Equals(object obj) => obj is FixedVector3 other && Equals(other);
        public override readonly int GetHashCode()
        {
            int hash = 17;
            unchecked { hash = hash * 31 + x.GetHashCode(); hash = hash * 31 + y.GetHashCode(); hash = hash * 31 + z.GetHashCode(); }
            return hash;
        }
        public override readonly string ToString() => ToString(null, CultureInfo.InvariantCulture);
        public readonly string ToString(string format) => ToString(format, CultureInfo.InvariantCulture);
        public readonly string ToString(string format, IFormatProvider provider) => string.Format(provider ?? CultureInfo.InvariantCulture,
            "({0}, {1}, {2})", x.ToString(format, provider ?? CultureInfo.InvariantCulture), y.ToString(format, provider ?? CultureInfo.InvariantCulture), z.ToString(format, provider ?? CultureInfo.InvariantCulture));
    }
}
