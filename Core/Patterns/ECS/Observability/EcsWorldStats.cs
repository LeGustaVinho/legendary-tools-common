using LegendaryTools.Common.Core.Patterns.ECS.Components;

namespace LegendaryTools.Common.Core.Patterns.ECS.Observability
{
    /// <summary>
    /// Lightweight snapshot of world-level runtime stats.
    /// Intended for debug/UI/telemetry.
    /// </summary>
    public readonly struct EcsWorldStats
    {
        public readonly int Tick;
        public readonly int SimulationHz;

        public readonly int NextIndex;
        public readonly int AliveCount;
        public readonly int FreeCount;

        public readonly int StructuralVersion;
        public readonly int ArchetypeVersion;

        public readonly int ArchetypeCount;
        public readonly int ChunkCount;

        public readonly ComponentManifest ComponentManifest;

        public EcsWorldStats(
            int tick,
            int simulationHz,
            int nextIndex,
            int aliveCount,
            int freeCount,
            int structuralVersion,
            int archetypeVersion,
            int archetypeCount,
            int chunkCount,
            ComponentManifest componentManifest)
        {
            Tick = tick;
            SimulationHz = simulationHz;
            NextIndex = nextIndex;
            AliveCount = aliveCount;
            FreeCount = freeCount;
            StructuralVersion = structuralVersion;
            ArchetypeVersion = archetypeVersion;
            ArchetypeCount = archetypeCount;
            ChunkCount = chunkCount;
            ComponentManifest = componentManifest;
        }

        public override string ToString()
        {
            return
                $"EcsWorldStats(Tick={Tick}, Hz={SimulationHz}, Alive={AliveCount}, NextIndex={NextIndex}, Free={FreeCount}, " +
                $"Archetypes={ArchetypeCount}, Chunks={ChunkCount}, StructuralVer={StructuralVersion}, ArchetypeVer={ArchetypeVersion}, " +
                $"{ComponentManifest})";
        }
    }
}