using System;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal
{
    /// <summary>
    /// Facade for storage operations. Responsibilities are split across specialized internal services:
    /// - ComponentTypeStore: component type ids + column factories
    /// - ArchetypeStore: archetype registry/creation + stable enumeration
    /// - ChunkStorageOps: placement/moves/removals inside chunks + location fixes
    /// - EntityComponentAccessor: hot-path per-entity component access (Has/GetRO/GetRW)
    /// </summary>
    internal sealed class StorageService
    {
        private readonly WorldState _state;

        private readonly ComponentTypeStore _components;
        private readonly ArchetypeStore _archetypes;
        private readonly ChunkStorageOps _chunks;
        private readonly EntityComponentAccessor _accessor;

        public StorageService(WorldState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));

            _components = new ComponentTypeStore();
            _archetypes = new ArchetypeStore(_state);
            _chunks = new ChunkStorageOps(_state, _archetypes, _components);
            _accessor = new EntityComponentAccessor(_state, _archetypes, _components);
        }

        public int ArchetypeVersion => _state.ArchetypeVersion;

        public int StructuralVersion => _state.StructuralVersion;

        public void InitializeEmptyArchetype()
        {
            _archetypes.InitializeEmptyArchetype();
        }

        public void RegisterComponent<T>() where T : struct
        {
            _components.RegisterComponent<T>();
        }

        public ComponentTypeId GetComponentTypeId<T>() where T : struct
        {
            return _components.GetComponentTypeId<T>();
        }

        public bool Has<T>(Entity entity) where T : struct
        {
            return _accessor.Has<T>(entity);
        }

        public bool Has<T>(Entity entity, in ComponentHandle<T> handle) where T : struct
        {
            return _accessor.Has(entity, handle);
        }

        public ref readonly T GetRO<T>(Entity entity) where T : struct
        {
            return ref _accessor.GetRO<T>(entity);
        }

        public ref readonly T GetRO<T>(Entity entity, in ComponentHandle<T> handle) where T : struct
        {
            return ref _accessor.GetRO(entity, handle);
        }

        public ref T GetRW<T>(Entity entity) where T : struct
        {
            return ref _accessor.GetRW<T>(entity);
        }

        public ref T GetRW<T>(Entity entity, in ComponentHandle<T> handle) where T : struct
        {
            return ref _accessor.GetRW(entity, handle);
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
            return _archetypes.GetEmptyArchetype();
        }

        public Archetype GetOrCreateArchetype(ArchetypeSignature signature)
        {
            return _archetypes.GetOrCreateArchetype(signature);
        }

        internal Archetype GetOrCreateArchetypeWithAdded(Archetype srcArchetype, ComponentTypeId addTypeId)
        {
            return _archetypes.GetOrCreateArchetypeWithAdded(srcArchetype, addTypeId);
        }

        internal Archetype GetOrCreateArchetypeWithRemoved(Archetype srcArchetype, ComponentTypeId removeTypeId)
        {
            return _archetypes.GetOrCreateArchetypeWithRemoved(srcArchetype, removeTypeId);
        }

        public Archetype GetArchetypeById(ArchetypeId id)
        {
            return _archetypes.GetArchetypeById(id);
        }

        public ArchetypeEnumerable EnumerateArchetypesStable()
        {
            return _archetypes.EnumerateArchetypesStable();
        }

        public void PlaceInEmptyArchetype(Entity entity)
        {
            _chunks.PlaceInEmptyArchetype(entity);
        }

        public void RemoveFromStorage(Entity entity)
        {
            _chunks.RemoveFromStorage(entity);
        }

        public Chunk AllocateDestinationSlot(Archetype dstArchetype, Entity entity, out int dstRow)
        {
            return _chunks.AllocateDestinationSlot(dstArchetype, entity, out dstRow);
        }

        public void CopyOverlappingComponents(
            Archetype srcArchetype,
            Chunk srcChunk,
            int srcRow,
            Archetype dstArchetype,
            Chunk dstChunk,
            int dstRow)
        {
            _chunks.CopyOverlappingComponents(srcArchetype, srcChunk, srcRow, dstArchetype, dstChunk, dstRow);
        }

        public void RemoveFromSourceAndFixSwap(
            Archetype srcArchetype,
            EntityLocation srcLoc,
            int removedRow)
        {
            _chunks.RemoveFromSourceAndFixSwap(srcArchetype, srcLoc, removedRow);
        }

        public IChunkColumn[] CreateColumnsForSignature(int capacity, ArchetypeSignature signature)
        {
            return _components.CreateColumnsForSignature(capacity, signature);
        }
    }
}