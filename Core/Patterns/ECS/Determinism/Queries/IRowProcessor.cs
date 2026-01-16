#nullable enable

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Non-allocating row processor for deterministic iteration.
    /// </summary>
    public interface IRowProcessor
    {
        /// <summary>
        /// Executes work for a specific row in a matching chunk.
        /// </summary>
        void Execute(Chunk chunk, int row);
    }
}