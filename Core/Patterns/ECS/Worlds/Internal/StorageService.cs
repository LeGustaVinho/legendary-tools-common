using System;
using System.Collections.Generic;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Memory;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal
{
    internal sealed class StorageService
    {
        private readonly WorldState _state;

        private readonly ComponentRegistry _componentRegistry;

        private readonly Dictionary<int, Func<int, IChunkColumn>> _typedColumnFactories;

        public StorageService(WorldState state)
        {
            _state = state;
            _componentRegistry = new ComponentRegistry();
            _typedColumnFactories = new Dictionary<int, Func<int, IChunkColumn>>(128);
        }

        public int ArchetypeVersion => _state.ArchetypeVersion;

        public void InitializeEmptyArchetype()
        {
            ArchetypeSignature emptySig = new(ReadOnlySpan<int>.Empty);
            _state.EmptyArchetype = GetOrCreateArchetype(emptySig);
        }

        public void RegisterComponent<T>() where T : struct
        {
            ComponentTypeId id = _componentRegistry.GetOrCreate<T>();
            if (_typedColumnFactories.ContainsKey(id.Value)) return;

            _typedColumnFactories.Add(id.Value, cap => new ChunkColumn<T>(cap));
        }

        public ComponentTypeId GetComponentTypeId<T>() where T : struct
        {
            return _componentRegistry.GetOrCreate<T>();
        }

        public bool Has<T>(Entity entity) where T : struct
        {
            int index = entity.Index;
            if ((uint)index >= (uint)_state.Locations.Length) return false;

            EntityLocation loc = _state.Locations[index];
            if (!loc.IsValid) return false;

            ComponentTypeId typeId = _componentRegistry.GetOrCreate<T>();
            Archetype archetype = GetArchetypeById(loc.ArchetypeId);
            return archetype.Contains(typeId);
        }

        public ref readonly T GetRO<T>(Entity entity) where T : struct
        {
            ValidateHasLocation(entity);

            ComponentTypeId typeId = _componentRegistry.GetOrCreate<T>();
            EntityLocation loc = _state.Locations[entity.Index];

            Archetype archetype = GetArchetypeById(loc.ArchetypeId);
            if (!archetype.TryGetColumnIndex(typeId, out int columnIndex))
                throw new InvalidOperationException($"Entity {entity} does not have component {typeof(T).Name}.");

            Chunk chunk = archetype.GetChunkById(loc.ChunkId);
            ChunkColumn<T> col = (ChunkColumn<T>)chunk.Columns[columnIndex];
            return ref col.Data[loc.Row];
        }

        public ref T GetRW<T>(Entity entity) where T : struct
        {
            ValidateHasLocation(entity);

            ComponentTypeId typeId = _componentRegistry.GetOrCreate<T>();
            EntityLocation loc = _state.Locations[entity.Index];

            Archetype archetype = GetArchetypeById(loc.ArchetypeId);
            if (!archetype.TryGetColumnIndex(typeId, out int columnIndex))
                throw new InvalidOperationException($"Entity {entity} does not have component {typeof(T).Name}.");

            Chunk chunk = archetype.GetChunkById(loc.ChunkId);
            ChunkColumn<T> col = (ChunkColumn<T>)chunk.Columns[columnIndex];
            return ref col.Data[loc.Row];
        }

        public EntityLocation GetLocation(Entity entity)
        {
            int index = entity.Index;
            if ((uint)index >= (uint)_state.Locations.Length) return EntityLocation.Invalid;

            return _state.Locations[index];
        }

        public void SetLocation(int entityIndex, EntityLocation loc)
        {
            _state.Locations[entityIndex] = loc;
        }

        public Archetype GetEmptyArchetype()
        {
            return _state.EmptyArchetype;
        }

        public Archetype GetOrCreateArchetype(ArchetypeSignature signature)
        {
            ulong hash = signature.ComputeStableHash64();
            ArchetypeId id = new(hash);

            if (_state.ArchetypesByHash.TryGetValue(hash, out List<Archetype> existing))
            {
                for (int i = 0; i < existing.Count; i++)
                {
                    if (existing[i].Signature.Equals(signature)) return existing[i];
                }

                Archetype created = new(signature, id);
                existing.Add(created);
                existing.Sort((a, b) => ArchetypeSignature.CompareLexicographic(a.Signature, b.Signature));

                unchecked
                {
                    _state.ArchetypeVersion++;
                }

                return created;
            }

            Archetype fresh = new(signature, id);
            _state.ArchetypesByHash.Add(hash, new List<Archetype>(1) { fresh });

            unchecked
            {
                _state.ArchetypeVersion++;
            }

            return fresh;
        }

        public Archetype GetArchetypeById(ArchetypeId id)
        {
            if (_state.ArchetypesByHash.TryGetValue(id.Value, out List<Archetype> list)) return list[0];

            throw new InvalidOperationException($"Archetype {id} was not found.");
        }

        public IEnumerable<Archetype> EnumerateArchetypesStable()
        {
            foreach (KeyValuePair<ulong, List<Archetype>> kv in _state.ArchetypesByHash)
            {
                List<Archetype> list = kv.Value;
                for (int i = 0; i < list.Count; i++)
                {
                    yield return list[i];
                }
            }
        }

        public void PlaceInEmptyArchetype(Entity entity)
        {
            Archetype empty = _state.EmptyArchetype;

            Chunk chunk = empty.GetOrCreateChunkWithSpace(
                WorldState.DefaultChunkCapacity,
                cap => CreateColumnsForSignature(cap, empty.Signature));

            int row = chunk.AddEntity(entity);

            _state.Locations[entity.Index] = new EntityLocation
            {
                ArchetypeId = empty.ArchetypeId,
                ChunkId = chunk.ChunkId,
                Row = row
            };
        }

        public void RemoveFromStorage(Entity entity)
        {
            int index = entity.Index;

            EntityLocation loc = _state.Locations[index];
            if (!loc.IsValid) return;

            Archetype archetype = GetArchetypeById(loc.ArchetypeId);
            Chunk chunk = archetype.GetChunkById(loc.ChunkId);

            chunk.RemoveAtSwapBack(loc.Row, out Entity swappedEntity, out bool didSwap);

            if (didSwap)
                _state.Locations[swappedEntity.Index] = new EntityLocation
                {
                    ArchetypeId = loc.ArchetypeId,
                    ChunkId = loc.ChunkId,
                    Row = loc.Row
                };

            _state.Locations[index] = EntityLocation.Invalid;
        }

        public Chunk AllocateDestinationSlot(Archetype dstArchetype, Entity entity, out int dstRow)
        {
            Chunk dstChunk = dstArchetype.GetOrCreateChunkWithSpace(
                WorldState.DefaultChunkCapacity,
                cap => CreateColumnsForSignature(cap, dstArchetype.Signature));

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

            srcChunk.RemoveAtSwapBack(removedRow, out Entity swappedEntity, out bool didSwap);
            if (didSwap)
                _state.Locations[swappedEntity.Index] = new EntityLocation
                {
                    ArchetypeId = srcArchetype.ArchetypeId,
                    ChunkId = srcLoc.ChunkId,
                    Row = removedRow
                };
        }

        public IChunkColumn[] CreateColumnsForSignature(int capacity, ArchetypeSignature signature)
        {
            int[] typeIds = signature.TypeIds;

            IChunkColumn[] cols = EcsArrayPool<IChunkColumn>.Rent(typeIds.Length);
            for (int i = 0; i < typeIds.Length; i++)
            {
                cols[i] = CreateTypedColumn(typeIds[i], capacity);
            }

            return cols;
        }

        private IChunkColumn CreateTypedColumn(int typeId, int capacity)
        {
            if (!_typedColumnFactories.TryGetValue(typeId, out Func<int, IChunkColumn> factory))
                throw new InvalidOperationException(
                    $"No column factory registered for ComponentTypeId {typeId}. " +
                    $"Call World.RegisterComponent<T>() for each component type used in chunks.");

            return factory(capacity);
        }

        private void ValidateHasLocation(Entity entity)
        {
            int index = entity.Index;

            if ((uint)index >= (uint)_state.Locations.Length)
                throw new InvalidOperationException($"Entity {entity} index is out of range.");

            if (!_state.Locations[index].IsValid)
                throw new InvalidOperationException($"Entity {entity} does not have a valid storage location.");
        }
    }
}