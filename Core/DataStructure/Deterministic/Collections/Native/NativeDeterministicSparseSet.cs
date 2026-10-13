using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace LegendaryTools.Deterministic
{
    [NativeContainer, NativeContainerIsReadOnly]
    public unsafe struct NativeSparseSetSnapshot<T> : IDisposable where T : unmanaged
    {
        internal NativeStorage<SparseSetEntry<T>> Dense;
        internal NativeStorage<SparseSetIndex> Sparse;
        internal SequenceState State;
        internal int DenseSlot, SparseSlot;
        internal ulong DenseLease, SparseLease;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif
        public bool IsCreated
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (Dense.IsCreated) AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                return Dense.ValidSnapshot(DenseSlot, DenseLease) && Sparse.ValidSnapshot(SparseSlot, SparseLease);
            }
        }
        public int Count => State.Count;
        public DeterministicStatus TryGet(StableHandle handle, out T value)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Dense.IsCreated) AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            value = default;
            if (!IsCreated) return DeterministicStatus.Disposed;
            if (!handle.IsValid || handle.Index >= (uint)Sparse.Data->SnapshotCapacities[SparseSlot]) return DeterministicStatus.NotFound;
            var cell = Sparse.ReadSnapshot(SparseSlot, (int)handle.Index);
            if (cell.DenseIndex == 0 || cell.Generation != handle.Generation) return DeterministicStatus.NotFound;
            value = Dense.ReadSnapshot(DenseSlot, cell.DenseIndex - 1).Value; return DeterministicStatus.None;
        }
        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Dense.IsCreated) AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            Dense.ReleaseSnapshot(DenseSlot, DenseLease); Sparse.ReleaseSnapshot(SparseSlot, SparseLease); Dense = default; Sparse = default;
        }
    }
    [NativeContainer]
    public unsafe partial struct NativeDeterministicSparseSet<T> : IDisposable where T : unmanaged
    {
        internal NativeStorage<SparseSetEntry<T>> Dense;
        internal NativeStorage<SparseSetIndex> Sparse;
        private int maxEntitySlots;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif
        public NativeDeterministicSparseSet(int componentCapacity, int entitySlots, Allocator allocator) : this(new BufferOptions(componentCapacity), new BufferOptions(entitySlots), allocator) { }
        public NativeDeterministicSparseSet(BufferOptions componentOptions, BufferOptions entityOptions, Allocator allocator)
        {
            this = default;
            if (!componentOptions.IsValid || !entityOptions.IsValid || componentOptions.SnapshotSlots != entityOptions.SnapshotSlots || componentOptions.Snapshots != entityOptions.Snapshots)
                DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            maxEntitySlots = entityOptions.MaxCapacity;
            Dense = NativeStorage<SparseSetEntry<T>>.Create(componentOptions, allocator);
            try { Sparse = NativeStorage<SparseSetIndex>.Create(entityOptions, allocator); } catch { Dense.Destroy(); throw; }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.Create(); AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);
#endif
        }
        public bool IsCreated => Dense.IsCreated && Sparse.IsCreated;
        public int Count { get { CheckRead(); return IsCreated ? Dense.Data->State.Count : 0; } }
        public int Capacity { get { CheckRead(); return Dense.Capacity; } }
        /// <summary>Read physical dense storage without an enumerator. Authoritative ordering needs a logical key.</summary>
        public DeterministicStatus TryGetDenseAt(int index, out SparseSetEntry<T> item)
        {
            CheckRead(); item = default;
            if (!IsCreated) return DeterministicStatus.Disposed;
            if (Dense.BatchActive) return DeterministicStatus.Invalidated;
            if ((uint)index >= (uint)Dense.Data->State.Count) return DeterministicStatus.OutOfRange;
            item = Dense.Read(index); return DeterministicStatus.None;
        }
        public int EntitySlotCapacity { get { CheckRead(); return Sparse.Capacity; } }
        /// <summary>Read a logical entity slot in O(1), without knowing its current generation.</summary>
        public DeterministicStatus TryGetSlot(int slot, out SparseSetEntry<T> item)
        {
            CheckRead(); item = default;
            if (!IsCreated) return DeterministicStatus.Disposed;
            if (Dense.BatchActive) return DeterministicStatus.Invalidated;
            if ((uint)slot >= (uint)Sparse.Capacity) return DeterministicStatus.OutOfRange;
            var index = Sparse.Read(slot);
            if (index.DenseIndex == 0) return DeterministicStatus.NotFound;
            item = Dense.Read(index.DenseIndex - 1); return DeterministicStatus.None;
        }
        public T this[StableHandle handle]
        { get { var status = TryGet(handle, out T value); NativeChecks.Status(status); return value; } set => NativeChecks.Status(TrySet(handle, value)); }
        public DeterministicStatus TryAdd(StableHandle handle, in T value)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            return SparseSetKernel<T, NativeStorage<SparseSetEntry<T>>, NativeStorage<SparseSetIndex>>.TryPut(ref Dense, ref Sparse, ref Dense.Data->State, maxEntitySlots, handle, value, true);
        }
        public void Add(StableHandle handle, T value) => NativeChecks.Status(TryAdd(handle, value));
        public DeterministicStatus TrySet(StableHandle handle, in T value)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            return SparseSetKernel<T, NativeStorage<SparseSetEntry<T>>, NativeStorage<SparseSetIndex>>.TryPut(ref Dense, ref Sparse, ref Dense.Data->State, maxEntitySlots, handle, value, false);
        }
        public DeterministicStatus TryGet(StableHandle handle, out T value)
        {
            CheckRead(); value = default; if (!IsCreated) return DeterministicStatus.Disposed;
            if (Dense.BatchActive) return DeterministicStatus.Invalidated;
            return SparseSetKernel<T, NativeStorage<SparseSetEntry<T>>, NativeStorage<SparseSetIndex>>.TryGet(ref Dense, ref Sparse, handle, out value);
        }
        public bool TryGetValue(StableHandle handle, out T value) => TryGet(handle, out value).IsSuccess();
        public bool ContainsKey(StableHandle handle)
        {
            CheckRead();
            if (!IsCreated || Dense.BatchActive) return false;
            return SparseSetKernel<T, NativeStorage<SparseSetEntry<T>>, NativeStorage<SparseSetIndex>>.ContainsKey(ref Dense, ref Sparse, handle);
        }
        public DeterministicStatus TryRemove(StableHandle handle)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            return SparseSetKernel<T, NativeStorage<SparseSetEntry<T>>, NativeStorage<SparseSetIndex>>.TryRemove(ref Dense, ref Sparse, ref Dense.Data->State, handle);
        }
        public bool Remove(StableHandle handle)
        { var status = TryRemove(handle); if (status == DeterministicStatus.NotFound) return false; NativeChecks.Status(status); return status.IsSuccess(); }
        public DeterministicStatus TryClear()
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            return SparseSetKernel<T, NativeStorage<SparseSetEntry<T>>, NativeStorage<SparseSetIndex>>.TryClear(ref Dense, ref Sparse, ref Dense.Data->State);
        }
        public void Clear() => NativeChecks.Status(TryClear());
        public DeterministicStatus TryCapture(out NativeSparseSetSnapshot<T> snapshot)
        {
            CheckWrite(); snapshot = default;
            var status = Dense.TryCapture(out int ds, out ulong dl); if (!status.IsSuccess()) return status;
            status = Sparse.TryCapture(out int ss, out ulong sl); if (!status.IsSuccess()) { Dense.ReleaseSnapshot(ds, dl); return status; }
            snapshot = new NativeSparseSetSnapshot<T> { Dense = Dense, Sparse = Sparse, State = Dense.Data->State, DenseSlot = ds, DenseLease = dl, SparseSlot = ss, SparseLease = sl };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            snapshot.m_Safety = m_Safety;
#endif
            return DeterministicStatus.None;
        }
        public DeterministicStatus TryRestore(in NativeSparseSetSnapshot<T> snapshot)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            if (Dense.BatchActive || Sparse.BatchActive) return DeterministicStatus.Invalidated;
            if (snapshot.Dense.Data != Dense.Data || snapshot.Sparse.Data != Sparse.Data) return DeterministicStatus.InvalidOwner;
            if (!snapshot.IsCreated) return DeterministicStatus.Disposed;
            var status = Dense.TryRestore(snapshot.DenseSlot, snapshot.DenseLease); if (!status.IsSuccess()) return status;
            status = Sparse.TryRestore(snapshot.SparseSlot, snapshot.SparseLease); if (!status.IsSuccess()) return status;
            Dense.Data->State = snapshot.State;
            return DeterministicStatus.None;
        }
        public Enumerator GetEnumerator() => CreateEnumerator(false);
        public SortedView OrderedByEntity => new SortedView { Owner = this };
        private Enumerator CreateEnumerator(bool sorted)
        {
            CheckRead(); int count = Count;
            var iterator = new Enumerator { Dense = Dense, Sparse = Sparse, Revision = Dense.Revision, Count = count, Sorted = sorted, Index = -1,
                DenseSelection = sorted && SparseSetKernel<T, NativeStorage<SparseSetEntry<T>>, NativeStorage<SparseSetIndex>>.PreferDenseOrdered(count, Sparse.Capacity) };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            iterator.m_Safety = m_Safety;
#endif
            return iterator;
        }
        public struct SortedView
        {
            internal NativeDeterministicSparseSet<T> Owner;
            public Enumerator GetEnumerator() => Owner.CreateEnumerator(true);
        }
        public void Dispose()
        {
            if (!IsCreated) return; CheckWrite();
            if (Dense.BatchActive || Sparse.BatchActive) DeterministicStatus.Invalidated.ThrowIfFailure();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(m_Safety);
#endif
            Dense.Destroy(); Sparse.Destroy();
        }
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void CheckRead()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (IsCreated) AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
        }
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void CheckWrite()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (IsCreated) AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
        }
        public struct Enumerator
        {
            internal NativeStorage<SparseSetEntry<T>> Dense;
            internal NativeStorage<SparseSetIndex> Sparse;
            internal ulong Revision;
            internal int Count, Index;
            internal bool Sorted;
            internal bool DenseSelection;
            private SparseSetEntry<T> current;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif
            public SparseSetEntry<T> Current => current;
            public DeterministicStatus TryMoveNext(out bool moved)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (Dense.IsCreated) AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                moved = false; if (!Dense.IsCreated) return DeterministicStatus.Disposed;
                if (Dense.BatchActive) return DeterministicStatus.Invalidated;
                if (Dense.Revision != Revision) return DeterministicStatus.Invalidated;
                if (DenseSelection)
                {
                    var status = SparseSetKernel<T, NativeStorage<SparseSetEntry<T>>, NativeStorage<SparseSetIndex>>.TryNextDenseOrdered(
                        ref Dense, Count, Sparse.Capacity, ref Index, out var item, out moved);
                    if (moved) current = item; return status;
                }
                int end = Sorted ? Sparse.Capacity : Count; if (Index >= end) return DeterministicStatus.None;
                while (++Index < end)
                {
                    int denseIndex = Sorted ? Sparse.Read(Index).DenseIndex - 1 : Index;
                    if (denseIndex < 0) continue;
                    current = Dense.Read(denseIndex); moved = true; break;
                }
                return DeterministicStatus.None;
            }
            public bool MoveNext() { var status = TryMoveNext(out bool moved); NativeChecks.Status(status); return moved; }
        }
    }
}
