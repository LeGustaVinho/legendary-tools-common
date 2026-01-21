using System;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal
{
    /// <summary>
    /// Hot-path per-entity access to components (Has/GetRO/GetRW).
    /// Avoids dictionary lookups inside archetypes by using Archetype.TryGetColumnIndexFast.
    /// </summary>
    internal sealed class EntityComponentAccessor
    {
        private readonly WorldState _state;
        private readonly ArchetypeStore _archetypes;
        private readonly ComponentTypeStore _components;

        public EntityComponentAccessor(WorldState state, ArchetypeStore archetypes, ComponentTypeStore components)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _archetypes = archetypes ?? throw new ArgumentNullException(nameof(archetypes));
            _components = components ?? throw new ArgumentNullException(nameof(components));
        }

        public bool Has<T>(Entity entity) where T : struct
        {
            int index = entity.Index;
            if ((uint)index >= (uint)_state.Locations.Length) return false;

            EntityLocation loc = _state.Locations[index];
            if (!loc.IsValid) return false;

            ComponentTypeId typeId = _components.GetComponentTypeId<T>();
            Archetype archetype = _archetypes.GetArchetypeById(loc.ArchetypeId);

            return archetype.TryGetColumnIndexFast(typeId, out _);
        }

        public bool Has<T>(Entity entity, in ComponentHandle<T> handle) where T : struct
        {
            int index = entity.Index;
            if ((uint)index >= (uint)_state.Locations.Length) return false;

            EntityLocation loc = _state.Locations[index];
            if (!loc.IsValid) return false;

            Archetype archetype = _archetypes.GetArchetypeById(loc.ArchetypeId);
            return archetype.TryGetColumnIndexFast(handle.TypeId, out _);
        }

        public ref readonly T GetRO<T>(Entity entity) where T : struct
        {
            ValidateHasLocation(entity);

            ComponentTypeId typeId = _components.GetComponentTypeId<T>();
            EntityLocation loc = _state.Locations[entity.Index];

            Archetype archetype = _archetypes.GetArchetypeById(loc.ArchetypeId);
            if (!archetype.TryGetColumnIndexFast(typeId, out int columnIndex))
                throw new InvalidOperationException($"Entity {entity} does not have component {typeof(T).Name}.");

            Chunk chunk = archetype.GetChunkById(loc.ChunkId);
            ChunkColumn<T> col = (ChunkColumn<T>)chunk.Columns[columnIndex];
            return ref col.Data[loc.Row];
        }

        public ref readonly T GetRO<T>(Entity entity, in ComponentHandle<T> handle) where T : struct
        {
            ValidateHasLocation(entity);

            EntityLocation loc = _state.Locations[entity.Index];

            Archetype archetype = _archetypes.GetArchetypeById(loc.ArchetypeId);
            if (!archetype.TryGetColumnIndexFast(handle.TypeId, out int columnIndex))
                throw new InvalidOperationException($"Entity {entity} does not have component {typeof(T).Name}.");

            Chunk chunk = archetype.GetChunkById(loc.ChunkId);
            ChunkColumn<T> col = (ChunkColumn<T>)chunk.Columns[columnIndex];
            return ref col.Data[loc.Row];
        }

        public ref T GetRW<T>(Entity entity) where T : struct
        {
            ValidateHasLocation(entity);

            ComponentTypeId typeId = _components.GetComponentTypeId<T>();
            EntityLocation loc = _state.Locations[entity.Index];

            Archetype archetype = _archetypes.GetArchetypeById(loc.ArchetypeId);
            if (!archetype.TryGetColumnIndexFast(typeId, out int columnIndex))
                throw new InvalidOperationException($"Entity {entity} does not have component {typeof(T).Name}.");

            Chunk chunk = archetype.GetChunkById(loc.ChunkId);
            ChunkColumn<T> col = (ChunkColumn<T>)chunk.Columns[columnIndex];
            return ref col.Data[loc.Row];
        }

        public ref T GetRW<T>(Entity entity, in ComponentHandle<T> handle) where T : struct
        {
            ValidateHasLocation(entity);

            EntityLocation loc = _state.Locations[entity.Index];

            Archetype archetype = _archetypes.GetArchetypeById(loc.ArchetypeId);
            if (!archetype.TryGetColumnIndexFast(handle.TypeId, out int columnIndex))
                throw new InvalidOperationException($"Entity {entity} does not have component {typeof(T).Name}.");

            Chunk chunk = archetype.GetChunkById(loc.ChunkId);
            ChunkColumn<T> col = (ChunkColumn<T>)chunk.Columns[columnIndex];
            return ref col.Data[loc.Row];
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