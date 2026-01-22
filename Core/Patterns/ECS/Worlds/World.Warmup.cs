using System;
using LegendaryTools.Common.Core.Patterns.ECS.Queries;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds
{
    public sealed partial class World
    {
        /// <summary>
        /// Pre-allocates ECB buffers and sets hard limits in strict determinism builds (ECS_DETERMINISM_CHECKS).
        /// Call during initialization (outside simulation update).
        /// </summary>
        public void WarmupEcb(int expectedCommands, int expectedTempEntities)
        {
            EnsureEcbInitialized();
            StateEcb.Warmup(expectedCommands, expectedTempEntities);
        }

        /// <summary>
        /// Pre-allocates ECB value storage for a given component type.
        /// Call during initialization (outside simulation update).
        /// </summary>
        public void WarmupEcbValues<T>(int expectedAddsForType) where T : struct
        {
            EnsureEcbInitialized();
            StateEcb.WarmupValues<T>(expectedAddsForType);
        }

        /// <summary>
        /// Warms up a query cache against the current structural state to avoid cache rebuild on first use.
        /// Call after initial structural setup (entities/components creation).
        /// </summary>
        public void WarmupQuery(Query query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            // Builds/refreshes cache for current StructuralVersion.
            query.GetOrBuildCache(Storage);
        }
    }
}