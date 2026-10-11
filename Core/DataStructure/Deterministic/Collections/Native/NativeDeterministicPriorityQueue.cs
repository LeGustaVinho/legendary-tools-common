using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace LegendaryTools.Deterministic
{
    [NativeContainer, NativeContainerIsReadOnly]
    public unsafe struct NativePriorityQueueSnapshot<T, TPriority> : IDisposable where T : unmanaged where TPriority : unmanaged
    {
        internal NativeStorage<PriorityQueueNode<T, TPriority>> Storage;
        internal PriorityQueueState State;
        internal int Slot;
        internal ulong Lease;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif
        public bool IsCreated
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (Storage.IsCreated) AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                return Storage.ValidSnapshot(Slot, Lease);
            }
        }
        public int Count => State.Count;
        public ulong NextSequence => State.NextSequence;
        public DeterministicStatus TryGetUnorderedItem(int index, out PriorityQueueNode<T, TPriority> node)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Storage.IsCreated) AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            node = default;
            if (!IsCreated) return DeterministicStatus.Disposed;
            if ((uint)index >= (uint)Count) return DeterministicStatus.OutOfRange;
            node = Storage.ReadSnapshot(Slot, index); return DeterministicStatus.None;
        }
        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Storage.IsCreated) AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            Storage.ReleaseSnapshot(Slot, Lease); Storage = default;
        }
    }

    [NativeContainer]
    public unsafe partial struct NativeDeterministicPriorityQueue<T, TPriority, TComparer> : IDisposable
        where T : unmanaged where TPriority : unmanaged where TComparer : unmanaged, IDeterministicComparer<TPriority>
    {
        internal NativeStorage<PriorityQueueNode<T, TPriority>> Storage;
        private TComparer comparer;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif
        public NativeDeterministicPriorityQueue(int capacity, Allocator allocator, TComparer comparer = default) : this(new BufferOptions(capacity), allocator, comparer) { }
        public NativeDeterministicPriorityQueue(BufferOptions options, Allocator allocator, TComparer comparer = default)
        {
            this = default;
            if (!comparer.IsValid) DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            this.comparer = comparer; Storage = NativeStorage<PriorityQueueNode<T, TPriority>>.Create(options, allocator);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.Create();
            AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);
#endif
        }
        public bool IsCreated => Storage.IsCreated;
        public int Count { get { CheckRead(); return IsCreated ? Storage.Data->PriorityState.Count : 0; } }
        public int Capacity { get { CheckRead(); return Storage.Capacity; } }
        public ulong NextSequence { get { CheckRead(); return IsCreated ? Storage.Data->PriorityState.NextSequence : 0; } }
        public DeterministicStatus TryEnqueue(in T element, in TPriority priority)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            return PriorityQueueKernel<T, TPriority, TComparer, NativeStorage<PriorityQueueNode<T, TPriority>>>.TryEnqueue(ref Storage, ref Storage.Data->PriorityState, element, priority, comparer);
        }
        public DeterministicStatus TryDequeue(out T element, out TPriority priority)
        {
            CheckWrite(); element = default; priority = default;
            if (!IsCreated) return DeterministicStatus.Disposed;
            return PriorityQueueKernel<T, TPriority, TComparer, NativeStorage<PriorityQueueNode<T, TPriority>>>.TryDequeue(ref Storage, ref Storage.Data->PriorityState, comparer, out element, out priority);
        }
        public DeterministicStatus TryPeek(out T element, out TPriority priority)
        {
            CheckRead(); element = default; priority = default;
            if (!IsCreated) return DeterministicStatus.Disposed;
            return PriorityQueueKernel<T, TPriority, TComparer, NativeStorage<PriorityQueueNode<T, TPriority>>>.TryPeek(ref Storage, Storage.Data->PriorityState, out element, out priority);
        }
        public DeterministicStatus TryClear()
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            return PriorityQueueKernel<T, TPriority, TComparer, NativeStorage<PriorityQueueNode<T, TPriority>>>.TryClear(ref Storage, ref Storage.Data->PriorityState);
        }
        public void Enqueue(T element, TPriority priority) => NativeChecks.Status(TryEnqueue(element, priority));
        public T Dequeue() { var status = TryDequeue(out T element, out _); NativeChecks.Status(status); return element; }
        public T Peek() { var status = TryPeek(out T element, out _); NativeChecks.Status(status); return element; }
        public void Clear() => NativeChecks.Status(TryClear());
        public DeterministicStatus TryGetUnorderedItem(int index, out PriorityQueueNode<T, TPriority> node)
        {
            CheckRead(); node = default;
            if (!IsCreated) return DeterministicStatus.Disposed;
            if ((uint)index >= (uint)Count) return DeterministicStatus.OutOfRange;
            node = Storage.Read(index); return DeterministicStatus.None;
        }
        public NativeSequenceEnumerator<PriorityQueueNode<T, TPriority>> GetUnorderedEnumerator()
        {
            CheckRead();
            var iterator = new NativeSequenceEnumerator<PriorityQueueNode<T, TPriority>> { Storage = Storage,
                State = new SequenceState { Count = Count }, Revision = Storage.Revision, Index = -1 };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            iterator.m_Safety = m_Safety;
#endif
            return iterator;
        }
        public DeterministicStatus TryCapture(out NativePriorityQueueSnapshot<T, TPriority> snapshot)
        {
            CheckWrite(); snapshot = default; var status = Storage.TryCapture(out int slot, out ulong lease);
            if (!status.IsSuccess()) return status;
            snapshot = new NativePriorityQueueSnapshot<T, TPriority> { Storage = Storage, State = Storage.Data->PriorityState, Slot = slot, Lease = lease };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            snapshot.m_Safety = m_Safety;
#endif
            return DeterministicStatus.None;
        }
        public DeterministicStatus TryRestore(in NativePriorityQueueSnapshot<T, TPriority> snapshot)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            if (snapshot.Storage.Data != Storage.Data) return DeterministicStatus.InvalidOwner;
            var status = Storage.TryRestore(snapshot.Slot, snapshot.Lease);
            if (status.IsSuccess()) Storage.Data->PriorityState = snapshot.State; return status;
        }
        public void Dispose()
        {
            if (!IsCreated) return; CheckWrite();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(m_Safety);
#endif
            Storage.Destroy();
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
    }
}
