using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace LegendaryTools.Deterministic
{
    [NativeContainer, NativeContainerIsReadOnly]
    public unsafe struct NativeMultiMapSnapshot<TKey, TValue> : IDisposable where TKey : unmanaged where TValue : unmanaged
    {
        internal NativeStorage<OrderedHashEntry<TKey, MultiMapChain>> Entries;
        internal NativeStorage<int> Buckets;
        internal NativeStorage<SlotMapNode<MultiMapValue<TKey, TValue>>> Values;
        internal OrderedMapState Map;
        internal SlotMapState Slots;
        internal int EntrySlot, BucketSlot, ValueSlot;
        internal ulong EntryLease, BucketLease, ValueLease;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif
        public bool IsCreated
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (Values.IsCreated) AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                return Entries.ValidSnapshot(EntrySlot, EntryLease) && Buckets.ValidSnapshot(BucketSlot, BucketLease) && Values.ValidSnapshot(ValueSlot, ValueLease);
            }
        }
        public int Count => Slots.Count;
        public int KeyCount => Map.Count;
        public DeterministicStatus TryGetPair(StableHandle handle, out KeyValuePair<TKey, TValue> pair)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Values.IsCreated) AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            pair = default; if (!IsCreated) return DeterministicStatus.Disposed;
            if (!handle.IsValid || handle.Index >= (uint)Slots.Used) return DeterministicStatus.NotFound;
            var node = Values.ReadSnapshot(ValueSlot, (int)handle.Index);
            if (node.Active == 0 || node.Generation != handle.Generation) return DeterministicStatus.NotFound;
            pair = new KeyValuePair<TKey, TValue>(node.Value.Key, node.Value.Value); return DeterministicStatus.None;
        }
        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Values.IsCreated) AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            Entries.ReleaseSnapshot(EntrySlot, EntryLease); Buckets.ReleaseSnapshot(BucketSlot, BucketLease); Values.ReleaseSnapshot(ValueSlot, ValueLease);
            Entries = default; Buckets = default; Values = default;
        }
    }
    [NativeContainer]
    public unsafe partial struct NativeDeterministicMultiMap<TKey, TValue, TPolicy> : IDisposable
        where TKey : unmanaged where TValue : unmanaged where TPolicy : unmanaged, IDeterministicEquality<TKey>
    {
        internal NativeStorage<OrderedHashEntry<TKey, MultiMapChain>> Entries;
        internal NativeStorage<int> Buckets;
        internal NativeStorage<SlotMapNode<MultiMapValue<TKey, TValue>>> Values;
        private BufferOptions options;
        private TPolicy policy;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif
        public NativeDeterministicMultiMap(int keyCapacity, int valueCapacity, Allocator allocator, TPolicy policy = default)
            : this(new BufferOptions(keyCapacity), new BufferOptions(valueCapacity), allocator, policy) { }
        public NativeDeterministicMultiMap(BufferOptions keyOptions, BufferOptions valueOptions, Allocator allocator, TPolicy policy = default)
        {
            this = default;
            if (!keyOptions.IsValid || !valueOptions.IsValid || !policy.IsValid || OrderedHashRules.BucketCapacity(keyOptions.MaxCapacity) == 0 ||
                keyOptions.SnapshotSlots != valueOptions.SnapshotSlots || keyOptions.Snapshots != valueOptions.Snapshots) DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            options = keyOptions; this.policy = policy;
            Entries = NativeStorage<OrderedHashEntry<TKey, MultiMapChain>>.Create(keyOptions, allocator);
            try
            { Buckets = NativeStorage<int>.Create(OrderedHashRules.BucketOptions(keyOptions), allocator); Values = NativeStorage<SlotMapNode<MultiMapValue<TKey, TValue>>>.Create(valueOptions, allocator); }
            catch { Entries.Destroy(); Buckets.Destroy(); throw; }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.Create(); AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);
#endif
        }
        public bool IsCreated => Entries.IsCreated && Buckets.IsCreated && Values.IsCreated;
        public int Count { get { CheckRead(); return IsCreated ? Values.Data->SlotState.Count : 0; } }
        public int KeyCount { get { CheckRead(); return IsCreated ? Entries.Data->MapState.Count : 0; } }
        public int KeyCapacity { get { CheckRead(); return Entries.Capacity; } }
        public int ValueCapacity { get { CheckRead(); return Values.Capacity; } }
        public ValuesView this[TKey key] => GetValues(key);
        public bool ContainsKey(TKey key)
        { CheckRead(); return MultiMapKernel<TKey, TValue, TPolicy, NativeStorage<OrderedHashEntry<TKey, MultiMapChain>>, NativeStorage<int>, NativeStorage<SlotMapNode<MultiMapValue<TKey, TValue>>>>.TryGetChain(ref Entries, ref Buckets, key, policy, out _).IsSuccess(); }
        public DeterministicStatus TryAdd(in TKey key, in TValue value, out StableHandle handle)
        {
            CheckWrite(); handle = default; if (!IsCreated) return DeterministicStatus.Disposed;
            return MultiMapKernel<TKey, TValue, TPolicy, NativeStorage<OrderedHashEntry<TKey, MultiMapChain>>, NativeStorage<int>, NativeStorage<SlotMapNode<MultiMapValue<TKey, TValue>>>>.TryAdd(
                ref Entries, ref Buckets, ref Values, ref Entries.Data->MapState, ref Values.Data->SlotState, options, key, value, policy, out handle);
        }
        public StableHandle Add(TKey key, TValue value) { var status = TryAdd(key, value, out var handle); NativeChecks.Status(status); return handle; }
        public DeterministicStatus TryGetPair(StableHandle handle, out KeyValuePair<TKey, TValue> pair)
        {
            CheckRead(); pair = default; if (!IsCreated) return DeterministicStatus.Disposed;
            var status = SlotMapKernel<MultiMapValue<TKey, TValue>, NativeStorage<SlotMapNode<MultiMapValue<TKey, TValue>>>>.TryGet(ref Values, Values.Data->SlotState, handle, out var node);
            pair = new KeyValuePair<TKey, TValue>(node.Key, node.Value); return status;
        }
        public DeterministicStatus TrySetValue(StableHandle handle, in TValue value)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            var status = SlotMapKernel<MultiMapValue<TKey, TValue>, NativeStorage<SlotMapNode<MultiMapValue<TKey, TValue>>>>.TryGet(ref Values, Values.Data->SlotState, handle, out var node);
            if (!status.IsSuccess()) return status; node.Value = value;
            return SlotMapKernel<MultiMapValue<TKey, TValue>, NativeStorage<SlotMapNode<MultiMapValue<TKey, TValue>>>>.TrySet(ref Values, Values.Data->SlotState, handle, node);
        }
        public DeterministicStatus TryRemove(StableHandle handle)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            return MultiMapKernel<TKey, TValue, TPolicy, NativeStorage<OrderedHashEntry<TKey, MultiMapChain>>, NativeStorage<int>, NativeStorage<SlotMapNode<MultiMapValue<TKey, TValue>>>>.TryRemove(
                ref Entries, ref Buckets, ref Values, ref Entries.Data->MapState, ref Values.Data->SlotState, options, handle, policy);
        }
        public bool Remove(StableHandle handle) { var status = TryRemove(handle); if (status == DeterministicStatus.NotFound) return false; NativeChecks.Status(status); return status.IsSuccess(); }
        public DeterministicStatus TryRemoveAll(in TKey key)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            return MultiMapKernel<TKey, TValue, TPolicy, NativeStorage<OrderedHashEntry<TKey, MultiMapChain>>, NativeStorage<int>, NativeStorage<SlotMapNode<MultiMapValue<TKey, TValue>>>>.TryRemoveAll(
                ref Entries, ref Buckets, ref Values, ref Entries.Data->MapState, ref Values.Data->SlotState, options, key, policy);
        }
        public bool RemoveAll(TKey key) { var status = TryRemoveAll(key); if (status == DeterministicStatus.NotFound) return false; NativeChecks.Status(status); return status.IsSuccess(); }
        public DeterministicStatus TryRemoveValue<TEquality>(in TKey key, in TValue value, TEquality equality) where TEquality : unmanaged, IDeterministicEquality<TValue>
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed; if (!equality.IsValid) return DeterministicStatus.UnsupportedType;
            var iterator = GetValues(key).GetEnumerator(); while (iterator.MoveNext()) if (equality.Equals(iterator.Current, value)) return TryRemove(iterator.Handle);
            return DeterministicStatus.NotFound;
        }
        public DeterministicStatus TryClear()
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            return MultiMapKernel<TKey, TValue, TPolicy, NativeStorage<OrderedHashEntry<TKey, MultiMapChain>>, NativeStorage<int>, NativeStorage<SlotMapNode<MultiMapValue<TKey, TValue>>>>.TryClear(
                ref Entries, ref Buckets, ref Values, ref Entries.Data->MapState, ref Values.Data->SlotState);
        }
        public void Clear() => NativeChecks.Status(TryClear());
        public ValuesView GetValues(TKey key)
        {
            var status = TryGetValues(key, out var view); if (status != DeterministicStatus.NotFound) NativeChecks.Status(status); return view;
        }
        public DeterministicStatus TryGetValues(in TKey key, out ValuesView view)
        {
            CheckRead(); view = default; if (!IsCreated) return DeterministicStatus.Disposed;
            var status = MultiMapKernel<TKey, TValue, TPolicy, NativeStorage<OrderedHashEntry<TKey, MultiMapChain>>, NativeStorage<int>, NativeStorage<SlotMapNode<MultiMapValue<TKey, TValue>>>>.TryGetChain(ref Entries, ref Buckets, key, policy, out var chain);
            view = new ValuesView { Values = Values, First = chain.First, Length = chain.Count, Revision = Values.Revision };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            view.m_Safety = m_Safety;
#endif
            return status;
        }
        public DeterministicStatus TryCapture(out NativeMultiMapSnapshot<TKey, TValue> snapshot)
        {
            CheckWrite(); snapshot = default;
            var status = Entries.TryCapture(out int es, out ulong el); if (!status.IsSuccess()) return status;
            status = Buckets.TryCapture(out int bs, out ulong bl); if (!status.IsSuccess()) { Entries.ReleaseSnapshot(es, el); return status; }
            status = Values.TryCapture(out int vs, out ulong vl); if (!status.IsSuccess()) { Entries.ReleaseSnapshot(es, el); Buckets.ReleaseSnapshot(bs, bl); return status; }
            snapshot = new NativeMultiMapSnapshot<TKey, TValue> { Entries = Entries, Buckets = Buckets, Values = Values, Map = Entries.Data->MapState, Slots = Values.Data->SlotState,
                EntrySlot = es, EntryLease = el, BucketSlot = bs, BucketLease = bl, ValueSlot = vs, ValueLease = vl };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            snapshot.m_Safety = m_Safety;
#endif
            return DeterministicStatus.None;
        }
        public DeterministicStatus TryRestore(in NativeMultiMapSnapshot<TKey, TValue> snapshot)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            if (snapshot.Entries.Data != Entries.Data || snapshot.Buckets.Data != Buckets.Data || snapshot.Values.Data != Values.Data) return DeterministicStatus.InvalidOwner;
            if (!snapshot.IsCreated) return DeterministicStatus.Disposed;
            Entries.TryRestore(snapshot.EntrySlot, snapshot.EntryLease); Buckets.TryRestore(snapshot.BucketSlot, snapshot.BucketLease); Values.TryRestore(snapshot.ValueSlot, snapshot.ValueLease);
            Entries.Data->MapState = snapshot.Map; Values.Data->SlotState = snapshot.Slots; return DeterministicStatus.None;
        }
        public Enumerator GetEnumerator()
        {
            CheckRead(); var iterator = new Enumerator { Entries = Entries, Values = Values, EntryRevision = Entries.Revision, ValueRevision = Values.Revision, NextKey = IsCreated ? Entries.Data->MapState.OrderHead : 0 };
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
            Entries.Destroy(); Buckets.Destroy(); Values.Destroy();
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
        [NativeContainer, NativeContainerIsReadOnly]
        public struct ValuesView
        {
            internal NativeStorage<SlotMapNode<MultiMapValue<TKey, TValue>>> Values;
            internal StableHandle First;
            internal int Length;
            internal ulong Revision;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif
            public int Count => Length;
            public ValueEnumerator GetEnumerator()
            {
                var iterator = new ValueEnumerator { Values = Values, Next = First, Revision = Revision };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                iterator.m_Safety = m_Safety;
#endif
                return iterator;
            }
        }
        public struct ValueEnumerator
        {
            internal NativeStorage<SlotMapNode<MultiMapValue<TKey, TValue>>> Values;
            internal StableHandle Next;
            internal ulong Revision;
            private TValue current;
            private StableHandle handle;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif
            public TValue Current => current;
            public StableHandle Handle => handle;
            public DeterministicStatus TryMoveNext(out bool moved)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (Values.IsCreated) AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                moved = false; if (!Values.IsCreated) return DeterministicStatus.Disposed;
                if (Values.Revision != Revision) return DeterministicStatus.Invalidated;
                if (!Next.IsValid) return DeterministicStatus.None;
                handle = Next; var node = Values.Read((int)Next.Index).Value; current = node.Value; Next = node.Next; moved = true; return DeterministicStatus.None;
            }
            public bool MoveNext() { var status = TryMoveNext(out bool moved); NativeChecks.Status(status); return moved; }
        }
        public struct Enumerator
        {
            internal NativeStorage<OrderedHashEntry<TKey, MultiMapChain>> Entries;
            internal NativeStorage<SlotMapNode<MultiMapValue<TKey, TValue>>> Values;
            internal ulong EntryRevision, ValueRevision;
            internal int NextKey;
            private StableHandle next;
            private KeyValuePair<TKey, TValue> current;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif
            public KeyValuePair<TKey, TValue> Current => current;
            public DeterministicStatus TryMoveNext(out bool moved)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (Values.IsCreated) AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                moved = false; if (!Entries.IsCreated || !Values.IsCreated) return DeterministicStatus.Disposed;
                if (Entries.Revision != EntryRevision || Values.Revision != ValueRevision) return DeterministicStatus.Invalidated;
                if (!next.IsValid)
                { if (NextKey == 0) return DeterministicStatus.None; var key = Entries.Read(NextKey - 1); NextKey = key.OrderNext; next = key.Value.First; }
                var node = Values.Read((int)next.Index).Value; current = new KeyValuePair<TKey, TValue>(node.Key, node.Value); next = node.Next; moved = true; return DeterministicStatus.None;
            }
            public bool MoveNext() { var status = TryMoveNext(out bool moved); NativeChecks.Status(status); return moved; }
        }
    }
}
