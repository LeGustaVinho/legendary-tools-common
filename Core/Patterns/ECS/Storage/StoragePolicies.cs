using System;

namespace LegendaryTools.Common.Core.Patterns.ECS.Storage
{
    /// <summary>
    /// Storage policies controlling chunk sizing and structural behaviors.
    /// </summary>
    public readonly struct StoragePolicies
    {
        public readonly int ChunkCapacity;
        public readonly StorageRemovalPolicy RemovalPolicy;
        public readonly ChunkAllocationPolicy AllocationPolicy;

        public StoragePolicies(
            int chunkCapacity,
            StorageRemovalPolicy removalPolicy = StorageRemovalPolicy.SwapBack,
            ChunkAllocationPolicy allocationPolicy = ChunkAllocationPolicy.ScanFirstFit)
        {
            if (chunkCapacity < 1)
                throw new ArgumentOutOfRangeException(nameof(chunkCapacity), "ChunkCapacity must be >= 1.");

            ChunkCapacity = chunkCapacity;
            RemovalPolicy = removalPolicy;
            AllocationPolicy = allocationPolicy;
        }

        public static StoragePolicies Default =>
            new(128, StorageRemovalPolicy.SwapBack, ChunkAllocationPolicy.ScanFirstFit);
    }
}