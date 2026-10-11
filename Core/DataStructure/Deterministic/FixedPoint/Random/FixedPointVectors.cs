using System;
using System.Runtime.InteropServices;

namespace LegendaryTools.FixedPoint
{
    [Serializable, StructLayout(LayoutKind.Sequential)]
    public readonly struct FixedPointVector2 : IEquatable<FixedPointVector2>
    {
        public readonly Fixed64 X, Y;
        public FixedPointVector2(Fixed64 x, Fixed64 y) { X = x; Y = y; }
        public static FixedPointVector2 Zero => default;
        public bool Equals(FixedPointVector2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is FixedPointVector2 other && Equals(other);
        public override int GetHashCode() => unchecked(X.GetHashCode() * 397 ^ Y.GetHashCode());
        public static bool operator ==(FixedPointVector2 a, FixedPointVector2 b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(FixedPointVector2 a, FixedPointVector2 b) => !(a == b);
    }

    [Serializable, StructLayout(LayoutKind.Sequential)]
    public readonly struct FixedPointVector3 : IEquatable<FixedPointVector3>
    {
        public readonly Fixed64 X, Y, Z;
        public FixedPointVector3(Fixed64 x, Fixed64 y, Fixed64 z) { X = x; Y = y; Z = z; }
        public static FixedPointVector3 Zero => default;
        public bool Equals(FixedPointVector3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object obj) => obj is FixedPointVector3 other && Equals(other);
        public override int GetHashCode() => unchecked((X.GetHashCode() * 397 ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode());
        public static bool operator ==(FixedPointVector3 a, FixedPointVector3 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
        public static bool operator !=(FixedPointVector3 a, FixedPointVector3 b) => !(a == b);
    }
}
