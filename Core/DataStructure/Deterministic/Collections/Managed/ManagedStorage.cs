using System;

namespace LegendaryTools.Deterministic
{
    internal struct ManagedMovement<T> : IDeterministicMovement<T, ManagedStorage<T>> where T : unmanaged
    {
        public void Move(ref ManagedStorage<T> buffer, int source, int destination, int count)
            => buffer.Owner.Move(source, destination, count);
    }

    /// <summary>Snapshot lease. Disposing an old copy cannot release a later snapshot occupying the same slot.</summary>
    public readonly struct ManagedBufferSnapshot<T> : IDisposable where T : unmanaged
    {
        internal readonly ManagedBufferOwner<T> Owner;
        internal readonly int Slot;
        internal readonly ulong Lease;
        internal ManagedBufferSnapshot(ManagedBufferOwner<T> owner, int slot, ulong lease)
        { Owner = owner; Slot = slot; Lease = lease; }
        public bool IsCreated => Owner != null && Owner.ValidSnapshot(Slot, Lease);
        public int Capacity => IsCreated ? Owner.SnapshotCapacity(Slot) : 0;
        public DeterministicStatus TryGet(int index, out T value)
        {
            value = default;
            if (!IsCreated) return DeterministicStatus.Disposed;
            if ((uint)index >= (uint)Capacity) return DeterministicStatus.OutOfRange;
            value = Owner.ReadSnapshot(Slot, index); return DeterministicStatus.None;
        }
        public void Dispose() { if (Owner != null) Owner.ReleaseSnapshot(Slot, Lease); }
    }

    internal struct ManagedStorage<T> : IDeterministicImportBuffer<T>, IDisposable where T : unmanaged
    {
        internal ManagedBufferOwner<T> Owner;
        internal ManagedStorage(BufferOptions options) { Owner = new ManagedBufferOwner<T>(options); }
        public int Capacity => Owner == null ? 0 : Owner.Capacity;
        public ulong Revision => Owner == null ? 0 : Owner.Revision;
        internal ulong HistoryRevision => Owner == null ? 0 : Owner.HistoryRevision;
        public bool IsCreated => Owner != null && !Owner.Disposed;
        public T Read(int index) => Owner.Read(index);
        public void Write(int index, in T value) => Owner.Write(index, value);
        public DeterministicStatus CanPrepareWrite(int requiredCapacity, int start, int count) =>
            Owner == null ? DeterministicStatus.Disposed : Owner.CanPrepareWrite(requiredCapacity, start, count);
        public DeterministicStatus PrepareWrite(int requiredCapacity, int start, int count) =>
            Owner == null ? DeterministicStatus.Disposed : Owner.PrepareWrite(requiredCapacity, start, count);
        public void CommitWrite() => Owner.CommitWrite();
        public DeterministicStatus CanReplaceCapacity(int capacity) => Owner == null ? DeterministicStatus.Disposed : Owner.CanReplaceCapacity(capacity);
        public DeterministicStatus PrepareReplace(int capacity) => Owner == null ? DeterministicStatus.Disposed : Owner.PrepareReplace(capacity);
        internal DeterministicStatus TryCapture(out ManagedBufferSnapshot<T> snapshot)
        {
            snapshot = default;
            return Owner == null ? DeterministicStatus.Disposed : Owner.TryCapture(out snapshot);
        }
        internal DeterministicStatus TryRestore(in ManagedBufferSnapshot<T> snapshot) =>
            Owner == null ? DeterministicStatus.Disposed : Owner.TryRestore(snapshot);
        public void Dispose() { if (Owner != null) Owner.Dispose(); }
    }

