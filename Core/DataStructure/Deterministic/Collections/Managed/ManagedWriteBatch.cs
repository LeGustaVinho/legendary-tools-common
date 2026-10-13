namespace LegendaryTools.Deterministic
{
    /// <summary>Prepare capacity/COW once on the host; publish once after every worker has stopped.</summary>
    public readonly struct ManagedWriteBatch<T> where T : unmanaged
    {
        private readonly ManagedBufferOwner<T> owner;
        private readonly ulong lease;
        public readonly int Length, PartitionCount;
        internal ManagedWriteBatch(ManagedBufferOwner<T> owner, ulong lease, int length, int partitions)
        { this.owner = owner; this.lease = lease; Length = length; PartitionCount = partitions; }
        public DeterministicStatus TryAcquirePartition(int partition, out ManagedWriteRange<T> range, bool checkThreadOwner = true)
        {
            range = default;
            if (owner == null) return DeterministicStatus.Invalidated;
            var status = owner.TryClaimBatch(lease, partition, PartitionCount, out int thread);
            if (!status.IsSuccess()) return status;
            int start = (int)((long)partition * Length / PartitionCount), end = (int)((long)(partition + 1) * Length / PartitionCount);
            range = new ManagedWriteRange<T>(owner, lease, start, end - start, partition, thread, checkThreadOwner);
            return DeterministicStatus.None;
        }
        public DeterministicStatus TryComplete() => owner == null ? DeterministicStatus.Invalidated : owner.TryCompleteBatch(lease);
    }
    /// <summary>A unique partition claim. Writes touch only prepared elements, never shared metadata.</summary>
    public readonly struct ManagedWriteRange<T> where T : unmanaged
    {
        private readonly ManagedBufferOwner<T> owner;
        private readonly ulong lease;
        private readonly int partition, thread;
        private readonly bool checkThread;
        public readonly int Start, Count;
        internal ManagedWriteRange(ManagedBufferOwner<T> owner, ulong lease, int start, int count, int partition, int thread, bool checkThread)
        { this.owner = owner; this.lease = lease; Start = start; Count = count; this.partition = partition; this.thread = thread; this.checkThread = checkThread; }
        private DeterministicStatus Check(int index)
        {
            if (owner == null || !owner.ValidBatchPartition(lease, partition, thread)) return DeterministicStatus.Invalidated;
            if ((uint)(index - Start) >= (uint)Count) return DeterministicStatus.OutOfRange;
            if (checkThread && thread != System.Environment.CurrentManagedThreadId) return DeterministicStatus.InvalidOwner;
            return DeterministicStatus.None;
        }
        public DeterministicStatus TryGet(int index, out T value)
        { value = default; var status = Check(index); if (status.IsSuccess()) value = owner.Read(index); return status; }
        public DeterministicStatus TrySet(int index, in T value)
        { var status = Check(index); if (status.IsSuccess()) owner.Write(index, value); return status; }
        public DeterministicStatus TryRelease() => owner == null ? DeterministicStatus.Invalidated : owner.TryReleaseBatchPartition(lease, partition, thread);
    }
    public sealed partial class DeterministicArray<T> where T : unmanaged
    {
        public DeterministicStatus TryBeginWriteBatch(int partitions, out ManagedWriteBatch<T> batch)
            => Storage.Owner.TryBeginBatch(Length, partitions, out batch);
    }
    public sealed partial class DeterministicSparseSet<T> where T : unmanaged
    {
        public DeterministicStatus TryBeginDenseWriteBatch(int partitions, out ManagedSparseWriteBatch<T> batch)
        {
            batch = default;
            var status = Dense.Owner.TryBeginBatch(Count, partitions, out var inner);
            if (status.IsSuccess()) batch = new ManagedSparseWriteBatch<T>(inner);
            return status;
        }
    }
    public readonly struct ManagedSparseWriteBatch<T> where T : unmanaged
    {
        private readonly ManagedWriteBatch<SparseSetEntry<T>> inner;
        internal ManagedSparseWriteBatch(ManagedWriteBatch<SparseSetEntry<T>> inner) { this.inner = inner; }
        public int Length => inner.Length;
        public int PartitionCount => inner.PartitionCount;
        public DeterministicStatus TryAcquirePartition(int partition, out ManagedSparseWriteRange<T> range, bool checkThreadOwner = true)
        {
            range = default; var status = inner.TryAcquirePartition(partition, out var part, checkThreadOwner);
            if (status.IsSuccess()) range = new ManagedSparseWriteRange<T>(part);
            return status;
        }
        public DeterministicStatus TryComplete() => inner.TryComplete();
    }
    public readonly struct ManagedSparseWriteRange<T> where T : unmanaged
    {
        private readonly ManagedWriteRange<SparseSetEntry<T>> inner;
        internal ManagedSparseWriteRange(ManagedWriteRange<SparseSetEntry<T>> inner) { this.inner = inner; }
        public int Start => inner.Start;
        public int Count => inner.Count;
        public DeterministicStatus TryGet(int index, out SparseSetEntry<T> item) => inner.TryGet(index, out item);
        public DeterministicStatus TrySet(int index, in T value)
        {
            var status = inner.TryGet(index, out var item); if (!status.IsSuccess()) return status;
            item.Value = value; return inner.TrySet(index, item);
        }
        public DeterministicStatus TryRelease() => inner.TryRelease();
    }
}
