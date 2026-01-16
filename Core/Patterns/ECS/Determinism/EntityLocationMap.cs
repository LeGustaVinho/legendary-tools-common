#nullable enable

using System;
using System.Runtime.CompilerServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// O(1) mapping from entity index to (archetype, chunk, row).
    /// </summary>
    /// <remarks>
    /// Arrays are indexed by <see cref="Entity.Index"/>. Index 0 is reserved for <see cref="Entity.Null"/>.
    /// For zero-GC in hot paths, preallocate with a capacity that covers the maximum entity index.
    /// </remarks>
    public sealed class EntityLocationMap
    {
        private ArchetypeId[] _archetypeIdByEntity;
        private ChunkId[] _chunkIdByEntity;
        private Chunk?[] _chunkRefByEntity;
        private int[] _rowByEntity;

        /// <summary>
        /// Initializes the location map with a given capacity (excluding index 0).
        /// </summary>
        public EntityLocationMap(int initialEntityCapacity = 1024)
        {
            if (initialEntityCapacity < 1)
                initialEntityCapacity = 1;

            int len = initialEntityCapacity + 1;

            _archetypeIdByEntity = new ArchetypeId[len];
            _chunkIdByEntity = new ChunkId[len];
            _chunkRefByEntity = new Chunk?[len];
            _rowByEntity = new int[len];

            // Row default is 0; we treat "no location" as chunkRef == null.
        }

        /// <summary>
        /// Gets current internal capacity (excluding index 0).
        /// </summary>
        public int Capacity => _rowByEntity.Length - 1;

        /// <summary>
        /// Ensures arrays can index up to <paramref name="entityIndex"/> (inclusive).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureCapacity(int entityIndex)
        {
            if (entityIndex < _rowByEntity.Length)
                return;

            int newLen = _rowByEntity.Length * 2;
            int minLen = entityIndex + 1;
            if (newLen < minLen)
                newLen = minLen;

            Array.Resize(ref _archetypeIdByEntity, newLen);
            Array.Resize(ref _chunkIdByEntity, newLen);
            Array.Resize(ref _chunkRefByEntity, newLen);
            Array.Resize(ref _rowByEntity, newLen);
        }

        /// <summary>
        /// Sets the location for an entity (used on create, migration, and post-swap updates).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(Entity e, ArchetypeId archetypeId, Chunk chunk, int row)
        {
            int idx = e.Index;
            EnsureCapacity(idx);

            _archetypeIdByEntity[idx] = archetypeId;
            _chunkIdByEntity[idx] = chunk.Id;
            _chunkRefByEntity[idx] = chunk;
            _rowByEntity[idx] = row;
        }

        /// <summary>
        /// Clears the location for an entity (used on destroy).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear(Entity e)
        {
            int idx = e.Index;
            if ((uint)idx >= (uint)_rowByEntity.Length)
                return;

            _chunkRefByEntity[idx] = null;
            _rowByEntity[idx] = 0;
            _chunkIdByEntity[idx] = default;
            _archetypeIdByEntity[idx] = default;
        }

        /// <summary>
        /// Returns true if the entity has a valid location set.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasLocation(Entity e)
        {
            int idx = e.Index;
            if ((uint)idx >= (uint)_chunkRefByEntity.Length)
                return false;

            return _chunkRefByEntity[idx] is not null;
        }

        /// <summary>
        /// Tries to get the chunk reference and row for an entity in O(1).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetChunkAndRow(Entity e, out Chunk chunk, out int row)
        {
            int idx = e.Index;
            if ((uint)idx >= (uint)_chunkRefByEntity.Length)
            {
                chunk = null!;
                row = 0;
                return false;
            }

            Chunk? c = _chunkRefByEntity[idx];
            if (c is null)
            {
                chunk = null!;
                row = 0;
                return false;
            }

            chunk = c;
            row = _rowByEntity[idx];
            return true;
        }

        /// <summary>
        /// Gets the full location for an entity in O(1). Throws if not set.
        /// </summary>
        public EntityLocation Get(Entity e)
        {
            int idx = e.Index;
            if ((uint)idx >= (uint)_chunkRefByEntity.Length || _chunkRefByEntity[idx] is null)
                throw new InvalidOperationException(
                    "Entity has no location (it may be destroyed or not yet placed into storage).");

            return new EntityLocation(_archetypeIdByEntity[idx], _chunkIdByEntity[idx], _rowByEntity[idx]);
        }

        /// <summary>
        /// Updates the entity that was moved due to swap-back removal.
        /// </summary>
        /// <remarks>
        /// Call pattern:
        /// - moved = chunk.RemoveRowSwapBack(removedRow)
        /// - if moved != Entity.Null: locationMap.OnSwapBackMoved(chunk, moved, removedRow)
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnSwapBackMoved(Chunk chunk, Entity movedEntity, int newRow)
        {
            // movedEntity now lives in 'chunk' at 'newRow'.
            Set(movedEntity, chunk.ArchetypeId, chunk, newRow);
        }

        /// <summary>
        /// Updates entity location for migration between chunks.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnMigrated(Entity e, Chunk newChunk, int newRow)
        {
            Set(e, newChunk.ArchetypeId, newChunk, newRow);
        }
    }
}