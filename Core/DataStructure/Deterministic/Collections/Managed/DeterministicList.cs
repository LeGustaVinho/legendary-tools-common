using System;
using System.Collections;
using System.Collections.Generic;

namespace LegendaryTools.Deterministic
{
    public readonly struct ManagedSequenceSnapshot<T> : IDisposable where T : unmanaged
    {
        internal readonly ManagedBufferSnapshot<T> Buffer;
        internal readonly SequenceState State;
        internal readonly bool Ring;
        internal ManagedSequenceSnapshot(ManagedBufferSnapshot<T> buffer, SequenceState state, bool ring = false)
        { Buffer = buffer; State = state; Ring = ring; }
        public bool IsCreated => Buffer.IsCreated;
        public int Count => State.Count;
        public int Capacity => Buffer.Capacity;
        public DeterministicStatus TryGet(int index, out T value)
        {
            value = default;
            if (!IsCreated) return DeterministicStatus.Disposed;
            if ((uint)index >= (uint)State.Count) return DeterministicStatus.OutOfRange;
            return Buffer.TryGet(Ring ? RingKernel<T, ManagedStorage<T>>.Physical(State, Capacity, index) : index, out value);
        }
        public void Dispose() => Buffer.Dispose();
    }

    /// <summary>GC-backed list. Supply equality for application structs; default equality covers integer primitives.</summary>
    public partial class DeterministicList<T> : IList<T>, IReadOnlyList<T>, IDisposable where T : unmanaged
    {
        internal ManagedStorage<T> Storage;
        internal SequenceState State;
        private readonly IDeterministicEquality<T> equality;
        public DeterministicList(int capacity) : this(new BufferOptions(capacity)) { }
        public DeterministicList(BufferOptions options, IDeterministicEquality<T> equality = null)
        {
            if (equality != null && !equality.IsValid) DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            Storage = new ManagedStorage<T>(options); this.equality = equality;
        }
        public int Count => State.Count;
        public int Capacity => Storage.Capacity;
        public bool IsCreated => Storage.IsCreated;
        public bool IsReadOnly => false;
        public T this[int index]
        {
            get { TryGet(index, out T value).ThrowIfFailure(); return value; }
            set { TrySet(index, value).ThrowIfFailure(); }
        }
        public DeterministicStatus TryGet(int index, out T value) => SequenceKernel<T, ManagedStorage<T>>.TryGet(ref Storage, State, index, out value);
        public DeterministicStatus TrySet(int index, in T value) => SequenceKernel<T, ManagedStorage<T>>.TrySet(ref Storage, State, index, value);
        public DeterministicStatus TryAdd(in T item) => TryInsert(Count, item);
        public DeterministicStatus TryInsert(int index, in T item) => SequenceKernel<T, ManagedStorage<T>>.TryInsert(ref Storage, ref State, index, item);
        public DeterministicStatus TryRemoveAt(int index) => SequenceKernel<T, ManagedStorage<T>>.TryRemoveAt(ref Storage, ref State, index);
        public DeterministicStatus TryRemoveAtSwapBack(int index) => SequenceKernel<T, ManagedStorage<T>>.TryRemoveAt(ref Storage, ref State, index, true);
        public DeterministicStatus TryClear() => SequenceKernel<T, ManagedStorage<T>>.TryClear(ref Storage, ref State);
        public void Add(T item) => TryAdd(item).ThrowIfFailure();
        public void Insert(int index, T item) => TryInsert(index, item).ThrowIfFailure();
        public void RemoveAt(int index) => TryRemoveAt(index).ThrowIfFailure();
        public void RemoveAtSwapBack(int index) => TryRemoveAtSwapBack(index).ThrowIfFailure();
        public void Clear() => TryClear().ThrowIfFailure();
        public bool Remove(T item) { int index = IndexOf(item); if (index < 0) return false; RemoveAt(index); return true; }
        public bool Contains(T item) => IndexOf(item) >= 0;
        public int IndexOf(T item)
        {
            if (!IsCreated) DeterministicStatus.Disposed.ThrowIfFailure();
            if (equality != null)
                for (int i = 0; i < Count; i++) { var value = Storage.Read(i); if (equality.Equals(value, item)) return i; }
            else
            {
                var policy = new PrimitiveEquality<T>();
                if (!policy.IsSupported) DeterministicStatus.UnsupportedType.ThrowIfFailure();
                return SequenceKernel<T, ManagedStorage<T>>.IndexOf(ref Storage, State, item, policy);
            }
            return -1;
        }
        public int IndexOf<TEquality>(in T item, TEquality policy) where TEquality : struct, IDeterministicEquality<T>
        {
            TryIndexOf(item, policy, out int index).ThrowIfFailure(); return index;
        }
        public DeterministicStatus TryIndexOf<TEquality>(in T item, TEquality policy, out int index) where TEquality : struct, IDeterministicEquality<T>
            => SequenceKernel<T, ManagedStorage<T>>.TryIndexOf(ref Storage, State, item, policy, out index);
        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (arrayIndex < 0 || arrayIndex > array.Length - Count) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            if (!IsCreated) DeterministicStatus.Disposed.ThrowIfFailure();
            for (int i = 0; i < Count; i++) array[arrayIndex + i] = Storage.Read(i);
        }
        public DeterministicStatus TryCapture(out ManagedSequenceSnapshot<T> snapshot)
        {
            var status = Storage.TryCapture(out var buffer); snapshot = new ManagedSequenceSnapshot<T>(buffer, State); return status;
        }
        public DeterministicStatus TryRestore(in ManagedSequenceSnapshot<T> snapshot)
        {
            if (snapshot.Ring) return DeterministicStatus.InvalidOwner;
            var status = Storage.TryRestore(snapshot.Buffer); if (status.IsSuccess()) State = snapshot.State; return status;
        }
        public Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void Dispose() { Storage.Dispose(); State = default; }

        public struct Enumerator : IEnumerator<T>
        {
            private readonly DeterministicList<T> owner;
            private readonly ulong revision;
            private int index;
            private T current;
            internal Enumerator(DeterministicList<T> owner) { this.owner = owner; revision = owner.Storage.Revision; index = -1; current = default; }
            public T Current => index < 0 || index >= owner.Count ? throw new InvalidOperationException() : current;
            object IEnumerator.Current => Current;
            public DeterministicStatus TryMoveNext(out bool moved)
            {
                moved = false;
                if (!owner.IsCreated) return DeterministicStatus.Disposed;
                if (revision != owner.Storage.Revision) return DeterministicStatus.Invalidated;
                if (index < owner.Count) index++;
                if (index < owner.Count) { current = owner.Storage.Read(index); moved = true; }
                return DeterministicStatus.None;
            }
            public bool MoveNext() { TryMoveNext(out bool moved).ThrowIfFailure(); return moved; }
            public void Reset() { if (revision != owner.Storage.Revision) DeterministicStatus.Invalidated.ThrowIfFailure(); index = -1; current = default; }
            public void Dispose() { }
        }
    }
}
