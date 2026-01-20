using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Demo.Simulation.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Queries;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;
using LegendaryTools.Common.Core.Patterns.ECS.Systems;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;

namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.Simulation.Systems
{
    /// <summary>
    /// Applies periodic damage to Health (cheap arithmetic to keep CPU busy).
    /// </summary>
    public sealed class DamageSystem : ISystem
    {
        private Query _query;

        public void OnCreate(World world)
        {
            world.RegisterComponent<Health>();

            // Avoid stackalloc/unsafe in demo. Query allocates once on create; hot path remains allocation-free.
            ComponentTypeId[] all = new ComponentTypeId[1];
            all[0] = world.GetComponentTypeId<Health>();

            _query = new Query(all, default);
        }

        public void OnUpdate(World world, int tick)
        {
            // Apply damage every 4 ticks to vary cost.
            if ((tick & 3) != 0) return;

            DamageChunkProcessor processor = new(world.GetComponentTypeId<Health>());
            world.ForEachChunk(_query, ref processor);
        }

        public void OnDestroy(World world)
        {
        }

        private struct DamageChunkProcessor : IChunkProcessor
        {
            private readonly ComponentTypeId _hpId;

            public DamageChunkProcessor(ComponentTypeId hpId)
            {
                _hpId = hpId;
            }

            public void Execute(Archetype archetype, Chunk chunk)
            {
                if (!archetype.TryGetColumnIndex(_hpId, out int hpIndex)) return;

                ChunkColumn<Health> hpCol = (ChunkColumn<Health>)chunk.Columns[hpIndex];
                Health[] hp = hpCol.Data;

                int count = chunk.Count;
                for (int i = 0; i < count; i++)
                {
                    int v = hp[i].Value - 1;
                    hp[i].Value = v < 0 ? 0 : v;
                }
            }
        }
    }
}