    /// <summary>GC-only storage. Fixed pools are allocated at construction; Grow directories follow materialized capacity.</summary>
    internal sealed unsafe class ManagedBufferOwner<T> where T : unmanaged
    {
        private sealed class Page
        {
            internal readonly T[] Values;
            internal readonly int Offset;
            internal int References;
            internal Page(int length) { Values = new T[length]; }
            internal Page(T[] values, int offset) { Values = values; Offset = offset; }
        }
        private readonly BufferOptions options;
        private readonly bool paged;
        private readonly int pageElements;
        private int maxPages;
        private Page[] pages;
        private Page[] freePages;
        private int freeCount, allocatedPages;
        private T[] values;
        private T[][] snapshotValues;
        private Page[][] snapshotPages;
        private int[] snapshotCapacities;
        private ulong[] leases;
        private ulong nextLease;
        internal bool BatchActive { get; private set; }
        private ulong batchLease;
        private int[] batchClaims = Array.Empty<int>();
        private int batchPartitions;
        internal int Capacity { get; private set; }
        internal ulong Revision { get; private set; }
        private bool historyRevisionExhausted;
        internal ulong HistoryRevision => Disposed || BatchActive || historyRevisionExhausted ? 0 : unchecked(Revision + 1UL);
        internal BufferOptions Options => options;
        internal int SnapshotSlots => leases.Length;
        internal int ActiveSnapshotCount
        { get { int count = 0; if (!Disposed) for (int i = 0; i < leases.Length; i++) if (leases[i] != 0) count++; return count; } }
        internal GraphMemoryStats MemoryStats
        {
            get
            {
                if (Disposed) return default;
                int active = 0, shared = 0; long snapshotElements = 0;
                for (int i = 0; i < leases.Length; i++) if (leases[i] != 0) { active++; snapshotElements += snapshotCapacities[i]; }
                if (paged)
                {
                    for (int i = 0; i < PageCount(Capacity); i++) if (pages[i].References > 1) shared++;
                    int live = allocatedPages - freeCount;
                    return new GraphMemoryStats((long)allocatedPages * pageElements * sizeof(T), (long)live * pageElements * sizeof(T), live, shared, active);
                }
                long reservedElements = values.Length;
                for (int i = 0; i < snapshotValues.Length; i++) if (snapshotValues[i] != null) reservedElements += snapshotValues[i].LongLength;
                return new GraphMemoryStats(reservedElements * sizeof(T),
                    ((long)Capacity + snapshotElements) * sizeof(T), 0, 0, active);
            }
        }
        internal DeterministicStatus CanReserveSnapshotSlots(int slots)
        {
            if (Disposed) return DeterministicStatus.Disposed;
            if (BatchActive) return DeterministicStatus.Invalidated;
            if (slots < leases.Length || (long)PageCount(options.MaxCapacity) * (slots + 1L) > int.MaxValue) return DeterministicStatus.InvalidConfiguration;
            return DeterministicStatus.None;
        }
        /// <summary>Host-only expansion. Existing slot/lease pairs and page identities remain valid.</summary>
        internal DeterministicStatus TryReserveSnapshotSlots(int slots)
        {
            var status = CanReserveSnapshotSlots(slots); if (!status.IsSuccess() || slots == leases.Length) return status;
            var nextCapacities = new int[slots]; var nextLeases = new ulong[slots];
            Array.Copy(snapshotCapacities, nextCapacities, leases.Length); Array.Copy(leases, nextLeases, leases.Length);
            if (paged)
            {
                var nextSnapshots = new Page[slots][]; Array.Copy(snapshotPages, nextSnapshots, leases.Length);
                for (int i = leases.Length; i < slots; i++) nextSnapshots[i] = new Page[maxPages];
                var nextFree = new Page[(int)((long)maxPages * (slots + 1L))]; Array.Copy(freePages, nextFree, freeCount);
                int nextAllocated = allocatedPages, nextFreeCount = freeCount;
                if (options.Allocation == AllocationPolicy.Fixed)
                    while (nextAllocated < nextFree.Length) { nextFree[nextFreeCount++] = new Page(pageElements); nextAllocated++; }
                snapshotPages = nextSnapshots; freePages = nextFree; allocatedPages = nextAllocated; freeCount = nextFreeCount;
            }
            else
            {
                var nextSnapshots = new T[slots][]; Array.Copy(snapshotValues, nextSnapshots, leases.Length);
                if (options.Allocation == AllocationPolicy.Fixed)
                    for (int i = leases.Length; i < slots; i++) nextSnapshots[i] = new T[options.MaxCapacity];
                snapshotValues = nextSnapshots;
            }
            snapshotCapacities = nextCapacities; leases = nextLeases; return DeterministicStatus.None;
        }
        internal DeterministicStatus CanReplaceCapacity(int capacity)
        {
            if (Disposed) return DeterministicStatus.Disposed;
            var status = options.TryCapacity(options.InitialCapacity, capacity, out int normalized);
            if (!status.IsSuccess() || capacity < options.InitialCapacity || normalized != capacity) return DeterministicStatus.ProfileMismatch;
            return CanPrepareWrite(Math.Max(Capacity, capacity), 0, Math.Max(Capacity, capacity));
        }
        internal DeterministicStatus PrepareReplace(int capacity)
        {
            var status = CanReplaceCapacity(capacity); if (!status.IsSuccess()) return status;
            int writable = Math.Max(Capacity, capacity);
            status = PrepareWrite(writable, 0, writable); if (!status.IsSuccess()) return status;
            for (int i = 0; i < writable; i++) Write(i, default);
            if (paged)
            {
                int oldPages = PageCount(Capacity), nextPages = PageCount(capacity);
                for (int i = nextPages; i < oldPages; i++) { ReleasePage(pages[i]); pages[i] = null; }
            }
            Capacity = capacity; return DeterministicStatus.None;
        }
        internal bool Disposed { get; private set; }

