using System;

namespace LargeWorldCoordinates
{
    /// <summary>
    /// Represents axis-aligned shared world bounds in global units.
    /// One global unit is one centimeter.
    /// </summary>
    [Serializable]
    public struct GlobalBounds : IEquatable<GlobalBounds>
    {
        public GlobalPosition Min;
        public GlobalPosition Max;

        public GlobalBounds(GlobalPosition min, GlobalPosition max)
        {
            Min = new GlobalPosition(
                Math.Min(min.X, max.X),
                Math.Min(min.Y, max.Y),
                Math.Min(min.Z, max.Z));

            Max = new GlobalPosition(
                Math.Max(min.X, max.X),
                Math.Max(min.Y, max.Y),
                Math.Max(min.Z, max.Z));
        }

        /// <summary>
        /// Gets the global size of the bounds.
        /// </summary>
        public GlobalVector Size => new GlobalVector(
            checked(Max.X - Min.X),
            checked(Max.Y - Min.Y),
            checked(Max.Z - Min.Z));

        /// <summary>
        /// Creates bounds from the provided minimum and maximum positions.
        /// </summary>
        /// <param name="min">The minimum global position.</param>
        /// <param name="max">The maximum global position.</param>
        /// <returns>The normalized global bounds.</returns>
        public static GlobalBounds FromMinMax(GlobalPosition min, GlobalPosition max)
        {
            return new GlobalBounds(min, max);
        }

        public static bool operator ==(GlobalBounds left, GlobalBounds right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GlobalBounds left, GlobalBounds right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        public bool Equals(GlobalBounds other)
        {
            return Min.Equals(other.Min) && Max.Equals(other.Max);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is GlobalBounds other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCode.Combine(Min, Max);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"Min={Min}, Max={Max}";
        }
    }
}
