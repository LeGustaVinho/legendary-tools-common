using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Common.Core.Patterns.ECS.Observability
{
    /// <summary>
    /// Snapshot stats for a single archetype.
    /// </summary>
    public readonly struct EcsArchetypeStats
    {
        public readonly ArchetypeId ArchetypeId;
        public readonly int[] SignatureTypeIds;

        public readonly int ChunkCount;
        public readonly int EntityCount;

        public readonly int ChunkCapacity;
        public readonly int TotalCapacity;

        public EcsArchetypeStats(
            ArchetypeId archetypeId,
            int[] signatureTypeIds,
            int chunkCount,
            int entityCount,
            int chunkCapacity,
            int totalCapacity)
        {
            ArchetypeId = archetypeId;
            SignatureTypeIds = signatureTypeIds;
            ChunkCount = chunkCount;
            EntityCount = entityCount;
            ChunkCapacity = chunkCapacity;
            TotalCapacity = totalCapacity;
        }

        public float Utilization01 => TotalCapacity <= 0 ? 0f : (float)EntityCount / TotalCapacity;

        public override string ToString()
        {
            return
                $"EcsArchetypeStats(Id={ArchetypeId}, Types={SignatureTypeIds?.Length ?? 0}, Chunks={ChunkCount}, " +
                $"Entities={EntityCount}, Capacity={TotalCapacity}, Util={Utilization01:0.000})";
        }
    }
}