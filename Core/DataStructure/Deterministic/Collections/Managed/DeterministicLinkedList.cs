using System;
using System.Collections;
using System.Collections.Generic;

namespace LegendaryTools.Deterministic
{
    public readonly struct ManagedLinkedListSnapshot<T> : IDisposable where T : unmanaged
    {
        internal readonly ManagedBufferSnapshot<SlotMapNode<LinkedListValue<T>>> Buffer;
        internal readonly SlotMapState Slots;
        internal readonly LinkedListState Links;
        internal ManagedLinkedListSnapshot(ManagedBufferSnapshot<SlotMapNode<LinkedListValue<T>>> buffer, SlotMapState slots, LinkedListState links) { Buffer = buffer; Slots = slots; Links = links; }
        public bool IsCreated => Buffer.IsCreated;
        public int Count => Slots.Count;
        public StableHandle First => Links.First;
        public StableHandle Last => Links.Last;
        public DeterministicStatus TryGet(StableHandle handle, out T value)
        {
            value = default; if (!IsCreated) return DeterministicStatus.Disposed;
            if (!handle.IsValid || handle.Index >= (uint)Slots.Used) return DeterministicStatus.NotFound;
            Buffer.TryGet((int)handle.Index, out var node); if (node.Active == 0 || node.Generation != handle.Generation) return DeterministicStatus.NotFound;
            value = node.Value.Value; return DeterministicStatus.None;
        }
        public void Dispose() => Buffer.Dispose();
    }
    /// <summary>Linked nodes are portable handles scoped to this collection, rather than managed node objects.</summary>
    public sealed partial class DeterministicLinkedList<T> : ICollection<T>, IReadOnlyCollection<T>, IDisposable where T : unmanaged
    {
        internal ManagedStorage<SlotMapNode<LinkedListValue<T>>> Storage;
        internal SlotMapState Slots;
        internal LinkedListState Links;
        private readonly IDeterministicEquality<T> equality;
        public DeterministicLinkedList(int capacity, IDeterministicEquality<T> equality = null) : this(new BufferOptions(capacity), equality) { }
        public DeterministicLinkedList(BufferOptions options, IDeterministicEquality<T> equality = null)
        { if (equality != null && !equality.IsValid) DeterministicStatus.InvalidConfiguration.ThrowIfFailure(); this.equality = equality; Storage = new ManagedStorage<SlotMapNode<LinkedListValue<T>>>(options); }
        public bool IsCreated => Storage.IsCreated;
        public int Count => Slots.Count;
        public int Capacity => Storage.Capacity;
        public bool IsReadOnly => false;
        public StableHandle First => Links.First;
        public StableHandle Last => Links.Last;
        public T this[StableHandle handle]
        { get { TryGet(handle, out T value).ThrowIfFailure(); return value; } set => TrySet(handle, value).ThrowIfFailure(); }
        private DeterministicStatus Insert(in T value, int position, StableHandle anchor, out StableHandle handle) => LinkedListKernel<T, ManagedStorage<SlotMapNode<LinkedListValue<T>>>>.TryAdd(ref Storage, ref Slots, ref Links, value, position, anchor, out handle);
        public DeterministicStatus TryAddFirst(in T value, out StableHandle handle) => Insert(value, 0, default, out handle);
        public DeterministicStatus TryAddLast(in T value, out StableHandle handle) => Insert(value, 1, default, out handle);
        public DeterministicStatus TryAddBefore(StableHandle node, in T value, out StableHandle handle) => Insert(value, 2, node, out handle);
        public DeterministicStatus TryAddAfter(StableHandle node, in T value, out StableHandle handle) => Insert(value, 3, node, out handle);
        public StableHandle AddFirst(T value) { TryAddFirst(value, out var handle).ThrowIfFailure(); return handle; }
        public StableHandle AddLast(T value) { TryAddLast(value, out var handle).ThrowIfFailure(); return handle; }
        public StableHandle AddBefore(StableHandle node, T value) { TryAddBefore(node, value, out var handle).ThrowIfFailure(); return handle; }
        public StableHandle AddAfter(StableHandle node, T value) { TryAddAfter(node, value, out var handle).ThrowIfFailure(); return handle; }
        void ICollection<T>.Add(T value) => AddLast(value);
        public DeterministicStatus TryGet(StableHandle handle, out T value) => LinkedListKernel<T, ManagedStorage<SlotMapNode<LinkedListValue<T>>>>.TryGet(ref Storage, Slots, handle, out value);
        public DeterministicStatus TrySet(StableHandle handle, in T value) => LinkedListKernel<T, ManagedStorage<SlotMapNode<LinkedListValue<T>>>>.TrySet(ref Storage, Slots, handle, value);
        public DeterministicStatus TryGetLinks(StableHandle handle, out StableHandle previous, out StableHandle next)
        { var status = SlotMapKernel<LinkedListValue<T>, ManagedStorage<SlotMapNode<LinkedListValue<T>>>>.TryGet(ref Storage, Slots, handle, out var node); previous = node.Previous; next = node.Next; return status; }
        public DeterministicStatus TryRemove(StableHandle handle) => LinkedListKernel<T, ManagedStorage<SlotMapNode<LinkedListValue<T>>>>.TryRemove(ref Storage, ref Slots, ref Links, handle);
        public void Remove(StableHandle handle) => TryRemove(handle).ThrowIfFailure();
        public void RemoveFirst() { if (Count == 0) DeterministicStatus.Empty.ThrowIfFailure(); Remove(First); }
        public void RemoveLast() { if (Count == 0) DeterministicStatus.Empty.ThrowIfFailure(); Remove(Last); }
        public StableHandle Find(T value)
        {
            if (!IsCreated) DeterministicStatus.Disposed.ThrowIfFailure();
            var primitive = default(PrimitiveEquality<T>); if (equality == null && !primitive.IsValid) DeterministicStatus.UnsupportedType.ThrowIfFailure();
            for (var current = First; current.IsValid; current = Storage.Read((int)current.Index).Value.Next)
            { var item = Storage.Read((int)current.Index).Value.Value; if (equality == null ? primitive.Equals(item, value) : equality.Equals(item, value)) return current; }
            return default;
        }
        public bool Contains(T value) => Find(value).IsValid;
        bool ICollection<T>.Remove(T value) { var handle = Find(value); if (!handle.IsValid) return false; Remove(handle); return true; }
        public DeterministicStatus TryClear() => LinkedListKernel<T, ManagedStorage<SlotMapNode<LinkedListValue<T>>>>.TryClear(ref Storage, ref Slots, ref Links);
        public void Clear() => TryClear().ThrowIfFailure();
        public void CopyTo(T[] array, int index)
        { if (array == null) throw new ArgumentNullException(nameof(array)); if (index < 0 || index > array.Length || Count > array.Length - index) throw new ArgumentOutOfRangeException(nameof(index)); foreach (var item in this) array[index++] = item; }
        public DeterministicStatus TryCapture(out ManagedLinkedListSnapshot<T> snapshot)
        { var status = Storage.TryCapture(out var buffer); snapshot = new ManagedLinkedListSnapshot<T>(buffer, Slots, Links); return status; }
        public DeterministicStatus TryRestore(in ManagedLinkedListSnapshot<T> snapshot)
        { var status = Storage.TryRestore(snapshot.Buffer); if (status.IsSuccess()) { Slots = snapshot.Slots; Links = snapshot.Links; } return status; }
        public Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void Dispose() { Storage.Dispose(); Slots = default; Links = default; }
        public struct Enumerator : IEnumerator<T>
        {
            private readonly DeterministicLinkedList<T> owner;
            private readonly ulong revision;
            private StableHandle next;
            private T current;
            internal Enumerator(DeterministicLinkedList<T> owner) { this.owner = owner; revision = owner.Storage.Revision; next = owner.First; current = default; }
            public T Current => current;
            object IEnumerator.Current => Current;
            public DeterministicStatus TryMoveNext(out bool moved)
            {
                moved = false; if (!owner.IsCreated) return DeterministicStatus.Disposed;
                if (owner.Storage.Revision != revision) return DeterministicStatus.Invalidated;
                if (!next.IsValid) return DeterministicStatus.None;
                var node = owner.Storage.Read((int)next.Index); current = node.Value.Value; next = node.Value.Next; moved = true; return DeterministicStatus.None;
            }
            public bool MoveNext() { TryMoveNext(out bool moved).ThrowIfFailure(); return moved; }
            public void Reset() { if (!owner.IsCreated) DeterministicStatus.Disposed.ThrowIfFailure(); if (owner.Storage.Revision != revision) DeterministicStatus.Invalidated.ThrowIfFailure(); next = owner.First; }
            public void Dispose() { }
        }
    }
}
