using System;
using System.Collections;
using System.Collections.Generic;

namespace LegendaryTools.Deterministic
{
    public readonly struct ManagedSparseSetSnapshot<T> : IDisposable where T : unmanaged
    {
        internal readonly ManagedBufferSnapshot<SparseSetEntry<T>> Dense;
        internal readonly ManagedBufferSnapshot<SparseSetIndex> Sparse;
        internal readonly SequenceState State;
        internal ManagedSparseSetSnapshot(ManagedBufferSnapshot<SparseSetEntry<T>> dense, ManagedBufferSnapshot<SparseSetIndex> sparse, SequenceState state)
        { Dense = dense; Sparse = sparse; State = state; }
        public bool IsCreated => Dense.IsCreated && Sparse.IsCreated;
        public int Count => State.Count;
        public DeterministicStatus TryGet(StableHandle handle, out T value)
        {
            value = default;
            if (!IsCreated) return DeterministicStatus.Disposed;
            if (!handle.IsValid || handle.Index >= (uint)Sparse.Capacity) return DeterministicStatus.NotFound;
            Sparse.TryGet((int)handle.Index, out var cell);
            if (cell.DenseIndex == 0 || cell.Generation != handle.Generation) return DeterministicStatus.NotFound;
            Dense.TryGet(cell.DenseIndex - 1, out var item); value = item.Value; return DeterministicStatus.None;
        }
        public void Dispose() { Dense.Dispose(); Sparse.Dispose(); }
    }

