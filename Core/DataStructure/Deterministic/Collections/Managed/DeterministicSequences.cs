using System;
using System.Collections;
using System.Collections.Generic;

namespace LegendaryTools.Deterministic
{
    public struct ManagedSequenceEnumerator<T> : IEnumerator<T> where T : unmanaged
    {
        private readonly ManagedBufferOwner<T> owner;
        private readonly SequenceState state;
        private readonly ulong revision;
        private readonly bool ring, reverse;
        private int index;
        private T current;
        internal ManagedSequenceEnumerator(ManagedStorage<T> storage, SequenceState state, bool ring, bool reverse = false)
        { owner = storage.Owner; this.state = state; revision = storage.Revision; this.ring = ring; this.reverse = reverse; index = -1; current = default; }
        public T Current => index < 0 || index >= state.Count ? throw new InvalidOperationException() : current;
        object IEnumerator.Current => Current;
        public DeterministicStatus TryMoveNext(out bool moved)
        {
            moved = false;
            if (owner == null || owner.Disposed) return DeterministicStatus.Disposed;
            if (revision != owner.Revision) return DeterministicStatus.Invalidated;
            if (index < state.Count) index++;
            if (index >= state.Count) return DeterministicStatus.None;
            int logical = reverse ? state.Count - index - 1 : index;
            current = owner.Read(ring ? RingKernel<T, ManagedStorage<T>>.Physical(state, owner.Capacity, logical) : logical);
            moved = true; return DeterministicStatus.None;
        }
        public bool MoveNext() { TryMoveNext(out bool moved).ThrowIfFailure(); return moved; }
        public void Reset()
        {
            if (owner == null || owner.Disposed) DeterministicStatus.Disposed.ThrowIfFailure();
            if (revision != owner.Revision) DeterministicStatus.Invalidated.ThrowIfFailure();
            index = -1; current = default;
        }
        public void Dispose() { }
    }

    public sealed partial class DeterministicArray<T> : IReadOnlyList<T>, IDisposable where T : unmanaged
    {
        internal ManagedStorage<T> Storage;
        private readonly SequenceState state;
        public DeterministicArray(int length, SnapshotPolicy snapshots = SnapshotPolicy.KeyframesAndDeltas, int snapshotSlots = 4)
            : this(new BufferOptions(length, snapshots: snapshots, snapshotSlots: snapshotSlots)) { }
        public DeterministicArray(BufferOptions options)
        {
            if (!options.IsValid || options.Allocation != AllocationPolicy.Fixed) DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            Storage = new ManagedStorage<T>(options); state = new SequenceState { Count = options.InitialCapacity };
        }
        public int Length => state.Count;
        public int Count => state.Count;
        public bool IsCreated => Storage.IsCreated;
        public T this[int index]
        {
            get { TryGet(index, out T value).ThrowIfFailure(); return value; }
            set { TrySet(index, value).ThrowIfFailure(); }
        }
        public DeterministicStatus TryGet(int index, out T value) => SequenceKernel<T, ManagedStorage<T>>.TryGet(ref Storage, state, index, out value);
        public DeterministicStatus TrySet(int index, in T value) => SequenceKernel<T, ManagedStorage<T>>.TrySet(ref Storage, state, index, value);
        public DeterministicStatus TryClear()
        {
            var status = Storage.PrepareWrite(Length, 0, Length); if (!status.IsSuccess()) return status;
            T zero = default; for (int i = 0; i < Length; i++) Storage.Write(i, zero);
            Storage.CommitWrite(); return DeterministicStatus.None;
        }
        public void Clear() => TryClear().ThrowIfFailure();
        public DeterministicStatus TryCapture(out ManagedSequenceSnapshot<T> snapshot)
        { var status = Storage.TryCapture(out var buffer); snapshot = new ManagedSequenceSnapshot<T>(buffer, state); return status; }
        public DeterministicStatus TryRestore(in ManagedSequenceSnapshot<T> snapshot)
        {
            if (snapshot.Ring || snapshot.Count != Length) return DeterministicStatus.InvalidOwner;
            return Storage.TryRestore(snapshot.Buffer);
        }
        public ManagedSequenceEnumerator<T> GetEnumerator() => new ManagedSequenceEnumerator<T>(Storage, state, false);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void Dispose() => Storage.Dispose();
    }

    public sealed partial class DeterministicCell<T> : IDisposable where T : unmanaged
    {
        private readonly DeterministicArray<T> storage;
        public DeterministicCell(T value = default, SnapshotPolicy snapshots = SnapshotPolicy.KeyframesAndDeltas, int snapshotSlots = 4)
        { storage = new DeterministicArray<T>(1, snapshots, snapshotSlots); storage[0] = value; }
        public DeterministicCell(BufferOptions options, T value = default)
        {
            if (options.InitialCapacity != 1) DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            storage = new DeterministicArray<T>(options); storage[0] = value;
        }
        public T Value { get => storage[0]; set => storage[0] = value; }
        public bool IsCreated => storage.IsCreated;
        public DeterministicStatus TryGet(out T value) => storage.TryGet(0, out value);
        public DeterministicStatus TrySet(in T value) => storage.TrySet(0, value);
        public DeterministicStatus TryCapture(out ManagedSequenceSnapshot<T> snapshot) => storage.TryCapture(out snapshot);
        public DeterministicStatus TryRestore(in ManagedSequenceSnapshot<T> snapshot) => storage.TryRestore(snapshot);
        public void Dispose() => storage.Dispose();
    }

