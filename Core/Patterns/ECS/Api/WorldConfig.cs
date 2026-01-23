using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Common.Core.Patterns.ECS.Api
{
    /// <summary>
    /// Game-ready World configuration container.
    /// Keep it simple: one place to define policies and deterministic mode.
    /// </summary>
    public readonly struct WorldConfig
    {
        public readonly int InitialCapacity;
        public readonly int ChunkCapacity;
        public readonly StorageRemovalPolicy RemovalPolicy;
        public readonly ChunkAllocationPolicy AllocationPolicy;
        public readonly int SimulationHz;
        public readonly bool Deterministic;

        public WorldConfig(
            int initialCapacity,
            int chunkCapacity,
            StorageRemovalPolicy removalPolicy,
            ChunkAllocationPolicy allocationPolicy,
            int simulationHz,
            bool deterministic)
        {
            InitialCapacity = initialCapacity < 1 ? 1 : initialCapacity;
            ChunkCapacity = chunkCapacity < 1 ? 1 : chunkCapacity;
            RemovalPolicy = removalPolicy;
            AllocationPolicy = allocationPolicy;
            SimulationHz = simulationHz < 1 ? 1 : simulationHz;
            Deterministic = deterministic;
        }

        public static WorldConfig Default =>
            new(
                1024,
                128,
                StorageRemovalPolicy.SwapBack,
                ChunkAllocationPolicy.ScanFirstFit,
                60,
                false);
    }
}