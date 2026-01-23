using LegendaryTools.Common.Core.Patterns.ECS.Demo.Boids.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Demo.Boids.Systems;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Systems;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;
using UnityEngine;

namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.Boids
{
    public sealed class BoidsEcsMultiThreadDemoBehaviour : MonoBehaviour
    {
        [Header("World")] [SerializeField] private int initialCapacity = 50000;
        [SerializeField] private int chunkCapacity = 128;
        [SerializeField] private bool deterministic = true;
        [SerializeField] private int simulationHz = 60;

        [Header("Boids")] [SerializeField] private int boidCount = 20000;
        [SerializeField] private float bounds = 50f;
        [SerializeField] private float maxSpeed = 8f;

        [Header("Rules")] [SerializeField] private float neighborRadius = 3.5f;
        [SerializeField] private float separationRadius = 1.2f;
        [SerializeField] private float alignmentWeight = 1.0f;
        [SerializeField] private float cohesionWeight = 0.7f;
        [SerializeField] private float separationWeight = 1.3f;

        [Header("MT")] [SerializeField] private int workerCount = 4;
        [SerializeField] private float hotSpeedThreshold = 7.0f;

        private World _world;
        private Scheduler _scheduler;

        private void Awake()
        {
            _world = new World(
                initialCapacity,
                chunkCapacity,
                simulationHz: simulationHz,
                deterministic: deterministic);

            _world.RegisterComponent<BoidPosition>();
            _world.RegisterComponent<BoidVelocity>();
            _world.RegisterComponent<BoidHot>();

            SpawnBoids();

            _scheduler = _world.CreateScheduler();

            _scheduler.AddSystem(
                SystemPhase.Simulation,
                new BoidsEcsMultiThreadSystem(
                    workerCount,
                    neighborRadius,
                    separationRadius,
                    maxSpeed,
                    bounds,
                    alignmentWeight,
                    cohesionWeight,
                    separationWeight,
                    hotSpeedThreshold),
                100);

            _scheduler.Create();
        }

        private void Update()
        {
            _scheduler.Advance(Time.deltaTime);
        }

        private void OnDestroy()
        {
            _scheduler?.Destroy();
        }

        private void SpawnBoids()
        {
            UnityEngine.Random.InitState(12345);

            for (int i = 0; i < boidCount; i++)
            {
                Entity e = _world.CreateEntity();

                Vector3 p = new(
                    UnityEngine.Random.Range(-bounds, bounds),
                    UnityEngine.Random.Range(-bounds, bounds),
                    UnityEngine.Random.Range(-bounds, bounds));

                Vector3 v = UnityEngine.Random.insideUnitSphere * (maxSpeed * 0.5f);

                _world.Add(e, new BoidPosition { Value = p });
                _world.Add(e, new BoidVelocity { Value = v });
            }
        }
    }
}