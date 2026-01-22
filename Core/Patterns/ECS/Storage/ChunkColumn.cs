using System;
using LegendaryTools.Common.Core.Patterns.ECS.Memory;

namespace LegendaryTools.Common.Core.Patterns.ECS.Storage
{
    /// <summary>
    /// Type-safe column of component data within a chunk.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    public sealed class ChunkColumn<T> : IChunkColumn where T : struct
    {
        /// <summary>
        /// The raw array of component data.
        /// </summary>
        public readonly T[] Data;

        /// <summary>
        /// Initializes a new instance of the <see cref="ChunkColumn{T}"/> class.
        /// </summary>
        /// <param name="capacity">Capacity of the column.</param>
        public ChunkColumn(int capacity)
        {
            if (capacity < 1) capacity = 1;

            Data = EcsArrayPool<T>.Rent(capacity);
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

        /// <inheritdoc/>
        public void ReturnToPool()
        {
            EcsArrayPool<T>.Return(Data, false);
        }
    }
}