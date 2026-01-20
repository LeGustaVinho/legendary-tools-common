namespace LegendaryTools.Common.Core.Patterns.ECS.Storage
{
    internal interface IChunkColumn
    {
        void CopyElementTo(int srcRow, IChunkColumn dst, int dstRow);

        void MoveElement(int fromRow, int toRow);

        void SetDefault(int row);

        /// <summary>
        /// Returns internal buffers back to pools.
        /// Note: chunks are currently kept around and reused; this is a hook for future recycling.
        /// </summary>
        void ReturnToPool();
    }
}