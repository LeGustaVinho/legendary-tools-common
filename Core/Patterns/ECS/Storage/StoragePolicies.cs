using System;

namespace LegendaryTools.Common.Core.Patterns.ECS.Storage
{
    /// <summary>
    /// Storage policies controlling chunk sizing and structural behaviors.
    /// </summary>
    public readonly struct StoragePolicies
    {
        /// <summary>
        /// Maximum number of entities per chunk.
        /// </summary>
        public readonly int ChunkCapacity;
        /// <summary>
        /// Policy for removing entities (swap-back vs stable).
        /// </summary>
        public readonly StorageRemovalPolicy RemovalPolicy;
        /// <summary>
        /// Policy for allocating new entities (first fit vs track last).
        /// </summary>
        public readonly ChunkAllocationPolicy AllocationPolicy;

        /// <summary>
        /// Initializes a new instance of the <see cref="StoragePolicies"/> struct.
        /// </summary>
        /// <param name="chunkCapacity">Chunk capacity (must be >= 1).</param>
        /// <param name="removalPolicy">Removal policy.</param>
        /// <param name="allocationPolicy">Allocation policy.</param>
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

        /// <summary>
        /// Gets the default storage policies (Capacity=128, SwapBack, ScanFirstFit).
        /// </summary>
        public static StoragePolicies Default =>
            new(128, StorageRemovalPolicy.SwapBack, ChunkAllocationPolicy.ScanFirstFit);
    }
}