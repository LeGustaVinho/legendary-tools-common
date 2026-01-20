namespace LegendaryTools.Common.Core.Patterns.ECS.Storage
{
    /// <summary>
    /// Non-generic interface for chunk columns (SoA arrays).
    /// </summary>
    internal interface IChunkColumn
    {
        /// <summary>
        /// Copies one element from this column into a destination column.
        /// </summary>
        /// <param name="srcRow">Source row.</param>
        /// <param name="dst">Destination column.</param>
        /// <param name="dstRow">Destination row.</param>
        void CopyElementTo(int srcRow, IChunkColumn dst, int dstRow);

        /// <summary>
        /// Moves the element from <paramref name="fromRow"/> into <paramref name="toRow"/>.
        /// Used by swap-back removals.
        /// </summary>
        /// <param name="fromRow">Source row.</param>
        /// <param name="toRow">Destination row.</param>
        void MoveElement(int fromRow, int toRow);

        /// <summary>
        /// Sets the element at <paramref name="row"/> to default.
        /// </summary>
        /// <param name="row">Row index.</param>
        void SetDefault(int row);
    }
}
