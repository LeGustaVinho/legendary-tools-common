using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Random;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;

namespace LegendaryTools.Tests.ECS.Boids
{
    public static class BoidsBootstrap
    {
        // Fixed-point scale
        private const int Scale = 1000;

        // World bounds (fixed-point)
        private const int MinX = -25 * Scale;
        private const int MaxX = 25 * Scale;
        private const int MinY = -25 * Scale;
        private const int MaxY = 25 * Scale;

        public static void Spawn(World world, ulong seed, int boidCount)
        {
            // One deterministic stream for bootstrap.
            DeterministicRng rng = new(seed, 1);

            for (int i = 0; i < boidCount; i++)
            {
                Entity e = world.CreateEntity();

                int px = rng.NextInt(MinX, MaxX);
                int py = rng.NextInt(MinY, MaxY);

                // Small initial velocity.
                int vx = rng.NextInt(-2 * Scale, 2 * Scale);
                int vy = rng.NextInt(-2 * Scale, 2 * Scale);

                world.Add(e, new BoidPosition(px, py));
                world.Add(e, new BoidVelocity(vx, vy));

                // Initialize next buffers.
                world.Add(e, new BoidNextPosition(px, py));
                world.Add(e, new BoidNextVelocity(vx, vy));
            }
        }
    }
}