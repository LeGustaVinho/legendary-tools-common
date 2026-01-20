using LegendaryTools.Common.Core.Patterns.ECS.Entities;

namespace LegendaryTools.Common.Core.Patterns.ECS.Storage
{
    /// <summary>
    /// Stores entities and components for a single archetype in SoA layout.
    /// </summary>
    public sealed class Chunk
    {
        /// <summary>
        /// Gets the deterministic chunk id within its archetype (incremental).
        /// </summary>
        public readonly int ChunkId;

        /// <summary>
        /// Gets the entities array for this chunk.
        /// </summary>
        public readonly Entity[] Entities;

        internal readonly IChunkColumn[] Columns;

        /// <summary>
        /// Gets the number of active entities in this chunk.
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        /// Gets the maximum number of entities this chunk can hold.
        /// </summary>
        public int Capacity => Entities.Length;

        internal Chunk(int chunkId, int capacity, IChunkColumn[] columns)
        {
            ChunkId = chunkId;
            Entities = new Entity[capacity];
            Columns = columns;
            Count = 0;
        }

        /// <summary>
        /// Gets a value indicating whether this chunk has space for one more entity.
        /// </summary>
        public bool HasSpace => Count < Entities.Length;

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

            // Clear the tail slot (optional hygiene).
            Entities[last] = Entity.Invalid;
            for (int i = 0; i < Columns.Length; i++)
            {
                Columns[i].SetDefault(last);
            }

            Count = last;
        }
    }
}