    /// <summary>Components addressed by portable generational IDs; no dependency on Unity.Entities.</summary>
    public sealed partial class DeterministicSparseSet<T> : IEnumerable<SparseSetEntry<T>>, IDisposable where T : unmanaged
    {
        internal ManagedStorage<SparseSetEntry<T>> Dense;
        internal ManagedStorage<SparseSetIndex> Sparse;
        internal SequenceState State;
        private readonly int maxEntitySlots;
        public DeterministicSparseSet(int componentCapacity, int entitySlots) : this(new BufferOptions(componentCapacity), new BufferOptions(entitySlots)) { }
        public DeterministicSparseSet(BufferOptions componentOptions, BufferOptions entityOptions)
        {
            if (!componentOptions.IsValid || !entityOptions.IsValid || componentOptions.SnapshotSlots != entityOptions.SnapshotSlots || componentOptions.Snapshots != entityOptions.Snapshots)
                DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            maxEntitySlots = entityOptions.MaxCapacity;
            Dense = new ManagedStorage<SparseSetEntry<T>>(componentOptions);
            try { Sparse = new ManagedStorage<SparseSetIndex>(entityOptions); } catch { Dense.Dispose(); throw; }
        }
        public bool IsCreated => Dense.IsCreated && Sparse.IsCreated;
        public int Count => State.Count;
        public int Capacity => Dense.Capacity;
        public int EntitySlotCapacity => Sparse.Capacity;
        public ulong Revision => Dense.Revision;
        public T this[StableHandle handle]
        { get { TryGet(handle, out T value).ThrowIfFailure(); return value; } set { TrySet(handle, value).ThrowIfFailure(); } }
        public DeterministicStatus TryAdd(StableHandle handle, in T value) => SparseSetKernel<T, ManagedStorage<SparseSetEntry<T>>, ManagedStorage<SparseSetIndex>>.TryPut(ref Dense, ref Sparse, ref State, maxEntitySlots, handle, value, true);
        public void Add(StableHandle handle, T value) => TryAdd(handle, value).ThrowIfFailure();
        public DeterministicStatus TrySet(StableHandle handle, in T value) => SparseSetKernel<T, ManagedStorage<SparseSetEntry<T>>, ManagedStorage<SparseSetIndex>>.TryPut(ref Dense, ref Sparse, ref State, maxEntitySlots, handle, value, false);
        public DeterministicStatus TryGet(StableHandle handle, out T value) => SparseSetKernel<T, ManagedStorage<SparseSetEntry<T>>, ManagedStorage<SparseSetIndex>>.TryGet(ref Dense, ref Sparse, handle, out value);
        public bool TryGetValue(StableHandle handle, out T value) => TryGet(handle, out value).IsSuccess();
        public bool ContainsKey(StableHandle handle) => TryGetValue(handle, out _);
        public DeterministicStatus TryRemove(StableHandle handle) => SparseSetKernel<T, ManagedStorage<SparseSetEntry<T>>, ManagedStorage<SparseSetIndex>>.TryRemove(ref Dense, ref Sparse, ref State, handle);
        public bool Remove(StableHandle handle) { var status = TryRemove(handle); if (status == DeterministicStatus.NotFound) return false; status.ThrowIfFailure(); return true; }
        public DeterministicStatus TryClear() => SparseSetKernel<T, ManagedStorage<SparseSetEntry<T>>, ManagedStorage<SparseSetIndex>>.TryClear(ref Dense, ref Sparse, ref State);
        public void Clear() => TryClear().ThrowIfFailure();
        public DeterministicStatus TryCapture(out ManagedSparseSetSnapshot<T> snapshot)
        {
            snapshot = default; var status = Dense.TryCapture(out var dense); if (!status.IsSuccess()) return status;
            status = Sparse.TryCapture(out var sparse); if (!status.IsSuccess()) { dense.Dispose(); return status; }
            snapshot = new ManagedSparseSetSnapshot<T>(dense, sparse, State); return DeterministicStatus.None;
        }
        public DeterministicStatus TryRestore(in ManagedSparseSetSnapshot<T> snapshot)
        {
            if (!IsCreated) return DeterministicStatus.Disposed;
            if (snapshot.Dense.Owner != Dense.Owner || snapshot.Sparse.Owner != Sparse.Owner) return DeterministicStatus.InvalidOwner;
            if (!snapshot.IsCreated) return DeterministicStatus.Disposed;
            Dense.TryRestore(snapshot.Dense); Sparse.TryRestore(snapshot.Sparse); State = snapshot.State; return DeterministicStatus.None;
        }
        public Enumerator GetEnumerator() => new Enumerator(this, false);
        public SortedView OrderedByEntity => new SortedView(this);
        IEnumerator<SparseSetEntry<T>> IEnumerable<SparseSetEntry<T>>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void Dispose() { Dense.Dispose(); Sparse.Dispose(); State = default; }
        public readonly struct SortedView
        {
            private readonly DeterministicSparseSet<T> owner;
            internal SortedView(DeterministicSparseSet<T> owner) { this.owner = owner; }
            public Enumerator GetEnumerator() => new Enumerator(owner, true);
        }
        public struct Enumerator : IEnumerator<SparseSetEntry<T>>
        {
            private readonly DeterministicSparseSet<T> owner;
            private readonly ulong revision;
            private readonly bool sorted;
            private int index;
            private SparseSetEntry<T> current;
            internal Enumerator(DeterministicSparseSet<T> owner, bool sorted) { this.owner = owner; revision = owner.Revision; this.sorted = sorted; index = -1; current = default; }
            public SparseSetEntry<T> Current => current;
            object IEnumerator.Current => Current;
            public DeterministicStatus TryMoveNext(out bool moved)
            {
                moved = false; if (!owner.IsCreated) return DeterministicStatus.Disposed;
                if (owner.Revision != revision) return DeterministicStatus.Invalidated;
                int end = sorted ? owner.Sparse.Capacity : owner.Count;
                if (index >= end) return DeterministicStatus.None;
                while (++index < end)
                {
                    int denseIndex = sorted ? owner.Sparse.Read(index).DenseIndex - 1 : index;
                    if (denseIndex < 0) continue;
                    current = owner.Dense.Read(denseIndex); moved = true; break;
                }
                return DeterministicStatus.None;
            }
            public bool MoveNext() { TryMoveNext(out bool moved).ThrowIfFailure(); return moved; }
            public void Reset() { if (!owner.IsCreated) DeterministicStatus.Disposed.ThrowIfFailure(); if (owner.Revision != revision) DeterministicStatus.Invalidated.ThrowIfFailure(); index = -1; }
            public void Dispose() { }
        }
    }
}
