using System;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Memory;

namespace LegendaryTools.Common.Core.Patterns.ECS.Storage
{
    /// <summary>
    /// Represents a block of memory storing a fixed number of entities with the same archetype.
    /// Data is stored in Structure of Arrays (SoA) layout (columns).
    /// </summary>
    public sealed class Chunk
    {
        /// <summary>
        /// Unique ID of this chunk within the archetype.
        /// </summary>
        public readonly int ChunkId;

        /// <summary>
        /// Array of entity handles stored in this chunk.
        /// </summary>
        public readonly Entity[] Entities;

        /// <summary>
        /// Backing array may be larger than <see cref="ColumnCount"/> when rented from the pool.
        /// Only indices [0..ColumnCount-1] are valid and must be iterated.
        /// </summary>
        public readonly IChunkColumn[] Columns;

        /// <summary>
        /// Number of valid columns in <see cref="Columns"/>.
        /// </summary>
        public readonly int ColumnCount;

        private readonly int _capacity;

        /// <summary>
        /// Gets the number of entities currently stored in this chunk.
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        /// Gets the maximum capacity of this chunk.
        /// </summary>
        public int Capacity => _capacity;

        internal Chunk(int chunkId, int capacity, IChunkColumn[] columns, int columnCount)
        {
            ChunkId = chunkId;

            if (capacity < 1) capacity = 1;

            if (columns == null) throw new ArgumentNullException(nameof(columns));
            if (columnCount < 0) throw new ArgumentOutOfRangeException(nameof(columnCount));
            if (columnCount > columns.Length) throw new ArgumentOutOfRangeException(nameof(columnCount));

            _capacity = capacity;

            Entities = EcsArrayPool<Entity>.Rent(capacity);

            Columns = columns;
            ColumnCount = columnCount;

            Count = 0;
        }

        /// <summary>
        /// Gets a value indicating whether this chunk has space for more entities.
        /// </summary>
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

                for (int i = 0; i < ColumnCount; i++)
                {
                    Columns[i].MoveElement(last, row);
                }
            }
            else
            {
                swappedEntity = Entities[row];
            }

            Entities[last] = Entity.Invalid;

            for (int i = 0; i < ColumnCount; i++)
            {
                Columns[i].SetDefault(last);
            }

            Count = last;
        }

        /// <summary>
        /// Internal helper used by stable-remove policy (StorageService). Do not call directly outside storage.
        /// </summary>
        internal void SetCountUnsafe(int newCount)
        {
            Count = newCount;
        }

        /// <summary>
        /// Returns a read-only span over a typed column for the active rows (0..Count).
        /// Intended for fast iteration inside chunk-based queries.
        /// </summary>
        public ReadOnlySpan<T> GetSpanRO<T>(int columnIndex) where T : struct
        {
            if ((uint)columnIndex >= (uint)ColumnCount)
                throw new ArgumentOutOfRangeException(nameof(columnIndex));

            ChunkColumn<T> col = (ChunkColumn<T>)Columns[columnIndex];
            return new ReadOnlySpan<T>(col.Data, 0, Count);
        }

        /// <summary>
        /// Returns a writable span over a typed column for the active rows (0..Count).
        /// Intended for fast iteration inside chunk-based queries.
        /// </summary>
        public Span<T> GetSpanRW<T>(int columnIndex) where T : struct
        {
            if ((uint)columnIndex >= (uint)ColumnCount)
                throw new ArgumentOutOfRangeException(nameof(columnIndex));

            ChunkColumn<T> col = (ChunkColumn<T>)Columns[columnIndex];
            return new Span<T>(col.Data, 0, Count);
        }

        internal void ReturnToPool()
        {
            for (int i = 0; i < ColumnCount; i++)
            {
                Columns[i].ReturnToPool();
            }

            EcsArrayPool<Entity>.Return(Entities, false);

            // This is a reference array; EcsArrayPool will clear it on Return automatically.
            EcsArrayPool<IChunkColumn>.Return(Columns, true);
        }
    }
}
