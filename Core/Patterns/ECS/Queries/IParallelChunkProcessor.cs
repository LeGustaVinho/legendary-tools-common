using LegendaryTools.Common.Core.Patterns.ECS.Commands;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Common.Core.Patterns.ECS.Queries
{
    /// <summary>
    /// Deterministic parallel chunk processor (managed "job-like" stage).
    /// Each call receives a worker-local ECB buffer (no contention) and a stable work ordering.
    /// </summary>
    public interface IParallelChunkProcessor
    {
        /// <summary>
        /// Executes work for a single chunk.
        /// </summary>
        /// <param name="archetype">Owning archetype (stable order provided by World).</param>
        /// <param name="chunk">Chunk to process.</param>
        /// <param name="workerIndex">Worker index that owns this work item.</param>
        /// <param name="ecb">Worker-local ECB buffer for deterministic structural changes.</param>
        void Execute(Archetype archetype, Chunk chunk, int workerIndex, ICommandBuffer ecb);
    }
}