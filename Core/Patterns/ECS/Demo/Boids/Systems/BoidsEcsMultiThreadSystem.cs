using System;
using System.Collections.Generic;
using LegendaryTools.Common.Core.Patterns.ECS.Commands;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Demo.Boids.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Parallel;
using LegendaryTools.Common.Core.Patterns.ECS.Queries;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;
using LegendaryTools.Common.Core.Patterns.ECS.Systems;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;
using UnityEngine;

namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.Boids.Systems
{
    /// <summary>
    /// Stage-1 deterministic "job-like" multi-worker simulation:
    /// - stable chunk work list
    /// - per-worker reductions
    /// - deterministic merge
    ///
    /// NOTE: This stage runs sequentially by worker to validate determinism rules.
    /// Real parallel execution comes later with Job System + NativeContainers.
    /// </summary>
    public sealed class BoidsEcsMultiThreadSystem : SystemBase
    {
        private readonly int _workerCount;

        private readonly float _neighborRadius;
        private readonly float _separationRadius;
        private readonly float _maxSpeed;
        private readonly float _bounds;

        private readonly float _alignmentWeight;
        private readonly float _cohesionWeight;
        private readonly float _separationWeight;

        private readonly float _hotSpeedThreshold;

        private Query _query;

        private ComponentHandle<BoidPosition> _posH;
        private ComponentHandle<BoidVelocity> _velH;

        private DeterministicReduction<WorkerBoidReduction> _reduction;

        public BoidsEcsMultiThreadSystem(
            int workerCount,
            float neighborRadius,
            float separationRadius,
            float maxSpeed,
            float bounds,
            float alignmentWeight,
            float cohesionWeight,
            float separationWeight,
            float hotSpeedThreshold)
        {
            _workerCount = Mathf.Max(1, workerCount);

            _neighborRadius = Mathf.Max(0.001f, neighborRadius);
            _separationRadius = Mathf.Max(0.001f, separationRadius);
            _maxSpeed = Mathf.Max(0.001f, maxSpeed);
            _bounds = Mathf.Max(0.001f, bounds);

            _alignmentWeight = alignmentWeight;
            _cohesionWeight = cohesionWeight;
            _separationWeight = separationWeight;

            _hotSpeedThreshold = Mathf.Max(0.001f, hotSpeedThreshold);

            _reduction = new DeterministicReduction<WorkerBoidReduction>(_workerCount, 8);
        }

        public override void OnCreate(World world)
        {
            _posH = world.GetComponentHandle<BoidPosition>();
            _velH = world.GetComponentHandle<BoidVelocity>();

            _query = world.QueryAll<BoidPosition, BoidVelocity>();
            world.WarmupQuery(_query);

            // Stage-1 shim (single ECB). Kept for API continuity.
            world.WarmupEcbParallel(_workerCount, 512, 1);
            world.WarmupEcbValuesParallel<BoidHot>(_workerCount, 1);

            _reduction.EnsureWorkers(_workerCount, 8);
        }

        protected override void Update(World world, int tick)
        {
            float dt = world.Time.TickDelta;

            List<ChunkWorkItem> work = BuildStableChunkWorkList(world, _query);
            if (work.Count == 0) return;

            _reduction.Clear();

            // Stage-1: sequential "worker loop" (deterministic ordering).
            for (int worker = 0; worker < _workerCount; worker++)
            {
                ICommandBuffer ecb = world.GetEcbWorker(worker); // Stage-1 returns shared ECB.
                ProcessWorker(world, worker, ecb, work, dt);
            }

            List<WorkerBoidReduction> merged = new(_workerCount);
            _reduction.MergeAndSort(merged, WorkerBoidReductionComparer.Instance);

            float sum = 0f;
            int count = 0;
            for (int i = 0; i < merged.Count; i++)
            {
                sum += merged[i].SpeedSum;
                count += merged[i].Count;
            }

            float avg = count > 0 ? sum / count : 0f;

            if ((tick & 255) == 0)
                Debug.Log(
                    $"[Boids JobLike] tick={tick} avgSpeed={avg:0.000} chunks={work.Count} workers={_workerCount}");
        }

        private void ProcessWorker(World world, int worker, ICommandBuffer ecb, List<ChunkWorkItem> work, float dt)
        {
            float speedSum = 0f;
            int boidCount = 0;

            float neighborR2 = _neighborRadius * _neighborRadius;
            float sepR2 = _separationRadius * _separationRadius;

            for (int k = worker; k < work.Count; k += _workerCount)
            {
                ChunkWorkItem item = work[k];

                Archetype archetype = item.Archetype;
                Chunk chunk = item.Chunk;

                if (!archetype.TryGetColumnIndexFast(_posH.TypeId, out int posCol)) continue;
                if (!archetype.TryGetColumnIndexFast(_velH.TypeId, out int velCol)) continue;

                Span<BoidPosition> pos = chunk.GetSpanRW<BoidPosition>(posCol);
                Span<BoidVelocity> vel = chunk.GetSpanRW<BoidVelocity>(velCol);

                int n = chunk.Count;
                if (n <= 1) continue;

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

                        v0 += accel * dt;
                    }

                    float sp = v0.magnitude;
                    if (sp > _maxSpeed) v0 = v0 * (_maxSpeed / (sp + 1e-6f));

                    p0 += v0 * dt;

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

                    speedSum += sp;
                    boidCount++;

                    // Structural demo (deterministic): shared ECB in Stage-1, stable sortKey.
                    if (sp >= _hotSpeedThreshold)
                    {
                        Entity e = chunk.Entities[i];
                        ecb.Add<BoidHot>(e, e.Index);
                    }
                }
            }

            _reduction.Add(worker, new WorkerBoidReduction(worker, speedSum, boidCount));
        }

        private static List<ChunkWorkItem> BuildStableChunkWorkList(World world, Query query)
        {
            List<ChunkWorkItem> list = new(1024);

            using (WorldQueryResult qr = world.BeginQuery(query))
            {
                ReadOnlySpan<Archetype> archetypes = qr.Archetypes;

                for (int a = 0; a < archetypes.Length; a++)
                {
                    Archetype archetype = archetypes[a];

                    Chunk[] chunks = archetype.ChunksBuffer;
                    int chunkCount = archetype.ChunkCount;

                    for (int c = 0; c < chunkCount; c++)
                    {
                        Chunk chunk = chunks[c];
                        if (chunk == null) continue;
                        if (chunk.Count == 0) continue;

                        list.Add(new ChunkWorkItem(archetype, chunk));
                    }
                }
            }

            return list;
        }

        private readonly struct ChunkWorkItem
        {
            public readonly Archetype Archetype;
            public readonly Chunk Chunk;

            public ChunkWorkItem(Archetype archetype, Chunk chunk)
            {
                Archetype = archetype;
                Chunk = chunk;
            }
        }
    }
}