using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace LegendaryTools.Deterministic
{
    internal struct NativeMovement<T> : IDeterministicMovement<T, NativeStorage<T>> where T : unmanaged
    {
        public void Move(ref NativeStorage<T> buffer, int source, int destination, int count)
            => buffer.Move(source, destination, count);
    }

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
            internal ulong Revision, NextLease, BatchLease;
            internal byte BatchActive, HistoryRevisionExhausted;
            internal int BatchPartitions, BatchClaimCapacity;
            internal int* BatchClaims;
            internal T* Values;
            internal Page* Pool;
            internal int* Pages;
            internal int* FreePages;
            internal int* SnapshotPages;
            internal T* SnapshotValues;
            internal T** GrowSnapshotValues;
            internal int* GrowSnapshotCapacities;
            internal int* SnapshotCapacities;
            internal ulong* Leases;
            internal byte* DirectoryArena;
        }
        [NativeDisableUnsafePtrRestriction] internal Header* Data;
        public bool IsCreated => Data != null;
        public int Capacity => Data == null ? 0 : Data->Capacity;
        public ulong Revision => Data == null ? 0 : Data->Revision;
        internal ulong HistoryRevision => !IsCreated || BatchActive || Data->HistoryRevisionExhausted != 0 ? 0 : unchecked(Data->Revision + 1UL);
        internal bool BatchActive => IsCreated && Data->BatchActive != 0;
        internal int ActiveSnapshotCount
        { get { int count = 0; if (Data != null) for (int i = 0; i < Data->Options.SnapshotSlots; i++) if (Data->Leases[i] != 0) count++; return count; } }
        internal GraphMemoryStats MemoryStats
        {
            get
            {
                if (!IsCreated) return default;
                int active = 0, shared = 0; long snapshotElements = 0;
                for (int i = 0; i < Data->Options.SnapshotSlots; i++) if (Data->Leases[i] != 0) { active++; snapshotElements += Data->SnapshotCapacities[i]; }
                if (Paged)
                {
                    for (int i = 0; i < PageCount(Capacity); i++) if (Data->Pool[Data->Pages[i]].References > 1) shared++;
                    int live = Data->AllocatedPages - Data->FreeCount;
                    return new GraphMemoryStats((long)Data->AllocatedPages * Data->PageElements * sizeof(T), (long)live * Data->PageElements * sizeof(T), live, shared, active);
                }
                long reservedElements = Data->PhysicalCapacity;
                if (Data->Options.Allocation == AllocationPolicy.Fixed) reservedElements += (long)Data->Options.MaxCapacity * Data->Options.SnapshotSlots;
                else for (int i = 0; i < Data->Options.SnapshotSlots; i++) reservedElements += Data->GrowSnapshotCapacities[i];
                return new GraphMemoryStats(reservedElements * sizeof(T),
                    ((long)Capacity + snapshotElements) * sizeof(T), 0, 0, active);
            }
        }
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
            storage.Data->PageElements = Math.Max(1, Math.Min(options.MaxCapacity, options.PageBytes / sizeof(T)));
            int configuredMaxPages = storage.PageCount(options.MaxCapacity);
            storage.Data->MaxPages = storage.Paged && options.Allocation == AllocationPolicy.Grow ? storage.PageCount(options.InitialCapacity) : configuredMaxPages;
            try
            {
                Header* data = storage.Data;
                data->SnapshotCapacities = (int*)Allocate((long)options.SnapshotSlots * sizeof(int), 16, allocator);
                data->Leases = (ulong*)Allocate((long)options.SnapshotSlots * sizeof(ulong), 16, allocator);
                if (storage.Paged)
                {
                    if ((long)configuredMaxPages * (options.SnapshotSlots + 1L) > int.MaxValue) DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
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
                    if (options.Allocation == AllocationPolicy.Fixed)
                        data->SnapshotValues = (T*)Allocate((long)options.MaxCapacity * options.SnapshotSlots * sizeof(T), Math.Max(16, UnsafeUtility.AlignOf<T>()), allocator);
                    else
                    {
                        data->GrowSnapshotValues = (T**)Allocate((long)options.SnapshotSlots * sizeof(T*), 16, allocator);
                        data->GrowSnapshotCapacities = (int*)Allocate((long)options.SnapshotSlots * sizeof(int), 16, allocator);
                    }
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
        private void FreeDirectory()
        {
            if (Data->DirectoryArena != null) Free(Data->DirectoryArena, Data->Allocator);
            else
            {
                Free(Data->Pool, Data->Allocator); Free(Data->Pages, Data->Allocator);
                Free(Data->FreePages, Data->Allocator); Free(Data->SnapshotPages, Data->Allocator);
            }
        }
        private static long Aligned(long bytes) => (bytes + 15) & ~15L;
        // One allocation makes directory growth fail before publication and remains callable from Burst.
        private void GrowDirectory(int pages, int slots)
        {
            int poolCapacity = (int)((long)pages * (slots + 1L));
            long rootBytes = Aligned((long)pages * sizeof(int));
            long poolBytes = Aligned((long)poolCapacity * sizeof(Page));
            long freeBytes = Aligned((long)poolCapacity * sizeof(int));
            long snapshotBytes = Aligned((long)pages * slots * sizeof(int));
            byte* arena = (byte*)Allocate(rootBytes + poolBytes + freeBytes + snapshotBytes, 16, Data->Allocator);
            int* nextPages = (int*)arena; Page* nextPool = (Page*)(arena + rootBytes);
            int* nextFree = (int*)(arena + rootBytes + poolBytes);
            int* nextSnapshots = (int*)(arena + rootBytes + poolBytes + freeBytes);
            if (Data->MaxPages != 0) UnsafeUtility.MemCpy(nextPages, Data->Pages, (long)Data->MaxPages * sizeof(int));
            if (Data->AllocatedPages != 0) UnsafeUtility.MemCpy(nextPool, Data->Pool, (long)Data->AllocatedPages * sizeof(Page));
            if (Data->FreeCount != 0) UnsafeUtility.MemCpy(nextFree, Data->FreePages, (long)Data->FreeCount * sizeof(int));
            if (Data->MaxPages != 0)
                for (int slot = 0; slot < Data->Options.SnapshotSlots; slot++)
                    UnsafeUtility.MemCpy(nextSnapshots + (long)slot * pages, Data->SnapshotPages + (long)slot * Data->MaxPages, (long)Data->MaxPages * sizeof(int));
            FreeDirectory();
            Data->Pages = nextPages; Data->Pool = nextPool; Data->FreePages = nextFree; Data->SnapshotPages = nextSnapshots;
            Data->DirectoryArena = arena; Data->MaxPages = pages; Data->PoolCapacity = poolCapacity;
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
        public void CommitWrite() { if (Data->Revision == ulong.MaxValue) Data->HistoryRevisionExhausted = 1; Data->Revision++; }
        internal void Move(int source, int destination, int count)
        {
            if (count == 0 || source == destination) return;
            if (!Paged) { UnsafeUtility.MemMove(Data->Values + destination, Data->Values + source, (long)count * sizeof(T)); return; }
            int pageElements = Data->PageElements;
            if (destination > source && destination < (long)source + count)
            {
                int sourceEnd = source + count, destinationEnd = destination + count;
                while (count != 0)
                {
                    int sourceOffset = (sourceEnd - 1) % pageElements + 1, destinationOffset = (destinationEnd - 1) % pageElements + 1;
                    int take = Math.Min(count, Math.Min(sourceOffset, destinationOffset));
                    UnsafeUtility.MemMove(Data->Pool[Data->Pages[(destinationEnd - 1) / pageElements]].Values + destinationOffset - take,
                        Data->Pool[Data->Pages[(sourceEnd - 1) / pageElements]].Values + sourceOffset - take, (long)take * sizeof(T));
                    sourceEnd -= take; destinationEnd -= take; count -= take;
                }
            }
            else
            {
                while (count != 0)
                {
                    int sourceOffset = source % pageElements, destinationOffset = destination % pageElements;
                    int take = Math.Min(count, Math.Min(pageElements - sourceOffset, pageElements - destinationOffset));
                    UnsafeUtility.MemMove(Data->Pool[Data->Pages[destination / pageElements]].Values + destinationOffset,
                        Data->Pool[Data->Pages[source / pageElements]].Values + sourceOffset, (long)take * sizeof(T));
                    source += take; destination += take; count -= take;
                }
            }
        }
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
            if (BatchActive) return DeterministicStatus.Invalidated;
            if (start < 0 || count < 0) return DeterministicStatus.OutOfRange;
            var status = Data->Options.TryCapacity(Capacity, requiredCapacity, out int nextCapacity);
            if (!status.IsSuccess()) return status;
            return (long)start + count > nextCapacity ? DeterministicStatus.OutOfRange : DeterministicStatus.None;
        }

        public DeterministicStatus PrepareWrite(int requiredCapacity, int start, int count)
        {
            if (!IsCreated) return DeterministicStatus.Disposed;
            if (BatchActive) return DeterministicStatus.Invalidated;
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
            if (nextPages > Data->MaxPages) GrowDirectory(nextPages, Data->Options.SnapshotSlots);
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
            if (BatchActive) return DeterministicStatus.Invalidated;
            if (Data->NextLease == ulong.MaxValue) return DeterministicStatus.GenerationExhausted;
            for (int i = 0; i < Data->Options.SnapshotSlots; i++) if (Data->Leases[i] == 0) { slot = i; break; }
            if (slot < 0) return DeterministicStatus.SnapshotCapacityExceeded;
            if (Paged)
                for (int i = 0; i < PageCount(Capacity); i++)
                { int id = Data->Pages[i]; Data->Pool[id].References++; Data->SnapshotPages[(long)slot * Data->MaxPages + i] = id; }
            else
            {
                if (Data->Options.Allocation == AllocationPolicy.Grow && Data->GrowSnapshotCapacities[slot] < Capacity)
                {
                    T* next = (T*)Allocate((long)Capacity * sizeof(T), Math.Max(16, UnsafeUtility.AlignOf<T>()), Data->Allocator);
                    Free(Data->GrowSnapshotValues[slot], Data->Allocator);
                    Data->GrowSnapshotValues[slot] = next; Data->GrowSnapshotCapacities[slot] = Capacity;
                }
                if (Capacity != 0) UnsafeUtility.MemCpy(SnapshotPointer(slot), Data->Values, (long)Capacity * sizeof(T));
            }
            Data->SnapshotCapacities[slot] = Capacity;
            lease = Data->Leases[slot] = ++Data->NextLease; return DeterministicStatus.None;
        }
        internal bool ValidSnapshot(int slot, ulong lease) => IsCreated && (uint)slot < (uint)Data->Options.SnapshotSlots && lease != 0 && Data->Leases[slot] == lease;
        internal DeterministicStatus CanReserveSnapshotSlots(int slots)
        {
            if (!IsCreated) return DeterministicStatus.Disposed;
            if (BatchActive) return DeterministicStatus.Invalidated;
            if (slots < Data->Options.SnapshotSlots || (long)PageCount(Data->Options.MaxCapacity) * (slots + 1L) > int.MaxValue) return DeterministicStatus.InvalidConfiguration;
            return DeterministicStatus.None;
        }
        /// <summary>Host-only expansion. Header and page addresses are preserved for existing snapshots.</summary>
        internal DeterministicStatus TryReserveSnapshotSlots(int slots)
        {
            var status = CanReserveSnapshotSlots(slots); if (!status.IsSuccess() || slots == Data->Options.SnapshotSlots) return status;
            if (Paged && Data->Options.Allocation == AllocationPolicy.Grow) return ReserveGrowSnapshotSlots(slots);
            int oldSlots = Data->Options.SnapshotSlots, poolCapacity = (int)((long)Data->MaxPages * (slots + 1L));
            int* capacities = null; ulong* leases = null; int* snapshotPages = null; T* snapshotValues = null;
            T** growSnapshotValues = null; int* growSnapshotCapacities = null; Page* pool = null; int* freePages = null;
            try
            {
                capacities = (int*)Allocate((long)slots * sizeof(int), 16, Data->Allocator);
                leases = (ulong*)Allocate((long)slots * sizeof(ulong), 16, Data->Allocator);
                if (oldSlots != 0) { UnsafeUtility.MemCpy(capacities, Data->SnapshotCapacities, (long)oldSlots * sizeof(int)); UnsafeUtility.MemCpy(leases, Data->Leases, (long)oldSlots * sizeof(ulong)); }
                if (Paged)
                {
                    snapshotPages = (int*)Allocate((long)Data->MaxPages * slots * sizeof(int), 16, Data->Allocator);
                    pool = (Page*)Allocate((long)poolCapacity * sizeof(Page), 16, Data->Allocator);
                    freePages = (int*)Allocate((long)poolCapacity * sizeof(int), 16, Data->Allocator);
                    if (Data->MaxPages != 0 && oldSlots != 0) UnsafeUtility.MemCpy(snapshotPages, Data->SnapshotPages, (long)Data->MaxPages * oldSlots * sizeof(int));
                    if (Data->AllocatedPages != 0) UnsafeUtility.MemCpy(pool, Data->Pool, (long)Data->AllocatedPages * sizeof(Page));
                    if (Data->FreeCount != 0) UnsafeUtility.MemCpy(freePages, Data->FreePages, (long)Data->FreeCount * sizeof(int));
                    int allocated = Data->AllocatedPages, count = Data->FreeCount;
                    try
                    {
                        if (Data->Options.Allocation == AllocationPolicy.Fixed)
                            while (allocated < poolCapacity)
                            { pool[allocated].Values = (T*)Allocate((long)Data->PageElements * sizeof(T), Math.Max(16, UnsafeUtility.AlignOf<T>()), Data->Allocator); freePages[count++] = allocated++; }
                    }
                    catch { for (int i = Data->AllocatedPages; i < allocated; i++) Free(pool[i].Values, Data->Allocator); throw; }
                    Free(Data->SnapshotPages, Data->Allocator); Free(Data->Pool, Data->Allocator); Free(Data->FreePages, Data->Allocator);
                    Data->SnapshotPages = snapshotPages; Data->Pool = pool; Data->FreePages = freePages; Data->PoolCapacity = poolCapacity; Data->AllocatedPages = allocated; Data->FreeCount = count;
                }
                else if (Data->Options.Allocation == AllocationPolicy.Fixed)
                {
                    snapshotValues = (T*)Allocate((long)Data->Options.MaxCapacity * slots * sizeof(T), Math.Max(16, UnsafeUtility.AlignOf<T>()), Data->Allocator);
                    if (Data->Options.MaxCapacity != 0 && oldSlots != 0) UnsafeUtility.MemCpy(snapshotValues, Data->SnapshotValues, (long)Data->Options.MaxCapacity * oldSlots * sizeof(T));
                    Free(Data->SnapshotValues, Data->Allocator); Data->SnapshotValues = snapshotValues;
                }
                else
                {
                    growSnapshotValues = (T**)Allocate((long)slots * sizeof(T*), 16, Data->Allocator);
                    growSnapshotCapacities = (int*)Allocate((long)slots * sizeof(int), 16, Data->Allocator);
                    if (oldSlots != 0)
                    {
                        UnsafeUtility.MemCpy(growSnapshotValues, Data->GrowSnapshotValues, (long)oldSlots * sizeof(T*));
                        UnsafeUtility.MemCpy(growSnapshotCapacities, Data->GrowSnapshotCapacities, (long)oldSlots * sizeof(int));
                    }
                    Free(Data->GrowSnapshotValues, Data->Allocator); Free(Data->GrowSnapshotCapacities, Data->Allocator);
                    Data->GrowSnapshotValues = growSnapshotValues; Data->GrowSnapshotCapacities = growSnapshotCapacities;
                }
                Free(Data->SnapshotCapacities, Data->Allocator); Free(Data->Leases, Data->Allocator);
                Data->SnapshotCapacities = capacities; Data->Leases = leases;
                var o = Data->Options; Data->Options = new BufferOptions(o.InitialCapacity, o.MaxCapacity, o.Allocation, o.Snapshots, slots, o.PageBytes);
                return DeterministicStatus.None;
            }
            catch { Free(capacities, Data->Allocator); Free(leases, Data->Allocator); Free(snapshotPages, Data->Allocator); Free(snapshotValues, Data->Allocator); Free(growSnapshotValues, Data->Allocator); Free(growSnapshotCapacities, Data->Allocator); Free(pool, Data->Allocator); Free(freePages, Data->Allocator); throw; }
        }
        private DeterministicStatus ReserveGrowSnapshotSlots(int slots)
        {
            int* capacities = null; ulong* leases = null;
            try
            {
                capacities = (int*)Allocate((long)slots * sizeof(int), 16, Data->Allocator);
                leases = (ulong*)Allocate((long)slots * sizeof(ulong), 16, Data->Allocator);
                if (Data->Options.SnapshotSlots != 0)
                {
                    UnsafeUtility.MemCpy(capacities, Data->SnapshotCapacities, (long)Data->Options.SnapshotSlots * sizeof(int));
                    UnsafeUtility.MemCpy(leases, Data->Leases, (long)Data->Options.SnapshotSlots * sizeof(ulong));
                }
                GrowDirectory(Data->MaxPages, slots);
            }
            catch { Free(capacities, Data->Allocator); Free(leases, Data->Allocator); throw; }
            Free(Data->SnapshotCapacities, Data->Allocator); Free(Data->Leases, Data->Allocator);
            Data->SnapshotCapacities = capacities; Data->Leases = leases;
            var o = Data->Options; Data->Options = new BufferOptions(o.InitialCapacity, o.MaxCapacity, o.Allocation, o.Snapshots, slots, o.PageBytes);
            return DeterministicStatus.None;
        }
        private T* SnapshotPointer(int slot) => Data->Options.Allocation == AllocationPolicy.Fixed ?
            Data->SnapshotValues + (long)slot * Data->Options.MaxCapacity : Data->GrowSnapshotValues[slot];
        internal T ReadSnapshot(int slot, int index) => Paged ?
            Data->Pool[Data->SnapshotPages[(long)slot * Data->MaxPages + index / Data->PageElements]].Values[index % Data->PageElements] :
            SnapshotPointer(slot)[index];
        // Internal use only: validate the lease and consume the reference before release/restore.
        internal ref readonly T ReadSnapshotReference(int slot, int index)
        {
            if (Paged) return ref Data->Pool[Data->SnapshotPages[(long)slot * Data->MaxPages + index / Data->PageElements]].Values[index % Data->PageElements];
            return ref SnapshotPointer(slot)[index];
        }
        internal DeterministicStatus TryRestore(int slot, ulong lease)
        {
            if (BatchActive) return DeterministicStatus.Invalidated;
            if (!ValidSnapshot(slot, lease)) return DeterministicStatus.Disposed;
            int restored = Data->SnapshotCapacities[slot];
            if (Paged)
            {
                for (int i = 0; i < PageCount(Capacity); i++) ReleasePage(Data->Pages[i]);
                for (int i = 0; i < PageCount(restored); i++)
                { int id = Data->SnapshotPages[(long)slot * Data->MaxPages + i]; Data->Pool[id].References++; Data->Pages[i] = id; }
            }
            else if (restored != 0) UnsafeUtility.MemCpy(Data->Values, SnapshotPointer(slot), (long)restored * sizeof(T));
            Data->Capacity = restored; CommitWrite(); return DeterministicStatus.None;
        }
        internal void ReleaseSnapshot(int slot, ulong lease)
        {
            if (!ValidSnapshot(slot, lease)) return;
            if (Paged)
                for (int i = 0; i < PageCount(Data->SnapshotCapacities[slot]); i++) ReleasePage(Data->SnapshotPages[(long)slot * Data->MaxPages + i]);
            Data->Leases[slot] = 0;
        }
        // Keep the shared header alive: copied Native owners alias it after a job releases its scratch.
        internal void ReleaseUnpagedWorkspace()
        {
            if (Data == null || Paged || Data->Options.SnapshotSlots != 0) return;
            Free(Data->Values, Data->Allocator); Data->Values = null;
            Data->Capacity = Data->PhysicalCapacity = 0; Data->Revision++;
        }
        internal void Destroy()
        {
            if (Data == null) return;
            if (BatchActive) DeterministicStatus.Invalidated.ThrowIfFailure();
            var allocator = Data->Allocator;
            if (Data->Pool != null) for (int i = 0; i < Data->AllocatedPages; i++) Free(Data->Pool[i].Values, allocator);
            if (Data->GrowSnapshotValues != null)
                for (int i = 0; i < Data->Options.SnapshotSlots; i++) Free(Data->GrowSnapshotValues[i], allocator);
            Free(Data->Values, allocator); Free(Data->SnapshotValues, allocator); FreeDirectory();
            Free(Data->GrowSnapshotValues, allocator); Free(Data->GrowSnapshotCapacities, allocator);
            Free(Data->SnapshotCapacities, allocator); Free(Data->Leases, allocator); Free(Data->BatchClaims, allocator); Free(Data, allocator); Data = null;
        }

        internal DeterministicStatus TryBeginBatch(int length, int partitions, out ulong lease)
        {
            lease = 0;
            if (!IsCreated) return DeterministicStatus.Disposed;
            if (BatchActive) return DeterministicStatus.ProtocolConflict;
            if (length < 0 || length > Capacity || partitions <= 0) return DeterministicStatus.OutOfRange;
            if (Data->BatchLease == ulong.MaxValue) return DeterministicStatus.GenerationExhausted;
            // Allocation and COW happen exclusively on the host, before any worker receives a view.
            if (partitions > Data->BatchClaimCapacity)
            {
                int* next = (int*)Allocate((long)partitions * sizeof(int), 16, Data->Allocator);
                Free(Data->BatchClaims, Data->Allocator); Data->BatchClaims = next; Data->BatchClaimCapacity = partitions;
            }
            var status = PrepareWrite(Capacity, 0, length); if (!status.IsSuccess()) return status;
            UnsafeUtility.MemClear(Data->BatchClaims, (long)partitions * sizeof(int));
            Data->BatchPartitions = partitions; lease = ++Data->BatchLease; Data->BatchActive = 1;
            return DeterministicStatus.None;
        }
        internal bool ValidBatch(ulong lease) => BatchActive && lease != 0 && Data->BatchLease == lease;
        internal DeterministicStatus TryClaimBatch(ulong lease, int partition, out int thread)
        {
            thread = 0;
            if (!ValidBatch(lease)) return DeterministicStatus.Invalidated;
            if ((uint)partition >= (uint)Data->BatchPartitions) return DeterministicStatus.OutOfRange;
            thread = NativeBatchThread.Current();
            return System.Threading.Interlocked.CompareExchange(ref Data->BatchClaims[partition], thread, 0) == 0
                ? DeterministicStatus.None : DeterministicStatus.ProtocolConflict;
        }
        internal bool ValidBatchPartition(ulong lease, int partition, int thread)
            => ValidBatch(lease) && (uint)partition < (uint)Data->BatchPartitions && thread > 0 && Data->BatchClaims[partition] == thread;
        internal DeterministicStatus TryReleaseBatch(ulong lease, int partition, int thread)
        {
            if (!ValidBatchPartition(lease, partition, thread)) return DeterministicStatus.Invalidated;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (thread != NativeBatchThread.Current()) return DeterministicStatus.InvalidOwner;
#endif
            return System.Threading.Interlocked.CompareExchange(ref Data->BatchClaims[partition], -1, thread) == thread
                ? DeterministicStatus.None : DeterministicStatus.Invalidated;
        }
        internal DeterministicStatus TryCompleteBatch(ulong lease)
        {
            if (!ValidBatch(lease)) return DeterministicStatus.Invalidated;
            for (int i = 0; i < Data->BatchPartitions; i++) if (Data->BatchClaims[i] > 0) return DeterministicStatus.Pending;
            Data->BatchActive = 0; CommitWrite(); return DeterministicStatus.None;
        }
    }
}
