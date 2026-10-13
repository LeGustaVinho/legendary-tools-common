using System.Diagnostics;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;

namespace LegendaryTools.Deterministic
{
    internal static class NativeBatchThread
    {
        [BurstDiscard] private static void Managed(ref int thread) { thread = System.Environment.CurrentManagedThreadId + 1024; }
        internal static int Current()
        { int thread = Unity.Jobs.LowLevel.Unsafe.JobsUtility.ThreadIndex + 1; Managed(ref thread); return thread; }
    }
    /// <summary>Host-prepared disjoint partitions. Length for Unity's range restriction is the partition count.</summary>
    [NativeContainer, NativeContainerSupportsMinMaxWriteRestriction]
    public unsafe struct NativeWriteBatch<T> where T : unmanaged
    {
        internal NativeStorage<T> Storage;
        internal ulong Lease;
        internal int ElementCount, LogicalPartitions;
        internal int m_Length, m_MinIndex, m_MaxIndex;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif
        public int Length => ElementCount;
        public int PartitionCount => LogicalPartitions;
        public int LaneCount => m_Length;
        public DeterministicStatus TryAcquirePartition(int partition, out NativeWriteRange<T> range)
        {
            range = default; CheckWrite();
            int lane = m_Length <= 0 ? -1 : partition % m_Length;
            if ((uint)partition >= (uint)LogicalPartitions || lane < m_MinIndex || lane > m_MaxIndex)
                return DeterministicStatus.OutOfRange;
            var status = Storage.TryClaimBatch(Lease, partition, out int thread); if (!status.IsSuccess()) return status;
            int start = (int)((long)partition * ElementCount / LogicalPartitions), end = (int)((long)(partition + 1) * ElementCount / LogicalPartitions);
            range = new NativeWriteRange<T> { Storage = Storage, Lease = Lease, Partition = partition, Thread = thread, Start = start, Count = end - start };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            range.m_Safety = m_Safety;
#endif
            return DeterministicStatus.None;
        }
        /// <summary>Host barrier operation. Every scheduled job must be completed first.</summary>
        public DeterministicStatus TryComplete()
        { CheckWrite(); return Storage.TryCompleteBatch(Lease); }
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void CheckWrite()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
        }
    }
    [NativeContainer]
    public unsafe struct NativeWriteRange<T> where T : unmanaged
    {
        internal NativeStorage<T> Storage;
        internal ulong Lease;
        internal int Partition, Thread;
        public int Start { get; internal set; }
        public int Count { get; internal set; }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif
        private DeterministicStatus Check(int index)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            if (!Storage.ValidBatchPartition(Lease, Partition, Thread)) return DeterministicStatus.Invalidated;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Thread != NativeBatchThread.Current()) return DeterministicStatus.InvalidOwner;
#endif
            return (uint)(index - Start) < (uint)Count ? DeterministicStatus.None : DeterministicStatus.OutOfRange;
        }
        public DeterministicStatus TryGet(int index, out T value)
        { value = default; var status = Check(index); if (status.IsSuccess()) value = Storage.Read(index); return status; }
        public DeterministicStatus TrySet(int index, in T value)
        { var status = Check(index); if (status.IsSuccess()) Storage.Write(index, value); return status; }
        public DeterministicStatus TryRelease()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            return Storage.TryReleaseBatch(Lease, Partition, Thread);
        }
    }
    /// <summary>Dense component partitions allow value changes and preserve each StableHandle.</summary>
    [NativeContainer, NativeContainerSupportsMinMaxWriteRestriction]
    public unsafe struct NativeSparseWriteBatch<T> where T : unmanaged
    {
        internal NativeStorage<SparseSetEntry<T>> Storage;
        internal ulong Lease;
        internal int ElementCount, LogicalPartitions;
        internal int m_Length, m_MinIndex, m_MaxIndex;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif
        public int Length => ElementCount;
        public int PartitionCount => LogicalPartitions;
        public int LaneCount => m_Length;
        public DeterministicStatus TryAcquirePartition(int partition, out NativeSparseWriteRange<T> range)
        {
            range = default;
            var batch = new NativeWriteBatch<SparseSetEntry<T>> { Storage = Storage, Lease = Lease,
                ElementCount = ElementCount, LogicalPartitions = LogicalPartitions,
                m_Length = m_Length, m_MinIndex = m_MinIndex, m_MaxIndex = m_MaxIndex };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            batch.m_Safety = m_Safety;
#endif
            var status = batch.TryAcquirePartition(partition, out var inner);
            if (status.IsSuccess()) range = new NativeSparseWriteRange<T> { Inner = inner };
            return status;
        }
        public DeterministicStatus TryComplete()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            return Storage.TryCompleteBatch(Lease);
        }
    }
    public struct NativeSparseWriteRange<T> where T : unmanaged
    {
        internal NativeWriteRange<SparseSetEntry<T>> Inner;
        public int Start => Inner.Start;
        public int Count => Inner.Count;
        public DeterministicStatus TryGet(int index, out SparseSetEntry<T> value) => Inner.TryGet(index, out value);
        public DeterministicStatus TrySet(int index, in T value)
        {
            var status = Inner.TryGet(index, out var entry); if (!status.IsSuccess()) return status;
            entry.Value = value; return Inner.TrySet(index, entry);
        }
        public DeterministicStatus TryRelease() => Inner.TryRelease();
    }
    public unsafe partial struct NativeDeterministicArray<T> where T : unmanaged
    {
        public DeterministicStatus TryBeginWriteBatch(int partitions, out NativeWriteBatch<T> batch)
            => TryBeginWriteBatch(partitions, partitions, out batch);
        public DeterministicStatus TryBeginWriteBatch(int partitions, int lanes, out NativeWriteBatch<T> batch)
        {
            CheckWrite(); batch = default;
            if (lanes <= 0 || lanes > partitions) return DeterministicStatus.OutOfRange;
            var status = Storage.TryBeginBatch(Length, partitions, out var lease); if (!status.IsSuccess()) return status;
            batch = new NativeWriteBatch<T> { Storage = Storage, Lease = lease, ElementCount = Length,
                LogicalPartitions = partitions, m_Length = lanes, m_MinIndex = 0, m_MaxIndex = lanes - 1 };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            batch.m_Safety = m_Safety;
#endif
            return DeterministicStatus.None;
        }
    }
    public unsafe partial struct NativeDeterministicSparseSet<T> where T : unmanaged
    {
        public DeterministicStatus TryBeginDenseWriteBatch(int partitions, out NativeSparseWriteBatch<T> batch)
            => TryBeginDenseWriteBatch(partitions, partitions, out batch);
        public DeterministicStatus TryBeginDenseWriteBatch(int partitions, int lanes, out NativeSparseWriteBatch<T> batch)
        {
            CheckWrite(); batch = default;
            if (lanes <= 0 || lanes > partitions) return DeterministicStatus.OutOfRange;
            var status = Dense.TryBeginBatch(Count, partitions, out var lease); if (!status.IsSuccess()) return status;
            batch = new NativeSparseWriteBatch<T> { Storage = Dense, Lease = lease, ElementCount = Count,
                LogicalPartitions = partitions, m_Length = lanes, m_MinIndex = 0, m_MaxIndex = lanes - 1 };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            batch.m_Safety = m_Safety;
#endif
            return DeterministicStatus.None;
        }
    }
}
