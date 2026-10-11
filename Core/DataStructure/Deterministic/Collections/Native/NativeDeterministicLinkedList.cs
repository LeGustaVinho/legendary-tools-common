using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace LegendaryTools.Deterministic
{
    [NativeContainer, NativeContainerIsReadOnly]
    public unsafe struct NativeLinkedListSnapshot<T> : IDisposable where T : unmanaged
    {
        internal NativeStorage<SlotMapNode<LinkedListValue<T>>> Storage;
        internal SlotMapState Slots;
        internal LinkedListState Links;
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
        public int Count => Slots.Count;
        public StableHandle First => Links.First;
        public StableHandle Last => Links.Last;
        public DeterministicStatus TryGet(StableHandle handle, out T value)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Storage.IsCreated) AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            value = default; if (!IsCreated) return DeterministicStatus.Disposed;
            if (!handle.IsValid || handle.Index >= (uint)Slots.Used) return DeterministicStatus.NotFound;
            var node = Storage.ReadSnapshot(Slot, (int)handle.Index);
            if (node.Active == 0 || node.Generation != handle.Generation) return DeterministicStatus.NotFound;
            value = node.Value.Value; return DeterministicStatus.None;
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
    public unsafe partial struct NativeDeterministicLinkedList<T> : IDisposable where T : unmanaged
    {
        internal NativeStorage<SlotMapNode<LinkedListValue<T>>> Storage;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif
        public NativeDeterministicLinkedList(int capacity, Allocator allocator) : this(new BufferOptions(capacity), allocator) { }
        public NativeDeterministicLinkedList(BufferOptions options, Allocator allocator)
        {
            this = default; Storage = NativeStorage<SlotMapNode<LinkedListValue<T>>>.Create(options, allocator);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.Create(); AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);
#endif
        }
        public bool IsCreated => Storage.IsCreated;
        public int Count { get { CheckRead(); return IsCreated ? Storage.Data->SlotState.Count : 0; } }
        public int Capacity { get { CheckRead(); return Storage.Capacity; } }
        public StableHandle First { get { CheckRead(); return IsCreated ? Storage.Data->Links.First : default; } }
        public StableHandle Last { get { CheckRead(); return IsCreated ? Storage.Data->Links.Last : default; } }
        public T this[StableHandle handle]
        { get { var status = TryGet(handle, out T value); NativeChecks.Status(status); return value; } set => NativeChecks.Status(TrySet(handle, value)); }
        private DeterministicStatus Insert(in T value, int position, StableHandle anchor, out StableHandle handle)
        {
            CheckWrite(); handle = default; if (!IsCreated) return DeterministicStatus.Disposed;
            return LinkedListKernel<T, NativeStorage<SlotMapNode<LinkedListValue<T>>>>.TryAdd(ref Storage, ref Storage.Data->SlotState, ref Storage.Data->Links, value, position, anchor, out handle);
        }
        public DeterministicStatus TryAddFirst(in T value, out StableHandle handle) => Insert(value, 0, default, out handle);
        public DeterministicStatus TryAddLast(in T value, out StableHandle handle) => Insert(value, 1, default, out handle);
        public DeterministicStatus TryAddBefore(StableHandle node, in T value, out StableHandle handle) => Insert(value, 2, node, out handle);
        public DeterministicStatus TryAddAfter(StableHandle node, in T value, out StableHandle handle) => Insert(value, 3, node, out handle);
        public StableHandle AddFirst(T value) { var status = TryAddFirst(value, out var handle); NativeChecks.Status(status); return handle; }
        public StableHandle AddLast(T value) { var status = TryAddLast(value, out var handle); NativeChecks.Status(status); return handle; }
        public StableHandle AddBefore(StableHandle node, T value) { var status = TryAddBefore(node, value, out var handle); NativeChecks.Status(status); return handle; }
        public StableHandle AddAfter(StableHandle node, T value) { var status = TryAddAfter(node, value, out var handle); NativeChecks.Status(status); return handle; }
        public DeterministicStatus TryGet(StableHandle handle, out T value)
        {
            CheckRead(); value = default; if (!IsCreated) return DeterministicStatus.Disposed;
            return LinkedListKernel<T, NativeStorage<SlotMapNode<LinkedListValue<T>>>>.TryGet(ref Storage, Storage.Data->SlotState, handle, out value);
        }
        public DeterministicStatus TrySet(StableHandle handle, in T value)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            return LinkedListKernel<T, NativeStorage<SlotMapNode<LinkedListValue<T>>>>.TrySet(ref Storage, Storage.Data->SlotState, handle, value);
        }
        public DeterministicStatus TryGetLinks(StableHandle handle, out StableHandle previous, out StableHandle next)
        {
            CheckRead(); previous = next = default; if (!IsCreated) return DeterministicStatus.Disposed;
            var status = SlotMapKernel<LinkedListValue<T>, NativeStorage<SlotMapNode<LinkedListValue<T>>>>.TryGet(ref Storage, Storage.Data->SlotState, handle, out var node);
            previous = node.Previous; next = node.Next; return status;
        }
        public DeterministicStatus TryRemove(StableHandle handle)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            return LinkedListKernel<T, NativeStorage<SlotMapNode<LinkedListValue<T>>>>.TryRemove(ref Storage, ref Storage.Data->SlotState, ref Storage.Data->Links, handle);
        }
        public void Remove(StableHandle handle) => NativeChecks.Status(TryRemove(handle));
        public DeterministicStatus TryRemoveFirst() => !First.IsValid ? DeterministicStatus.Empty : TryRemove(First);
        public DeterministicStatus TryRemoveLast() => !Last.IsValid ? DeterministicStatus.Empty : TryRemove(Last);
        public DeterministicStatus TryFind<TEquality>(in T value, TEquality equality, out StableHandle handle) where TEquality : unmanaged, IDeterministicEquality<T>
        {
            CheckRead(); handle = default; if (!IsCreated) return DeterministicStatus.Disposed;
            if (!equality.IsValid) return DeterministicStatus.UnsupportedType;
            for (var current = First; current.IsValid; current = Storage.Read((int)current.Index).Value.Next)
            { var item = Storage.Read((int)current.Index).Value.Value; if (equality.Equals(item, value)) { handle = current; return DeterministicStatus.None; } }
            return DeterministicStatus.NotFound;
        }
        public DeterministicStatus TryClear()
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            return LinkedListKernel<T, NativeStorage<SlotMapNode<LinkedListValue<T>>>>.TryClear(ref Storage, ref Storage.Data->SlotState, ref Storage.Data->Links);
        }
        public void Clear() => NativeChecks.Status(TryClear());
        public DeterministicStatus TryCapture(out NativeLinkedListSnapshot<T> snapshot)
        {
            CheckWrite(); snapshot = default; var status = Storage.TryCapture(out int slot, out ulong lease); if (!status.IsSuccess()) return status;
            snapshot = new NativeLinkedListSnapshot<T> { Storage = Storage, Slots = Storage.Data->SlotState, Links = Storage.Data->Links, Slot = slot, Lease = lease };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            snapshot.m_Safety = m_Safety;
#endif
            return DeterministicStatus.None;
        }
        public DeterministicStatus TryRestore(in NativeLinkedListSnapshot<T> snapshot)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            if (snapshot.Storage.Data != Storage.Data) return DeterministicStatus.InvalidOwner;
            var status = Storage.TryRestore(snapshot.Slot, snapshot.Lease); if (status.IsSuccess()) { Storage.Data->SlotState = snapshot.Slots; Storage.Data->Links = snapshot.Links; } return status;
        }
        public Enumerator GetEnumerator()
        {
            CheckRead(); var iterator = new Enumerator { Storage = Storage, Revision = Storage.Revision, Next = First };
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
            internal NativeStorage<SlotMapNode<LinkedListValue<T>>> Storage;
            internal ulong Revision;
            internal StableHandle Next;
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
                moved = false; if (!Storage.IsCreated) return DeterministicStatus.Disposed;
                if (Storage.Revision != Revision) return DeterministicStatus.Invalidated;
                if (!Next.IsValid) return DeterministicStatus.None;
                var node = Storage.Read((int)Next.Index); current = node.Value.Value; Next = node.Value.Next; moved = true; return DeterministicStatus.None;
            }
            public bool MoveNext() { var status = TryMoveNext(out bool moved); NativeChecks.Status(status); return moved; }
        }
    }
}
