namespace LegendaryTools.Common.Core.Patterns.ECS.Storage
{
    /// <summary>
    /// O(1) mapping: entityIndex -> (ArchetypeId, ChunkId, Row).
    /// </summary>
    public struct EntityLocation
    {
        /// <summary>
        /// Gets or sets the archetype id.
        /// </summary>
        public ArchetypeId ArchetypeId;

        /// <summary>
        /// Gets or sets the chunk id within the archetype.
        /// </summary>
        public int ChunkId;

        /// <summary>
        /// Gets or sets the row index within the chunk.
        /// </summary>
        public int Row;

        /// <summary>
        /// Gets a value indicating whether this location points to a valid live placement.
        /// </summary>
        public bool IsValid => ChunkId >= 0 && Row >= 0;

        /// <summary>
        /// Gets an invalid location value.
        /// </summary>
        public static EntityLocation Invalid => new EntityLocation
        {
            ArchetypeId = new ArchetypeId(0),
            ChunkId = -1,
            Row = -1,
        };
    }
}