        internal ManagedBufferOwner(BufferOptions options)
        {
            if (!options.IsValid) DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            this.options = options; Capacity = options.InitialCapacity;
            paged = options.Snapshots == SnapshotPolicy.CopyOnWrite;
            pageElements = Math.Max(1, Math.Min(options.MaxCapacity, options.PageBytes / sizeof(T)));
            int configuredMaxPages = PageCount(options.MaxCapacity);
            maxPages = paged && options.Allocation == AllocationPolicy.Grow ? PageCount(Capacity) : configuredMaxPages;
            snapshotCapacities = new int[options.SnapshotSlots];
            leases = new ulong[options.SnapshotSlots];
            if (paged)
            {
                if ((long)configuredMaxPages * (options.SnapshotSlots + 1L) > int.MaxValue) DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
                long poolSize = (long)maxPages * (options.SnapshotSlots + 1L);
                if (poolSize > int.MaxValue) DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
                pages = new Page[maxPages]; freePages = new Page[(int)poolSize];
                snapshotPages = new Page[options.SnapshotSlots][];
                for (int i = 0; i < snapshotPages.Length; i++) snapshotPages[i] = new Page[maxPages];
                // The fixed pool covers the worst case: every retained root owns private pages.
                int reserve = options.Allocation == AllocationPolicy.Fixed ? (int)poolSize : PageCount(Capacity);
                // Fixed pages share a slab while retaining independent refcounts and lease identity.
                T[] slab = options.Allocation == AllocationPolicy.Fixed && reserve != 0 && (long)reserve * pageElements <= int.MaxValue ? new T[reserve * pageElements] : null;
                for (int i = 0; i < reserve; i++) { freePages[freeCount++] = slab == null ? new Page(pageElements) : new Page(slab, i * pageElements); allocatedPages++; }
                for (int i = 0; i < PageCount(Capacity); i++) pages[i] = AcquirePage();
            }
            else
            {
                values = new T[Capacity]; snapshotValues = new T[options.SnapshotSlots][];
                if (options.Allocation == AllocationPolicy.Fixed)
                    for (int i = 0; i < snapshotValues.Length; i++) snapshotValues[i] = new T[options.MaxCapacity];
            }
        }

