using LegendaryTools.Common.Core.Patterns.ECS.Queries;
using NUnit.Framework;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;

namespace LegendaryTools.Tests.ECS.Boids
{
    public sealed class DeterministicParallelBoidsEcsTests
    {
        [Test]
        public void TwoParallelBoidsSimulations_ProduceIdenticalSnapshot()
        {
            const int workerCount = 4;
            const int ticks = 120;
            const int boidCount = 4096;
            const ulong seed = 0xB01D5EEDUL;

            ulong a = RunBoidsSimulation(seed, workerCount, ticks, boidCount);
            ulong b = RunBoidsSimulation(seed, workerCount, ticks, boidCount);

            Assert.AreEqual(a, b, "Parallel boids ECS simulation produced different snapshots between runs.");
        }

        [Test]
        public void TwoParallelBoidsSimulations_WithEcbSpawnDestroy_ProduceIdenticalSnapshot()
        {
            const int workerCount = 4;
            const int ticks = 120;
            const int boidCount = 4096;
            const ulong seed = 0xB01D5EEDUL;

            ulong a = RunBoidsSimulationWithEcb(seed, workerCount, ticks, boidCount);
            ulong b = RunBoidsSimulationWithEcb(seed, workerCount, ticks, boidCount);

            Assert.AreEqual(a, b,
                "Parallel boids ECS + ECB (spawn/destroy) produced different snapshots between runs.");
        }

        private static ulong RunBoidsSimulation(ulong seed, int workerCount, int ticks, int boidCount)
        {
            World world = new(
                boidCount * 2,
                128,
                deterministic: true);

            world.RegisterComponent<BoidPosition>();
            world.RegisterComponent<BoidVelocity>();
            world.RegisterComponent<BoidNextPosition>();
            world.RegisterComponent<BoidNextVelocity>();

            // ECB not used in this test, but keep it warmed up.
            world.WarmupEcbParallel(workerCount, 1024, 256);

            BoidsBootstrap.Spawn(world, seed, boidCount);

            // World.QueryAll supports up to 3 generic args in this project.
            // We query only the required "current state" components; the processors assume the next buffers exist.
            Query query = world.QueryAll<BoidPosition, BoidVelocity>();

            ParallelBoidsStepProcessor step = new(
                seed,
                world.GetComponentTypeId<BoidPosition>(),
                world.GetComponentTypeId<BoidVelocity>(),
                world.GetComponentTypeId<BoidNextPosition>(),
                world.GetComponentTypeId<BoidNextVelocity>());

            ApplyBoidsNextStateProcessor apply = new(
                world.GetComponentTypeId<BoidPosition>(),
                world.GetComponentTypeId<BoidVelocity>(),
                world.GetComponentTypeId<BoidNextPosition>(),
                world.GetComponentTypeId<BoidNextVelocity>());

            for (int tick = 1; tick <= ticks; tick++)
            {
                world.BeginTick(tick);

                step.Tick = tick;
                world.ForEachChunkParallel(query, workerCount, ref step);

                world.ForEachChunk(query, ref apply);

                world.EndTick();
            }

            return BoidsWorldSnapshotHasher.ComputeSnapshot(world);
        }

        private static ulong RunBoidsSimulationWithEcb(ulong seed, int workerCount, int ticks, int boidCount)
        {
            World world = new(
                boidCount * 3,
                128,
                deterministic: true);

            world.RegisterComponent<BoidPosition>();
            world.RegisterComponent<BoidVelocity>();
            world.RegisterComponent<BoidNextPosition>();
            world.RegisterComponent<BoidNextVelocity>();

            // ECB is required for this test (spawn/destroy). In determinism mode, value stores must be warmed up.
            world.WarmupEcbParallel(workerCount, 4096, 1024);
            world.WarmupEcbValuesParallel<BoidPosition>(workerCount, 1024);
            world.WarmupEcbValuesParallel<BoidVelocity>(workerCount, 1024);
            world.WarmupEcbValuesParallel<BoidNextPosition>(workerCount, 1024);
            world.WarmupEcbValuesParallel<BoidNextVelocity>(workerCount, 1024);

            BoidsBootstrap.Spawn(world, seed, boidCount);

            Query query = world.QueryAll<BoidPosition, BoidVelocity>();

            ParallelBoidsEcbStepProcessor step = new(
                seed,
                world.GetComponentTypeId<BoidPosition>(),
                world.GetComponentTypeId<BoidVelocity>(),
                world.GetComponentTypeId<BoidNextPosition>(),
                world.GetComponentTypeId<BoidNextVelocity>(),
                257,
                97);

            ApplyBoidsNextStateProcessor apply = new(
                world.GetComponentTypeId<BoidPosition>(),
                world.GetComponentTypeId<BoidVelocity>(),
                world.GetComponentTypeId<BoidNextPosition>(),
                world.GetComponentTypeId<BoidNextVelocity>());

            for (int tick = 1; tick <= ticks; tick++)
            {
                world.BeginTick(tick);

                step.Tick = tick;
                world.ForEachChunkParallel(query, workerCount, ref step);

                world.ForEachChunk(query, ref apply);

                world.EndTick();
            }

            return BoidsWorldSnapshotHasher.ComputeSnapshot(world);
        }
    }
}