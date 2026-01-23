using System;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Demo.Boids.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Queries;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;
using LegendaryTools.Common.Core.Patterns.ECS.Systems;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;
using UnityEngine;

namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.Boids.Systems
{
    /// <summary>
    /// Heavy boids: naive O(n^2) per chunk. Single-thread ECS.
    /// </summary>
    public sealed class BoidsEcsSingleThreadSystem : SystemBase
    {
        private readonly float _neighborRadius;
        private readonly float _separationRadius;
        private readonly float _maxSpeed;
        private readonly float _bounds;

        private readonly float _alignmentWeight;
        private readonly float _cohesionWeight;
        private readonly float _separationWeight;

        private Query _query;

        private ComponentHandle<BoidPosition> _posH;
        private ComponentHandle<BoidVelocity> _velH;

        public BoidsEcsSingleThreadSystem(
            float neighborRadius,
            float separationRadius,
            float maxSpeed,
            float bounds,
            float alignmentWeight,
            float cohesionWeight,
            float separationWeight)
        {
            _neighborRadius = Mathf.Max(0.001f, neighborRadius);
            _separationRadius = Mathf.Max(0.001f, separationRadius);
            _maxSpeed = Mathf.Max(0.001f, maxSpeed);
            _bounds = Mathf.Max(0.001f, bounds);

            _alignmentWeight = alignmentWeight;
            _cohesionWeight = cohesionWeight;
            _separationWeight = separationWeight;
        }

        public override void OnCreate(World world)
        {
            _posH = world.GetComponentHandle<BoidPosition>();
            _velH = world.GetComponentHandle<BoidVelocity>();

            _query = world.QueryAll<BoidPosition, BoidVelocity>();
            world.WarmupQuery(_query);
        }

        protected override void Update(World world, int tick)
        {
            float dt = world.Time.TickDelta;

            Processor p = new(
                _posH,
                _velH,
                _neighborRadius,
                _separationRadius,
                _maxSpeed,
                _bounds,
                _alignmentWeight,
                _cohesionWeight,
                _separationWeight,
                dt);

            world.ForEachChunk(_query, ref p);
        }

        private struct Processor : IChunkProcessor
        {
            private readonly ComponentHandle<BoidPosition> _posH;
            private readonly ComponentHandle<BoidVelocity> _velH;

            private readonly float _neighborRadius;
            private readonly float _separationRadius;
            private readonly float _maxSpeed;
            private readonly float _bounds;

            private readonly float _alignmentWeight;
            private readonly float _cohesionWeight;
            private readonly float _separationWeight;

            private readonly float _dt;

            public Processor(
                ComponentHandle<BoidPosition> posH,
                ComponentHandle<BoidVelocity> velH,
                float neighborRadius,
                float separationRadius,
                float maxSpeed,
                float bounds,
                float alignmentWeight,
                float cohesionWeight,
                float separationWeight,
                float dt)
            {
                _posH = posH;
                _velH = velH;

                _neighborRadius = neighborRadius;
                _separationRadius = separationRadius;
                _maxSpeed = maxSpeed;
                _bounds = bounds;

                _alignmentWeight = alignmentWeight;
                _cohesionWeight = cohesionWeight;
                _separationWeight = separationWeight;

                _dt = dt;
            }

            public void Execute(Archetype archetype, Chunk chunk)
            {
                if (!archetype.TryGetColumnIndexFast(_posH.TypeId, out int posCol)) return;
                if (!archetype.TryGetColumnIndexFast(_velH.TypeId, out int velCol)) return;

                Span<BoidPosition> pos = chunk.GetSpanRW<BoidPosition>(posCol);
                Span<BoidVelocity> vel = chunk.GetSpanRW<BoidVelocity>(velCol);

                int n = chunk.Count;
                if (n <= 1) return;

                float neighborR2 = _neighborRadius * _neighborRadius;
                float sepR2 = _separationRadius * _separationRadius;

                for (int i = 0; i < n; i++)
                {
                    Vector3 p0 = pos[i].Value;
                    Vector3 v0 = vel[i].Value;

                    Vector3 alignment = Vector3.zero;
                    Vector3 cohesion = Vector3.zero;
                    Vector3 separation = Vector3.zero;
                    int neighbors = 0;

                    for (int j = 0; j < n; j++)
                    {
                        if (i == j) continue;

                        Vector3 dp = pos[j].Value - p0;
                        float d2 = dp.sqrMagnitude;
                        if (d2 > neighborR2) continue;

                        neighbors++;
                        alignment += vel[j].Value;
                        cohesion += pos[j].Value;

                        if (d2 < sepR2)
                        {
                            float inv = 1.0f / (Mathf.Sqrt(d2) + 1e-5f);
                            separation -= dp * inv;
                        }
                    }

                    if (neighbors > 0)
                    {
                        float inv = 1.0f / neighbors;

                        alignment = (alignment * inv).normalized * _maxSpeed - v0;
                        cohesion = (cohesion * inv - p0).normalized * _maxSpeed - v0;
                        separation = separation.normalized * _maxSpeed - v0;

                        Vector3 accel =
                            alignment * _alignmentWeight +
                            cohesion * _cohesionWeight +
                            separation * _separationWeight;

                        v0 += accel * _dt;
                    }

                    float sp = v0.magnitude;
                    if (sp > _maxSpeed) v0 = v0 * (_maxSpeed / (sp + 1e-6f));

                    p0 += v0 * _dt;

                    // Bounds clamp + bounce (deterministic, branchy but stable)
                    if (p0.x < -_bounds)
                    {
                        p0.x = -_bounds;
                        v0.x = -v0.x;
                    }
                    else if (p0.x > _bounds)
                    {
                        p0.x = _bounds;
                        v0.x = -v0.x;
                    }

                    if (p0.y < -_bounds)
                    {
                        p0.y = -_bounds;
                        v0.y = -v0.y;
                    }
                    else if (p0.y > _bounds)
                    {
                        p0.y = _bounds;
                        v0.y = -v0.y;
                    }

                    if (p0.z < -_bounds)
                    {
                        p0.z = -_bounds;
                        v0.z = -v0.z;
                    }
                    else if (p0.z > _bounds)
                    {
                        p0.z = _bounds;
                        v0.z = -v0.z;
                    }

                    pos[i] = new BoidPosition { Value = p0 };
                    vel[i] = new BoidVelocity { Value = v0 };
                }
            }
        }
    }
}