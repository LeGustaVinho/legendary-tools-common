using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Demo.Profiling;
using LegendaryTools.Common.Core.Patterns.ECS.Demo.Simulation.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Queries;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;
using LegendaryTools.Common.Core.Patterns.ECS.Systems;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;

namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.Simulation.Systems
{
    /// <summary>
    /// Decrements Lifetime and destroys entities via ECB when it reaches zero.
    /// </summary>
    public sealed class LifetimeSystem : ISystem
    {
        private readonly EcsDemoTickCounters _counters;
        private Query _query;

        public LifetimeSystem(EcsDemoTickCounters counters)
        {
            _counters = counters;
        }

        public void OnCreate(World world)
        {
            world.RegisterComponent<Lifetime>();

            // Avoid stackalloc/unsafe in demo. Query allocates once on create; hot path remains allocation-free.
            ComponentTypeId[] all = new ComponentTypeId[1];
            all[0] = world.GetComponentTypeId<Lifetime>();

            _query = new Query(all, default);
        }

        public void OnUpdate(World world, int tick)
        {
            LifetimeChunkProcessor processor = new(world.GetComponentTypeId<Lifetime>(), world, _counters);
            world.ForEachChunk(_query, ref processor);
        }

        public void OnDestroy(World world)
        {
        }

        private struct LifetimeChunkProcessor : IChunkProcessor
        {
            private readonly ComponentTypeId _lifeId;
            private readonly World _world;
            private readonly EcsDemoTickCounters _counters;

            public LifetimeChunkProcessor(ComponentTypeId lifeId, World world, EcsDemoTickCounters counters)
            {
                _lifeId = lifeId;
                _world = world;
                _counters = counters;
            }

            public void Execute(Archetype archetype, Chunk chunk)
            {
                if (!archetype.TryGetColumnIndex(_lifeId, out int lifeIndex)) return;

                ChunkColumn<Lifetime> lifeCol = (ChunkColumn<Lifetime>)chunk.Columns[lifeIndex];
                Lifetime[] life = lifeCol.Data;

                Entity[] entities = chunk.Entities;
                int count = chunk.Count;

                int destroyed = 0;

                for (int i = 0; i < count; i++)
                {
                    int remaining = life[i].TicksRemaining - 1;
                    life[i].TicksRemaining = remaining;

                    if (remaining <= 0)
                    {
                        _world.ECB.DestroyEntity(entities[i]);
                        destroyed++;
                    }
                }

                if (_counters != null && destroyed > 0) _counters.AddDestroy(destroyed);
            }
        }
    }
}