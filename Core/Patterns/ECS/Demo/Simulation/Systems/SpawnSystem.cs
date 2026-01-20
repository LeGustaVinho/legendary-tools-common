using UnityEngine;
using LegendaryTools.Common.Core.Patterns.ECS.Demo.Profiling;
using LegendaryTools.Common.Core.Patterns.ECS.Demo.Simulation.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Systems;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;

namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.Simulation.Systems
{
    /// <summary>
    /// Spawns a fixed number of entities per tick via ECB (optional).
    /// </summary>
    public sealed class SpawnSystem : ISystem
    {
        private readonly EcsDemoTickCounters _counters;
        private readonly int _spawnPerTick;
        private readonly int _lifeMin;
        private readonly int _lifeMax;

        public SpawnSystem(EcsDemoTickCounters counters, int spawnPerTick, int lifetimeMinTicks, int lifetimeMaxTicks)
        {
            _counters = counters;
            _spawnPerTick = Mathf.Max(0, spawnPerTick);
            _lifeMin = Mathf.Max(1, lifetimeMinTicks);
            _lifeMax = Mathf.Max(_lifeMin, lifetimeMaxTicks);
        }

        public void OnCreate(World world)
        {
            world.RegisterComponent<Position>();
            world.RegisterComponent<Velocity>();
            world.RegisterComponent<Lifetime>();
            world.RegisterComponent<Health>();
        }

        public void OnUpdate(World world, int tick)
        {
            if (_spawnPerTick <= 0) return;

            for (int i = 0; i < _spawnPerTick; i++)
            {
                Entity e = world.ECB.CreateEntity();

                Position p = new()
                {
                    X = i % 20 * 0.2f,
                    Y = 0.0f,
                    Z = i / 20 * 0.2f
                };

                Velocity v = new()
                {
                    X = 0.02f,
                    Y = 0.0f,
                    Z = 0.01f
                };

                Lifetime life = new()
                {
                    TicksRemaining = Random.Range(_lifeMin, _lifeMax + 1)
                };

                Health hp = new()
                {
                    Value = 100
                };

                world.ECB.Add(e, p);
                world.ECB.Add(e, v);
                world.ECB.Add(e, life);
                world.ECB.Add(e, hp);
            }

            if (_counters != null) _counters.AddSpawn(_spawnPerTick);
        }

        public void OnDestroy(World world)
        {
        }
    }
}