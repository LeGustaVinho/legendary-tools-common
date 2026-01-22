using LegendaryTools.Common.Core.Patterns.ECS.Entities;

namespace LegendaryTools.Common.Core.Patterns.ECS.Commands
{
    /// <summary>
    /// Command buffer for structural changes. Designed to be extended for multithreading and deterministic sorting.
    /// </summary>
    public interface ICommandBuffer
    {
        /// <summary>
        /// Creates an entity (deferred).
        /// Returns a temporary entity handle valid only for commands within this buffer.
        /// </summary>
        /// <returns>Temporary entity handle.</returns>
        Entity CreateEntity();

        /// <summary>
        /// Creates a temporary entity with a deterministic sort key.
        /// The sort key participates in ECB playback ordering.
        /// </summary>
        Entity CreateEntity(int sortKey);

        /// <summary>
        /// Destroys an entity.
        /// </summary>
        /// <param name="entity">The entity to destroy.</param>
        void DestroyEntity(Entity entity);

        /// <summary>
        /// Destroys an entity with a deterministic sort key.
        /// The sort key participates in ECB playback ordering.
        /// </summary>
        void DestroyEntity(Entity entity, int sortKey);

        /// <summary>
        /// Adds a component of type T to the entity. default(T) is used if T has data.
        /// </summary>
        /// <typeparam name="T">The component type.</typeparam>
        /// <param name="entity">The target entity.</param>
        void Add<T>(Entity entity) where T : struct;

        /// <summary>
        /// Adds a component of type T to the entity with a deterministic sort key.
        /// </summary>
        /// <typeparam name="T">The component type.</typeparam>
        /// <param name="entity">The target entity.</param>
        /// <param name="sortKey">The sort key for deterministic playback.</param>
        void Add<T>(Entity entity, int sortKey) where T : struct;

        /// <summary>
        /// Adds a component of type T to the entity with an initial value.
        /// </summary>
        /// <typeparam name="T">The component type.</typeparam>
        /// <param name="entity">The target entity.</param>
        /// <param name="value">The component value.</param>
        void Add<T>(Entity entity, in T value) where T : struct;

        /// <summary>
        /// Adds a component of type T to the entity with an initial value and a deterministic sort key.
        /// </summary>
        /// <typeparam name="T">The component type.</typeparam>
        /// <param name="entity">The target entity.</param>
        /// <param name="value">The component value.</param>
        /// <param name="sortKey">The sort key for deterministic playback.</param>
        void Add<T>(Entity entity, in T value, int sortKey) where T : struct;

        /// <summary>
        /// Removes a component of type T from the entity.
        /// </summary>
        /// <typeparam name="T">The component type.</typeparam>
        /// <param name="entity">The target entity.</param>
        void Remove<T>(Entity entity) where T : struct;

        /// <summary>
        /// Removes a component of type T from the entity with a deterministic sort key.
        /// </summary>
        /// <typeparam name="T">The component type.</typeparam>
        /// <param name="entity">The target entity.</param>
        /// <param name="sortKey">The sort key for deterministic playback.</param>
        void Remove<T>(Entity entity, int sortKey) where T : struct;
    }
}