    public abstract class DeterministicRing<T> : IReadOnlyList<T>, IDisposable where T : unmanaged
    {
        internal ManagedStorage<T> Storage;
        internal SequenceState State;
        protected DeterministicRing(BufferOptions options) { Storage = new ManagedStorage<T>(options); }
        public int Count => State.Count;
        public int Capacity => Storage.Capacity;
        public bool IsCreated => Storage.IsCreated;
        public T this[int index] { get { TryGet(index, out T value).ThrowIfFailure(); return value; } }
        public DeterministicStatus TryGet(int index, out T value) => RingKernel<T, ManagedStorage<T>>.TryGet(ref Storage, State, index, out value);
        protected DeterministicStatus TryPush(in T value, bool front) => RingKernel<T, ManagedStorage<T>>.TryPush(ref Storage, ref State, value, front);
        protected DeterministicStatus TryPop(bool front, out T value) => RingKernel<T, ManagedStorage<T>>.TryPop(ref Storage, ref State, front, out value);
        protected DeterministicStatus TryPeek(bool front, out T value)
        {
            value = default;
            if (!IsCreated) return DeterministicStatus.Disposed;
            if (Count == 0) return DeterministicStatus.Empty;
            return TryGet(front ? 0 : Count - 1, out value);
        }
        public DeterministicStatus TryClear()
        {
            var status = Storage.PrepareWrite(Capacity, 0, Capacity); if (!status.IsSuccess()) return status;
            T zero = default; for (int i = 0; i < Capacity; i++) Storage.Write(i, zero);
            State = default; Storage.CommitWrite(); return DeterministicStatus.None;
        }
        public void Clear() => TryClear().ThrowIfFailure();
        public DeterministicStatus TryCapture(out ManagedSequenceSnapshot<T> snapshot)
        { var status = Storage.TryCapture(out var buffer); snapshot = new ManagedSequenceSnapshot<T>(buffer, State, true); return status; }
        public DeterministicStatus TryRestore(in ManagedSequenceSnapshot<T> snapshot)
        {
            if (!snapshot.Ring) return DeterministicStatus.InvalidOwner;
            var status = Storage.TryRestore(snapshot.Buffer); if (status.IsSuccess()) State = snapshot.State; return status;
        }
        public ManagedSequenceEnumerator<T> GetEnumerator() => new ManagedSequenceEnumerator<T>(Storage, State, true);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void Dispose() { Storage.Dispose(); State = default; }
    }

    public sealed partial class DeterministicQueue<T> : DeterministicRing<T> where T : unmanaged
    {
        public DeterministicQueue(int capacity) : base(new BufferOptions(capacity)) { }
        public DeterministicQueue(BufferOptions options) : base(options) { }
        public DeterministicStatus TryEnqueue(in T value) => TryPush(value, false);
        public DeterministicStatus TryDequeue(out T value) => TryPop(true, out value);
        public DeterministicStatus TryPeek(out T value) => TryPeek(true, out value);
        public void Enqueue(T value) => TryEnqueue(value).ThrowIfFailure();
        public T Dequeue() { TryDequeue(out T value).ThrowIfFailure(); return value; }
        public T Peek() { TryPeek(out T value).ThrowIfFailure(); return value; }
    }

    public sealed partial class DeterministicDeque<T> : DeterministicRing<T> where T : unmanaged
    {
        public DeterministicDeque(int capacity) : base(new BufferOptions(capacity)) { }
        public DeterministicDeque(BufferOptions options) : base(options) { }
        public DeterministicStatus TryAddFirst(in T value) => TryPush(value, true);
        public DeterministicStatus TryAddLast(in T value) => TryPush(value, false);
        public DeterministicStatus TryRemoveFirst(out T value) => TryPop(true, out value);
        public DeterministicStatus TryRemoveLast(out T value) => TryPop(false, out value);
        public void AddFirst(T value) => TryAddFirst(value).ThrowIfFailure();
        public void AddLast(T value) => TryAddLast(value).ThrowIfFailure();
        public T RemoveFirst() { TryRemoveFirst(out T value).ThrowIfFailure(); return value; }
        public T RemoveLast() { TryRemoveLast(out T value).ThrowIfFailure(); return value; }
    }

