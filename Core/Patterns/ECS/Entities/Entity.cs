namespace LegendaryTools.Common.Core.Patterns.ECS.Entities
{
    /// <summary>
    /// A stable value identifier for an entity: (Index, Version).
    /// Version is used to detect stale references after destroy/reuse.
    /// </summary>
    public readonly struct Entity
    {
        /// <summary>
        /// Gets the entity index in the world's entity arrays.
        /// </summary>
        public readonly int Index;

        /// <summary>
        /// Gets the entity version for stale reference detection.
        /// </summary>
        public readonly int Version;

        /// <summary>
        /// Initializes a new instance of the <see cref="Entity"/> struct.
        /// </summary>
        /// <param name="index">Entity index.</param>
        /// <param name="version">Entity version.</param>
        public Entity(int index, int version)
        {
            Index = index;
            Version = version;
        }

        /// <summary>
        /// Gets an invalid entity value.
        /// </summary>
        public static Entity Invalid => new(-1, 0);

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"Entity(Index={Index}, Version={Version})";
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return (Index * 397) ^ Version;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is Entity other && Equals(other);
        }

        /// <summary>
        /// Checks value equality.
        /// </summary>
        /// <param name="other">Other entity.</param>
        /// <returns>True if both index and version match.</returns>
        public bool Equals(Entity other)
        {
            return Index == other.Index && Version == other.Version;
        }

        /// <summary>
        /// Checks if two entities are equal.
        /// </summary>
        /// <param name="a">First entity.</param>
        /// <param name="b">Second entity.</param>
        /// <returns>True if equal.</returns>
        public static bool operator ==(Entity a, Entity b)
        {
            return a.Equals(b);
        }

        /// <summary>
        /// Checks if two entities are not equal.
        /// </summary>
        /// <param name="a">First entity.</param>
        /// <param name="b">Second entity.</param>
        /// <returns>True if not equal.</returns>
        public static bool operator !=(Entity a, Entity b)
        {
            return !a.Equals(b);
        }
    }
}