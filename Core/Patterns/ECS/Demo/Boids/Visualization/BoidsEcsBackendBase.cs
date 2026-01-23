using LegendaryTools.Common.Core.Patterns.ECS.Demo.Boids.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Systems;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;
using UnityEngine;

namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.Boids.Visualization
{
    /// <summary>
    /// Base for ECS backends. Owns a World + Scheduler and spawns boids.
    /// </summary>
    public abstract class BoidsEcsBackendBase : IBoidsBackend
    {
        private readonly BoidsSimConfig _cfg;

        protected BoidsSimConfig Cfg => _cfg;

        protected World World;
        protected Scheduler Scheduler;

        private Entity[] _entities;

        protected BoidsEcsBackendBase(BoidsSimConfig cfg, uint seed = 12345)
        {
            _cfg = Sanitize(cfg);

            World = new World(
                _cfg.InitialCapacity,
                _cfg.ChunkCapacity,
                simulationHz: _cfg.SimulationHz,
                deterministic: _cfg.Deterministic);

            World.RegisterComponent<BoidPosition>();
            World.RegisterComponent<BoidVelocity>();
            World.RegisterComponent<BoidHot>();

            SpawnBoids(seed);

            Scheduler = World.CreateScheduler();

            InstallSystems(Scheduler);

            Scheduler.Create();
        }

        public int BoidCount => _cfg.BoidCount;

        public void Step(float deltaTime)
        {
            if (Scheduler == null) return;
            Scheduler.Advance(deltaTime);
        }

        public void CopyPositions(Vector3[] destination)
        {
            int n = _cfg.BoidCount;
            if (destination == null || destination.Length < n)
                throw new System.ArgumentException("Destination array is null or too small.", nameof(destination));

            for (int i = 0; i < n; i++)
            {
                destination[i] = World.GetRO<BoidPosition>(_entities[i]).Value;
            }
        }

        public void Dispose()
        {
            try
            {
                Scheduler?.Destroy();
            }
            catch
            {
                // Ignore teardown errors in demo context.
            }

            Scheduler = null;
            World = null;
            _entities = null;
        }

        protected abstract void InstallSystems(Scheduler scheduler);

        private void SpawnBoids(uint seed)
        {
            _entities = new Entity[_cfg.BoidCount];

            UnityEngine.Random.InitState(unchecked((int)seed));

            for (int i = 0; i < _cfg.BoidCount; i++)
            {
                Entity e = World.CreateEntity();
                _entities[i] = e;

                Vector3 p = new(
                    UnityEngine.Random.Range(-_cfg.Bounds, _cfg.Bounds),
                    UnityEngine.Random.Range(-_cfg.Bounds, _cfg.Bounds),
                    UnityEngine.Random.Range(-_cfg.Bounds, _cfg.Bounds));

                Vector3 v = UnityEngine.Random.insideUnitSphere * (_cfg.MaxSpeed * 0.5f);

                World.Add(e, new BoidPosition { Value = p });
                World.Add(e, new BoidVelocity { Value = v });
            }
        }

        private static BoidsSimConfig Sanitize(BoidsSimConfig cfg)
        {
            if (cfg.BoidCount < 1) cfg.BoidCount = 1;

            if (cfg.MaxSpeed <= 0f) cfg.MaxSpeed = 1f;
            if (cfg.Bounds <= 0f) cfg.Bounds = 1f;

            if (cfg.NeighborRadius <= 0f) cfg.NeighborRadius = 1f;
            if (cfg.SeparationRadius <= 0f) cfg.SeparationRadius = 0.5f;

            if (cfg.SimulationHz < 1) cfg.SimulationHz = 60;
            if (cfg.ChunkCapacity < 1) cfg.ChunkCapacity = 128;
            if (cfg.InitialCapacity < cfg.BoidCount) cfg.InitialCapacity = cfg.BoidCount;

            if (cfg.WorkerCount < 1) cfg.WorkerCount = 1;
            if (cfg.HotSpeedThreshold <= 0f) cfg.HotSpeedThreshold = cfg.MaxSpeed;

            return cfg;
        }
    }
}