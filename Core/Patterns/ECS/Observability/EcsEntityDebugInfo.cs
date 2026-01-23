using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Common.Core.Patterns.ECS.Observability
{
    /// <summary>
    /// Debug-friendly view of an entity state.
    /// </summary>
    public readonly struct EcsEntityDebugInfo
    {
        public readonly Entity Entity;
        public readonly bool IsAlive;
        public readonly int CurrentVersion;

        public readonly EntityLocation Location;
        public readonly bool HasValidLocation;

        public readonly ArchetypeId ArchetypeId;
        public readonly int ChunkId;
        public readonly int Row;

        public EcsEntityDebugInfo(
            Entity entity,
            bool isAlive,
            int currentVersion,
            EntityLocation location)
        {
            Entity = entity;
            IsAlive = isAlive;
            CurrentVersion = currentVersion;

            Location = location;
            HasValidLocation = location.IsValid;

            ArchetypeId = location.ArchetypeId;
            ChunkId = location.ChunkId;
            Row = location.Row;
        }

        public override string ToString()
        {
            return
                $"EcsEntityDebugInfo(Entity={Entity}, Alive={IsAlive}, CurrentVersion={CurrentVersion}, " +
                $"LocValid={HasValidLocation}, Archetype={ArchetypeId}, Chunk={ChunkId}, Row={Row})";
        }
    }
}