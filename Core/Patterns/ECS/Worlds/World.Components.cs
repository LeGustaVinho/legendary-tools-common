using System;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds
{
    public sealed partial class World
    {
        /// <summary>
        /// Registers a component type so the world can create typed SoA columns for it.
        /// Call this once during bootstrap for every component type you plan to store in chunks.
        /// </summary>
        /// <typeparam name="T">Component type.</typeparam>
        public void RegisterComponent<T>() where T : struct
        {
            Storage.RegisterComponent<T>();

            // Also register remove delegate so ECB can remove by type id without reflection.
            RegisterRemoveDelegate<T>();
        }

        /// <summary>
        /// Gets the stable component type id (within this World) for <typeparamref name="T"/>.
        /// </summary>
        public ComponentTypeId GetComponentTypeId<T>() where T : struct
        {
            return Storage.GetComponentTypeId<T>();
        }

        /// <summary>
        /// Gets a cached handle for a component type. Systems should store this handle and reuse it.
        /// This avoids repeated type registry lookups in hot paths.
        /// </summary>
        public ComponentHandle<T> GetComponentHandle<T>() where T : struct
        {
            return new ComponentHandle<T>(Storage.GetComponentTypeId<T>().Value);
        }

        public bool Has<T>(Entity e) where T : struct
        {
            return Storage.Has<T>(e);
        }

        /// <summary>
        /// Hot path overload: uses a cached handle to avoid repeated type registry lookups.
        /// </summary>
        public bool Has<T>(Entity e, in ComponentHandle<T> handle) where T : struct
        {
            return Storage.Has(e, handle);
        }

        public ref readonly T GetRO<T>(Entity e) where T : struct
        {
            if (!IsAlive(e)) throw new InvalidOperationException($"Entity {e} is not alive (or is stale).");

            return ref Storage.GetRO<T>(e);
        }

        /// <summary>
        /// Hot path overload: uses a cached handle to avoid repeated type registry lookups.
        /// </summary>
        public ref readonly T GetRO<T>(Entity e, in ComponentHandle<T> handle) where T : struct
        {
            if (!IsAlive(e)) throw new InvalidOperationException($"Entity {e} is not alive (or is stale).");

            return ref Storage.GetRO(e, handle);
        }

        public ref T GetRW<T>(Entity e) where T : struct
        {
            if (!IsAlive(e)) throw new InvalidOperationException($"Entity {e} is not alive (or is stale).");

            return ref Storage.GetRW<T>(e);
        }

        /// <summary>
        /// Hot path overload: uses a cached handle to avoid repeated type registry lookups.
        /// </summary>
        public ref T GetRW<T>(Entity e, in ComponentHandle<T> handle) where T : struct
        {
            if (!IsAlive(e)) throw new InvalidOperationException($"Entity {e} is not alive (or is stale).");

            return ref Storage.GetRW(e, handle);
        }

        public void Add<T>(Entity e) where T : struct
        {
            if (State.IsUpdating)
                throw new InvalidOperationException(
                    "Structural changes are not allowed during update. Use ECB.Add<T>(entity).");

            Structural.Add(e, default(T));
        }

        /// <summary>
        /// Adds (or replaces) a component value on an entity (structural change).
        /// Forbidden during tick update; use <see cref="ECB"/> instead.
        /// </summary>
        public void Add<T>(Entity e, in T value) where T : struct
        {
            if (State.IsUpdating)
                throw new InvalidOperationException(
                    "Structural changes are not allowed during update. Use ECB.Add<T>(entity, value).");

            Structural.Add(e, value);
        }

        /// <summary>
        /// Removes a component from an entity (structural change).
        /// Forbidden during tick update; use <see cref="ECB"/> instead.
        /// </summary>
        public void Remove<T>(Entity e) where T : struct
        {
            if (State.IsUpdating)
                throw new InvalidOperationException(
                    "Structural changes are not allowed during update. Use ECB.Remove<T>(entity).");

            Structural.Remove<T>(e);
        }
    }
}