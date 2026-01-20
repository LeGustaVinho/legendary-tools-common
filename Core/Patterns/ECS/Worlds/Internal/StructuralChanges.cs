using System;

using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal
{
    /// <summary>
    /// Structural changes (Add/Remove/move between archetypes).
    /// In this MVP, changes apply immediately; later this will be routed through ECB.
    /// </summary>
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
            {
                throw new InvalidOperationException("Structural changes are not allowed during iteration. Use an ECB.");
            }
        }

        public void Add<T>(Entity entity, in T value) where T : struct
        {
            AssertNotIterating();

            if (!_entities.IsAlive(entity))
            {
                throw new InvalidOperationException($"Entity {entity} is not alive (or is stale).");
            }

            ComponentTypeId typeId = _storage.GetComponentTypeId<T>();

            EntityLocation srcLoc = _storage.GetLocation(entity);
            Archetype srcArchetype = _storage.GetArchetypeById(srcLoc.ArchetypeId);

            // If already has component, just set value in-place.
            if (srcArchetype.TryGetColumnIndex(typeId, out int existingColumnIndex))
            {
                Chunk srcChunk0 = srcArchetype.GetChunkById(srcLoc.ChunkId);
                ChunkColumn<T> col0 = (ChunkColumn<T>)srcChunk0.Columns[existingColumnIndex];
                col0.Data[srcLoc.Row] = value;
                return;
            }

            ArchetypeSignature dstSig = srcArchetype.Signature.WithAdded(typeId);
            Archetype dstArchetype = _storage.GetOrCreateArchetype(dstSig);

            // 1) Allocate destination slot (stable, deterministic).
            Chunk dstChunk = _storage.AllocateDestinationSlot(dstArchetype, entity, out int dstRow);

            // 2) Copy overlapping components src -> dst.
            Chunk srcChunk = srcArchetype.GetChunkById(srcLoc.ChunkId);
            _storage.CopyOverlappingComponents(srcArchetype, srcChunk, srcLoc.Row, dstArchetype, dstChunk, dstRow);

            // 3) Set the new component value.
            if (!dstArchetype.TryGetColumnIndex(typeId, out int newColIndex))
            {
                throw new InvalidOperationException("Destination archetype does not contain the expected new component.");
            }

            ChunkColumn<T> newCol = (ChunkColumn<T>)dstChunk.Columns[newColIndex];
            newCol.Data[dstRow] = value;

            // 4) Update entity location to destination.
            _storage.SetLocation(entity.Index, new EntityLocation
            {
                ArchetypeId = dstArchetype.ArchetypeId,
                ChunkId = dstChunk.ChunkId,
                Row = dstRow,
            });

            // 5) Remove from source (swap-back) and fix swapped entity location.
            _storage.RemoveFromSourceAndFixSwap(srcArchetype, srcLoc, srcLoc.Row);
        }

        public void Remove<T>(Entity entity) where T : struct
        {
            AssertNotIterating();

            if (!_entities.IsAlive(entity))
            {
                return;
            }

            ComponentTypeId typeId = _storage.GetComponentTypeId<T>();

            EntityLocation srcLoc = _storage.GetLocation(entity);
            if (!srcLoc.IsValid)
            {
                return;
            }

            Archetype srcArchetype = _storage.GetArchetypeById(srcLoc.ArchetypeId);
            if (!srcArchetype.Contains(typeId))
            {
                return;
            }

            ArchetypeSignature dstSig = srcArchetype.Signature.WithRemoved(typeId);
            Archetype dstArchetype = _storage.GetOrCreateArchetype(dstSig);

            // 1) Allocate destination slot.
            Chunk dstChunk = _storage.AllocateDestinationSlot(dstArchetype, entity, out int dstRow);

            // 2) Copy overlapping components src -> dst.
            Chunk srcChunk = srcArchetype.GetChunkById(srcLoc.ChunkId);
            _storage.CopyOverlappingComponents(srcArchetype, srcChunk, srcLoc.Row, dstArchetype, dstChunk, dstRow);

            // 3) Update entity location to destination.
            _storage.SetLocation(entity.Index, new EntityLocation
            {
                ArchetypeId = dstArchetype.ArchetypeId,
                ChunkId = dstChunk.ChunkId,
                Row = dstRow,
            });

            // 4) Remove from source and fix swap.
            _storage.RemoveFromSourceAndFixSwap(srcArchetype, srcLoc, srcLoc.Row);
        }
    }
}
