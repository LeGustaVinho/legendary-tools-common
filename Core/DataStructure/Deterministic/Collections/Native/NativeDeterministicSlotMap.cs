using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace LegendaryTools.Deterministic
{
    /// <summary>Borrowed snapshot. Its originating owner must outlive all aliases and scheduled readers.</summary>
    [NativeContainer, NativeContainerIsReadOnly]
    public unsafe struct NativeSlotMapSnapshot<T> : IDisposable where T : unmanaged
    {
        internal NativeStorage<SlotMapNode<T>> Storage;
        internal SlotMapState State;
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
        public DeterministicStatus TryGet(StableHandle handle, out T value)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Storage.IsCreated) AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            value = default;
            if (!IsCreated) return DeterministicStatus.Disposed;
            if (!handle.IsValid || handle.Index >= (uint)State.Used) return DeterministicStatus.NotFound;
            var node = Storage.ReadSnapshot(Slot, (int)handle.Index);
            if (node.Active == 0 || node.Generation != handle.Generation) return DeterministicStatus.NotFound;
            value = node.Value; return DeterministicStatus.None;
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
    public unsafe partial struct NativeDeterministicSlotMap<T> : IDisposable where T : unmanaged
    {
        internal NativeStorage<SlotMapNode<T>> Storage;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif
        public NativeDeterministicSlotMap(int capacity, Allocator allocator) : this(new BufferOptions(capacity), allocator) { }
        public NativeDeterministicSlotMap(BufferOptions options, Allocator allocator)
        {
            this = default; Storage = NativeStorage<SlotMapNode<T>>.Create(options, allocator);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.Create();
            AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);
#endif
        }
        public bool IsCreated => Storage.IsCreated;
        public int Count { get { CheckRead(); return IsCreated ? Storage.Data->SlotState.Count : 0; } }
        public int Capacity { get { CheckRead(); return Storage.Capacity; } }
        public T this[StableHandle handle]
        {
            get { var status = TryGet(handle, out T value); NativeChecks.Status(status); return value; }
            set => NativeChecks.Status(TrySet(handle, value));
        }
        public DeterministicStatus TryAdd(in T value, out StableHandle handle)
        {
            CheckWrite(); handle = default; if (!IsCreated) return DeterministicStatus.Disposed;
            return SlotMapKernel<T, NativeStorage<SlotMapNode<T>>>.TryAdd(ref Storage, ref Storage.Data->SlotState, value, out handle);
        }
        public StableHandle Add(T value) { var status = TryAdd(value, out var handle); NativeChecks.Status(status); return handle; }
        public DeterministicStatus TryGet(StableHandle handle, out T value)
        {
            CheckRead(); value = default; if (!IsCreated) return DeterministicStatus.Disposed;
            return SlotMapKernel<T, NativeStorage<SlotMapNode<T>>>.TryGet(ref Storage, Storage.Data->SlotState, handle, out value);
        }
        public bool TryGetValue(StableHandle handle, out T value) => TryGet(handle, out value).IsSuccess();
        public bool ContainsKey(StableHandle handle) => TryGetValue(handle, out _);
        public DeterministicStatus TrySet(StableHandle handle, in T value)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            return SlotMapKernel<T, NativeStorage<SlotMapNode<T>>>.TrySet(ref Storage, Storage.Data->SlotState, handle, value);
        }
        public DeterministicStatus TryRemove(StableHandle handle)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            return SlotMapKernel<T, NativeStorage<SlotMapNode<T>>>.TryRemove(ref Storage, ref Storage.Data->SlotState, handle);
        }
        public bool Remove(StableHandle handle)
        { var status = TryRemove(handle); if (status == DeterministicStatus.NotFound) return false; NativeChecks.Status(status); return status.IsSuccess(); }
        public DeterministicStatus TryClear()
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            return SlotMapKernel<T, NativeStorage<SlotMapNode<T>>>.TryClear(ref Storage, ref Storage.Data->SlotState);
        }
        public void Clear() => NativeChecks.Status(TryClear());
        public DeterministicStatus TryCapture(out NativeSlotMapSnapshot<T> snapshot)
        {
            CheckWrite(); snapshot = default; var status = Storage.TryCapture(out int slot, out ulong lease);
            if (!status.IsSuccess()) return status;
            snapshot = new NativeSlotMapSnapshot<T> { Storage = Storage, State = Storage.Data->SlotState, Slot = slot, Lease = lease };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            snapshot.m_Safety = m_Safety;
#endif
            return DeterministicStatus.None;
        }
        public DeterministicStatus TryRestore(in NativeSlotMapSnapshot<T> snapshot)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            if (snapshot.Storage.Data != Storage.Data) return DeterministicStatus.InvalidOwner;
            var status = Storage.TryRestore(snapshot.Slot, snapshot.Lease);
            if (status.IsSuccess()) Storage.Data->SlotState = snapshot.State; return status;
        }
        public Enumerator GetEnumerator()
        {
            CheckRead(); var iterator = new Enumerator { Storage = Storage, Revision = Storage.Revision, Index = -1 };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            iterator.m_Safety = m_Safety;
#endif
            return iterator;
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
        public struct Enumerator
        {
            internal NativeStorage<SlotMapNode<T>> Storage;
            internal ulong Revision;
            internal int Index;
            private SlotMapItem<T> current;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif
            public SlotMapItem<T> Current => current;
            public DeterministicStatus TryMoveNext(out bool moved)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (Storage.IsCreated) AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                moved = false;
                if (!Storage.IsCreated) return DeterministicStatus.Disposed;
                if (Storage.Revision != Revision) return DeterministicStatus.Invalidated;
                if (Index >= Storage.Data->SlotState.Used) return DeterministicStatus.None;
                while (++Index < Storage.Data->SlotState.Used)
                {
                    var node = Storage.Read(Index); if (node.Active == 0) continue;
                    current = new SlotMapItem<T>(new StableHandle((uint)Index, node.Generation), node.Value); moved = true; break;
                }
                return DeterministicStatus.None;
            }
            public bool MoveNext() { var status = TryMoveNext(out bool moved); NativeChecks.Status(status); return moved; }
        }
    }
}
