using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Common.Core.Patterns.ECS.Parallel
{
    /// <summary>
    /// Stable work item describing one chunk to process.
    /// </summary>
    public readonly struct ChunkWorkItem
    {
        public readonly Archetype Archetype;
        public readonly Chunk Chunk;

        public readonly int ArchetypeOrdinal;
        public readonly int ChunkOrdinal;

        public ChunkWorkItem(Archetype archetype, Chunk chunk, int archetypeOrdinal, int chunkOrdinal)
        {
            Archetype = archetype;
            Chunk = chunk;
            ArchetypeOrdinal = archetypeOrdinal;
            ChunkOrdinal = chunkOrdinal;
        }
    }
}