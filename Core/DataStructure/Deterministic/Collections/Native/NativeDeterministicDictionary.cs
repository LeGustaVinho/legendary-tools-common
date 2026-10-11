using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace LegendaryTools.Deterministic
{
    [NativeContainer, NativeContainerIsReadOnly]
    public unsafe struct NativeDictionarySnapshot<TKey, TValue> : IDisposable where TKey : unmanaged where TValue : unmanaged
    {
        internal NativeStorage<OrderedHashEntry<TKey, TValue>> Entries;
        internal NativeStorage<int> Buckets;
        internal OrderedMapState State;
        internal int EntrySlot, BucketSlot;
        internal ulong EntryLease, BucketLease;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif
        public bool IsCreated
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (Entries.IsCreated) AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                return Entries.ValidSnapshot(EntrySlot, EntryLease) && Buckets.ValidSnapshot(BucketSlot, BucketLease);
            }
        }
        public int Count => State.Count;
        public int Capacity => IsCreated ? Entries.Data->SnapshotCapacities[EntrySlot] : 0;
        public DeterministicStatus TryGetAt(int index, out KeyValuePair<TKey, TValue> pair)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Entries.IsCreated) AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            pair = default;
            if (!IsCreated) return DeterministicStatus.Disposed;
            if ((uint)index >= (uint)Count) return DeterministicStatus.OutOfRange;
            int encoded = State.OrderHead;
            for (int i = 0; i <= index; i++)
            {
                var entry = Entries.ReadSnapshot(EntrySlot, encoded - 1);
                if (i == index) { pair = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value); return DeterministicStatus.None; }
                encoded = entry.OrderNext;
            }
            return DeterministicStatus.InvalidEncoding;
        }
        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Entries.IsCreated) AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            Entries.ReleaseSnapshot(EntrySlot, EntryLease); Buckets.ReleaseSnapshot(BucketSlot, BucketLease);
            Entries = default; Buckets = default;
        }
    }

    [NativeContainer]
    public unsafe partial struct NativeDeterministicDictionary<TKey, TValue, TPolicy> : IDisposable
        where TKey : unmanaged where TValue : unmanaged where TPolicy : unmanaged, IDeterministicEquality<TKey>
    {
        internal NativeStorage<OrderedHashEntry<TKey, TValue>> Entries;
        internal NativeStorage<int> Buckets;
        private BufferOptions options;
        private TPolicy policy;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif
        public NativeDeterministicDictionary(int capacity, Allocator allocator, TPolicy policy = default) : this(new BufferOptions(capacity), allocator, policy) { }
        public NativeDeterministicDictionary(BufferOptions options, Allocator allocator, TPolicy policy = default)
        {
            this = default;
            if (!options.IsValid || OrderedHashRules.BucketCapacity(options.MaxCapacity) == 0 || !policy.IsValid)
                DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            this.options = options; this.policy = policy;
            Entries = NativeStorage<OrderedHashEntry<TKey, TValue>>.Create(options, allocator);
            try { Buckets = NativeStorage<int>.Create(OrderedHashRules.BucketOptions(options), allocator); }
            catch { Entries.Destroy(); throw; }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.Create();
            AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);
#endif
        }
        public bool IsCreated => Entries.IsCreated && Buckets.IsCreated;
        public int Count { get { CheckRead(); return IsCreated ? Entries.Data->MapState.Count : 0; } }
        public int Capacity { get { CheckRead(); return Entries.Capacity; } }
        public TPolicy KeyPolicy => policy;
        public TValue this[TKey key]
        {
            get { var status = TryGet(key, out TValue value); NativeChecks.Status(status); return value; }
            set { NativeChecks.Status(TrySet(key, value)); }
        }
        public DeterministicStatus TryGet(in TKey key, out TValue value)
        {
            CheckRead();
            return OrderedHashKernel<TKey, TValue, TPolicy, NativeStorage<OrderedHashEntry<TKey, TValue>>, NativeStorage<int>>.TryGet(ref Entries, ref Buckets, key, policy, out value);
        }
        public bool TryGetValue(TKey key, out TValue value) => TryGet(key, out value).IsSuccess();
        public bool ContainsKey(TKey key) => TryGetValue(key, out _);
        public DeterministicStatus TryAdd(in TKey key, in TValue value) => Put(key, value, true);
        public DeterministicStatus TrySet(in TKey key, in TValue value) => Put(key, value, false);
        private DeterministicStatus Put(in TKey key, in TValue value, bool addOnly)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            return OrderedHashKernel<TKey, TValue, TPolicy, NativeStorage<OrderedHashEntry<TKey, TValue>>, NativeStorage<int>>.TryPut(ref Entries, ref Buckets, ref Entries.Data->MapState, options, key, value, policy, addOnly);
        }
        public DeterministicStatus TryRemove(in TKey key)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            return OrderedHashKernel<TKey, TValue, TPolicy, NativeStorage<OrderedHashEntry<TKey, TValue>>, NativeStorage<int>>.TryRemove(ref Entries, ref Buckets, ref Entries.Data->MapState, key, policy);
        }
        public DeterministicStatus TryClear()
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            return OrderedHashKernel<TKey, TValue, TPolicy, NativeStorage<OrderedHashEntry<TKey, TValue>>, NativeStorage<int>>.TryClear(ref Entries, ref Buckets, ref Entries.Data->MapState);
        }
        public void Add(TKey key, TValue value) => NativeChecks.Status(TryAdd(key, value));
        public bool Remove(TKey key) { var status = TryRemove(key); if (status == DeterministicStatus.NotFound) return false; NativeChecks.Status(status); return status.IsSuccess(); }
        public void Clear() => NativeChecks.Status(TryClear());
        public DeterministicStatus TryCapture(out NativeDictionarySnapshot<TKey, TValue> snapshot)
        {
            CheckWrite(); snapshot = default;
            var status = Entries.TryCapture(out int entrySlot, out ulong entryLease); if (!status.IsSuccess()) return status;
            status = Buckets.TryCapture(out int bucketSlot, out ulong bucketLease);
            if (!status.IsSuccess()) { Entries.ReleaseSnapshot(entrySlot, entryLease); return status; }
            snapshot = new NativeDictionarySnapshot<TKey, TValue> { Entries = Entries, Buckets = Buckets,
                State = Entries.Data->MapState, EntrySlot = entrySlot, EntryLease = entryLease, BucketSlot = bucketSlot, BucketLease = bucketLease };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            snapshot.m_Safety = m_Safety;
#endif
            return DeterministicStatus.None;
        }
        public DeterministicStatus TryRestore(in NativeDictionarySnapshot<TKey, TValue> snapshot)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            if (snapshot.Entries.Data != Entries.Data || snapshot.Buckets.Data != Buckets.Data) return DeterministicStatus.InvalidOwner;
            if (!snapshot.IsCreated) return DeterministicStatus.Disposed;
            Entries.TryRestore(snapshot.EntrySlot, snapshot.EntryLease); Buckets.TryRestore(snapshot.BucketSlot, snapshot.BucketLease);
            Entries.Data->MapState = snapshot.State; return DeterministicStatus.None;
        }
        public Enumerator GetEnumerator()
        {
            CheckRead(); var enumerator = new Enumerator { Entries = Entries, Revision = Entries.Revision, Next = IsCreated ? Entries.Data->MapState.OrderHead : 0 };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            enumerator.m_Safety = m_Safety;
#endif
            return enumerator;
        }
        public void Dispose()
        {
            if (!IsCreated) return; CheckWrite();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(m_Safety);
#endif
            Entries.Destroy(); Buckets.Destroy();
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
            internal NativeStorage<OrderedHashEntry<TKey, TValue>> Entries;
            internal ulong Revision;
            internal int Next;
            private KeyValuePair<TKey, TValue> current;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif
            public KeyValuePair<TKey, TValue> Current => current;
            public DeterministicStatus TryMoveNext(out bool moved)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (Entries.IsCreated) AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                moved = false;
                if (!Entries.IsCreated) return DeterministicStatus.Disposed;
                if (Revision != Entries.Revision) return DeterministicStatus.Invalidated;
                if (Next == 0) return DeterministicStatus.None;
                var entry = Entries.Read(Next - 1); Next = entry.OrderNext;
                current = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value); moved = true; return DeterministicStatus.None;
            }
            public bool MoveNext() { var status = TryMoveNext(out bool moved); NativeChecks.Status(status); return moved; }
        }
    }
}
