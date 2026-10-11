using System;
using System.Collections;
using System.Collections.Generic;

namespace LegendaryTools.Deterministic
{
    /// <summary>Managed collection conventions shared by contiguous sorted maps and AVL maps.</summary>
    public abstract class DeterministicSortedMapBase<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>, IDisposable
        where TKey : unmanaged where TValue : unmanaged
    {
        private readonly IDeterministicEquality<TValue> valueEquality;
        private readonly KeyCollection keys;
        private readonly ValueCollection values;
        protected DeterministicSortedMapBase(IDeterministicEquality<TValue> valueEquality)
        {
            if (valueEquality != null && !valueEquality.IsValid) DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            this.valueEquality = valueEquality; keys = new KeyCollection(this); values = new ValueCollection(this);
        }
        public abstract int Count { get; }
        public abstract int Capacity { get; }
        public abstract bool IsCreated { get; }
        public abstract ulong Revision { get; }
        public bool IsReadOnly => false;
        public KeyCollection Keys => keys;
        public ValueCollection Values => values;
        ICollection<TKey> IDictionary<TKey, TValue>.Keys => keys;
        ICollection<TValue> IDictionary<TKey, TValue>.Values => values;
        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => keys;
        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => values;
        public TValue this[TKey key]
        { get { TryGet(key, out TValue value).ThrowIfFailure(); return value; } set => TrySet(key, value).ThrowIfFailure(); }
        public abstract DeterministicStatus TryAdd(in TKey key, in TValue value);
        public abstract DeterministicStatus TrySet(in TKey key, in TValue value);
        public abstract DeterministicStatus TryGet(in TKey key, out TValue value);
        public abstract DeterministicStatus TryRemove(in TKey key);
        public abstract DeterministicStatus TryClear();
        public abstract DeterministicStatus TryGetAt(int index, out KeyValuePair<TKey, TValue> item);
        protected internal abstract int FirstCursor { get; }
        protected internal abstract bool ReadNext(ref int cursor, out KeyValuePair<TKey, TValue> item);
        public abstract void Dispose();
        public void Add(TKey key, TValue value) => TryAdd(key, value).ThrowIfFailure();
        public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);
        public bool ContainsKey(TKey key) => TryGet(key, out _).IsSuccess();
        public bool TryGetValue(TKey key, out TValue value) => TryGet(key, out value).IsSuccess();
        public bool Remove(TKey key) { var status = TryRemove(key); if (status == DeterministicStatus.NotFound) return false; status.ThrowIfFailure(); return true; }
        public void Clear() => TryClear().ThrowIfFailure();
        private bool EqualValues(in TValue a, in TValue b)
        {
            if (valueEquality != null) return valueEquality.Equals(a, b);
            var equality = default(PrimitiveEquality<TValue>); if (!equality.IsValid) DeterministicStatus.UnsupportedType.ThrowIfFailure(); return equality.Equals(a, b);
        }
        public bool Contains(KeyValuePair<TKey, TValue> item) => TryGetValue(item.Key, out var found) && EqualValues(found, item.Value);
        public bool Remove(KeyValuePair<TKey, TValue> item) => Contains(item) && Remove(item.Key);
        public bool ContainsValue(TValue value) { foreach (var item in this) if (EqualValues(item.Value, value)) return true; return false; }
        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int index)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (index < 0 || index > array.Length || Count > array.Length - index) throw new ArgumentOutOfRangeException(nameof(index));
            foreach (var item in this) array[index++] = item;
        }
        public Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
        {
            private readonly DeterministicSortedMapBase<TKey, TValue> owner;
            private readonly ulong revision;
            private int cursor;
            private KeyValuePair<TKey, TValue> current;
            internal Enumerator(DeterministicSortedMapBase<TKey, TValue> owner) { this.owner = owner; revision = owner.Revision; cursor = owner.FirstCursor; current = default; }
            public KeyValuePair<TKey, TValue> Current => current;
            object IEnumerator.Current => Current;
            public DeterministicStatus TryMoveNext(out bool moved)
            {
                moved = false; if (!owner.IsCreated) return DeterministicStatus.Disposed;
                if (revision != owner.Revision) return DeterministicStatus.Invalidated;
                moved = owner.ReadNext(ref cursor, out current); return DeterministicStatus.None;
            }
            public bool MoveNext() { TryMoveNext(out bool moved).ThrowIfFailure(); return moved; }
            public void Reset() { if (!owner.IsCreated) DeterministicStatus.Disposed.ThrowIfFailure(); if (owner.Revision != revision) DeterministicStatus.Invalidated.ThrowIfFailure(); cursor = owner.FirstCursor; }
            public void Dispose() { }
        }
        public sealed class KeyCollection : ICollection<TKey>, IReadOnlyCollection<TKey>
        {
            private readonly DeterministicSortedMapBase<TKey, TValue> owner;
            internal KeyCollection(DeterministicSortedMapBase<TKey, TValue> owner) { this.owner = owner; }
            public int Count => owner.Count;
            public bool IsReadOnly => true;
            public bool Contains(TKey item) => owner.ContainsKey(item);
            public void CopyTo(TKey[] array, int index) { CheckCopy(array, index, Count); foreach (var item in owner) array[index++] = item.Key; }
            public KeyEnumerator GetEnumerator() => new KeyEnumerator { Inner = owner.GetEnumerator() };
            IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            public void Add(TKey item) => throw new NotSupportedException();
            public bool Remove(TKey item) => throw new NotSupportedException();
            public void Clear() => throw new NotSupportedException();
        }
        public sealed class ValueCollection : ICollection<TValue>, IReadOnlyCollection<TValue>
        {
            private readonly DeterministicSortedMapBase<TKey, TValue> owner;
            internal ValueCollection(DeterministicSortedMapBase<TKey, TValue> owner) { this.owner = owner; }
            public int Count => owner.Count;
            public bool IsReadOnly => true;
            public bool Contains(TValue item) => owner.ContainsValue(item);
            public void CopyTo(TValue[] array, int index) { CheckCopy(array, index, Count); foreach (var item in owner) array[index++] = item.Value; }
            public ValueEnumerator GetEnumerator() => new ValueEnumerator { Inner = owner.GetEnumerator() };
            IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            public void Add(TValue item) => throw new NotSupportedException();
            public bool Remove(TValue item) => throw new NotSupportedException();
            public void Clear() => throw new NotSupportedException();
        }
        private static void CheckCopy<T>(T[] array, int index, int count)
        { if (array == null) throw new ArgumentNullException(nameof(array)); if (index < 0 || index > array.Length || count > array.Length - index) throw new ArgumentOutOfRangeException(nameof(index)); }
        public struct KeyEnumerator : IEnumerator<TKey>
        {
            internal Enumerator Inner;
            public TKey Current => Inner.Current.Key;
            object IEnumerator.Current => Current;
            public bool MoveNext() => Inner.MoveNext();
            public void Reset() => Inner.Reset();
            public void Dispose() { }
        }
        public struct ValueEnumerator : IEnumerator<TValue>
        {
            internal Enumerator Inner;
            public TValue Current => Inner.Current.Value;
            object IEnumerator.Current => Current;
            public bool MoveNext() => Inner.MoveNext();
            public void Reset() => Inner.Reset();
            public void Dispose() { }
        }
    }
}
