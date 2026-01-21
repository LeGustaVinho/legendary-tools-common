namespace LegendaryTools.Common.Core.Patterns.ECS.Storage
{
    /// <summary>
    /// Defines how an archetype decides which chunk to use when inserting a new entity.
    /// </summary>
    public enum ChunkAllocationPolicy : byte
    {
        /// <summary>
        /// Scans chunks linearly and picks the first one with space. Falls back to creating a new chunk.
        /// </summary>
        ScanFirstFit = 0,

        /// <summary>
        /// Tries a cached "last chunk with space" first, then falls back to scanning.
        /// </summary>
        TrackLastWithSpace = 1
    }
}