        private int PageCount(int capacity) => (int)(((long)capacity + pageElements - 1) / pageElements);
        private void EnsureDirectory(int required)
        {
            if (required <= maxPages) return;
            var nextPages = new Page[required]; Array.Copy(pages, nextPages, maxPages);
            var nextFree = new Page[(int)((long)required * (leases.Length + 1L))]; Array.Copy(freePages, nextFree, freeCount);
            var nextSnapshots = new Page[leases.Length][];
            for (int i = 0; i < leases.Length; i++) { nextSnapshots[i] = new Page[required]; Array.Copy(snapshotPages[i], nextSnapshots[i], maxPages); }
            pages = nextPages; freePages = nextFree; snapshotPages = nextSnapshots; maxPages = required;
        }
        private Page AcquirePage()
        {
            Page page;
            if (freeCount != 0) { page = freePages[--freeCount]; freePages[freeCount] = null; }
            else { page = new Page(pageElements); allocatedPages++; }
            page.References = 1; return page;
        }
        private void ReleasePage(Page page)
        {
            if (--page.References == 0)
            { Array.Clear(page.Values, page.Offset, pageElements); freePages[freeCount++] = page; }
        }
        internal T Read(int index)
        { if (!paged) return values[index]; var page = pages[index / pageElements]; return page.Values[page.Offset + index % pageElements]; }
        internal void Write(int index, in T value)
        { if (paged) { var page = pages[index / pageElements]; page.Values[page.Offset + index % pageElements] = value; } else values[index] = value; }
        internal void CommitWrite() { if (Revision == ulong.MaxValue) historyRevisionExhausted = true; Revision++; }
        internal DeterministicStatus TryBeginBatch(int length, int partitions, out ManagedWriteBatch<T> batch)
        {
            batch = default;
            if (Disposed) return DeterministicStatus.Disposed;
            if (BatchActive) return DeterministicStatus.ProtocolConflict;
            if (partitions <= 0 || length < 0 || length > Capacity) return DeterministicStatus.InvalidConfiguration;
            if (batchLease == ulong.MaxValue) return DeterministicStatus.GenerationExhausted;
            if (batchClaims.Length < partitions) batchClaims = new int[partitions];
            var status = PrepareWrite(Capacity, 0, length); if (!status.IsSuccess()) return status;
            Array.Clear(batchClaims, 0, partitions); batchPartitions = partitions; BatchActive = true;
            batch = new ManagedWriteBatch<T>(this, ++batchLease, length, partitions); return DeterministicStatus.None;
        }
        internal bool ValidBatch(ulong lease) => !Disposed && BatchActive && lease == batchLease;
        internal bool ValidBatchPartition(ulong lease, int partition, int thread)
            => ValidBatch(lease) && (uint)partition < (uint)batchPartitions && System.Threading.Volatile.Read(ref batchClaims[partition]) == thread;
        internal DeterministicStatus TryReleaseBatchPartition(ulong lease, int partition, int thread)
        {
            if (!ValidBatchPartition(lease, partition, thread)) return DeterministicStatus.Invalidated;
            if (thread != System.Environment.CurrentManagedThreadId) return DeterministicStatus.InvalidOwner;
            return System.Threading.Interlocked.CompareExchange(ref batchClaims[partition], -1, thread) == thread
                ? DeterministicStatus.None : DeterministicStatus.Invalidated;
        }
        internal DeterministicStatus TryClaimBatch(ulong lease, int partition, int partitions, out int thread)
        {
            thread = 0;
            if (!ValidBatch(lease)) return DeterministicStatus.Invalidated;
            if ((uint)partition >= (uint)partitions) return DeterministicStatus.OutOfRange;
            thread = System.Environment.CurrentManagedThreadId;
            return System.Threading.Interlocked.CompareExchange(ref batchClaims[partition], thread, 0) == 0
                ? DeterministicStatus.None : DeterministicStatus.ProtocolConflict;
        }
        internal DeterministicStatus TryCompleteBatch(ulong lease)
        {
            if (!ValidBatch(lease)) return DeterministicStatus.Invalidated;
            for (int i = 0; i < batchPartitions; i++) if (System.Threading.Volatile.Read(ref batchClaims[i]) > 0) return DeterministicStatus.Pending;
            BatchActive = false; CommitWrite(); return DeterministicStatus.None;
        }

