namespace LegendaryTools.Common.Core.Patterns.ECS.Storage
{
    /// <summary>
    /// Type-erased interface for chunk columns.
    /// </summary>
    public interface IChunkColumn
    {
        /// <summary>
        /// Copies an element from this column to another column.
        /// </summary>
        /// <param name="srcRow">Source row index.</param>
        /// <param name="dst">Destination column.</param>
        /// <param name="dstRow">Destination row index.</param>
        void CopyElementTo(int srcRow, IChunkColumn dst, int dstRow);

        /// <summary>
        /// Moves an element within this column.
        /// </summary>
        /// <param name="fromRow">Source row index.</param>
        /// <param name="toRow">Destination row index.</param>
        void MoveElement(int fromRow, int toRow);

        /// <summary>
        /// Sets the element at the specified row to its default value.
        /// </summary>
        /// <param name="row">Row index.</param>
        void SetDefault(int row);

        /// <summary>
        /// Returns internal buffers back to pools.
        /// Note: chunks are currently kept around and reused; this is a hook for future recycling.
        /// </summary>
        void ReturnToPool();
    }
}