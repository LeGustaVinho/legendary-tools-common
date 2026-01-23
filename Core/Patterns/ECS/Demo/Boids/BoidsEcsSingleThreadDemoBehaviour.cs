using System;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Demo.Boids.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Queries;
using LegendaryTools.Common.Core.Patterns.ECS.Systems;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;
using UnityEngine;

namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.Boids
{
    public sealed class BoidsEcsSingleThreadDemoBehaviour : MonoBehaviour
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

        [Header("Debug")] [SerializeField] private bool drawGizmos = true;
        [SerializeField] private int gizmosMax = 2000;

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
                new Systems.BoidsEcsSingleThreadSystem(
                    neighborRadius,
                    separationRadius,
                    maxSpeed,
                    bounds,
                    alignmentWeight,
                    cohesionWeight,
                    separationWeight),
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

        private void OnDrawGizmos()
        {
            if (!drawGizmos) return;
            if (_world == null) return;

            Query q = _world.QueryAll<BoidPosition, BoidVelocity>();
            ComponentHandle<BoidPosition> posH = _world.GetComponentHandle<BoidPosition>();

            int drawn = 0;

            // FIX: You cannot pass "ref new ..." in C#. Create an assignable local variable.
            GizmoProcessor processor = new(posH, gizmosMax);
            _world.ForEachChunk(q, ref processor);
        }

        private struct GizmoProcessor : IChunkProcessor
        {
            private readonly ComponentHandle<BoidPosition> _posH;
            private readonly int _max;

            private int _drawn;

            public GizmoProcessor(ComponentHandle<BoidPosition> posH,
                int max)
            {
                _posH = posH;
                _max = max;
                _drawn = 0;
            }

            public void Execute(Storage.Archetype archetype, Storage.Chunk chunk)
            {
                if (_drawn >= _max) return;
                if (!archetype.TryGetColumnIndexFast(_posH.TypeId, out int posCol)) return;

                ReadOnlySpan<BoidPosition> pos = chunk.GetSpanRO<BoidPosition>(posCol);

                int n = chunk.Count;
                for (int i = 0; i < n && _drawn < _max; i++)
                {
                    Gizmos.DrawSphere(pos[i].Value, 0.08f);
                    _drawn++;
                }
            }
        }
    }
}