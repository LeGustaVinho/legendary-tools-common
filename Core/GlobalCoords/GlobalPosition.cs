using System;
using UnityEngine;

namespace LargeWorldCoordinates
{
    /// <summary>
    /// Represents a shared world position in global units.
    /// One global unit is one centimeter.
    /// </summary>
    [Serializable]
    public struct GlobalPosition : IEquatable<GlobalPosition>
    {
        public long X;
        public long Y;
        public long Z;

        public GlobalPosition(long x, long y, long z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>
        /// Gets the zero global position.
        /// </summary>
        public static GlobalPosition Zero => new GlobalPosition(0L, 0L, 0L);

        public static GlobalPosition operator +(GlobalPosition left, GlobalPosition right)
        {
            return new GlobalPosition(
                checked(left.X + right.X),
                checked(left.Y + right.Y),
                checked(left.Z + right.Z));
        }

        public static GlobalPosition operator -(GlobalPosition left, GlobalPosition right)
        {
            return new GlobalPosition(
                checked(left.X - right.X),
                checked(left.Y - right.Y),
                checked(left.Z - right.Z));
        }

        public static bool operator ==(GlobalPosition left, GlobalPosition right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GlobalPosition left, GlobalPosition right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        public bool Equals(GlobalPosition other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is GlobalPosition other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Z);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"({X}, {Y}, {Z}) cm";
        }
    }
}
