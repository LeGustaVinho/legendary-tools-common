using System;
using System.Collections.Generic;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds
{
    public sealed partial class World
    {
        // Single ECB instance (MVP). Later: per-system/per-thread buffers + merge/sort.
        internal EntityCommandBuffer StateEcb { get; private set; }

        private void EnsureEcbInitialized()
        {
            if (StateEcb == null) StateEcb = new EntityCommandBuffer(this);
        }

        /// <summary>
        /// Internal immediate create used by ECB playback.
        /// </summary>
        /// <returns>Created entity.</returns>
        internal Entity InternalCreateEntity()
        {
            Entity e = Entities.CreateEntity();
            Storage.PlaceInEmptyArchetype(e);
            return e;
        }

        /// <summary>
        /// Internal immediate destroy used by ECB playback.
        /// </summary>
        /// <param name="e">Entity to destroy.</param>
        internal void InternalDestroyEntity(Entity e)
        {
            // If entity is already dead/stale, ignore for MVP resilience.
            if (!Entities.IsAlive(e)) return;

            Storage.RemoveFromStorage(e);
            Entities.FinalizeDestroy(e);
        }

        /// <summary>
        /// Internal immediate add used by ECB playback.
        /// </summary>
        /// <typeparam name="T">Component type.</typeparam>
        /// <param name="e">Entity.</param>
        /// <param name="value">Component value.</param>
        internal void InternalAdd<T>(Entity e, in T value) where T : struct
        {
            Structural.Add(e, value);
        }

        /// <summary>
        /// Internal immediate remove used by ECB playback.
        /// </summary>
        /// <typeparam name="T">Component type.</typeparam>
        /// <param name="e">Entity.</param>
        internal void InternalRemove<T>(Entity e) where T : struct
        {
            Structural.Remove<T>(e);
        }

        /// <summary>
        /// Internal remove by ComponentTypeId used by ECB playback.
        /// MVP implementation: resolves the typeId via registered remove delegates.
        /// </summary>
        /// <param name="e">Entity.</param>
        /// <param name="componentTypeId">Component type ID.</param>
        internal void InternalRemoveByTypeId(Entity e, int componentTypeId)
        {
            if (!_removeByTypeId.TryGetValue(componentTypeId, out Action<Entity> remover))
                throw new InvalidOperationException(
                    $"No remove delegate registered for ComponentTypeId {componentTypeId}. " +
                    "Call World.RegisterComponent<T>() for each component type used in structural changes.");

            remover(e);
        }

        private readonly Dictionary<int, Action<Entity>> _removeByTypeId = new(128);

        private void RegisterRemoveDelegate<T>() where T : struct
        {
            ComponentTypeId id = GetComponentTypeId<T>();
            if (_removeByTypeId.ContainsKey(id.Value)) return;

            _removeByTypeId.Add(id.Value, ent => InternalRemove<T>(ent));
        }
    }
}