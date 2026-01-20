using UnityEngine;
using LegendaryTools.Common.Core.Patterns.ECS.Demo.Simulation.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;

namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.Simulation
{
    /// <summary>
    /// Creates initial demo entities (outside tick, immediate structural changes allowed).
    /// </summary>
    public static class EcsDemoEntityFactory
    {
        public static void CreateInitialEntities(World world, EcsDemoConfig config)
        {
            int count = Mathf.Max(1, config.InitialEntityCount);

            for (int i = 0; i < count; i++)
            {
                Entity e = world.CreateEntity();

                Position p = new()
                {
                    X = i % 100 * 0.1f,
                    Y = 0.0f,
                    Z = i / 100 * 0.1f
                };

                Velocity v = new()
                {
                    X = 0.01f + (i & 7) * 0.001f,
                    Y = 0.0f,
                    Z = 0.01f + (i & 3) * 0.001f
                };

                Lifetime life = new()
                {
                    TicksRemaining = Random.Range(config.LifetimeMinTicks, config.LifetimeMaxTicks + 1)
                };

                Health hp = new()
                {
                    Value = 100
                };

                world.Add(e, p);
                world.Add(e, v);
                world.Add(e, life);
                world.Add(e, hp);
            }
        }
    }
}