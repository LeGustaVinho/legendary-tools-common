using System;
using System.Collections;
using System.Collections.Generic;

namespace LegendaryTools.Deterministic
{
    public readonly struct ManagedSlotMapSnapshot<T> : IDisposable where T : unmanaged
    {
        internal readonly ManagedBufferSnapshot<SlotMapNode<T>> Buffer;
        internal readonly SlotMapState State;
        internal ManagedSlotMapSnapshot(ManagedBufferSnapshot<SlotMapNode<T>> buffer, SlotMapState state) { Buffer = buffer; State = state; }
        public bool IsCreated => Buffer.IsCreated;
        public int Count => State.Count;
        public int Capacity => Buffer.Capacity;
        public DeterministicStatus TryGet(StableHandle handle, out T value)
        {
            value = default;
            if (!IsCreated) return DeterministicStatus.Disposed;
            if (!handle.IsValid || handle.Index >= (uint)State.Used) return DeterministicStatus.NotFound;
            var status = Buffer.TryGet((int)handle.Index, out var node); if (!status.IsSuccess()) return status;
            if (node.Active == 0 || node.Generation != handle.Generation) return DeterministicStatus.NotFound;
            value = node.Value; return DeterministicStatus.None;
        }
        public void Dispose() => Buffer.Dispose();
    }

    public sealed partial class DeterministicSlotMap<T> : IEnumerable<SlotMapItem<T>>, IDisposable where T : unmanaged
    {
        internal ManagedStorage<SlotMapNode<T>> Storage;
        internal SlotMapState State;
        public DeterministicSlotMap(int capacity) : this(new BufferOptions(capacity)) { }
        public DeterministicSlotMap(BufferOptions options) { Storage = new ManagedStorage<SlotMapNode<T>>(options); }
        public int Count => State.Count;
        public int Capacity => Storage.Capacity;
        public bool IsCreated => Storage.IsCreated;
        public ulong Revision => Storage.Revision;
        public T this[StableHandle handle]
        {
            get { TryGet(handle, out T value).ThrowIfFailure(); return value; }
            set { TrySet(handle, value).ThrowIfFailure(); }
        }
        public DeterministicStatus TryAdd(in T value, out StableHandle handle) => SlotMapKernel<T, ManagedStorage<SlotMapNode<T>>>.TryAdd(ref Storage, ref State, value, out handle);
        public StableHandle Add(T value) { TryAdd(value, out var handle).ThrowIfFailure(); return handle; }
        public DeterministicStatus TryGet(StableHandle handle, out T value) => SlotMapKernel<T, ManagedStorage<SlotMapNode<T>>>.TryGet(ref Storage, State, handle, out value);
        public bool TryGetValue(StableHandle handle, out T value) => TryGet(handle, out value).IsSuccess();
        public bool ContainsKey(StableHandle handle) => TryGetValue(handle, out _);
        public DeterministicStatus TrySet(StableHandle handle, in T value) => SlotMapKernel<T, ManagedStorage<SlotMapNode<T>>>.TrySet(ref Storage, State, handle, value);
        public DeterministicStatus TryRemove(StableHandle handle) => SlotMapKernel<T, ManagedStorage<SlotMapNode<T>>>.TryRemove(ref Storage, ref State, handle);
        public bool Remove(StableHandle handle)
        { var status = TryRemove(handle); if (status == DeterministicStatus.NotFound) return false; status.ThrowIfFailure(); return true; }
        public DeterministicStatus TryClear() => SlotMapKernel<T, ManagedStorage<SlotMapNode<T>>>.TryClear(ref Storage, ref State);
        public void Clear() => TryClear().ThrowIfFailure();
        public DeterministicStatus TryCapture(out ManagedSlotMapSnapshot<T> snapshot)
        { var status = Storage.TryCapture(out var buffer); snapshot = new ManagedSlotMapSnapshot<T>(buffer, State); return status; }
        public DeterministicStatus TryRestore(in ManagedSlotMapSnapshot<T> snapshot)
        { var status = Storage.TryRestore(snapshot.Buffer); if (status.IsSuccess()) State = snapshot.State; return status; }
        public Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<SlotMapItem<T>> IEnumerable<SlotMapItem<T>>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void Dispose() { Storage.Dispose(); State = default; }
        public struct Enumerator : IEnumerator<SlotMapItem<T>>
        {
            private readonly DeterministicSlotMap<T> owner;
            private readonly ulong revision;
            private int index;
            private SlotMapItem<T> current;
            internal Enumerator(DeterministicSlotMap<T> owner) { this.owner = owner; revision = owner.Revision; index = -1; current = default; }
            public SlotMapItem<T> Current => index < 0 || index >= owner.State.Used ? throw new InvalidOperationException() : current;
            object IEnumerator.Current => Current;
            public DeterministicStatus TryMoveNext(out bool moved)
            {
                moved = false;
                if (!owner.IsCreated) return DeterministicStatus.Disposed;
                if (owner.Revision != revision) return DeterministicStatus.Invalidated;
                if (index >= owner.State.Used) return DeterministicStatus.None;
                while (++index < owner.State.Used)
                {
                    var node = owner.Storage.Read(index); if (node.Active == 0) continue;
                    current = new SlotMapItem<T>(new StableHandle((uint)index, node.Generation), node.Value); moved = true; break;
                }
                return DeterministicStatus.None;
            }
            public bool MoveNext() { TryMoveNext(out bool moved).ThrowIfFailure(); return moved; }
            public void Reset()
            { if (!owner.IsCreated) DeterministicStatus.Disposed.ThrowIfFailure(); if (owner.Revision != revision) DeterministicStatus.Invalidated.ThrowIfFailure(); index = -1; }
            public void Dispose() { }
        }
    }
}
