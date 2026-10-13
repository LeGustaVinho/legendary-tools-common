using System;

namespace LegendaryTools.Deterministic
{
    /// <summary>Host-side payload admission. Excludes allocator/GC headers, page directories and caller-owned query buffers.</summary>
    public static unsafe class SpatialMemory
    {
        internal static long RoundedPayload(int capacity, int elementBytes, int pageBytes)
        {
            int elements = Math.Max(1, Math.Min(capacity, pageBytes / elementBytes));
            return ((long)capacity + elements - 1) / elements * elements * elementBytes;
        }
        private static bool Accumulate(int capacity, int elementBytes, int pageBytes, int snapshots, ref long active, ref long history)
        {
            int elements = Math.Max(1, Math.Min(capacity, pageBytes / elementBytes));
            long pages = ((long)capacity + elements - 1) / elements;
            if (pages * (snapshots + 1L) > int.MaxValue) return false;
            long bytes = pages * elements * elementBytes;
            active = checked(active + bytes); history = checked(history + bytes * snapshots); return true;
        }
        /// <summary>Worst-case payload at MaxCapacity with every retained root owning private pages, including the transaction spare.</summary>
        public static DeterministicStatus TryMeasure<TKey, TData, TBounds>(SpatialIndexOptions options, out SpatialMemoryStats stats)
            where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
        {
            stats = default; if (!options.IsValid) return DeterministicStatus.InvalidConfiguration;
            if (options.Records.SnapshotSlots >= int.MaxValue - 1) return DeterministicStatus.CapacityExceeded;
            int n = options.Records.MaxCapacity, pages = options.Records.PageBytes;
            int recordSlots = options.Records.SnapshotSlots + 1;
            int cacheSlots = options.Rollback == SpatialRollbackPolicy.FastRestore ? recordSlots : 1;
            int grid = options.Strategy == SpatialIndexStrategy.SparseGrid ? n * options.MaxCellsPerObject : 0;
            var status = SpatialPairScratch.TryMeasure<TKey>(n, n * 2, out _); if (!status.IsSuccess()) return status;
            long records = 0, acceleration = 0, history = 0;
            try
            {
                if (!Accumulate(n, sizeof(OrderedHashEntry<TKey, SpatialRecord<TKey, TData, TBounds>>), pages, recordSlots, ref records, ref history) ||
                    !Accumulate(OrderedHashRules.BucketCapacity(n), sizeof(int), pages, recordSlots, ref records, ref history) ||
                    !Accumulate(n, sizeof(SpatialPlacement), pages, cacheSlots, ref acceleration, ref history) ||
                    !Accumulate(n * 2, sizeof(SpatialBvhNode<TKey>), pages, cacheSlots, ref acceleration, ref history) ||
                    !Accumulate(grid, sizeof(OrderedHashEntry<SpatialCellKey, int>), pages, cacheSlots, ref acceleration, ref history) ||
                    !Accumulate(OrderedHashRules.BucketCapacity(grid), sizeof(int), pages, cacheSlots, ref acceleration, ref history) ||
                    !Accumulate(grid, sizeof(SpatialGridLink), pages, cacheSlots, ref acceleration, ref history)) return DeterministicStatus.CapacityExceeded;
                long workspace = checked((long)n * sizeof(int) + (long)n * 2 * sizeof(SpatialBuildTask));
                long total = checked(records + acceleration + workspace + history);
                stats = new SpatialMemoryStats(records, acceleration, workspace, history, 0); return DeterministicStatus.None;
            }
            catch (OverflowException) { return DeterministicStatus.CapacityExceeded; }
        }
    }
}
