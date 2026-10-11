using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace LegendaryTools.FixedPoint
{
    /// <summary>Deterministic 2D vector. Components use the frozen FixedRuntime settings.</summary>
    [Serializable, StructLayout(LayoutKind.Sequential)]
    public struct FixedVector2 : IEquatable<FixedVector2>, IFormattable
    {
        public Fixed64 x;
        public Fixed64 y;
        public FixedVector2(Fixed64 x, Fixed64 y) { this.x = x; this.y = y; }
        internal readonly FixedVectorBits Bits => new FixedVectorBits(x.Bits, y.Bits);
        internal static FixedVector2 FromBits(FixedVectorBits value) => new FixedVector2(Fixed64.FromBits(value.X), Fixed64.FromBits(value.Y));
        public static FixedVector2 FromRaw(long x, long y) => new FixedVector2(Fixed64.FromRaw(x), Fixed64.FromRaw(y));
        public void Set(Fixed64 x, Fixed64 y) { this.x = x; this.y = y; }
        public Fixed64 this[int index]
        {
            readonly get { if (TryGetComponent(index, out Fixed64 value)) return value; throw new IndexOutOfRangeException("Invalid vector component."); }
            set { if (!TrySetComponent(index, value)) throw new IndexOutOfRangeException("Invalid vector component."); }
        }
        public readonly bool TryGetComponent(int index, out Fixed64 value)
        {
            switch (index) { case 0: value = x; return true; case 1: value = y; return true; default: value = default; return false; }
        }
        public bool TrySetComponent(int index, Fixed64 value)
        {
            switch (index) { case 0: x = value; return true; case 1: y = value; return true; default: return false; }
        }
        public readonly Fixed64 magnitude => Magnitude(this, out _);
        public readonly Fixed64 sqrMagnitude => SqrMagnitude(this, out _);
        public readonly FixedVector2 normalized => Normalize(this, out _);
        public static Fixed64 Magnitude(in FixedVector2 value, out FixedStatus status) => Fixed64.FromBits(FixedVectorKernel.Magnitude(value.Bits, 2, false, FixedRuntime.State, out status));
        public static Fixed64 SqrMagnitude(in FixedVector2 value, out FixedStatus status) => Fixed64.FromBits(FixedVectorKernel.Magnitude(value.Bits, 2, true, FixedRuntime.State, out status));
        public void Normalize() => Normalize(out _);
        public void Normalize(out FixedStatus status) { this = Normalize(this, out status); }
        public static FixedVector2 Normalize(in FixedVector2 value) => Normalize(value, out _);
        public static FixedVector2 Normalize(in FixedVector2 value, out FixedStatus status) => FromBits(FixedVectorKernel.Normalize(value.Bits, 2, FixedRuntime.State, out status));
        public static bool TryNormalize(in FixedVector2 value, out FixedVector2 result, out FixedStatus status)
        { result = Normalize(value, out status); return status.IsSuccess(); }
        public static bool IsFinite(in FixedVector2 value) => Fixed64.IsFinite(value.x) && Fixed64.IsFinite(value.y);
        public static bool IsNaN(in FixedVector2 value) => Fixed64.IsNaN(value.x) || Fixed64.IsNaN(value.y);
        public static FixedVector2 zero => default;
        public static FixedVector2 one => new FixedVector2(Fixed64.One, Fixed64.One);
        public static FixedVector2 positiveInfinity => new FixedVector2(Fixed64.PositiveInfinity, Fixed64.PositiveInfinity);
        public static FixedVector2 negativeInfinity => new FixedVector2(Fixed64.NegativeInfinity, Fixed64.NegativeInfinity);
        public static FixedVector2 NaN => new FixedVector2(Fixed64.NaN, Fixed64.NaN);
        public static FixedVector2 right => new FixedVector2(Fixed64.One, Fixed64.Zero);
        public static FixedVector2 left => new FixedVector2(-Fixed64.One, Fixed64.Zero);
        public static FixedVector2 up => new FixedVector2(Fixed64.Zero, Fixed64.One);
        public static FixedVector2 down => new FixedVector2(Fixed64.Zero, -Fixed64.One);
        public static FixedVector2 Add(in FixedVector2 a, in FixedVector2 b) => Add(a, b, out _);
        public static FixedVector2 Add(in FixedVector2 a, in FixedVector2 b, out FixedStatus status) => FromBits(FixedVectorKernel.Binary(a.Bits, b.Bits, 2, FixedBinary.Add, FixedRuntime.State, out status));
        public static FixedVector2 Subtract(in FixedVector2 a, in FixedVector2 b) => Subtract(a, b, out _);
        public static FixedVector2 Subtract(in FixedVector2 a, in FixedVector2 b, out FixedStatus status) => FromBits(FixedVectorKernel.Binary(a.Bits, b.Bits, 2, FixedBinary.Subtract, FixedRuntime.State, out status));
        public static FixedVector2 Scale(in FixedVector2 a, in FixedVector2 b) => Scale(a, b, out _);
        public static FixedVector2 Scale(in FixedVector2 a, in FixedVector2 b, out FixedStatus status) => FromBits(FixedVectorKernel.Binary(a.Bits, b.Bits, 2, FixedBinary.Multiply, FixedRuntime.State, out status));
        public void Scale(FixedVector2 scale) { this = Scale(this, scale); }
        public void Scale(FixedVector2 scale, out FixedStatus status) { this = Scale(this, scale, out status); }
        public static FixedVector2 Negate(in FixedVector2 value, out FixedStatus status) => FromBits(FixedVectorKernel.Negate(value.Bits, 2, FixedRuntime.State, out status));
        public static FixedVector2 Multiply(in FixedVector2 value, Fixed64 scalar) => Multiply(value, scalar, out _);
        public static FixedVector2 Multiply(in FixedVector2 value, Fixed64 scalar, out FixedStatus status) => FromBits(FixedVectorKernel.Binary(value.Bits, new FixedVectorBits(scalar.Bits, scalar.Bits, scalar.Bits), 2, FixedBinary.Multiply, FixedRuntime.State, out status));
        public static FixedVector2 Divide(in FixedVector2 value, Fixed64 scalar) => Divide(value, scalar, out _);
        public static FixedVector2 Divide(in FixedVector2 value, Fixed64 scalar, out FixedStatus status) => FromBits(FixedVectorKernel.Binary(value.Bits, new FixedVectorBits(scalar.Bits, scalar.Bits, scalar.Bits), 2, FixedBinary.Divide, FixedRuntime.State, out status));
        public static bool TryDivide(in FixedVector2 value, Fixed64 scalar, out FixedVector2 result, out FixedStatus status)
        { result = Divide(value, scalar, out status); return status.IsSuccess(); }
        public static Fixed64 Dot(in FixedVector2 a, in FixedVector2 b) => Dot(a, b, out _);
        public static Fixed64 Dot(in FixedVector2 a, in FixedVector2 b, out FixedStatus status) => Fixed64.FromBits(FixedVectorKernel.Dot(a.Bits, b.Bits, 2, FixedRuntime.State, out status));
        public static Fixed64 Distance(in FixedVector2 a, in FixedVector2 b) => Distance(a, b, out _);
        public static Fixed64 Distance(in FixedVector2 a, in FixedVector2 b, out FixedStatus status) => Fixed64.FromBits(FixedVectorKernel.Distance(a.Bits, b.Bits, 2, FixedRuntime.State, out status));
        public static FixedVector2 Min(in FixedVector2 a, in FixedVector2 b) => new FixedVector2(FixedMath.Min(a.x, b.x), FixedMath.Min(a.y, b.y));
        public static FixedVector2 Max(in FixedVector2 a, in FixedVector2 b) => new FixedVector2(FixedMath.Max(a.x, b.x), FixedMath.Max(a.y, b.y));
        public static FixedVector2 Lerp(in FixedVector2 a, in FixedVector2 b, Fixed64 t) => Lerp(a, b, t, out _);
        public static FixedVector2 Lerp(in FixedVector2 a, in FixedVector2 b, Fixed64 t, out FixedStatus status) => FromBits(FixedVectorKernel.Lerp(a.Bits, b.Bits, t.Bits, 2, true, FixedRuntime.State, out status));
        public static FixedVector2 LerpUnclamped(in FixedVector2 a, in FixedVector2 b, Fixed64 t) => LerpUnclamped(a, b, t, out _);
        public static FixedVector2 LerpUnclamped(in FixedVector2 a, in FixedVector2 b, Fixed64 t, out FixedStatus status) => FromBits(FixedVectorKernel.Lerp(a.Bits, b.Bits, t.Bits, 2, false, FixedRuntime.State, out status));
        public static FixedVector2 MoveTowards(in FixedVector2 current, in FixedVector2 target, Fixed64 maxDistanceDelta) => MoveTowards(current, target, maxDistanceDelta, out _);
        public static FixedVector2 MoveTowards(in FixedVector2 current, in FixedVector2 target, Fixed64 maxDistanceDelta, out FixedStatus status) => FromBits(FixedVectorKernel.MoveTowards(current.Bits, target.Bits, maxDistanceDelta.Bits, 2, FixedRuntime.State, out status));
        public static FixedVector2 ClampMagnitude(in FixedVector2 vector, Fixed64 maxLength) => ClampMagnitude(vector, maxLength, out _);
        public static FixedVector2 ClampMagnitude(in FixedVector2 vector, Fixed64 maxLength, out FixedStatus status) => FromBits(FixedVectorKernel.ClampMagnitude(vector.Bits, maxLength.Bits, 2, FixedRuntime.State, out status));
        public static FixedVector2 Reflect(in FixedVector2 direction, in FixedVector2 normal) => Reflect(direction, normal, out _);
        public static FixedVector2 Reflect(in FixedVector2 direction, in FixedVector2 normal, out FixedStatus status) => FromBits(FixedVectorKernel.Reflect(direction.Bits, normal.Bits, 2, FixedRuntime.State, out status));
        public static Fixed64 Angle(in FixedVector2 from, in FixedVector2 to) => Angle(from, to, out _);
        public static Fixed64 Angle(in FixedVector2 from, in FixedVector2 to, out FixedStatus status) => Fixed64.FromBits(FixedVectorKernel.Angle(from.Bits, to.Bits, default, 2, false, FixedRuntime.State, out status));
        public static bool Approximately(in FixedVector2 a, in FixedVector2 b) => Approximately(a, b, Fixed64.Epsilon);
        public static bool Approximately(in FixedVector2 a, in FixedVector2 b, Fixed64 tolerance) => FixedMath.Approximately(a.x, b.x, tolerance) && FixedMath.Approximately(a.y, b.y, tolerance);
        /// <summary>deltaTime is supplied by the deterministic simulation clock; maxSpeed may be +Infinity.</summary>
        public static FixedVector2 SmoothDamp(in FixedVector2 current, in FixedVector2 target, ref FixedVector2 currentVelocity, Fixed64 smoothTime, Fixed64 maxSpeed, Fixed64 deltaTime) => SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime, out _);
        public static FixedVector2 SmoothDamp(in FixedVector2 current, in FixedVector2 target, ref FixedVector2 currentVelocity, Fixed64 smoothTime, Fixed64 maxSpeed, Fixed64 deltaTime, out FixedStatus status)
        {
            FixedVectorBits result = FixedVectorKernel.SmoothDamp(current.Bits, target.Bits, currentVelocity.Bits, smoothTime.Bits, maxSpeed.Bits, deltaTime.Bits, 2, FixedRuntime.State, out FixedVectorBits velocity, out status);
            if (status.IsSuccess()) currentVelocity = FromBits(velocity);
            return FromBits(result);
        }
        public static bool TrySmoothDamp(in FixedVector2 current, in FixedVector2 target, ref FixedVector2 currentVelocity, Fixed64 smoothTime, Fixed64 maxSpeed, Fixed64 deltaTime, out FixedVector2 result, out FixedStatus status)
        { result = SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime, out status); return status.IsSuccess(); }
        public static FixedVector2 Perpendicular(in FixedVector2 value) => Perpendicular(value, out _);
        public static FixedVector2 Perpendicular(in FixedVector2 value, out FixedStatus status) => new FixedVector2(Fixed64.Negate(value.y, out status), value.x);
        public static Fixed64 SignedAngle(in FixedVector2 from, in FixedVector2 to) => SignedAngle(from, to, out _);
        public static Fixed64 SignedAngle(in FixedVector2 from, in FixedVector2 to, out FixedStatus status) => Fixed64.FromBits(FixedVectorKernel.Angle(from.Bits, to.Bits, default, 2, true, FixedRuntime.State, out status));
        public static FixedVector2 Divide(in FixedVector2 a, in FixedVector2 b, out FixedStatus status) => FromBits(FixedVectorKernel.Binary(a.Bits, b.Bits, 2, FixedBinary.Divide, FixedRuntime.State, out status));
        public static bool TryDivide(in FixedVector2 a, in FixedVector2 b, out FixedVector2 result, out FixedStatus status)
        { result = Divide(a, b, out status); return status.IsSuccess(); }
        public static FixedVector2 operator *(FixedVector2 a, FixedVector2 b) => Scale(a, b);
        public static FixedVector2 operator /(FixedVector2 a, FixedVector2 b) => Divide(a, b, out _);
        public static implicit operator FixedVector3(FixedVector2 value) => new FixedVector3(value.x, value.y, Fixed64.Zero);
        public static explicit operator FixedVector2(FixedVector3 value) => new FixedVector2(value.x, value.y);
        public static FixedVector2 operator +(FixedVector2 value) => value;
        public static FixedVector2 operator -(FixedVector2 value) => Negate(value, out _);
        public static FixedVector2 operator +(FixedVector2 a, FixedVector2 b) => Add(a, b);
        public static FixedVector2 operator -(FixedVector2 a, FixedVector2 b) => Subtract(a, b);
        public static FixedVector2 operator *(FixedVector2 value, Fixed64 scalar) => Multiply(value, scalar);
        public static FixedVector2 operator *(Fixed64 scalar, FixedVector2 value) => Multiply(value, scalar);
        public static FixedVector2 operator /(FixedVector2 value, Fixed64 scalar) => Divide(value, scalar);
        public static bool operator ==(FixedVector2 a, FixedVector2 b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(FixedVector2 a, FixedVector2 b) => !(a == b);
        public readonly bool Equals(FixedVector2 other) => x.Equals(other.x) && y.Equals(other.y);
        public override readonly bool Equals(object obj) => obj is FixedVector2 other && Equals(other);
        public override readonly int GetHashCode()
        {
            int hash = 17;
            unchecked { hash = hash * 31 + x.GetHashCode(); hash = hash * 31 + y.GetHashCode(); }
            return hash;
        }
        public override readonly string ToString() => ToString(null, CultureInfo.InvariantCulture);
        public readonly string ToString(string format) => ToString(format, CultureInfo.InvariantCulture);
        public readonly string ToString(string format, IFormatProvider provider) => string.Format(provider ?? CultureInfo.InvariantCulture,
            "({0}, {1})", x.ToString(format, provider ?? CultureInfo.InvariantCulture), y.ToString(format, provider ?? CultureInfo.InvariantCulture));
    }
}
