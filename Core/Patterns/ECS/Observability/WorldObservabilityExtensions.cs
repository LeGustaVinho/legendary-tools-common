using System;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;

namespace LegendaryTools.Common.Core.Patterns.ECS.Observability
{
    /// <summary>
    /// Convenience API for tooling and gameplay code.
    /// Keeps usage ergonomic without changing core.
    /// </summary>
    public static class WorldObservabilityExtensions
    {
        public static EcsWorldStats GetStats(this World world)
        {
            return EcsWorldInspector.GetStats(world);
        }

        public static string DumpWorld(this World world, bool includeArchetypes = true, bool includeChunks = false)
        {
            return EcsWorldInspector.DumpWorld(world, includeArchetypes, includeChunks);
        }

        public static EcsEntityDebugInfo GetEntityInfo(this World world, Entity entity)
        {
            return EcsWorldInspector.GetEntityInfo(world, entity);
        }

        public static bool ValidateInvariants(this World world, out string error)
        {
            return EcsInvariantValidator.Validate(world, out error);
        }

        public static void ThrowIfInvalid(this World world)
        {
            if (!world.ValidateInvariants(out string error))
                throw new InvalidOperationException(error);
        }
    }
}