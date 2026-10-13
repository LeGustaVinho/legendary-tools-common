using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

namespace LegendaryTools.Deterministic
{
    internal static class NativeChecks
    {
        // Callers pass mutations as arguments; removing the call would remove them too.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Status(DeterministicStatus status)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!status.IsSuccess()) throw new InvalidOperationException("Deterministic collection failure. Use Try methods to handle the status.");
#endif
        }
    }

    /// <summary>Borrowed snapshot. The originating container must outlive every snapshot and scheduled reader.</summary>
    [NativeContainer, NativeContainerIsReadOnly]
    public unsafe struct NativeSequenceSnapshot<T> : IDisposable where T : unmanaged
    {
        internal NativeStorage<T> Storage;
        internal SequenceState State;
        internal int Slot;
        internal ulong Lease;
        internal bool Ring;
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
        public int Capacity { get { CheckRead(); return IsCreated ? Storage.Data->SnapshotCapacities[Slot] : 0; } }
        public DeterministicStatus TryGet(int index, out T value)
        {
            CheckRead(); value = default;
            if (!IsCreated) return DeterministicStatus.Disposed;
            if ((uint)index >= (uint)Count) return DeterministicStatus.OutOfRange;
            int capacity = Storage.Data->SnapshotCapacities[Slot];
            value = Storage.ReadSnapshot(Slot, Ring ? RingKernel<T, NativeStorage<T>>.Physical(State, capacity, index) : index);
            return DeterministicStatus.None;
        }
        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Storage.IsCreated) AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            Storage.ReleaseSnapshot(Slot, Lease); Storage = default;
        }
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void CheckRead()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Storage.IsCreated) AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
        }
    }

    /// <summary>Read-only, revision-scoped view. Mutation and restore invalidate it.</summary>
    [NativeContainer, NativeContainerIsReadOnly]
    public unsafe struct NativeSequenceReadOnly<T> where T : unmanaged
    {
        internal NativeStorage<T> Storage;
        internal SequenceState State;
        internal ulong Revision;
        internal bool Ring;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif
        public int Count => State.Count;
        public DeterministicStatus TryGet(int index, out T value)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Storage.IsCreated) AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            value = default;
            if (!Storage.IsCreated) return DeterministicStatus.Disposed;
            if (Storage.BatchActive) return DeterministicStatus.Invalidated;
            if (Storage.Revision != Revision) return DeterministicStatus.Invalidated;
            if ((uint)index >= (uint)Count) return DeterministicStatus.OutOfRange;
            value = Storage.Read(Ring ? RingKernel<T, NativeStorage<T>>.Physical(State, Storage.Capacity, index) : index);
            return DeterministicStatus.None;
        }
        public T this[int index] { get { var status = TryGet(index, out T value); NativeChecks.Status(status); return value; } }
    }

    public unsafe struct NativeSequenceEnumerator<T> where T : unmanaged
    {
        internal NativeStorage<T> Storage;
        internal SequenceState State;
        internal ulong Revision;
        internal bool Ring, Reverse;
        internal int Index;
        private T current;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif
        public T Current => current;
        public DeterministicStatus TryMoveNext(out bool moved)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Storage.IsCreated) AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            moved = false;
            if (!Storage.IsCreated) return DeterministicStatus.Disposed;
            if (Storage.BatchActive) return DeterministicStatus.Invalidated;
            if (Storage.Revision != Revision) return DeterministicStatus.Invalidated;
            if (Index < State.Count) Index++;
            if (Index >= State.Count) return DeterministicStatus.None;
            int logical = Reverse ? State.Count - Index - 1 : Index;
            current = Storage.Read(Ring ? RingKernel<T, NativeStorage<T>>.Physical(State, Storage.Capacity, logical) : logical);
            moved = true; return DeterministicStatus.None;
        }
        public bool MoveNext() { var status = TryMoveNext(out bool moved); NativeChecks.Status(status); return moved; }
    }
}
