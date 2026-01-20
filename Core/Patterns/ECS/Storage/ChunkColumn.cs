using System;
using LegendaryTools.Common.Core.Patterns.ECS.Memory;

namespace LegendaryTools.Common.Core.Patterns.ECS.Storage
{
    public sealed class ChunkColumn<T> : IChunkColumn where T : struct
    {
        public readonly T[] Data;

        public ChunkColumn(int capacity)
        {
            if (capacity < 1) capacity = 1;

            Data = EcsArrayPool<T>.Rent(capacity);
        }

        public void CopyElementTo(int srcRow, IChunkColumn dst, int dstRow)
        {
            ChunkColumn<T> typed = (ChunkColumn<T>)dst;
            typed.Data[dstRow] = Data[srcRow];
        }

        public void MoveElement(int fromRow, int toRow)
        {
            Data[toRow] = Data[fromRow];
        }

        public void SetDefault(int row)
        {
            Data[row] = default;
        }

        public void ReturnToPool()
        {
            EcsArrayPool<T>.Return(Data, false);
        }
    }
}