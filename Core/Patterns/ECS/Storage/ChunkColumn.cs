namespace LegendaryTools.Common.Core.Patterns.ECS.Storage
{
    /// <summary>
    /// Strongly typed SoA column inside a chunk.
    /// </summary>
    /// <typeparam name="T">Component type.</typeparam>
    internal sealed class ChunkColumn<T> : IChunkColumn where T : struct
    {
        /// <summary>
        /// Gets the raw backing array.
        /// </summary>
        public readonly T[] Data;

        /// <summary>
        /// Initializes a new instance of the <see cref="ChunkColumn{T}"/> class.
        /// </summary>
        /// <param name="capacity">Chunk capacity.</param>
        public ChunkColumn(int capacity)
        {
            Data = new T[capacity];
        }

        /// <inheritdoc/>
        public void CopyElementTo(int srcRow, IChunkColumn dst, int dstRow)
        {
            ChunkColumn<T> typed = (ChunkColumn<T>)dst;
            typed.Data[dstRow] = Data[srcRow];
        }

        /// <inheritdoc/>
        public void MoveElement(int fromRow, int toRow)
        {
            Data[toRow] = Data[fromRow];
        }

        /// <inheritdoc/>
        public void SetDefault(int row)
        {
            Data[row] = default;
        }
    }
}
