using System;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Queries;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Tests.ECS.Boids
{
    /// <summary>
    /// Copies next buffers into the current state (single-thread pass).
    /// </summary>
    public struct ApplyBoidsNextStateProcessor : IChunkProcessor
    {
        private readonly ComponentTypeId _posTypeId;
        private readonly ComponentTypeId _velTypeId;
        private readonly ComponentTypeId _nextPosTypeId;
        private readonly ComponentTypeId _nextVelTypeId;

        public ApplyBoidsNextStateProcessor(
            ComponentTypeId positionTypeId,
            ComponentTypeId velocityTypeId,
            ComponentTypeId nextPositionTypeId,
            ComponentTypeId nextVelocityTypeId)
        {
            _posTypeId = positionTypeId;
            _velTypeId = velocityTypeId;
            _nextPosTypeId = nextPositionTypeId;
            _nextVelTypeId = nextVelocityTypeId;
        }

        public void Execute(Archetype archetype, Chunk chunk)
        {
            if (!archetype.TryGetColumnIndex(_posTypeId, out int posCol))
                throw new InvalidOperationException("BoidPosition column not found.");

            if (!archetype.TryGetColumnIndex(_velTypeId, out int velCol))
                throw new InvalidOperationException("BoidVelocity column not found.");

            if (!archetype.TryGetColumnIndex(_nextPosTypeId, out int nextPosCol))
                throw new InvalidOperationException("BoidNextPosition column not found.");

            if (!archetype.TryGetColumnIndex(_nextVelTypeId, out int nextVelCol))
                throw new InvalidOperationException("BoidNextVelocity column not found.");

            Span<BoidPosition> pos = chunk.GetSpanRW<BoidPosition>(posCol);
            Span<BoidVelocity> vel = chunk.GetSpanRW<BoidVelocity>(velCol);

            ReadOnlySpan<BoidNextPosition> nextPos = chunk.GetSpanRO<BoidNextPosition>(nextPosCol);
            ReadOnlySpan<BoidNextVelocity> nextVel = chunk.GetSpanRO<BoidNextVelocity>(nextVelCol);

            int count = chunk.Count;
            for (int i = 0; i < count; i++)
            {
                BoidNextPosition np = nextPos[i];
                BoidNextVelocity nv = nextVel[i];

                pos[i] = new BoidPosition(np.X, np.Y);
                vel[i] = new BoidVelocity(nv.X, nv.Y);
            }
        }
    }
}