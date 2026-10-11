using System;

namespace LegendaryTools.Deterministic
{
    public readonly struct ManagedPriorityQueueSnapshot<T, TPriority> : IDisposable where T : unmanaged where TPriority : unmanaged
    {
        internal readonly ManagedBufferSnapshot<PriorityQueueNode<T, TPriority>> Buffer;
        internal readonly PriorityQueueState State;
        internal ManagedPriorityQueueSnapshot(ManagedBufferSnapshot<PriorityQueueNode<T, TPriority>> buffer, PriorityQueueState state)
        { Buffer = buffer; State = state; }
        public bool IsCreated => Buffer.IsCreated;
        public int Count => State.Count;
        public int Capacity => Buffer.Capacity;
        public ulong NextSequence => State.NextSequence;
        public DeterministicStatus TryGetUnorderedItem(int index, out PriorityQueueNode<T, TPriority> node)
        {
            node = default;
            if ((uint)index >= (uint)Count) return DeterministicStatus.OutOfRange;
            return Buffer.TryGet(index, out node);
        }
        public void Dispose() => Buffer.Dispose();
    }

    public sealed partial class DeterministicPriorityQueue<T, TPriority, TComparer> : IDisposable
        where T : unmanaged where TPriority : unmanaged where TComparer : unmanaged, IDeterministicComparer<TPriority>
    {
        internal ManagedStorage<PriorityQueueNode<T, TPriority>> Storage;
        internal PriorityQueueState State;
        private readonly TComparer comparer;
        public DeterministicPriorityQueue(int capacity, TComparer comparer = default) : this(new BufferOptions(capacity), comparer) { }
        public DeterministicPriorityQueue(BufferOptions options, TComparer comparer = default)
        {
            if (!comparer.IsValid) DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            this.comparer = comparer; Storage = new ManagedStorage<PriorityQueueNode<T, TPriority>>(options);
        }
        public int Count => State.Count;
        public int Capacity => Storage.Capacity;
        public ulong NextSequence => State.NextSequence;
        public bool IsCreated => Storage.IsCreated;
        public DeterministicStatus TryEnqueue(in T element, in TPriority priority) =>
            PriorityQueueKernel<T, TPriority, TComparer, ManagedStorage<PriorityQueueNode<T, TPriority>>>.TryEnqueue(ref Storage, ref State, element, priority, comparer);
        public DeterministicStatus TryDequeue(out T element, out TPriority priority) =>
            PriorityQueueKernel<T, TPriority, TComparer, ManagedStorage<PriorityQueueNode<T, TPriority>>>.TryDequeue(ref Storage, ref State, comparer, out element, out priority);
        public DeterministicStatus TryPeek(out T element, out TPriority priority) =>
            PriorityQueueKernel<T, TPriority, TComparer, ManagedStorage<PriorityQueueNode<T, TPriority>>>.TryPeek(ref Storage, State, out element, out priority);
        public DeterministicStatus TryClear() => PriorityQueueKernel<T, TPriority, TComparer, ManagedStorage<PriorityQueueNode<T, TPriority>>>.TryClear(ref Storage, ref State);
        public void Enqueue(T element, TPriority priority) => TryEnqueue(element, priority).ThrowIfFailure();
        public T Dequeue() { TryDequeue(out T element, out _).ThrowIfFailure(); return element; }
        public T Peek() { TryPeek(out T element, out _).ThrowIfFailure(); return element; }
        public void Clear() => TryClear().ThrowIfFailure();
        public DeterministicStatus TryGetUnorderedItem(int index, out PriorityQueueNode<T, TPriority> node)
        {
            node = default;
            if (!IsCreated) return DeterministicStatus.Disposed;
            if ((uint)index >= (uint)Count) return DeterministicStatus.OutOfRange;
            node = Storage.Read(index); return DeterministicStatus.None;
        }
        public ManagedSequenceEnumerator<PriorityQueueNode<T, TPriority>> GetUnorderedEnumerator() =>
            new ManagedSequenceEnumerator<PriorityQueueNode<T, TPriority>>(Storage, new SequenceState { Count = Count }, false);
        public DeterministicStatus TryCapture(out ManagedPriorityQueueSnapshot<T, TPriority> snapshot)
        { var status = Storage.TryCapture(out var buffer); snapshot = new ManagedPriorityQueueSnapshot<T, TPriority>(buffer, State); return status; }
        public DeterministicStatus TryRestore(in ManagedPriorityQueueSnapshot<T, TPriority> snapshot)
        {
            var status = Storage.TryRestore(snapshot.Buffer); if (status.IsSuccess()) State = snapshot.State; return status;
        }
        public void Dispose() { Storage.Dispose(); State = default; }
    }
}