        internal void Move(int source, int destination, int count)
        {
            if (count == 0 || source == destination) return;
            if (!paged) { Array.Copy(values, source, values, destination, count); return; }
            if (destination > source && destination < (long)source + count)
            {
                int sourceEnd = source + count, destinationEnd = destination + count;
                while (count != 0)
                {
                    int sourceOffset = (sourceEnd - 1) % pageElements + 1, destinationOffset = (destinationEnd - 1) % pageElements + 1;
                    int take = Math.Min(count, Math.Min(sourceOffset, destinationOffset));
                    var from = pages[(sourceEnd - 1) / pageElements]; var to = pages[(destinationEnd - 1) / pageElements];
                    Array.Copy(from.Values, from.Offset + sourceOffset - take, to.Values, to.Offset + destinationOffset - take, take);
                    sourceEnd -= take; destinationEnd -= take; count -= take;
                }
            }
            else
            {
                while (count != 0)
                {
                    int sourceOffset = source % pageElements, destinationOffset = destination % pageElements;
                    int take = Math.Min(count, Math.Min(pageElements - sourceOffset, pageElements - destinationOffset));
                    var from = pages[source / pageElements]; var to = pages[destination / pageElements];
                    Array.Copy(from.Values, from.Offset + sourceOffset, to.Values, to.Offset + destinationOffset, take);
                    source += take; destination += take; count -= take;
                }
            }
        }

        internal DeterministicStatus CanPrepareWrite(int requiredCapacity, int start, int count)
        {
            if (Disposed) return DeterministicStatus.Disposed;
            if (BatchActive) return DeterministicStatus.Invalidated;
            if (start < 0 || count < 0) return DeterministicStatus.OutOfRange;
            var status = options.TryCapacity(Capacity, requiredCapacity, out int nextCapacity);
            if (!status.IsSuccess()) return status;
            if ((long)start + count > nextCapacity) return DeterministicStatus.OutOfRange;
            // The COW pool is sized for all retained roots plus the live root.
            return DeterministicStatus.None;
        }

        internal DeterministicStatus PrepareWrite(int requiredCapacity, int start, int count)
        {
            if (Disposed) return DeterministicStatus.Disposed;
            if (BatchActive) return DeterministicStatus.Invalidated;
            if (start < 0 || count < 0) return DeterministicStatus.OutOfRange;
            var status = options.TryCapacity(Capacity, requiredCapacity, out int nextCapacity);
            if (!status.IsSuccess()) return status;
            if ((long)start + count > nextCapacity) return DeterministicStatus.OutOfRange;
            if (!paged)
            {
                if (nextCapacity > values.Length)
                {
                    var next = new T[nextCapacity]; Array.Copy(values, next, Capacity); values = next;
                }
                Capacity = nextCapacity; return DeterministicStatus.None;
            }
            int oldPageCount = PageCount(Capacity), nextPageCount = PageCount(nextCapacity);
            EnsureDirectory(nextPageCount);
            int first = start / pageElements;
            int last = count == 0 ? first - 1 : (int)(((long)start + count - 1) / pageElements);
            int needed = nextPageCount - oldPageCount;
            for (int i = first; i <= last && i < oldPageCount; i++) if (pages[i].References > 1) needed++;
            if (needed > freeCount && options.Allocation == AllocationPolicy.Fixed) return DeterministicStatus.SnapshotCapacityExceeded;
            if ((long)allocatedPages + Math.Max(0, needed - freeCount) > freePages.Length) return DeterministicStatus.SnapshotCapacityExceeded;
            // Allocate all missing pages before changing a live root (including possible GC allocation failures).
            while (freeCount < needed) { freePages[freeCount++] = new Page(pageElements); allocatedPages++; }
            for (int i = oldPageCount; i < nextPageCount; i++) pages[i] = AcquirePage();
            for (int i = first; i <= last && i < oldPageCount; i++)
            {
                if (pages[i].References <= 1) continue;
                var original = pages[i]; var copy = AcquirePage();
                Array.Copy(original.Values, original.Offset, copy.Values, copy.Offset, pageElements); pages[i] = copy; ReleasePage(original);
            }
            Capacity = nextCapacity; return DeterministicStatus.None;
        }

