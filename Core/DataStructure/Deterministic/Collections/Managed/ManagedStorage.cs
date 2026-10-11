using System;

namespace LegendaryTools.Deterministic
{
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

    /// <summary>GC-only storage. All snapshot objects, tables and fixed-policy pages are allocated at construction.</summary>
    internal sealed unsafe class ManagedBufferOwner<T> where T : unmanaged
    {
        private sealed class Page
        {
            internal readonly T[] Values;
            internal int References;
            internal Page(int length) { Values = new T[length]; }
        }
        private readonly BufferOptions options;
        private readonly bool paged;
        private readonly int pageElements, maxPages;
        private readonly Page[] pages, freePages;
        private int freeCount, allocatedPages;
        private T[] values;
        private readonly T[][] snapshotValues;
        private readonly Page[][] snapshotPages;
        private readonly int[] snapshotCapacities;
        private readonly ulong[] leases;
        private ulong nextLease;
        internal int Capacity { get; private set; }
        internal ulong Revision { get; private set; }
        internal BufferOptions Options => options;
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
            pageElements = Math.Max(1, options.PageBytes / sizeof(T));
            maxPages = PageCount(options.MaxCapacity);
            snapshotCapacities = new int[options.SnapshotSlots];
            leases = new ulong[options.SnapshotSlots];
            if (paged)
            {
                long poolSize = (long)maxPages * (options.SnapshotSlots + 1L);
                if (poolSize > int.MaxValue) DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
                pages = new Page[maxPages]; freePages = new Page[(int)poolSize];
                snapshotPages = new Page[options.SnapshotSlots][];
                for (int i = 0; i < snapshotPages.Length; i++) snapshotPages[i] = new Page[maxPages];
                // The fixed pool covers the worst case: every retained root owns private pages.
                int reserve = options.Allocation == AllocationPolicy.Fixed ? (int)poolSize : PageCount(Capacity);
                for (int i = 0; i < reserve; i++) { freePages[freeCount++] = new Page(pageElements); allocatedPages++; }
                for (int i = 0; i < PageCount(Capacity); i++) pages[i] = AcquirePage();
            }
            else
            {
                values = new T[Capacity]; snapshotValues = new T[options.SnapshotSlots][];
                for (int i = 0; i < snapshotValues.Length; i++) snapshotValues[i] = new T[options.MaxCapacity];
            }
        }

        private int PageCount(int capacity) => (int)(((long)capacity + pageElements - 1) / pageElements);
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
            { Array.Clear(page.Values, 0, page.Values.Length); freePages[freeCount++] = page; }
        }
        internal T Read(int index) => paged ? pages[index / pageElements].Values[index % pageElements] : values[index];
        internal void Write(int index, in T value)
        { if (paged) pages[index / pageElements].Values[index % pageElements] = value; else values[index] = value; }
        internal void CommitWrite() { Revision++; }

        internal DeterministicStatus CanPrepareWrite(int requiredCapacity, int start, int count)
        {
            if (Disposed) return DeterministicStatus.Disposed;
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
                Array.Copy(original.Values, copy.Values, pageElements); pages[i] = copy; ReleasePage(original);
            }
            Capacity = nextCapacity; return DeterministicStatus.None;
        }

        internal DeterministicStatus TryCapture(out ManagedBufferSnapshot<T> snapshot)
        {
            snapshot = default;
            if (Disposed) return DeterministicStatus.Disposed;
            if (nextLease == ulong.MaxValue) return DeterministicStatus.GenerationExhausted;
            int slot = -1;
            for (int i = 0; i < leases.Length; i++) if (leases[i] == 0) { slot = i; break; }
            if (slot < 0) return DeterministicStatus.SnapshotCapacityExceeded;
            snapshotCapacities[slot] = Capacity;
            if (paged)
                for (int i = 0; i < PageCount(Capacity); i++) { var page = pages[i]; page.References++; snapshotPages[slot][i] = page; }
            else Array.Copy(values, snapshotValues[slot], Capacity);
            leases[slot] = ++nextLease;
            snapshot = new ManagedBufferSnapshot<T>(this, slot, leases[slot]); return DeterministicStatus.None;
        }

        internal bool ValidSnapshot(int slot, ulong lease) => !Disposed && (uint)slot < (uint)leases.Length && lease != 0 && leases[slot] == lease;
        internal int SnapshotCapacity(int slot) => snapshotCapacities[slot];
        internal T ReadSnapshot(int slot, int index) => paged ? snapshotPages[slot][index / pageElements].Values[index % pageElements] : snapshotValues[slot][index];
        internal DeterministicStatus TryRestore(in ManagedBufferSnapshot<T> snapshot)
        {
            if (Disposed) return DeterministicStatus.Disposed;
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
            Capacity = restoredCapacity; Revision++; return DeterministicStatus.None;
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
            for (int i = 0; i < leases.Length; i++) ReleaseSnapshot(i, leases[i]);
            if (paged) for (int i = 0; i < PageCount(Capacity); i++) { ReleasePage(pages[i]); pages[i] = null; }
            if (freePages != null) Array.Clear(freePages, 0, freePages.Length);
            if (snapshotPages != null) Array.Clear(snapshotPages, 0, snapshotPages.Length);
            if (snapshotValues != null) Array.Clear(snapshotValues, 0, snapshotValues.Length);
            values = null; Capacity = 0; Disposed = true; Revision++;
        }
    }
}
