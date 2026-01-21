using System;
using System.Collections.Generic;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Memory;

namespace LegendaryTools.Common.Core.Patterns.ECS.Storage
{
    public sealed class Archetype
    {
        private readonly Dictionary<int, int> _typeIdToColumnIndex;

        // Fast path: typeId -> (columnIndex + 1). 0 means "not present".
        private readonly int[] _columnIndexByTypeIdPlus1;

        private readonly PooledList<Chunk> _chunks;
        private int _nextChunkId;

        private int _lastChunkWithSpaceIndex;

        public ArchetypeId ArchetypeId { get; }

        public ArchetypeSignature Signature { get; }

        public int ChunkCount => _chunks.Count;

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

            // Build O(1) column index lookup table.
            _columnIndexByTypeIdPlus1 = BuildColumnIndexMap(signature.TypeIds);

            _chunks = new PooledList<Chunk>(16);
            _nextChunkId = 0;

            _lastChunkWithSpaceIndex = -1;
        }

        public bool Contains(ComponentTypeId typeId)
        {
            return Signature.Contains(typeId);
        }

        public bool TryGetColumnIndex(ComponentTypeId typeId, out int columnIndex)
        {
            return _typeIdToColumnIndex.TryGetValue(typeId.Value, out columnIndex);
        }

        /// <summary>
        /// Fast O(1) lookup for a column index. Prefer this in hot paths.
        /// </summary>
        public bool TryGetColumnIndexFast(ComponentTypeId typeId, out int columnIndex)
        {
            return TryGetColumnIndexFast(typeId.Value, out columnIndex);
        }

        /// <summary>
        /// Fast O(1) lookup for a column index. Prefer this in hot paths.
        /// </summary>
        internal bool TryGetColumnIndexFast(int typeIdValue, out int columnIndex)
        {
            int[] map = _columnIndexByTypeIdPlus1;

            if ((uint)typeIdValue >= (uint)map.Length)
            {
                columnIndex = -1;
                return false;
            }

            int encoded = map[typeIdValue];
            if (encoded == 0)
            {
                columnIndex = -1;
                return false;
            }

            columnIndex = encoded - 1;
            return true;
        }

        internal Chunk GetOrCreateChunkWithSpace(
            int chunkCapacity,
            ChunkAllocationPolicy allocationPolicy,
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
            Chunk chunk = new(id, chunkCapacity, cols);
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

        private static int[] BuildColumnIndexMap(int[] sortedTypeIds)
        {
            if (sortedTypeIds == null || sortedTypeIds.Length == 0) return Array.Empty<int>();

            int maxId = sortedTypeIds[sortedTypeIds.Length - 1];
            if (maxId < 0) return Array.Empty<int>();

            // Index directly by type id. Store (columnIndex + 1) so 0 can mean "absent".
            int[] map = new int[maxId + 1];
            for (int col = 0; col < sortedTypeIds.Length; col++)
            {
                int typeId = sortedTypeIds[col];
                if (typeId >= 0 && typeId < map.Length)
                    map[typeId] = col + 1;
            }

            return map;
        }
    }
}