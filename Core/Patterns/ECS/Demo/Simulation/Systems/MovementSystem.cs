using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Demo.Simulation.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Queries;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;
using LegendaryTools.Common.Core.Patterns.ECS.Systems;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;

namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.Simulation.Systems
{
    /// <summary>
    /// Updates Position by Velocity each tick.
    /// </summary>
    public sealed class MovementSystem : ISystem
    {
        private Query _query;

        public void OnCreate(World world)
        {
            world.RegisterComponent<Position>();
            world.RegisterComponent<Velocity>();

            // Avoid stackalloc/unsafe in demo. Query allocates once on create; hot path remains allocation-free.
            ComponentTypeId[] all = new ComponentTypeId[2];
            all[0] = world.GetComponentTypeId<Position>();
            all[1] = world.GetComponentTypeId<Velocity>();

            _query = new Query(all, default);
        }

        public void OnUpdate(World world, int tick)
        {
            var processor = new MovementChunkProcessor(
                world.GetComponentTypeId<Position>(),
                world.GetComponentTypeId<Velocity>());

            world.ForEachChunk(_query, ref processor);
        }

        public void OnDestroy(World world)
        {
        }

        private struct MovementChunkProcessor : IChunkProcessor
        {
            private readonly ComponentTypeId _posId;
            private readonly ComponentTypeId _velId;

            public MovementChunkProcessor(ComponentTypeId posId, ComponentTypeId velId)
            {
                _posId = posId;
                _velId = velId;
            }

            public void Execute(Archetype archetype, Chunk chunk)
            {
                if (!archetype.TryGetColumnIndex(_posId, out int posIndex) ||
                    !archetype.TryGetColumnIndex(_velId, out int velIndex))
                {
                    return;
                }

                ChunkColumn<Position> posCol = (ChunkColumn<Position>)chunk.Columns[posIndex];
                ChunkColumn<Velocity> velCol = (ChunkColumn<Velocity>)chunk.Columns[velIndex];

                Position[] pos = posCol.Data;
                Velocity[] vel = velCol.Data;

                int count = chunk.Count;
                for (int i = 0; i < count; i++)
                {
                    pos[i].X += vel[i].X;
                    pos[i].Y += vel[i].Y;
                    pos[i].Z += vel[i].Z;
                }
            }
        }
    }
}
