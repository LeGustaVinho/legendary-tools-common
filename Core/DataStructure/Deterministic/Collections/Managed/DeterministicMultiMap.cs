using System;
using System.Collections;
using System.Collections.Generic;

namespace LegendaryTools.Deterministic
{
    public readonly struct ManagedMultiMapSnapshot<TKey, TValue> : IDisposable where TKey : unmanaged where TValue : unmanaged
    {
        internal readonly ManagedDictionarySnapshot<TKey, MultiMapChain> Keys;
        internal readonly ManagedSlotMapSnapshot<MultiMapValue<TKey, TValue>> Values;
        internal ManagedMultiMapSnapshot(ManagedDictionarySnapshot<TKey, MultiMapChain> keys, ManagedSlotMapSnapshot<MultiMapValue<TKey, TValue>> values) { Keys = keys; Values = values; }
        public bool IsCreated => Keys.IsCreated && Values.IsCreated;
        public int Count => Values.Count;
        public int KeyCount => Keys.Count;
        public DeterministicStatus TryGetPair(StableHandle handle, out KeyValuePair<TKey, TValue> pair)
        { var status = Values.TryGet(handle, out var node); pair = new KeyValuePair<TKey, TValue>(node.Key, node.Value); return status; }
        public void Dispose() { Keys.Dispose(); Values.Dispose(); }
    }
    public sealed partial class DeterministicMultiMap<TKey, TValue, TPolicy> : IEnumerable<KeyValuePair<TKey, TValue>>, IDisposable
        where TKey : unmanaged where TValue : unmanaged where TPolicy : unmanaged, IDeterministicEquality<TKey>
    {
        private readonly DeterministicDictionary<TKey, MultiMapChain, TPolicy> keys;
        private readonly DeterministicSlotMap<MultiMapValue<TKey, TValue>> values;
        private readonly BufferOptions options;
        private readonly TPolicy policy;
        private readonly IDeterministicEquality<TValue> valueEquality;
        public DeterministicMultiMap(int keyCapacity, int valueCapacity, TPolicy policy = default, IDeterministicEquality<TValue> valueEquality = null)
            : this(new BufferOptions(keyCapacity), new BufferOptions(valueCapacity), policy, valueEquality) { }
        public DeterministicMultiMap(BufferOptions keyOptions, BufferOptions valueOptions, TPolicy policy = default, IDeterministicEquality<TValue> valueEquality = null)
        {
            if (!keyOptions.IsValid || !valueOptions.IsValid || keyOptions.SnapshotSlots != valueOptions.SnapshotSlots || keyOptions.Snapshots != valueOptions.Snapshots || valueEquality != null && !valueEquality.IsValid)
                DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            options = keyOptions; this.policy = policy; this.valueEquality = valueEquality;
            keys = new DeterministicDictionary<TKey, MultiMapChain, TPolicy>(keyOptions, policy);
            try { values = new DeterministicSlotMap<MultiMapValue<TKey, TValue>>(valueOptions); } catch { keys.Dispose(); throw; }
        }
        public bool IsCreated => keys.IsCreated && values.IsCreated;
        public int Count => values.Count;
        public int KeyCount => keys.Count;
        public int KeyCapacity => keys.Capacity;
        public int ValueCapacity => values.Capacity;
        public DeterministicDictionary<TKey, MultiMapChain, TPolicy>.KeyCollection Keys => keys.Keys;
        public ValuesView this[TKey key] => GetValues(key);
        public bool ContainsKey(TKey key) => keys.ContainsKey(key);
        public DeterministicStatus TryAdd(in TKey key, in TValue value, out StableHandle handle)
            => MultiMapKernel<TKey, TValue, TPolicy, ManagedStorage<OrderedHashEntry<TKey, MultiMapChain>>, ManagedStorage<int>, ManagedStorage<SlotMapNode<MultiMapValue<TKey, TValue>>>>.TryAdd(
                ref keys.Entries, ref keys.Buckets, ref values.Storage, ref keys.State, ref values.State, options, key, value, policy, out handle);
        public StableHandle Add(TKey key, TValue value) { TryAdd(key, value, out var handle).ThrowIfFailure(); return handle; }
        public DeterministicStatus TryGetPair(StableHandle handle, out KeyValuePair<TKey, TValue> pair)
        { var status = values.TryGet(handle, out var node); pair = new KeyValuePair<TKey, TValue>(node.Key, node.Value); return status; }
        public DeterministicStatus TrySetValue(StableHandle handle, in TValue value)
        { var status = values.TryGet(handle, out var node); if (!status.IsSuccess()) return status; node.Value = value; return values.TrySet(handle, node); }
        public DeterministicStatus TryRemove(StableHandle handle)
            => MultiMapKernel<TKey, TValue, TPolicy, ManagedStorage<OrderedHashEntry<TKey, MultiMapChain>>, ManagedStorage<int>, ManagedStorage<SlotMapNode<MultiMapValue<TKey, TValue>>>>.TryRemove(
                ref keys.Entries, ref keys.Buckets, ref values.Storage, ref keys.State, ref values.State, options, handle, policy);
        public bool Remove(StableHandle handle) { var status = TryRemove(handle); if (status == DeterministicStatus.NotFound) return false; status.ThrowIfFailure(); return true; }
        public DeterministicStatus TryRemoveAll(in TKey key)
            => MultiMapKernel<TKey, TValue, TPolicy, ManagedStorage<OrderedHashEntry<TKey, MultiMapChain>>, ManagedStorage<int>, ManagedStorage<SlotMapNode<MultiMapValue<TKey, TValue>>>>.TryRemoveAll(
                ref keys.Entries, ref keys.Buckets, ref values.Storage, ref keys.State, ref values.State, options, key, policy);
        public bool RemoveAll(TKey key) { var status = TryRemoveAll(key); if (status == DeterministicStatus.NotFound) return false; status.ThrowIfFailure(); return true; }
        public bool Remove(TKey key, TValue value)
        {
            var equality = default(PrimitiveEquality<TValue>); if (valueEquality == null && !equality.IsValid) DeterministicStatus.UnsupportedType.ThrowIfFailure();
            var iterator = GetValues(key).GetEnumerator();
            while (iterator.MoveNext()) if (valueEquality == null ? equality.Equals(iterator.Current, value) : valueEquality.Equals(iterator.Current, value)) return Remove(iterator.Handle);
            return false;
        }
        public DeterministicStatus TryClear()
            => MultiMapKernel<TKey, TValue, TPolicy, ManagedStorage<OrderedHashEntry<TKey, MultiMapChain>>, ManagedStorage<int>, ManagedStorage<SlotMapNode<MultiMapValue<TKey, TValue>>>>.TryClear(
                ref keys.Entries, ref keys.Buckets, ref values.Storage, ref keys.State, ref values.State);
        public void Clear() => TryClear().ThrowIfFailure();
        public ValuesView GetValues(TKey key)
        {
            var status = keys.TryGet(key, out var chain); if (status != DeterministicStatus.NotFound) status.ThrowIfFailure();
            return new ValuesView(this, chain, values.Revision);
        }
        public DeterministicStatus TryCapture(out ManagedMultiMapSnapshot<TKey, TValue> snapshot)
        {
            snapshot = default; var status = keys.TryCapture(out var ks); if (!status.IsSuccess()) return status;
            status = values.TryCapture(out var vs); if (!status.IsSuccess()) { ks.Dispose(); return status; }
            snapshot = new ManagedMultiMapSnapshot<TKey, TValue>(ks, vs); return DeterministicStatus.None;
        }
        public DeterministicStatus TryRestore(in ManagedMultiMapSnapshot<TKey, TValue> snapshot)
        {
            if (!IsCreated) return DeterministicStatus.Disposed;
            if (snapshot.Keys.Entries.Owner != keys.Entries.Owner || snapshot.Keys.Buckets.Owner != keys.Buckets.Owner || snapshot.Values.Buffer.Owner != values.Storage.Owner) return DeterministicStatus.InvalidOwner;
            if (!snapshot.IsCreated) return DeterministicStatus.Disposed;
            keys.TryRestore(snapshot.Keys); values.TryRestore(snapshot.Values); return DeterministicStatus.None;
        }
        public Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void Dispose() { keys.Dispose(); values.Dispose(); }
        public readonly struct ValuesView : IEnumerable<TValue>
        {
            private readonly DeterministicMultiMap<TKey, TValue, TPolicy> owner;
            private readonly MultiMapChain chain;
            private readonly ulong revision;
            internal ValuesView(DeterministicMultiMap<TKey, TValue, TPolicy> owner, MultiMapChain chain, ulong revision) { this.owner = owner; this.chain = chain; this.revision = revision; }
            public int Count => chain.Count;
            public ValueEnumerator GetEnumerator() => new ValueEnumerator(owner, chain.First, revision);
            IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
        public struct ValueEnumerator : IEnumerator<TValue>
        {
            private readonly DeterministicMultiMap<TKey, TValue, TPolicy> owner;
            private readonly StableHandle first;
            private readonly ulong revision;
            private StableHandle next, handle;
            private TValue current;
            internal ValueEnumerator(DeterministicMultiMap<TKey, TValue, TPolicy> owner, StableHandle first, ulong revision) { this.owner = owner; this.first = first; next = first; this.revision = revision; handle = default; current = default; }
            public TValue Current => current;
            public StableHandle Handle => handle;
            object IEnumerator.Current => Current;
            public DeterministicStatus TryMoveNext(out bool moved)
            {
                moved = false; if (!owner.IsCreated) return DeterministicStatus.Disposed;
                if (owner.values.Revision != revision) return DeterministicStatus.Invalidated;
                if (!next.IsValid) return DeterministicStatus.None;
                handle = next; var node = owner.values.Storage.Read((int)next.Index).Value; current = node.Value; next = node.Next; moved = true; return DeterministicStatus.None;
            }
            public bool MoveNext() { TryMoveNext(out bool moved).ThrowIfFailure(); return moved; }
            public void Reset() { if (!owner.IsCreated) DeterministicStatus.Disposed.ThrowIfFailure(); if (owner.values.Revision != revision) DeterministicStatus.Invalidated.ThrowIfFailure(); next = first; }
            public void Dispose() { }
        }
        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
        {
            private readonly DeterministicMultiMap<TKey, TValue, TPolicy> owner;
            private readonly ulong revision;
            private DeterministicDictionary<TKey, MultiMapChain, TPolicy>.Enumerator keys;
            private StableHandle next;
            private KeyValuePair<TKey, TValue> current;
            internal Enumerator(DeterministicMultiMap<TKey, TValue, TPolicy> owner) { this.owner = owner; revision = owner.values.Revision; keys = owner.keys.GetEnumerator(); next = default; current = default; }
            public KeyValuePair<TKey, TValue> Current => current;
            object IEnumerator.Current => Current;
            public DeterministicStatus TryMoveNext(out bool moved)
            {
                moved = false; if (!owner.IsCreated) return DeterministicStatus.Disposed;
                if (owner.values.Revision != revision) return DeterministicStatus.Invalidated;
                if (!next.IsValid)
                { var status = keys.TryMoveNext(out bool keyMoved); if (!status.IsSuccess() || !keyMoved) return status; next = keys.Current.Value.First; }
                var node = owner.values.Storage.Read((int)next.Index).Value; current = new KeyValuePair<TKey, TValue>(node.Key, node.Value); next = node.Next; moved = true; return DeterministicStatus.None;
            }
            public bool MoveNext() { TryMoveNext(out bool moved).ThrowIfFailure(); return moved; }
            public void Reset() { if (!owner.IsCreated) DeterministicStatus.Disposed.ThrowIfFailure(); if (owner.values.Revision != revision) DeterministicStatus.Invalidated.ThrowIfFailure(); keys.Reset(); next = default; }
            public void Dispose() { }
        }
    }
}
