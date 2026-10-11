using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace LegendaryTools.Deterministic
{
    internal unsafe struct NativeStorage<T> : IDeterministicImportBuffer<T> where T : unmanaged
    {
        internal struct Page { internal T* Values; internal int References; }
        internal struct Header
        {
            internal BufferOptions Options;
            internal Allocator Allocator;
            internal SequenceState State;
            internal OrderedMapState MapState;
            internal PriorityQueueState PriorityState;
            internal SlotMapState SlotState;
            internal AvlState TreeState;
            internal LinkedListState Links;
            internal int Capacity, PhysicalCapacity, PageElements, MaxPages, PoolCapacity, FreeCount, AllocatedPages;
            internal ulong Revision, NextLease;
            internal T* Values;
            internal Page* Pool;
            internal int* Pages;
            internal int* FreePages;
            internal int* SnapshotPages;
            internal T* SnapshotValues;
            internal int* SnapshotCapacities;
            internal ulong* Leases;
        }
        [NativeDisableUnsafePtrRestriction] internal Header* Data;
        public bool IsCreated => Data != null;
        public int Capacity => Data == null ? 0 : Data->Capacity;
        public ulong Revision => Data == null ? 0 : Data->Revision;
        private bool Paged => Data->Options.Snapshots == SnapshotPolicy.CopyOnWrite;
        private int PageCount(int capacity) => (int)(((long)capacity + Data->PageElements - 1) / Data->PageElements);
        private static void* Allocate(long bytes, int alignment, Allocator allocator)
        {
            if (bytes == 0) return null;
            void* pointer = UnsafeUtility.MallocTracked(bytes, alignment, allocator, 0);
            if (pointer == null) throw new OutOfMemoryException();
            UnsafeUtility.MemClear(pointer, bytes); return pointer;
        }
        private static void Free(void* pointer, Allocator allocator) { if (pointer != null) UnsafeUtility.FreeTracked(pointer, allocator); }

        internal static NativeStorage<T> Create(BufferOptions options, Allocator allocator)
        {
            if (!options.IsValid || allocator < Allocator.Temp || allocator > Allocator.Persistent)
                DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            var storage = new NativeStorage<T> { Data = (Header*)Allocate(sizeof(Header), 16, allocator) };
            storage.Data->Options = options; storage.Data->Allocator = allocator;
            storage.Data->Capacity = storage.Data->PhysicalCapacity = options.InitialCapacity;
            storage.Data->PageElements = Math.Max(1, options.PageBytes / sizeof(T));
            storage.Data->MaxPages = storage.PageCount(options.MaxCapacity);
            try
            {
                Header* data = storage.Data;
                data->SnapshotCapacities = (int*)Allocate((long)options.SnapshotSlots * sizeof(int), 16, allocator);
                data->Leases = (ulong*)Allocate((long)options.SnapshotSlots * sizeof(ulong), 16, allocator);
                if (storage.Paged)
                {
                    long poolCapacity = (long)data->MaxPages * (options.SnapshotSlots + 1L);
                    if (poolCapacity > int.MaxValue) DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
                    data->PoolCapacity = (int)poolCapacity;
                    data->Pool = (Page*)Allocate(poolCapacity * sizeof(Page), 16, allocator);
                    data->Pages = (int*)Allocate((long)data->MaxPages * sizeof(int), 16, allocator);
                    data->FreePages = (int*)Allocate(poolCapacity * sizeof(int), 16, allocator);
                    data->SnapshotPages = (int*)Allocate((long)data->MaxPages * options.SnapshotSlots * sizeof(int), 16, allocator);
                    int reserve = options.Allocation == AllocationPolicy.Fixed ? data->PoolCapacity : storage.PageCount(data->Capacity);
                    storage.ReserveFreePages(reserve);
                    for (int i = 0; i < storage.PageCount(data->Capacity); i++) data->Pages[i] = storage.AcquirePage();
                }
                else
                {
                    data->Values = (T*)Allocate((long)data->Capacity * sizeof(T), Math.Max(16, UnsafeUtility.AlignOf<T>()), allocator);
                    data->SnapshotValues = (T*)Allocate((long)options.MaxCapacity * options.SnapshotSlots * sizeof(T), Math.Max(16, UnsafeUtility.AlignOf<T>()), allocator);
                }
                return storage;
            }
            catch { storage.Destroy(); throw; }
        }

        private void ReserveFreePages(int required)
        {
            while (Data->FreeCount < required)
            {
                int id = Data->AllocatedPages;
                T* values = (T*)Allocate((long)Data->PageElements * sizeof(T), Math.Max(16, UnsafeUtility.AlignOf<T>()), Data->Allocator);
                Data->Pool[id].Values = values; Data->FreePages[Data->FreeCount++] = id; Data->AllocatedPages++;
            }
        }
        private int AcquirePage()
        { int id = Data->FreePages[--Data->FreeCount]; Data->Pool[id].References = 1; return id; }
        private void ReleasePage(int id)
        {
            if (--Data->Pool[id].References != 0) return;
            UnsafeUtility.MemClear(Data->Pool[id].Values, (long)Data->PageElements * sizeof(T)); Data->FreePages[Data->FreeCount++] = id;
        }
        public T Read(int index) => Paged ? Data->Pool[Data->Pages[index / Data->PageElements]].Values[index % Data->PageElements] : Data->Values[index];
        public void Write(int index, in T value)
        {
            if (Paged) Data->Pool[Data->Pages[index / Data->PageElements]].Values[index % Data->PageElements] = value;
            else Data->Values[index] = value;
        }
        public void CommitWrite() { Data->Revision++; }
        public DeterministicStatus CanReplaceCapacity(int capacity)
        {
            if (!IsCreated) return DeterministicStatus.Disposed;
            var status = Data->Options.TryCapacity(Data->Options.InitialCapacity, capacity, out int normalized);
            if (!status.IsSuccess() || capacity < Data->Options.InitialCapacity || normalized != capacity) return DeterministicStatus.ProfileMismatch;
            return CanPrepareWrite(Math.Max(Capacity, capacity), 0, Math.Max(Capacity, capacity));
        }
        public DeterministicStatus PrepareReplace(int capacity)
        {
            var status = CanReplaceCapacity(capacity); if (!status.IsSuccess()) return status;
            int writable = Math.Max(Capacity, capacity); status = PrepareWrite(writable, 0, writable); if (!status.IsSuccess()) return status;
            for (int i = 0; i < writable; i++) Write(i, default);
            if (Paged)
            { int oldPages = PageCount(Capacity), nextPages = PageCount(capacity); for (int i = nextPages; i < oldPages; i++) { ReleasePage(Data->Pages[i]); Data->Pages[i] = 0; } }
            Data->Capacity = capacity; return DeterministicStatus.None;
        }

        public DeterministicStatus CanPrepareWrite(int requiredCapacity, int start, int count)
        {
            if (!IsCreated) return DeterministicStatus.Disposed;
            if (start < 0 || count < 0) return DeterministicStatus.OutOfRange;
            var status = Data->Options.TryCapacity(Capacity, requiredCapacity, out int nextCapacity);
            if (!status.IsSuccess()) return status;
            return (long)start + count > nextCapacity ? DeterministicStatus.OutOfRange : DeterministicStatus.None;
        }

        public DeterministicStatus PrepareWrite(int requiredCapacity, int start, int count)
        {
            if (!IsCreated) return DeterministicStatus.Disposed;
            if (start < 0 || count < 0) return DeterministicStatus.OutOfRange;
            var status = Data->Options.TryCapacity(Capacity, requiredCapacity, out int nextCapacity);
            if (!status.IsSuccess()) return status;
            if ((long)start + count > nextCapacity) return DeterministicStatus.OutOfRange;
            if (!Paged)
            {
                if (nextCapacity > Data->PhysicalCapacity)
                {
                    var next = (T*)Allocate((long)nextCapacity * sizeof(T), Math.Max(16, UnsafeUtility.AlignOf<T>()), Data->Allocator);
                    if (Capacity != 0) UnsafeUtility.MemCpy(next, Data->Values, (long)Capacity * sizeof(T));
                    Free(Data->Values, Data->Allocator); Data->Values = next; Data->PhysicalCapacity = nextCapacity;
                }
                Data->Capacity = nextCapacity; return DeterministicStatus.None;
            }
            int oldPages = PageCount(Capacity), nextPages = PageCount(nextCapacity);
            int first = start / Data->PageElements;
            int last = count == 0 ? first - 1 : (int)(((long)start + count - 1) / Data->PageElements);
            int needed = nextPages - oldPages;
            for (int i = first; i <= last && i < oldPages; i++) if (Data->Pool[Data->Pages[i]].References > 1) needed++;
            if (needed > Data->FreeCount && Data->Options.Allocation == AllocationPolicy.Fixed) return DeterministicStatus.SnapshotCapacityExceeded;
            if ((long)Data->AllocatedPages + Math.Max(0, needed - Data->FreeCount) > Data->PoolCapacity) return DeterministicStatus.SnapshotCapacityExceeded;
            ReserveFreePages(needed);
            for (int i = oldPages; i < nextPages; i++) Data->Pages[i] = AcquirePage();
            for (int i = first; i <= last && i < oldPages; i++)
            {
                int original = Data->Pages[i]; if (Data->Pool[original].References <= 1) continue;
                int copy = AcquirePage();
                UnsafeUtility.MemCpy(Data->Pool[copy].Values, Data->Pool[original].Values, (long)Data->PageElements * sizeof(T));
                Data->Pages[i] = copy; ReleasePage(original);
            }
            Data->Capacity = nextCapacity; return DeterministicStatus.None;
        }

        internal DeterministicStatus TryCapture(out int slot, out ulong lease)
        {
            slot = -1; lease = 0;
            if (!IsCreated) return DeterministicStatus.Disposed;
            if (Data->NextLease == ulong.MaxValue) return DeterministicStatus.GenerationExhausted;
            for (int i = 0; i < Data->Options.SnapshotSlots; i++) if (Data->Leases[i] == 0) { slot = i; break; }
            if (slot < 0) return DeterministicStatus.SnapshotCapacityExceeded;
            Data->SnapshotCapacities[slot] = Capacity;
            if (Paged)
                for (int i = 0; i < PageCount(Capacity); i++)
                { int id = Data->Pages[i]; Data->Pool[id].References++; Data->SnapshotPages[(long)slot * Data->MaxPages + i] = id; }
            else if (Capacity != 0) UnsafeUtility.MemCpy(Data->SnapshotValues + (long)slot * Data->Options.MaxCapacity, Data->Values, (long)Capacity * sizeof(T));
            lease = Data->Leases[slot] = ++Data->NextLease; return DeterministicStatus.None;
        }
        internal bool ValidSnapshot(int slot, ulong lease) => IsCreated && (uint)slot < (uint)Data->Options.SnapshotSlots && lease != 0 && Data->Leases[slot] == lease;
        internal T ReadSnapshot(int slot, int index) => Paged ?
            Data->Pool[Data->SnapshotPages[(long)slot * Data->MaxPages + index / Data->PageElements]].Values[index % Data->PageElements] :
            Data->SnapshotValues[(long)slot * Data->Options.MaxCapacity + index];
        internal DeterministicStatus TryRestore(int slot, ulong lease)
        {
            if (!ValidSnapshot(slot, lease)) return DeterministicStatus.Disposed;
            int restored = Data->SnapshotCapacities[slot];
            if (Paged)
            {
                for (int i = 0; i < PageCount(Capacity); i++) ReleasePage(Data->Pages[i]);
                for (int i = 0; i < PageCount(restored); i++)
                { int id = Data->SnapshotPages[(long)slot * Data->MaxPages + i]; Data->Pool[id].References++; Data->Pages[i] = id; }
            }
            else if (restored != 0) UnsafeUtility.MemCpy(Data->Values, Data->SnapshotValues + (long)slot * Data->Options.MaxCapacity, (long)restored * sizeof(T));
            Data->Capacity = restored; Data->Revision++; return DeterministicStatus.None;
        }
        internal void ReleaseSnapshot(int slot, ulong lease)
        {
            if (!ValidSnapshot(slot, lease)) return;
            if (Paged)
                for (int i = 0; i < PageCount(Data->SnapshotCapacities[slot]); i++) ReleasePage(Data->SnapshotPages[(long)slot * Data->MaxPages + i]);
            Data->Leases[slot] = 0;
        }
        internal void Destroy()
        {
            if (Data == null) return;
            var allocator = Data->Allocator;
            if (Data->Pool != null) for (int i = 0; i < Data->AllocatedPages; i++) Free(Data->Pool[i].Values, allocator);
            Free(Data->Values, allocator); Free(Data->SnapshotValues, allocator); Free(Data->Pool, allocator);
            Free(Data->Pages, allocator); Free(Data->FreePages, allocator); Free(Data->SnapshotPages, allocator);
            Free(Data->SnapshotCapacities, allocator); Free(Data->Leases, allocator); Free(Data, allocator); Data = null;
        }
    }
}
