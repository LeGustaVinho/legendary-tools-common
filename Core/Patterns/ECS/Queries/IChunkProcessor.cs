using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Common.Core.Patterns.ECS.Queries
{
    /// <summary>
    /// Allocation-free chunk processing contract.
    /// Implement this as a struct and pass by ref to avoid allocations.
    /// </summary>
    public interface IChunkProcessor
    {
        /// <summary>
        /// Executes logic for a matching chunk.
        /// </summary>
        /// <param name="archetype">Owning archetype.</param>
        /// <param name="chunk">Chunk.</param>
        void Execute(Archetype archetype, Chunk chunk);
    }
}
