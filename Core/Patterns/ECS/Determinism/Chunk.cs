#nullable enable

using System;
using System.Runtime.CompilerServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Chunk storage for a single archetype using SoA layout (one contiguous array per component).
    /// </summary>
    /// <remarks>
    /// Allocations happen only at chunk creation. Add/Remove operations are allocation-free.
    ///
    /// Removal policy:
    /// - Uses swap-back removal (<see cref="RemoveRowSwapBack"/>).
    /// - The last slot after a removal may contain stale data but is out of range (Count decreased).
    /// - When reusing a previously occupied row index, columns are cleared to deterministic defaults.
    /// </remarks>
    public sealed class Chunk
    {
        private readonly ComponentTypeRegistry _registry;

        private readonly ComponentTypeId[] _dataTypeIds; // data-only ids, ordered
        private readonly Array[] _dataColumns; // parallel arrays to _dataTypeIds

        private readonly Entity[] _entities;
        private int _count;

        // Tracks the maximum row index ever assigned in this chunk (high-water mark).
        // Used to avoid clearing columns on first-time row use, while still clearing on reuse after removals.
        private int _highWaterMark;

        /// <summary>
        /// Gets the chunk id (deterministic within its archetype).
        /// </summary>
        public ChunkId Id { get; }

        /// <summary>
        /// Gets the archetype id of this chunk.
        /// </summary>
        public ArchetypeId ArchetypeId { get; }

        /// <summary>
        /// Gets the archetype signature for this chunk.
        /// </summary>
        public ComponentTypeSet Signature { get; }

        /// <summary>
        /// Gets the owning archetype (runtime reference).
        /// </summary>
        internal Archetype OwnerArchetype { get; }

        /// <summary>
        /// Gets the number of live rows in this chunk.
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Gets the maximum number of rows supported by this chunk.
        /// </summary>
        public int Capacity => _entities.Length;

        /// <summary>
        /// Gets the entity array (Only [0..Count) is valid).
        /// </summary>
        public Entity[] Entities => _entities;

        internal Chunk(ChunkId id, Archetype archetype, ComponentTypeRegistry registry, int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            Id = id;
            OwnerArchetype = archetype ?? throw new ArgumentNullException(nameof(archetype));

            ArchetypeId = archetype.Id;
            Signature = archetype.Signature;

            _registry = registry ?? throw new ArgumentNullException(nameof(registry));

            _entities = new Entity[capacity];
            _count = 0;
            _highWaterMark = 0;

            // Build columns for data components only (tags have no payload arrays).
            ReadOnlySpan<ComponentTypeId> signatureIds = archetype.Signature.AsSpan();

            int dataCount = 0;
            for (int i = 0; i < signatureIds.Length; i++)
            {
                ref readonly ComponentTypeInfo info = ref _registry.GetInfo(signatureIds[i]);
                if ((info.Flags & ComponentTypeFlags.Data) != 0)
                    dataCount++;
            }

            _dataTypeIds = dataCount == 0 ? Array.Empty<ComponentTypeId>() : new ComponentTypeId[dataCount];
            _dataColumns = dataCount == 0 ? Array.Empty<Array>() : new Array[dataCount];

            int write = 0;
            for (int i = 0; i < signatureIds.Length; i++)
            {
                ComponentTypeId typeId = signatureIds[i];
                ref readonly ComponentTypeInfo info = ref _registry.GetInfo(typeId);

                if ((info.Flags & ComponentTypeFlags.Data) == 0)
                    continue;

                Array column = Array.CreateInstance(info.ManagedType, capacity);

                _dataTypeIds[write] = typeId;
                _dataColumns[write] = column;
                write++;
            }
        }

        /// <summary>
        /// Adds a row for the given entity and returns the row index.
        /// </summary>
        /// <remarks>
        /// Does not allocate. Throws if chunk is full.
        /// Ensures deterministic default initialization for all data columns only when reusing a row index.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AddRow(Entity e)
        {
            int row = _count;
            if ((uint)row >= (uint)_entities.Length)
                throw new InvalidOperationException("Chunk is full.");

            _entities[row] = e;

            // Optimization:
            // - First time a row index is used, the arrays are already zero-initialized (default(T)).
            // - After swap-back removals, the reused row index may contain stale data in component columns.
            // - Clear only when reusing a previously occupied row index.
            if (row < _highWaterMark)
                for (int i = 0; i < _dataColumns.Length; i++)
                {
                    Array.Clear(_dataColumns[i], row, 1);
                }
            else
                _highWaterMark = row + 1;

            _count = row + 1;
            return row;
        }

        /// <summary>
        /// Removes the row by swapping the last row into the removed slot (swap-back).
        /// </summary>
        /// <param name="row">Row to remove. Must be in [0..Count).</param>
        /// <returns>
        /// The entity that was moved into <paramref name="row"/> (if a swap occurred),
        /// or <see cref="Entity.Null"/> if the removed row was the last row.
        /// </returns>
        public Entity RemoveRowSwapBack(int row)
        {
            int last = _count - 1;
            if ((uint)row >= (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(row));

            Entity moved = Entity.Null;

            if (row != last)
            {
                moved = _entities[last];
                _entities[row] = moved;

                for (int i = 0; i < _dataColumns.Length; i++)
                {
                    Array col = _dataColumns[i];
                    Array.Copy(col, last, col, row, 1);
                }
            }

            _count = last;
            return moved;
        }

        /// <summary>
        /// Gets the component SoA array for the given component type.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T[] GetComponentArray<T>() where T : struct
        {
            ComponentTypeId id = _registry.GetId(typeof(T));

            int colIndex = FindDataColumnIndex(id.Value);
            if (colIndex < 0)
                throw new InvalidOperationException(
                    $"Component {typeof(T).FullName} is not a data component in this chunk.");

            return (T[])_dataColumns[colIndex];
        }

        /// <summary>
        /// Tries to get the component SoA array for the given component type id.
        /// </summary>
        internal bool TryGetComponentArray(ComponentTypeId id, out Array array)
        {
            int colIndex = FindDataColumnIndex(id.Value);
            if (colIndex < 0)
            {
                array = null!;
                return false;
            }

            array = _dataColumns[colIndex];
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindDataColumnIndex(int typeIdValue)
        {
            int lo = 0;
            int hi = _dataTypeIds.Length - 1;

            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                int midVal = _dataTypeIds[mid].Value;

                if (midVal == typeIdValue)
                    return mid;

                if (midVal < typeIdValue)
                    lo = mid + 1;
                else
                    hi = mid - 1;
            }

            return -1;
        }
    }
}