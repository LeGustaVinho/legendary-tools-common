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
        /// Destroys an entity (deferred).
        /// </summary>
        /// <param name="entity">Entity to destroy.</param>
        void DestroyEntity(Entity entity);

        /// <summary>
        /// Adds a component with default value (deferred).
        /// </summary>
        /// <typeparam name="T">Component type.</typeparam>
        /// <param name="entity">Target entity.</param>
        void Add<T>(Entity entity) where T : struct;

        /// <summary>
        /// Adds a component value (deferred).
        /// </summary>
        /// <typeparam name="T">Component type.</typeparam>
        /// <param name="entity">Target entity.</param>
        /// <param name="value">Component value.</param>
        void Add<T>(Entity entity, in T value) where T : struct;

        /// <summary>
        /// Removes a component (deferred).
        /// </summary>
        /// <typeparam name="T">Component type.</typeparam>
        /// <param name="entity">Target entity.</param>
        void Remove<T>(Entity entity) where T : struct;
    }
}