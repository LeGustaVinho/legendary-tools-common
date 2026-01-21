using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Common.Core.Patterns.ECS.Queries
{
    /// <summary>
    /// Per-chunk processor. Recommended for hot paths.
    /// Inside <see cref="Execute"/>, prefer getting column spans from <see cref="Chunk"/>
    /// and iterating with plain for loops for best performance.
    /// </summary>
    public interface IChunkProcessor
    {
        /// <summary>
        /// Executes once per non-empty chunk.
        /// </summary>
        /// <param name="archetype">Archetype that owns the chunk.</param>
        /// <param name="chunk">Chunk to process.</param>
        void Execute(Archetype archetype, Chunk chunk);
    }
}