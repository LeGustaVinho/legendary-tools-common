using System;
using System.Collections.Generic;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Memory;

namespace LegendaryTools.Common.Core.Patterns.ECS.Storage
{
    /// <summary>
    /// Represents a unique combination of component types (archetype).
    /// Stores entities in chunks (SoA layout) for cache efficiency.
    /// </summary>
    public sealed class Archetype
    {
        private readonly Dictionary<int, int> _typeIdToColumnIndex;
        private readonly PooledList<Chunk> _chunks;
        private int _nextChunkId;
        private int _lastChunkWithSpaceIndex;

        /// <summary>
        /// Gets the unique ID of this archetype.
        /// </summary>
        public ArchetypeId ArchetypeId { get; }

        /// <summary>
        /// Gets the component type signature of this archetype.
        /// </summary>
        public ArchetypeSignature Signature { get; }

        /// <summary>
        /// Gets the number of allocated chunks in this archetype.
        /// </summary>
        public int ChunkCount => _chunks.Count;

        /// <summary>
        /// Gets the raw buffer of chunks. Iterate up to <see cref="ChunkCount"/>.
        /// </summary>
        public Chunk[] ChunksBuffer => _chunks.DangerousGetBuffer();

        internal Archetype(ArchetypeSignature signature, ArchetypeId archetypeId)
        {
            Signature = signature;
            ArchetypeId = archetypeId;

            _typeIdToColumnIndex = new Dictionary<int, int>(signature.TypeIds.Length);
            for (int i = 0; i < signature.TypeIds.Length; i++)
            {
                _typeIdToColumnIndex.Add(signature.TypeIds[i], i);
            }

            _chunks = new PooledList<Chunk>(16);
            _nextChunkId = 0;
            _lastChunkWithSpaceIndex = -1;
        }

        /// <summary>
        /// Checks if this archetype contains a specific component type.
        /// </summary>
        /// <param name="typeId">The component type ID.</param>
        /// <returns>True if the archetype contains the component.</returns>
        public bool Contains(ComponentTypeId typeId)
        {
            return Signature.Contains(typeId);
        }

        /// <summary>
        /// Tries to get the column index for a specific component type using a dictionary (O(1)).
        /// </summary>
        /// <param name="typeId">The component type ID.</param>
        /// <param name="columnIndex">The column index if found.</param>
        /// <returns>True if found.</returns>
        public bool TryGetColumnIndex(ComponentTypeId typeId, out int columnIndex)
        {
            return _typeIdToColumnIndex.TryGetValue(typeId.Value, out columnIndex);
        }

        /// <summary>
        /// Tries to get the column index using binary search on the signature (O(log N)).
        /// Avoids dictionary lookup overhead for small component counts.
        /// </summary>
        /// <param name="typeId">The component type ID.</param>
        /// <param name="columnIndex">The column index if found.</param>
        /// <returns>True if found.</returns>
        public bool TryGetColumnIndexFast(ComponentTypeId typeId, out int columnIndex)
        {
            int idx = Array.BinarySearch(Signature.TypeIds, typeId.Value);
            if (idx >= 0)
            {
                columnIndex = idx;
                return true;
            }

            columnIndex = -1;
            return false;
        }

        internal bool TryGetColumnIndexFast(int typeIdValue, out int columnIndex)
        {
            int idx = Array.BinarySearch(Signature.TypeIds, typeIdValue);
            if (idx >= 0)
            {
                columnIndex = idx;
                return true;
            }

            columnIndex = -1;
            return false;
        }

        internal Chunk GetOrCreateChunkWithSpace(
            int chunkCapacity,
            ChunkAllocationPolicy allocationPolicy,
            int columnCount,
            Func<int, IChunkColumn[]> createColumns)
        {
            if (allocationPolicy == ChunkAllocationPolicy.TrackLastWithSpace)
            {
                int last = _lastChunkWithSpaceIndex;
                if ((uint)last < (uint)_chunks.Count)
                {
                    Chunk cached = _chunks[last];
                    if (cached.HasSpace) return cached;
                }
            }

            for (int i = 0; i < _chunks.Count; i++)
            {
                Chunk c = _chunks[i];
                if (c.HasSpace)
                {
                    _lastChunkWithSpaceIndex = i;
                    return c;
                }
            }

            int id = _nextChunkId++;
            IChunkColumn[] cols = createColumns(chunkCapacity);
            Chunk chunk = new(id, chunkCapacity, cols, columnCount);
            _chunks.Add(chunk);

            _lastChunkWithSpaceIndex = _chunks.Count - 1;
            return chunk;
        }

        internal Chunk GetChunkById(int chunkId)
        {
            if ((uint)chunkId >= (uint)_chunks.Count)
                throw new InvalidOperationException($"ChunkId {chunkId} is out of range for archetype {ArchetypeId}.");

            Chunk c = _chunks[chunkId];
            if (c.ChunkId != chunkId)
                throw new InvalidOperationException($"ChunkId mismatch for archetype {ArchetypeId}.");

            return c;
        }
    }
}