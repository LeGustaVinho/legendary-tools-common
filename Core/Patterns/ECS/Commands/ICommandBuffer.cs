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

        void DestroyEntity(Entity entity);

        /// <summary>
        /// Destroys an entity with a deterministic sort key.
        /// The sort key participates in ECB playback ordering.
        /// </summary>
        void DestroyEntity(Entity entity, int sortKey);

        void Add<T>(Entity entity) where T : struct;

        void Add<T>(Entity entity, int sortKey) where T : struct;

        void Add<T>(Entity entity, in T value) where T : struct;

        void Add<T>(Entity entity, in T value, int sortKey) where T : struct;

        void Remove<T>(Entity entity) where T : struct;

        void Remove<T>(Entity entity, int sortKey) where T : struct;
    }
}
