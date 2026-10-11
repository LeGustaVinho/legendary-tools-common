using System;
using System.Collections;
using System.Collections.Generic;

namespace LegendaryTools.Deterministic
{
    public readonly struct ManagedDictionarySnapshot<TKey, TValue> : IDisposable where TKey : unmanaged where TValue : unmanaged
    {
        internal readonly ManagedBufferSnapshot<OrderedHashEntry<TKey, TValue>> Entries;
        internal readonly ManagedBufferSnapshot<int> Buckets;
        internal readonly OrderedMapState State;
        internal ManagedDictionarySnapshot(ManagedBufferSnapshot<OrderedHashEntry<TKey, TValue>> entries, ManagedBufferSnapshot<int> buckets, OrderedMapState state)
        { Entries = entries; Buckets = buckets; State = state; }
        public bool IsCreated => Entries.IsCreated && Buckets.IsCreated;
        public int Count => State.Count;
        public int Capacity => Entries.Capacity;
        public DeterministicStatus TryGetAt(int index, out KeyValuePair<TKey, TValue> value)
        {
            value = default;
            if (!IsCreated) return DeterministicStatus.Disposed;
            if ((uint)index >= (uint)Count) return DeterministicStatus.OutOfRange;
            int encoded = State.OrderHead;
            for (int i = 0; i <= index; i++)
            {
                var status = Entries.TryGet(encoded - 1, out var entry); if (!status.IsSuccess()) return status;
                if (i == index) { value = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value); return DeterministicStatus.None; }
                encoded = entry.OrderNext;
            }
            return DeterministicStatus.InvalidEncoding;
        }
        public void Dispose() { Entries.Dispose(); Buckets.Dispose(); }
    }

    public sealed partial class DeterministicDictionary<TKey, TValue, TPolicy> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>, IDisposable
        where TKey : unmanaged where TValue : unmanaged where TPolicy : unmanaged, IDeterministicEquality<TKey>
    {
        internal ManagedStorage<OrderedHashEntry<TKey, TValue>> Entries;
        internal ManagedStorage<int> Buckets;
        internal OrderedMapState State;
        private readonly BufferOptions options;
        private readonly TPolicy policy;
        private readonly IDeterministicEquality<TValue> valueEquality;
        private readonly KeyCollection keys;
        private readonly ValueCollection values;
        public DeterministicDictionary(int capacity, TPolicy policy = default, IDeterministicEquality<TValue> valueEquality = null)
            : this(new BufferOptions(capacity), policy, valueEquality) { }
        public DeterministicDictionary(BufferOptions options, TPolicy policy = default, IDeterministicEquality<TValue> valueEquality = null)
        {
            if (!options.IsValid || OrderedHashRules.BucketCapacity(options.MaxCapacity) == 0 || !policy.IsValid || valueEquality != null && !valueEquality.IsValid)
                DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            this.options = options; this.policy = policy; this.valueEquality = valueEquality;
            Entries = new ManagedStorage<OrderedHashEntry<TKey, TValue>>(options);
            Buckets = new ManagedStorage<int>(OrderedHashRules.BucketOptions(options));
            keys = new KeyCollection(this); values = new ValueCollection(this);
        }
        public int Count => State.Count;
        public int Capacity => Entries.Capacity;
        public bool IsCreated => Entries.IsCreated && Buckets.IsCreated;
        public bool IsReadOnly => false;
        public KeyCollection Keys => keys;
        public ValueCollection Values => values;
        ICollection<TKey> IDictionary<TKey, TValue>.Keys => keys;
        ICollection<TValue> IDictionary<TKey, TValue>.Values => values;
        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => keys;
        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => values;
        public TValue this[TKey key]
        {
            get { TryGet(key, out TValue value).ThrowIfFailure(); return value; }
            set { TrySet(key, value).ThrowIfFailure(); }
        }
        public DeterministicStatus TryGet(in TKey key, out TValue value) =>
            OrderedHashKernel<TKey, TValue, TPolicy, ManagedStorage<OrderedHashEntry<TKey, TValue>>, ManagedStorage<int>>.TryGet(ref Entries, ref Buckets, key, policy, out value);
        public bool TryGetValue(TKey key, out TValue value) => TryGet(key, out value).IsSuccess();
        public bool ContainsKey(TKey key) => TryGetValue(key, out _);
        public DeterministicStatus TryAdd(in TKey key, in TValue value) => Put(key, value, true);
        public DeterministicStatus TrySet(in TKey key, in TValue value) => Put(key, value, false);
        private DeterministicStatus Put(in TKey key, in TValue value, bool addOnly) =>
            OrderedHashKernel<TKey, TValue, TPolicy, ManagedStorage<OrderedHashEntry<TKey, TValue>>, ManagedStorage<int>>.TryPut(ref Entries, ref Buckets, ref State, options, key, value, policy, addOnly);
        public DeterministicStatus TryRemove(in TKey key) =>
            OrderedHashKernel<TKey, TValue, TPolicy, ManagedStorage<OrderedHashEntry<TKey, TValue>>, ManagedStorage<int>>.TryRemove(ref Entries, ref Buckets, ref State, key, policy);
        public DeterministicStatus TryClear() =>
            OrderedHashKernel<TKey, TValue, TPolicy, ManagedStorage<OrderedHashEntry<TKey, TValue>>, ManagedStorage<int>>.TryClear(ref Entries, ref Buckets, ref State);
        public void Add(TKey key, TValue value) => TryAdd(key, value).ThrowIfFailure();
        public bool Remove(TKey key)
        { var status = TryRemove(key); if (status == DeterministicStatus.NotFound) return false; status.ThrowIfFailure(); return true; }
        public void Clear() => TryClear().ThrowIfFailure();
        public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);
        private bool ValueEquals(in TValue left, in TValue right)
        {
            if (valueEquality != null) return valueEquality.Equals(left, right);
            var primitive = new PrimitiveEquality<TValue>();
            if (!primitive.IsValid) DeterministicStatus.UnsupportedType.ThrowIfFailure();
            return primitive.Equals(left, right);
        }
        public bool Contains(KeyValuePair<TKey, TValue> item) => TryGetValue(item.Key, out var value) && ValueEquals(value, item.Value);
        public bool Remove(KeyValuePair<TKey, TValue> item) => Contains(item) && Remove(item.Key);
        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            ValidateCopy(array, arrayIndex, Count);
            foreach (var pair in this) array[arrayIndex++] = pair;
        }
        private static void ValidateCopy<TElement>(TElement[] array, int index, int count)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (index < 0 || index > array.Length - count) throw new ArgumentOutOfRangeException(nameof(index));
        }
        public DeterministicStatus TryCapture(out ManagedDictionarySnapshot<TKey, TValue> snapshot)
        {
            snapshot = default; var status = Entries.TryCapture(out var entries); if (!status.IsSuccess()) return status;
            status = Buckets.TryCapture(out var buckets);
            if (!status.IsSuccess()) { entries.Dispose(); return status; }
            snapshot = new ManagedDictionarySnapshot<TKey, TValue>(entries, buckets, State); return DeterministicStatus.None;
        }
        public DeterministicStatus TryRestore(in ManagedDictionarySnapshot<TKey, TValue> snapshot)
        {
            if (!IsCreated) return DeterministicStatus.Disposed;
            if (!ReferenceEquals(snapshot.Entries.Owner, Entries.Owner) || !ReferenceEquals(snapshot.Buckets.Owner, Buckets.Owner)) return DeterministicStatus.InvalidOwner;
            if (!snapshot.IsCreated) return DeterministicStatus.Disposed;
            Entries.TryRestore(snapshot.Entries); Buckets.TryRestore(snapshot.Buckets); State = snapshot.State; return DeterministicStatus.None;
        }
        public void Dispose() { Entries.Dispose(); Buckets.Dispose(); State = default; }
        public Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
        {
            private readonly DeterministicDictionary<TKey, TValue, TPolicy> owner;
            private readonly ulong revision;
            private int next, currentIndex;
            private KeyValuePair<TKey, TValue> current;
            internal Enumerator(DeterministicDictionary<TKey, TValue, TPolicy> owner)
            { this.owner = owner; revision = owner.Entries.Revision; next = owner.State.OrderHead; currentIndex = -1; current = default; }
            public KeyValuePair<TKey, TValue> Current => currentIndex < 0 ? throw new InvalidOperationException() : current;
            object IEnumerator.Current => Current;
            public DeterministicStatus TryMoveNext(out bool moved)
            {
                moved = false;
                if (!owner.IsCreated) return DeterministicStatus.Disposed;
                if (revision != owner.Entries.Revision) return DeterministicStatus.Invalidated;
                currentIndex = next - 1;
                if (next == 0) return DeterministicStatus.None;
                var entry = owner.Entries.Read(currentIndex); next = entry.OrderNext;
                current = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value); moved = true; return DeterministicStatus.None;
            }
            public bool MoveNext() { TryMoveNext(out bool moved).ThrowIfFailure(); return moved; }
            public void Reset()
            {
                if (!owner.IsCreated) DeterministicStatus.Disposed.ThrowIfFailure();
                if (revision != owner.Entries.Revision) DeterministicStatus.Invalidated.ThrowIfFailure();
                next = owner.State.OrderHead; currentIndex = -1;
            }
            public void Dispose() { }
        }
        public sealed class KeyCollection : ICollection<TKey>
        {
            private readonly DeterministicDictionary<TKey, TValue, TPolicy> owner;
            internal KeyCollection(DeterministicDictionary<TKey, TValue, TPolicy> owner) { this.owner = owner; }
            public int Count => owner.Count;
            public bool IsReadOnly => true;
            public bool Contains(TKey item) => owner.ContainsKey(item);
            public void CopyTo(TKey[] array, int index) { ValidateCopy(array, index, Count); foreach (var pair in owner) array[index++] = pair.Key; }
            public KeyEnumerator GetEnumerator() => new KeyEnumerator(owner.GetEnumerator());
            IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            public void Add(TKey item) => throw new NotSupportedException();
            public void Clear() => throw new NotSupportedException();
            public bool Remove(TKey item) => throw new NotSupportedException();
        }
        public sealed class ValueCollection : ICollection<TValue>
        {
            private readonly DeterministicDictionary<TKey, TValue, TPolicy> owner;
            internal ValueCollection(DeterministicDictionary<TKey, TValue, TPolicy> owner) { this.owner = owner; }
            public int Count => owner.Count;
            public bool IsReadOnly => true;
            public bool Contains(TValue item) { foreach (var pair in owner) if (owner.ValueEquals(pair.Value, item)) return true; return false; }
            public void CopyTo(TValue[] array, int index) { ValidateCopy(array, index, Count); foreach (var pair in owner) array[index++] = pair.Value; }
            public ValueEnumerator GetEnumerator() => new ValueEnumerator(owner.GetEnumerator());
            IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            public void Add(TValue item) => throw new NotSupportedException();
            public void Clear() => throw new NotSupportedException();
            public bool Remove(TValue item) => throw new NotSupportedException();
        }
        public struct KeyEnumerator : IEnumerator<TKey>
        {
            private Enumerator inner;
            internal KeyEnumerator(Enumerator inner) { this.inner = inner; }
            public TKey Current => inner.Current.Key;
            object IEnumerator.Current => Current;
            public bool MoveNext() => inner.MoveNext();
            public void Reset() => inner.Reset();
            public void Dispose() { }
        }
        public struct ValueEnumerator : IEnumerator<TValue>
        {
            private Enumerator inner;
            internal ValueEnumerator(Enumerator inner) { this.inner = inner; }
            public TValue Current => inner.Current.Value;
            object IEnumerator.Current => Current;
            public bool MoveNext() => inner.MoveNext();
            public void Reset() => inner.Reset();
            public void Dispose() { }
        }
    }
}
