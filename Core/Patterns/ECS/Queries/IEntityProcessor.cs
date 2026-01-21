using LegendaryTools.Common.Core.Patterns.ECS.Entities;

namespace LegendaryTools.Common.Core.Patterns.ECS.Queries
{
    /// <summary>
    /// Per-entity callback processor.
    /// Convenience API only: prefer chunk/span iteration for hot paths.
    /// </summary>
    public interface IEntityProcessor
    {
        /// <summary>
        /// Executes once per entity.
        /// </summary>
        /// <param name="entity">Entity handle.</param>
        void Execute(Entity entity);
    }
}