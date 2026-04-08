using System;

namespace LargeWorldCoordinates
{
    /// <summary>
    /// Represents a shared world delta in global units.
    /// One global unit is one centimeter.
    /// </summary>
    [Serializable]
    public struct GlobalVector : IEquatable<GlobalVector>
    {
        public long X;
        public long Y;
        public long Z;

        public GlobalVector(long x, long y, long z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>
        /// Gets the zero global delta.
        /// </summary>
        public static GlobalVector Zero => new GlobalVector(0L, 0L, 0L);

        public static GlobalVector operator +(GlobalVector left, GlobalVector right)
        {
            return new GlobalVector(
                checked(left.X + right.X),
                checked(left.Y + right.Y),
                checked(left.Z + right.Z));
        }

        public static GlobalVector operator -(GlobalVector left, GlobalVector right)
        {
            return new GlobalVector(
                checked(left.X - right.X),
                checked(left.Y - right.Y),
                checked(left.Z - right.Z));
        }

        public static bool operator ==(GlobalVector left, GlobalVector right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GlobalVector left, GlobalVector right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        public bool Equals(GlobalVector other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is GlobalVector other && Equals(other);
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
