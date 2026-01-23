using System;
using LegendaryTools.Common.Core.Patterns.ECS.Commands;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Queries;
using LegendaryTools.Common.Core.Patterns.ECS.Random;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Tests.ECS.Boids
{
    /// <summary>
    /// Boids step that also uses ECB to deterministically spawn and destroy entities in a parallel pass.
    /// </summary>
    public struct ParallelBoidsEcbStepProcessor : IParallelChunkProcessor
    {
        private const int Scale = 1000;

        // Bounds (fixed-point)
        private const int MinX = -25 * Scale;
        private const int MaxX = 25 * Scale;
        private const int MinY = -25 * Scale;
        private const int MaxY = 25 * Scale;

        // Rule weights
        private const int CohesionWeight = 12;
        private const int AlignmentWeight = 10;
        private const int SeparationWeight = 22;
        private const int JitterWeight = 6;

        private const int NeighborRadius = 2;
        private const int SeparationDist = 2 * Scale;
        private const int MaxSpeed = 4 * Scale;

        private readonly ulong _seed;

        private readonly ComponentTypeId _posTypeId;
        private readonly ComponentTypeId _velTypeId;
        private readonly ComponentTypeId _nextPosTypeId;
        private readonly ComponentTypeId _nextVelTypeId;

        private readonly int _spawnModulo;
        private readonly int _destroyModulo;

        public int Tick;

        public ParallelBoidsEcbStepProcessor(
            ulong seed,
            ComponentTypeId positionTypeId,
            ComponentTypeId velocityTypeId,
            ComponentTypeId nextPositionTypeId,
            ComponentTypeId nextVelocityTypeId,
            int spawnModulo,
            int destroyModulo)
        {
            _seed = seed;

            _posTypeId = positionTypeId;
            _velTypeId = velocityTypeId;
            _nextPosTypeId = nextPositionTypeId;
            _nextVelTypeId = nextVelocityTypeId;

            _spawnModulo = Math.Max(2, spawnModulo);
            _destroyModulo = Math.Max(2, destroyModulo);

            Tick = 0;
        }

        public void Execute(Archetype archetype, Chunk chunk, int workerIndex, ICommandBuffer ecb)
        {
            if (!archetype.TryGetColumnIndex(_posTypeId, out int posCol))
                throw new InvalidOperationException("BoidPosition column not found.");

            if (!archetype.TryGetColumnIndex(_velTypeId, out int velCol))
                throw new InvalidOperationException("BoidVelocity column not found.");

            if (!archetype.TryGetColumnIndex(_nextPosTypeId, out int nextPosCol))
                throw new InvalidOperationException("BoidNextPosition column not found.");

            if (!archetype.TryGetColumnIndex(_nextVelTypeId, out int nextVelCol))
                throw new InvalidOperationException("BoidNextVelocity column not found.");

            ReadOnlySpan<BoidPosition> pos = chunk.GetSpanRO<BoidPosition>(posCol);
            ReadOnlySpan<BoidVelocity> vel = chunk.GetSpanRO<BoidVelocity>(velCol);

            Span<BoidNextPosition> nextPos = chunk.GetSpanRW<BoidNextPosition>(nextPosCol);
            Span<BoidNextVelocity> nextVel = chunk.GetSpanRW<BoidNextVelocity>(nextVelCol);

            int count = chunk.Count;

            for (int i = 0; i < count; i++)
            {
                Entity e = chunk.Entities[i];

                BoidPosition p = pos[i];
                BoidVelocity v = vel[i];

                // Neighbor averages (within chunk only).
                int sumPx = 0, sumPy = 0;
                int sumVx = 0, sumVy = 0;
                int neighbors = 0;

                int sepX = 0, sepY = 0;

                int start = i - NeighborRadius;
                if (start < 0) start = 0;

                int end = i + NeighborRadius;
                if (end >= count) end = count - 1;

                for (int j = start; j <= end; j++)
                {
                    if (j == i) continue;

                    BoidPosition pj = pos[j];
                    BoidVelocity vj = vel[j];

                    sumPx += pj.X;
                    sumPy += pj.Y;
                    sumVx += vj.X;
                    sumVy += vj.Y;
                    neighbors++;

                    int dx = p.X - pj.X;
                    int dy = p.Y - pj.Y;

                    int adx = dx >= 0 ? dx : -dx;
                    int ady = dy >= 0 ? dy : -dy;

                    if (adx < SeparationDist && ady < SeparationDist)
                    {
                        sepX += dx;
                        sepY += dy;
                    }
                }

                int cohX = 0, cohY = 0;
                int aliX = 0, aliY = 0;

                if (neighbors > 0)
                {
                    int avgPx = sumPx / neighbors;
                    int avgPy = sumPy / neighbors;

                    int avgVx = sumVx / neighbors;
                    int avgVy = sumVy / neighbors;

                    cohX = avgPx - p.X;
                    cohY = avgPy - p.Y;

                    aliX = avgVx - v.X;
                    aliY = avgVy - v.Y;
                }

                // Deterministic per-entity jitter stream.
                ulong streamId = MakeStreamId(e.Index, Tick);
                DeterministicRng rng = new(_seed, streamId);

                int jitX = rng.NextInt(-Scale, Scale);
                int jitY = rng.NextInt(-Scale, Scale);

                int ax =
                    cohX / 64 * CohesionWeight +
                    aliX / 64 * AlignmentWeight +
                    sepX / 64 * SeparationWeight +
                    jitX / 64 * JitterWeight;

                int ay =
                    cohY / 64 * CohesionWeight +
                    aliY / 64 * AlignmentWeight +
                    sepY / 64 * SeparationWeight +
                    jitY / 64 * JitterWeight;

                int nvx = v.X + ax;
                int nvy = v.Y + ay;

                ClampSpeed(ref nvx, ref nvy, MaxSpeed);

                int npx = p.X + nvx;
                int npy = p.Y + nvy;

                Wrap(ref npx, MinX, MaxX);
                Wrap(ref npy, MinY, MaxY);

                nextVel[i] = new BoidNextVelocity(nvx, nvy);
                nextPos[i] = new BoidNextPosition(npx, npy);

                // ECB structural changes (deterministic):
                // - Use stable, non-zero sortKey so command ordering does not depend on emission order across threads.
                if (e.Index >= 0 && (e.Index + Tick) % _spawnModulo == 0)
                {
                    int sortKey = MakeNonZeroSortKey(Tick, e.Index);

                    // Create child near the parent using deterministic RNG stream.
                    int offX = rng.NextInt(-2 * Scale, 2 * Scale);
                    int offY = rng.NextInt(-2 * Scale, 2 * Scale);

                    int cvx = nvx + rng.NextInt(-Scale, Scale);
                    int cvy = nvy + rng.NextInt(-Scale, Scale);
                    ClampSpeed(ref cvx, ref cvy, MaxSpeed);

                    int cpx = npx + offX;
                    int cpy = npy + offY;
                    Wrap(ref cpx, MinX, MaxX);
                    Wrap(ref cpy, MinY, MaxY);

                    Entity temp = ecb.CreateEntity(sortKey);

                    // Temp entities require a stable, non-zero sortKey in determinism mode.
                    ecb.Add(temp, new BoidPosition(cpx, cpy), sortKey);
                    ecb.Add(temp, new BoidVelocity(cvx, cvy), sortKey);
                    ecb.Add(temp, new BoidNextPosition(cpx, cpy), sortKey);
                    ecb.Add(temp, new BoidNextVelocity(cvx, cvy), sortKey);
                }

                if (e.Index >= 0 && (e.Index + Tick) % _destroyModulo == 0)
                {
                    int sortKey = e.Index != 0 ? e.Index : 1;
                    ecb.DestroyEntity(e, sortKey);
                }
            }
        }

        private static ulong MakeStreamId(int entityIndex, int tick)
        {
            unchecked
            {
                // Stable stream id, works for entityIndex >= 0.
                return (ulong)(entityIndex + 1) ^ ((ulong)(uint)tick << 32) ^ 0x9E3779B97F4A7C15UL;
            }
        }

        private static int MakeNonZeroSortKey(int tick, int entityIndex)
        {
            unchecked
            {
                int sk = (tick * 73856093) ^ (entityIndex * 19349663) ^ 0x5F356495;
                if (sk == 0) sk = 1;
                return sk;
            }
        }

        private static void Wrap(ref int v, int min, int max)
        {
            int range = max - min;
            if (range <= 0) return;

            if (v < min)
            {
                int d = min - v;
                int m = d % range;
                v = max - m;
                return;
            }

            if (v > max)
            {
                int d = v - max;
                int m = d % range;
                v = min + m;
            }
        }

        private static void ClampSpeed(ref int vx, ref int vy, int maxSpeed)
        {
            int avx = vx >= 0 ? vx : -vx;
            int avy = vy >= 0 ? vy : -vy;

            if (avx <= maxSpeed && avy <= maxSpeed) return;

            if (vx > maxSpeed) vx = maxSpeed;
            else if (vx < -maxSpeed) vx = -maxSpeed;

            if (vy > maxSpeed) vy = maxSpeed;
            else if (vy < -maxSpeed) vy = -maxSpeed;
        }
    }
}