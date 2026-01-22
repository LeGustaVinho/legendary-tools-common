using System;
using System.Collections.Generic;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Memory;

namespace LegendaryTools.Common.Core.Patterns.ECS.Storage
{
    public sealed class Archetype
    {
        private readonly Dictionary<int, int> _typeIdToColumnIndex;
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
