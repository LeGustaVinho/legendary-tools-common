using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;

namespace LegendaryTools.Deterministic
{
    [NativeContainer, NativeContainerIsReadOnly]
    public unsafe struct NativeAvlSnapshot<TKey, TValue> : IDisposable where TKey : unmanaged where TValue : unmanaged
    {
        internal NativeStorage<AvlNode<TKey, TValue>> Storage;
        internal AvlState State;
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
        public DeterministicStatus TryGetAt(int index, out KeyValuePair<TKey, TValue> item)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Storage.IsCreated) AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            item = default; if (!IsCreated) return DeterministicStatus.Disposed;
            if ((uint)index >= (uint)Count) return DeterministicStatus.OutOfRange;
            int root = State.Root;
            while (root != 0)
            {
                var node = Storage.ReadSnapshot(Slot, root - 1);
                int size = node.Left == 0 ? 0 : Storage.ReadSnapshot(Slot, node.Left - 1).Size;
                if (index == size) { item = new KeyValuePair<TKey, TValue>(node.Key, node.Value); return DeterministicStatus.None; }
                if (index < size) root = node.Left; else { root = node.Right; index -= size + 1; }
            }
            return DeterministicStatus.InvalidEncoding;
        }
        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Storage.IsCreated) AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            Storage.ReleaseSnapshot(Slot, Lease); Storage = default;
        }
    }
}
