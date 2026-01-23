using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Queries;
using NUnit.Framework;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;

namespace LegendaryTools.Tests.ECS
{
    public sealed class DeterministicParallelEcsTests
    {
        [Test]
        public void TwoParallelSimulations_ProduceIdenticalWorldSnapshot()
        {
            const int workerCount = 4;
            const int ticks = 64;
            const int initialEntities = 2000;
            const uint seed = 0xC0FFEEu;

            ulong a = RunSimulation(seed, workerCount, ticks, initialEntities);
            ulong b = RunSimulation(seed, workerCount, ticks, initialEntities);

            Assert.AreEqual(a, b, "Deterministic parallel ECS simulation produced different snapshots between runs.");
        }

        private static ulong RunSimulation(uint seed, int workerCount, int ticks, int initialEntities)
        {
            // Deterministic mode must be enabled to activate strict ECB rules.
            World world = new(
                initialEntities * 2,
                128,
                deterministic: true);

            world.RegisterComponent<TestPosition>();
            world.RegisterComponent<TestVelocity>();

            // Warmup ECB for deterministic parallel use.
            // Keep these safely above the expected per-tick traffic.
            world.WarmupEcbParallel(workerCount, 4096, 512);
            world.WarmupEcbValuesParallel<TestPosition>(workerCount, 2048);
            world.WarmupEcbValuesParallel<TestVelocity>(workerCount, 2048);

            // Create initial entities deterministically.
            uint rng = seed;
            for (int i = 0; i < initialEntities; i++)
            {
                Entity e = world.CreateEntity();

                // Deterministic RNG (LCG).
                rng = unchecked(rng * 1664525u + 1013904223u);
                int px = (int)(rng % 2000u) - 1000;
                rng = unchecked(rng * 1664525u + 1013904223u);
                int py = (int)(rng % 2000u) - 1000;

                rng = unchecked(rng * 1664525u + 1013904223u);
                int vx = (int)(rng % 7u) - 3;
                rng = unchecked(rng * 1664525u + 1013904223u);
                int vy = (int)(rng % 7u) - 3;

                world.Add(e, new TestPosition(px, py));
                world.Add(e, new TestVelocity(vx, vy));
            }

            // Drive deterministic multi-thread simulation.
            Query query = world.QueryAll<TestPosition, TestVelocity>();

            ParallelMoveSpawnProcessor processor = new(
                world.GetComponentTypeId<TestPosition>(),
                world.GetComponentTypeId<TestVelocity>(),
                257,
                97);

            for (int tick = 1; tick <= ticks; tick++)
            {
                world.BeginTick(tick);

                processor.Tick = tick;
                world.ForEachChunkParallel(query, workerCount, ref processor);

                world.EndTick();
            }

            // Snapshot hash must match between independent runs.
            return EcsWorldSnapshotHasher.ComputePositionVelocitySnapshot(world);
        }
    }
}