        internal DeterministicStatus TryCapture(out ManagedBufferSnapshot<T> snapshot)
        {
            snapshot = default;
            if (Disposed) return DeterministicStatus.Disposed;
            if (BatchActive) return DeterministicStatus.Invalidated;
            if (nextLease == ulong.MaxValue) return DeterministicStatus.GenerationExhausted;
            int slot = -1;
            for (int i = 0; i < leases.Length; i++) if (leases[i] == 0) { slot = i; break; }
            if (slot < 0) return DeterministicStatus.SnapshotCapacityExceeded;
            if (paged)
                for (int i = 0; i < PageCount(Capacity); i++) { var page = pages[i]; page.References++; snapshotPages[slot][i] = page; }
            else
            {
                // Grow allocates only the occupied slot, retaining its backing for reuse after release.
                if (snapshotValues[slot] == null || snapshotValues[slot].Length < Capacity)
                    snapshotValues[slot] = new T[Capacity];
                Array.Copy(values, snapshotValues[slot], Capacity);
            }
            snapshotCapacities[slot] = Capacity;
            leases[slot] = ++nextLease;
            snapshot = new ManagedBufferSnapshot<T>(this, slot, leases[slot]); return DeterministicStatus.None;
        }

        internal bool ValidSnapshot(int slot, ulong lease) => !Disposed && (uint)slot < (uint)leases.Length && lease != 0 && leases[slot] == lease;
        internal int SnapshotCapacity(int slot) => snapshotCapacities[slot];
        internal T ReadSnapshot(int slot, int index)
        { if (!paged) return snapshotValues[slot][index]; var page = snapshotPages[slot][index / pageElements]; return page.Values[page.Offset + index % pageElements]; }
        // Internal use only: validate the lease and consume the reference before release/restore.
        internal ref readonly T ReadSnapshotReference(int slot, int index)
        {
            if (!paged) return ref snapshotValues[slot][index];
            var page = snapshotPages[slot][index / pageElements];
            return ref page.Values[page.Offset + index % pageElements];
        }
        internal DeterministicStatus TryRestore(in ManagedBufferSnapshot<T> snapshot)
        {
            if (Disposed) return DeterministicStatus.Disposed;
            if (BatchActive) return DeterministicStatus.Invalidated;
            if (!ReferenceEquals(snapshot.Owner, this)) return DeterministicStatus.InvalidOwner;
            if (!ValidSnapshot(snapshot.Slot, snapshot.Lease)) return DeterministicStatus.Disposed;
            int restoredCapacity = snapshotCapacities[snapshot.Slot];
            if (paged)
            {
                for (int i = 0; i < PageCount(Capacity); i++) { ReleasePage(pages[i]); pages[i] = null; }
                for (int i = 0; i < PageCount(restoredCapacity); i++)
                { var page = snapshotPages[snapshot.Slot][i]; page.References++; pages[i] = page; }
            }
            else Array.Copy(snapshotValues[snapshot.Slot], values, restoredCapacity);
            Capacity = restoredCapacity; CommitWrite(); return DeterministicStatus.None;
        }
        internal void ReleaseSnapshot(int slot, ulong lease)
        {
            if (!ValidSnapshot(slot, lease)) return;
            if (paged)
                for (int i = 0; i < PageCount(snapshotCapacities[slot]); i++)
                { ReleasePage(snapshotPages[slot][i]); snapshotPages[slot][i] = null; }
            leases[slot] = 0;
        }
        internal void Dispose()
        {
            if (Disposed) return;
            if (BatchActive) throw new InvalidOperationException("Complete prepared writes before disposal.");
            for (int i = 0; i < leases.Length; i++) ReleaseSnapshot(i, leases[i]);
            if (paged) for (int i = 0; i < PageCount(Capacity); i++) { ReleasePage(pages[i]); pages[i] = null; }
            if (freePages != null) Array.Clear(freePages, 0, freePages.Length);
            if (snapshotPages != null) Array.Clear(snapshotPages, 0, snapshotPages.Length);
            if (snapshotValues != null) Array.Clear(snapshotValues, 0, snapshotValues.Length);
            values = null; Capacity = 0; Disposed = true; Revision++;
        }
    }
}
