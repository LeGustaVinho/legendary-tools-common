using LegendaryTools.Common.Core.Patterns.ECS.Entities;

namespace LegendaryTools.Common.Core.Patterns.ECS.Queries
{
    /// <summary>
    /// Allocation-free entity processing contract.
    /// Implement this as a struct and pass by ref to avoid allocations.
    /// </summary>
    public interface IEntityProcessor
    {
        /// <summary>
        /// Executes logic for an entity during iteration.
        /// </summary>
        /// <param name="entity">Entity.</param>
        void Execute(Entity entity);
    }
}
