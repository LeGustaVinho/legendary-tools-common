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

        public ArchetypeId ArchetypeId { get; }

        public ArchetypeSignature Signature { get; }

        /// <summary>
        /// Number of chunks currently owned by this archetype.
        /// </summary>
        public int ChunkCount => _chunks.Count;

        /// <summary>
        /// Direct access to the internal chunk buffer (only first <see cref="ChunkCount"/> entries are valid).
        /// This is provided to support zero-allocation iteration in hot paths.
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
        }

        public bool Contains(ComponentTypeId typeId)
        {
            return Signature.Contains(typeId);
        }

        public bool TryGetColumnIndex(ComponentTypeId typeId, out int columnIndex)
        {
            return _typeIdToColumnIndex.TryGetValue(typeId.Value, out columnIndex);
        }

        internal Chunk GetOrCreateChunkWithSpace(int chunkCapacity, Func<int, IChunkColumn[]> createColumns)
        {
            for (int i = 0; i < _chunks.Count; i++)
            {
                Chunk c = _chunks[i];
                if (c.HasSpace) return c;
            }

            int id = _nextChunkId++;
            IChunkColumn[] cols = createColumns(chunkCapacity);
            Chunk chunk = new(id, chunkCapacity, cols);
            _chunks.Add(chunk);
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