    public sealed partial class DeterministicRingBuffer<T> : DeterministicRing<T> where T : unmanaged
    {
        public DeterministicRingBuffer(int capacity, SnapshotPolicy snapshots = SnapshotPolicy.KeyframesAndDeltas, int snapshotSlots = 4)
            : this(new BufferOptions(capacity, snapshots: snapshots, snapshotSlots: snapshotSlots)) { }
        public DeterministicRingBuffer(BufferOptions options) : base(ValidateOptions(options)) { }
        private static BufferOptions ValidateOptions(BufferOptions options)
        {
            if (!options.IsValid || options.Allocation != AllocationPolicy.Fixed)
                DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            return options;
        }
        public DeterministicStatus TryEnqueue(in T value, bool overwriteOldest = false)
        {
            if (!IsCreated) return DeterministicStatus.Disposed;
            if (Count != Capacity || !overwriteOldest || Capacity == 0) return TryPush(value, false);
            var status = Storage.PrepareWrite(Capacity, State.Head, 1); if (!status.IsSuccess()) return status;
            Storage.Write(State.Head, value); State.Head = (State.Head + 1) % Capacity;
            Storage.CommitWrite(); return DeterministicStatus.None;
        }
        public DeterministicStatus TryDequeue(out T value) => TryPop(true, out value);
        public void Enqueue(T value, bool overwriteOldest = false) => TryEnqueue(value, overwriteOldest).ThrowIfFailure();
        public T Dequeue() { TryDequeue(out T value).ThrowIfFailure(); return value; }
    }

    public sealed partial class DeterministicStack<T> : IEnumerable<T>, IDisposable where T : unmanaged
    {
        private readonly DeterministicList<T> list;
        public DeterministicStack(int capacity) : this(new BufferOptions(capacity)) { }
        public DeterministicStack(BufferOptions options) { list = new DeterministicList<T>(options); }
        public int Count => list.Count;
        public int Capacity => list.Capacity;
        public bool IsCreated => list.IsCreated;
        public DeterministicStatus TryPush(in T value) => list.TryAdd(value);
        public DeterministicStatus TryPeek(out T value)
        {
            value = default;
            if (!IsCreated) return DeterministicStatus.Disposed;
            return Count == 0 ? DeterministicStatus.Empty : list.TryGet(Count - 1, out value);
        }
        public DeterministicStatus TryPop(out T value)
        {
            var status = TryPeek(out value); if (!status.IsSuccess()) return status;
            status = list.TryRemoveAt(Count - 1); if (!status.IsSuccess()) value = default; return status;
        }
        public void Push(T value) => TryPush(value).ThrowIfFailure();
        public T Pop() { TryPop(out T value).ThrowIfFailure(); return value; }
        public T Peek() { TryPeek(out T value).ThrowIfFailure(); return value; }
        public void Clear() => list.Clear();
        public DeterministicStatus TryCapture(out ManagedSequenceSnapshot<T> snapshot) => list.TryCapture(out snapshot);
        public DeterministicStatus TryRestore(in ManagedSequenceSnapshot<T> snapshot) => list.TryRestore(snapshot);
        public ManagedSequenceEnumerator<T> GetEnumerator() => new ManagedSequenceEnumerator<T>(list.Storage, list.State, false, true);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void Dispose() => list.Dispose();
    }

    public sealed partial class DeterministicBitSet : IDisposable
    {
        private readonly DeterministicArray<ulong> words;
        public int Length { get; }
        public bool IsCreated => words.IsCreated;
        public DeterministicBitSet(int length, SnapshotPolicy snapshots = SnapshotPolicy.KeyframesAndDeltas, int snapshotSlots = 4)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            Length = length; words = new DeterministicArray<ulong>((int)(((long)length + 63) / 64), snapshots, snapshotSlots);
        }
        public bool this[int index]
        {
            get { TryGet(index, out bool value).ThrowIfFailure(); return value; }
            set { TrySet(index, value).ThrowIfFailure(); }
        }
        public DeterministicStatus TryGet(int index, out bool value)
        {
            return BitSetKernel<ManagedStorage<ulong>>.TryGet(ref words.Storage, Length, index, out value);
        }
        public DeterministicStatus TrySet(int index, bool value)
        {
            return BitSetKernel<ManagedStorage<ulong>>.TrySet(ref words.Storage, Length, index, value);
        }
        public int Count
        {
            get
            {
                if (!IsCreated) DeterministicStatus.Disposed.ThrowIfFailure();
                return BitSetKernel<ManagedStorage<ulong>>.Count(ref words.Storage);
            }
        }
        public int NextSetBit(int start)
        {
            if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));
            if (!IsCreated) DeterministicStatus.Disposed.ThrowIfFailure();
            return BitSetKernel<ManagedStorage<ulong>>.NextSetBit(ref words.Storage, Length, start);
        }
        public void Clear() => words.Clear();
        public DeterministicStatus TryCapture(out ManagedSequenceSnapshot<ulong> snapshot) => words.TryCapture(out snapshot);
        public DeterministicStatus TryRestore(in ManagedSequenceSnapshot<ulong> snapshot) => words.TryRestore(snapshot);
        public void Dispose() => words.Dispose();
    }
}
