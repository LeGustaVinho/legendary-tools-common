namespace LegendaryTools.Common.Core.Patterns.ECS.Storage
{
    /// <summary>
    /// Defines how entities are removed from a chunk.
    /// </summary>
    public enum StorageRemovalPolicy : byte
    {
        /// <summary>
        /// Fast removal by swapping the last entity into the removed slot (order not preserved).
        /// </summary>
        SwapBack = 0,

        /// <summary>
        /// Stable removal that preserves entity order by shifting elements (more expensive).
        /// </summary>
        StableRemove = 1
    }
}