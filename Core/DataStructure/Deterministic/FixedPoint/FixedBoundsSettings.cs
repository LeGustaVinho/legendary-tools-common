using System;

namespace LegendaryTools.FixedPoint
{
    public enum FixedBoundsRepresentation : byte { MinMax, CenterExtents }

    /// <summary>Frozen geometry semantics. The representation is shared by all bounds in a domain.</summary>
    public readonly struct FixedBoundsSettings : IEquatable<FixedBoundsSettings>
    {
        public const ushort GeometryVersion = 1;
        public readonly FixedBoundsRepresentation Representation;
        public static FixedBoundsSettings Default => new FixedBoundsSettings(FixedBoundsRepresentation.MinMax);
        public FixedBoundsSettings(FixedBoundsRepresentation representation) { Representation = representation; }
        public bool IsValid => Representation <= FixedBoundsRepresentation.CenterExtents;
        public ulong Fingerprint => unchecked((0x4650424F554E4431UL ^ GeometryVersion) * 1099511628211UL ^ (byte)Representation);
        public bool Equals(FixedBoundsSettings other) => Representation == other.Representation;
        public override bool Equals(object obj) => obj is FixedBoundsSettings other && Equals(other);
        public override int GetHashCode() => (int)Representation;
        public static bool operator ==(FixedBoundsSettings a, FixedBoundsSettings b) => a.Equals(b);
        public static bool operator !=(FixedBoundsSettings a, FixedBoundsSettings b) => !a.Equals(b);
    }
}
