using System;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal
{
    /// <summary>
    /// Owns chunk placement/moves/removals and fixes entity locations according to policies.
    /// </summary>
    internal sealed class ChunkStorageOps
    {
        private readonly WorldState _state;
        private readonly ArchetypeStore _archetypes;
        private readonly ComponentTypeStore _components;

        public ChunkStorageOps(WorldState state, ArchetypeStore archetypes, ComponentTypeStore components)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _archetypes = archetypes ?? throw new ArgumentNullException(nameof(archetypes));
            _components = components ?? throw new ArgumentNullException(nameof(components));
        }

        public void PlaceInEmptyArchetype(Entity entity)
        {
            Archetype empty = _archetypes.GetEmptyArchetype();

            Chunk chunk = empty.GetOrCreateChunkWithSpace(
                _state.ChunkCapacity,
                _state.StoragePolicies.AllocationPolicy,
                cap => _components.CreateColumnsForSignature(cap, empty.Signature));

            int row = chunk.AddEntity(entity);

            _state.Locations[entity.Index] = new EntityLocation
            {
                ArchetypeId = empty.ArchetypeId,
                ChunkId = chunk.ChunkId,
                Row = row
            };

            _state.IncrementStructuralVersion();
        }

        public void RemoveFromStorage(Entity entity)
        {
            int index = entity.Index;

            EntityLocation loc = _state.Locations[index];
            if (!loc.IsValid) return;

            Archetype archetype = _archetypes.GetArchetypeById(loc.ArchetypeId);
            Chunk chunk = archetype.GetChunkById(loc.ChunkId);

            RemoveFromChunkAndFixLocations(
                loc.ArchetypeId,
                loc.ChunkId,
                chunk,
                loc.Row,
                _state.StoragePolicies.RemovalPolicy);

            _state.Locations[index] = EntityLocation.Invalid;

            _state.IncrementStructuralVersion();
        }

        public Chunk AllocateDestinationSlot(Archetype dstArchetype, Entity entity, out int dstRow)
        {
            Chunk dstChunk = dstArchetype.GetOrCreateChunkWithSpace(
                _state.ChunkCapacity,
                _state.StoragePolicies.AllocationPolicy,
                cap => _components.CreateColumnsForSignature(cap, dstArchetype.Signature));

            dstRow = dstChunk.AddEntity(entity);
            return dstChunk;
        }

        public void CopyOverlappingComponents(
            Archetype srcArchetype,
            Chunk srcChunk,
            int srcRow,
            Archetype dstArchetype,
            Chunk dstChunk,
            int dstRow)
        {
            int[] srcTypes = srcArchetype.Signature.TypeIds;

            for (int i = 0; i < srcTypes.Length; i++)
            {
                ComponentTypeId typeId = new(srcTypes[i]);

                // Structural path; dictionary lookup is acceptable here.
                if (dstArchetype.TryGetColumnIndex(typeId, out int dstColumnIndex))
                {
                    IChunkColumn srcCol = srcChunk.Columns[i];
                    IChunkColumn dstCol = dstChunk.Columns[dstColumnIndex];
                    srcCol.CopyElementTo(srcRow, dstCol, dstRow);
                }
            }
        }

        public void RemoveFromSourceAndFixSwap(
            Archetype srcArchetype,
            EntityLocation srcLoc,
            int removedRow)
        {
            Chunk srcChunk = srcArchetype.GetChunkById(srcLoc.ChunkId);

            RemoveFromChunkAndFixLocations(
                srcArchetype.ArchetypeId,
                srcLoc.ChunkId,
                srcChunk,
                removedRow,
                _state.StoragePolicies.RemovalPolicy);
        }

        private void RemoveFromChunkAndFixLocations(
            ArchetypeId archetypeId,
            int chunkId,
            Chunk chunk,
            int removedRow,
            StorageRemovalPolicy removalPolicy)
        {
            switch (removalPolicy)
            {
                case StorageRemovalPolicy.SwapBack:
                    RemoveFromChunkSwapBack(archetypeId, chunkId, chunk, removedRow);
                    break;

                case StorageRemovalPolicy.StableRemove:
                    RemoveFromChunkStable(archetypeId, chunkId, chunk, removedRow);
                    break;

                default:
                    throw new InvalidOperationException("Unknown storage removal policy.");
            }
        }

        private void RemoveFromChunkSwapBack(
            ArchetypeId archetypeId,
            int chunkId,
            Chunk chunk,
            int removedRow)
        {
            chunk.RemoveAtSwapBack(removedRow, out Entity swappedEntity, out bool didSwap);

            if (didSwap)
                _state.Locations[swappedEntity.Index] = new EntityLocation
                {
                    ArchetypeId = archetypeId,
                    ChunkId = chunkId,
                    Row = removedRow
                };
        }

        private void RemoveFromChunkStable(
            ArchetypeId archetypeId,
            int chunkId,
            Chunk chunk,
            int removedRow)
        {
            int count = chunk.Count;
            int last = count - 1;

            for (int row = removedRow; row < last; row++)
            {
                Entity moved = chunk.Entities[row + 1];
                chunk.Entities[row] = moved;

                for (int c = 0; c < chunk.Columns.Length; c++)
                {
                    chunk.Columns[c].MoveElement(row + 1, row);
                }

                _state.Locations[moved.Index] = new EntityLocation
                {
                    ArchetypeId = archetypeId,
                    ChunkId = chunkId,
                    Row = row
                };
            }

            if (last >= 0)
            {
                chunk.Entities[last] = Entity.Invalid;

                for (int c = 0; c < chunk.Columns.Length; c++)
                {
                    chunk.Columns[c].SetDefault(last);
                }
            }

            chunk.SetCountUnsafe(last);
        }
    }
}