using System;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal
{
    internal sealed class StructuralChanges
    {
        private readonly WorldState _state;
        private readonly StorageService _storage;
        private readonly EntityManager _entities;

        public StructuralChanges(WorldState state, StorageService storage, EntityManager entities)
        {
            _state = state;
            _storage = storage;
            _entities = entities;
        }

        public void AssertNotIterating()
        {
            if (_state.IterationDepth > 0)
                throw new InvalidOperationException("Structural changes are not allowed during iteration. Use an ECB.");
        }

        public void Add<T>(Entity entity, in T value) where T : struct
        {
            AssertNotIterating();

            if (!_entities.IsAlive(entity))
                throw new InvalidOperationException($"Entity {entity} is not alive (or is stale).");

            ComponentTypeId typeId = _storage.GetComponentTypeId<T>();

            EntityLocation srcLoc = _storage.GetLocation(entity);
            Archetype srcArchetype = _storage.GetArchetypeById(srcLoc.ArchetypeId);

            // Not a structural change: component already present, only write the value.
            if (srcArchetype.TryGetColumnIndex(typeId, out int existingColumnIndex))
            {
                Chunk srcChunk0 = srcArchetype.GetChunkById(srcLoc.ChunkId);
                ChunkColumn<T> col0 = (ChunkColumn<T>)srcChunk0.Columns[existingColumnIndex];
                col0.Data[srcLoc.Row] = value;
                return;
            }

            // Zero-GC hot path (pooled temp signature).
            Archetype dstArchetype = _storage.GetOrCreateArchetypeWithAdded(srcArchetype, typeId);

            // Allocate destination slot (adds entity to destination chunk).
            Chunk dstChunk = _storage.AllocateDestinationSlot(dstArchetype, entity, out int dstRow);

            // Copy overlapping components from source to destination.
            Chunk srcChunk = srcArchetype.GetChunkById(srcLoc.ChunkId);
            _storage.CopyOverlappingComponents(srcArchetype, srcChunk, srcLoc.Row, dstArchetype, dstChunk, dstRow);

            // Write the newly added component value.
            if (!dstArchetype.TryGetColumnIndex(typeId, out int newColIndex))
                throw new InvalidOperationException(
                    "Destination archetype does not contain the expected new component.");

            ChunkColumn<T> newCol = (ChunkColumn<T>)dstChunk.Columns[newColIndex];
            newCol.Data[dstRow] = value;

            // Update entity location to destination.
            _storage.SetLocation(entity.Index, new EntityLocation
            {
                ArchetypeId = dstArchetype.ArchetypeId,
                ChunkId = dstChunk.ChunkId,
                Row = dstRow
            });

            // Policy-based removal (SwapBack or StableRemove).
            _storage.RemoveFromSourceAndFixSwap(srcArchetype, srcLoc, srcLoc.Row);

            // Structural change: entity moved between archetypes/chunks.
            _state.IncrementStructuralVersion();
        }

        public void Remove<T>(Entity entity) where T : struct
        {
            AssertNotIterating();

            if (!_entities.IsAlive(entity)) return;

            ComponentTypeId typeId = _storage.GetComponentTypeId<T>();

            EntityLocation srcLoc = _storage.GetLocation(entity);
            if (!srcLoc.IsValid) return;

            Archetype srcArchetype = _storage.GetArchetypeById(srcLoc.ArchetypeId);
            if (!srcArchetype.Contains(typeId)) return;

            // Zero-GC hot path (pooled temp signature).
            Archetype dstArchetype = _storage.GetOrCreateArchetypeWithRemoved(srcArchetype, typeId);

            // Allocate destination slot (adds entity to destination chunk).
            Chunk dstChunk = _storage.AllocateDestinationSlot(dstArchetype, entity, out int dstRow);

            // Copy overlapping components from source to destination (excluding removed type).
            Chunk srcChunk = srcArchetype.GetChunkById(srcLoc.ChunkId);
            _storage.CopyOverlappingComponents(srcArchetype, srcChunk, srcLoc.Row, dstArchetype, dstChunk, dstRow);

            // Update entity location to destination.
            _storage.SetLocation(entity.Index, new EntityLocation
            {
                ArchetypeId = dstArchetype.ArchetypeId,
                ChunkId = dstChunk.ChunkId,
                Row = dstRow
            });

            // Policy-based removal (SwapBack or StableRemove).
            _storage.RemoveFromSourceAndFixSwap(srcArchetype, srcLoc, srcLoc.Row);

            // Structural change: entity moved between archetypes/chunks.
            _state.IncrementStructuralVersion();
        }
    }
}