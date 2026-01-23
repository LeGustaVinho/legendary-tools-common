using System;
using LegendaryTools.Common.Core.Patterns.ECS.Commands;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Queries;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Tests.ECS
{
    public struct ParallelMoveSpawnProcessor : IParallelChunkProcessor
    {
        private readonly ComponentTypeId _posTypeId;
        private readonly ComponentTypeId _velTypeId;

        private readonly int _spawnModulo;
        private readonly int _destroyModulo;

        public int Tick;

        public ParallelMoveSpawnProcessor(ComponentTypeId posTypeId, ComponentTypeId velTypeId, int spawnModulo,
            int destroyModulo)
        {
            _posTypeId = posTypeId;
            _velTypeId = velTypeId;
            _spawnModulo = Math.Max(2, spawnModulo);
            _destroyModulo = Math.Max(2, destroyModulo);
            Tick = 0;
        }

        public void Execute(Archetype archetype, Chunk chunk, int workerIndex, ICommandBuffer ecb)
        {
            if (!archetype.TryGetColumnIndex(_posTypeId, out int posCol))
                throw new InvalidOperationException("Position column not found for archetype.");

            if (!archetype.TryGetColumnIndex(_velTypeId, out int velCol))
                throw new InvalidOperationException("Velocity column not found for archetype.");

            Span<TestPosition> pos = chunk.GetSpanRW<TestPosition>(posCol);
            ReadOnlySpan<TestVelocity> vel = chunk.GetSpanRO<TestVelocity>(velCol);

            int count = chunk.Count;
            for (int i = 0; i < count; i++)
            {
                // Deterministic movement.
                TestPosition p = pos[i];
                TestVelocity v = vel[i];
                p.X += v.X;
                p.Y += v.Y;
                pos[i] = p;

                Entity e = chunk.Entities[i];

                // Deterministic spawn via ECB (temp entity) with stable, non-zero sortKey.
                // Uses only stable inputs: tick and owner entity index.
                if (e.Index >= 0 && e.Index % _spawnModulo == 0)
                {
                    int sortKey = MakeNonZeroSortKey(Tick, e.Index);

                    Entity temp = ecb.CreateEntity(sortKey);
                    ecb.Add(temp, new TestPosition(p.X, p.Y), sortKey);
                    ecb.Add(temp, new TestVelocity(v.X, v.Y), sortKey);
                }

                // Deterministic destroy via ECB.
                if (e.Index >= 0 && e.Index % _destroyModulo == 0)
                {
                    int sortKey = e.Index != 0 ? e.Index : 1;
                    ecb.DestroyEntity(e, sortKey);
                }
            }
        }

        private static int MakeNonZeroSortKey(int tick, int entityIndex)
        {
            unchecked
            {
                // Mix tick + entityIndex into a stable, non-zero int.
                int sk = (tick * 73856093) ^ (entityIndex * 19349663);
                if (sk == 0) sk = 1;
                return sk;
            }
        }
    }
}