#nullable enable

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Non-allocating chunk processor for deterministic iteration.
    /// </summary>
    public interface IChunkProcessor
    {
        /// <summary>
        /// Executes work for a matching chunk.
        /// </summary>
        void Execute(Chunk chunk);
    }
}