using System;
using System.Collections.Generic;
using LegendaryTools.Common.Core.Patterns.ECS.Components;

namespace LegendaryTools.Common.Core.Patterns.ECS.Storage
{
    /// <summary>
    /// An archetype is an ordered set of component types and owns a deterministic list of chunks.
    /// </summary>
    public sealed class Archetype
    {
        private readonly Dictionary<int, int> _typeIdToColumnIndex;
        private readonly List<Chunk> _chunks;
        private int _nextChunkId;

        /// <summary>
        /// Gets the deterministic archetype id derived from the signature.
        /// </summary>
        public ArchetypeId ArchetypeId { get; }

        /// <summary>
        /// Gets the ordered signature for this archetype.
        /// </summary>
        public ArchetypeSignature Signature { get; }

        /// <summary>
        /// Gets the chunks list (in deterministic ChunkId order).
        /// </summary>
        public IReadOnlyList<Chunk> Chunks => _chunks;

        internal Archetype(ArchetypeSignature signature, ArchetypeId archetypeId)
        {
            Signature = signature;
            ArchetypeId = archetypeId;

            _typeIdToColumnIndex = new Dictionary<int, int>(signature.TypeIds.Length);
            for (int i = 0; i < signature.TypeIds.Length; i++)
            {
                _typeIdToColumnIndex.Add(signature.TypeIds[i], i);
            }

            _chunks = new List<Chunk>(16);
            _nextChunkId = 0;
        }

        /// <summary>
        /// Checks whether this archetype contains the given component type.
        /// </summary>
        /// <param name="typeId">Component type id.</param>
        /// <returns>True if present.</returns>
        public bool Contains(ComponentTypeId typeId)
        {
            return Signature.Contains(typeId);
        }

        internal bool TryGetColumnIndex(ComponentTypeId typeId, out int columnIndex)
        {
            return _typeIdToColumnIndex.TryGetValue(typeId.Value, out columnIndex);
        }

        internal Chunk GetOrCreateChunkWithSpace(int chunkCapacity, Func<int, IChunkColumn[]> createColumns)
        {
            // Deterministic: chunks are appended in ChunkId order.
            for (int i = 0; i < _chunks.Count; i++)
            {
                if (_chunks[i].HasSpace) return _chunks[i];
            }

            int id = _nextChunkId++;
            IChunkColumn[] cols = createColumns(chunkCapacity);
            Chunk chunk = new(id, chunkCapacity, cols);
            _chunks.Add(chunk);
            return chunk;
        }

        internal Chunk GetChunkById(int chunkId)
        {
            // Since chunk ids are incremental and list order matches id, we can index directly.
            // This assumes no chunk removal (MVP).
            if ((uint)chunkId >= (uint)_chunks.Count)
                throw new InvalidOperationException($"ChunkId {chunkId} is out of range for archetype {ArchetypeId}.");

            Chunk c = _chunks[chunkId];
            if (c.ChunkId != chunkId)
                // Defensive: should never happen.
                throw new InvalidOperationException($"ChunkId mismatch for archetype {ArchetypeId}.");

            return c;
        }
    }
}