using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Memory;

namespace LegendaryTools.Common.Core.Patterns.ECS.Storage
{
    public sealed class Chunk
    {
        public readonly int ChunkId;

        public readonly Entity[] Entities;

        internal readonly IChunkColumn[] Columns;

        private readonly int _capacity;

        public int Count { get; private set; }

        public int Capacity => _capacity;

        internal Chunk(int chunkId, int capacity, IChunkColumn[] columns)
        {
            ChunkId = chunkId;

            if (capacity < 1) capacity = 1;

            _capacity = capacity;
            Entities = EcsArrayPool<Entity>.Rent(capacity);
            Columns = columns;
            Count = 0;
        }

        public bool HasSpace => Count < _capacity;

        internal int AddEntity(Entity entity)
        {
            int row = Count++;
            Entities[row] = entity;
            return row;
        }

        internal void RemoveAtSwapBack(int row, out Entity swappedEntity, out bool didSwap)
        {
            int last = Count - 1;
            didSwap = row != last;

            if (didSwap)
            {
                swappedEntity = Entities[last];
                Entities[row] = swappedEntity;

                for (int i = 0; i < Columns.Length; i++)
                {
                    Columns[i].MoveElement(last, row);
                }
            }
            else
            {
                swappedEntity = Entities[row];
            }

            Entities[last] = Entity.Invalid;

            for (int i = 0; i < Columns.Length; i++)
            {
                Columns[i].SetDefault(last);
            }

            Count = last;
        }

        internal void ReturnToPool()
        {
            for (int i = 0; i < Columns.Length; i++)
            {
                Columns[i].ReturnToPool();
            }

            EcsArrayPool<Entity>.Return(Entities, false);
            EcsArrayPool<IChunkColumn>.Return(Columns, true);
        }
    }
}