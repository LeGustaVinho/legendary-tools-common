using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace LegendaryTools.Deterministic
{
    [NativeContainer]
    public unsafe partial struct NativeDeterministicHashSet<T, TPolicy> : IDisposable
        where T : unmanaged where TPolicy : unmanaged, IDeterministicEquality<T>
    {
        internal NativeStorage<OrderedHashEntry<T, byte>> Entries;
        internal NativeStorage<int> Buckets;
        private BufferOptions options;
        private TPolicy policy;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif
        public NativeDeterministicHashSet(int capacity, Allocator allocator, TPolicy policy = default) : this(new BufferOptions(capacity), allocator, policy) { }
        public NativeDeterministicHashSet(BufferOptions options, Allocator allocator, TPolicy policy = default)
        {
            this = default;
            if (!options.IsValid || OrderedHashRules.BucketCapacity(options.MaxCapacity) == 0 || !policy.IsValid)
                DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            this.options = options; this.policy = policy;
            Entries = NativeStorage<OrderedHashEntry<T, byte>>.Create(options, allocator);
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
        public DeterministicStatus TryAdd(in T item)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed; byte zero = 0;
            return OrderedHashKernel<T, byte, TPolicy, NativeStorage<OrderedHashEntry<T, byte>>, NativeStorage<int>>.TryPut(ref Entries, ref Buckets, ref Entries.Data->MapState, options, item, zero, policy, true);
        }
        public bool Add(T item)
        { var status = TryAdd(item); if (status == DeterministicStatus.DuplicateKey) return false; NativeChecks.Status(status); return status.IsSuccess(); }
        public bool Contains(T item)
        {
            CheckRead();
            return OrderedHashKernel<T, byte, TPolicy, NativeStorage<OrderedHashEntry<T, byte>>, NativeStorage<int>>.TryGet(ref Entries, ref Buckets, item, policy, out _).IsSuccess();
        }
        public DeterministicStatus TryRemove(in T item)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            return OrderedHashKernel<T, byte, TPolicy, NativeStorage<OrderedHashEntry<T, byte>>, NativeStorage<int>>.TryRemove(ref Entries, ref Buckets, ref Entries.Data->MapState, item, policy);
        }
        public bool Remove(T item)
        { var status = TryRemove(item); if (status == DeterministicStatus.NotFound) return false; NativeChecks.Status(status); return status.IsSuccess(); }
        public DeterministicStatus TryClear()
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            return OrderedHashKernel<T, byte, TPolicy, NativeStorage<OrderedHashEntry<T, byte>>, NativeStorage<int>>.TryClear(ref Entries, ref Buckets, ref Entries.Data->MapState);
        }
        public void Clear() => NativeChecks.Status(TryClear());
        public DeterministicStatus TryCapture(out NativeDictionarySnapshot<T, byte> snapshot)
        {
            CheckWrite(); snapshot = default;
            var status = Entries.TryCapture(out int entrySlot, out ulong entryLease); if (!status.IsSuccess()) return status;
            status = Buckets.TryCapture(out int bucketSlot, out ulong bucketLease);
            if (!status.IsSuccess()) { Entries.ReleaseSnapshot(entrySlot, entryLease); return status; }
            snapshot = new NativeDictionarySnapshot<T, byte> { Entries = Entries, Buckets = Buckets,
                State = Entries.Data->MapState, EntrySlot = entrySlot, EntryLease = entryLease, BucketSlot = bucketSlot, BucketLease = bucketLease };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            snapshot.m_Safety = m_Safety;
#endif
            return DeterministicStatus.None;
        }
        public DeterministicStatus TryRestore(in NativeDictionarySnapshot<T, byte> snapshot)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            if (snapshot.Entries.Data != Entries.Data || snapshot.Buckets.Data != Buckets.Data) return DeterministicStatus.InvalidOwner;
            if (!snapshot.IsCreated) return DeterministicStatus.Disposed;
            Entries.TryRestore(snapshot.EntrySlot, snapshot.EntryLease); Buckets.TryRestore(snapshot.BucketSlot, snapshot.BucketLease);
            Entries.Data->MapState = snapshot.State; return DeterministicStatus.None;
        }
        public Enumerator GetEnumerator()
        {
            CheckRead();
            var inner = new NativeDeterministicDictionary<T, byte, TPolicy>.Enumerator { Entries = Entries, Revision = Entries.Revision, Next = IsCreated ? Entries.Data->MapState.OrderHead : 0 };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            inner.m_Safety = m_Safety;
#endif
            return new Enumerator { Inner = inner };
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
            internal NativeDeterministicDictionary<T, byte, TPolicy>.Enumerator Inner;
            public T Current => Inner.Current.Key;
            public DeterministicStatus TryMoveNext(out bool moved) => Inner.TryMoveNext(out moved);
            public bool MoveNext() => Inner.MoveNext();
        }
    }
}
