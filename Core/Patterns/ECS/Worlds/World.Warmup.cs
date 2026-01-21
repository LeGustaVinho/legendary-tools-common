using System;
using LegendaryTools.Common.Core.Patterns.ECS.Queries;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds
{
    public sealed partial class World
    {
        /// <summary>
        /// Pre-allocates ECB buffers to avoid growth (Rent/Return) during hotpaths.
        /// Call during initialization (outside simulation update).
        /// </summary>
        public void WarmupEcb(int expectedCommands, int expectedTempEntities)
        {
            EnsureEcbInitialized();
            StateEcb.Warmup(expectedCommands, expectedTempEntities);
        }

        /// <summary>
        /// Warms up a query cache against the current structural state to avoid cache rebuild on first use.
        /// Call during initialization after your initial structural setup (entities/components creation).
        /// </summary>
        public void WarmupQuery(Query query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            // Builds/refreshes cache for current StructuralVersion.
            query.GetOrBuildCache(Storage);
        }
    }
}