namespace LegendaryTools.Deterministic
{
    public struct SortedEntry<TKey, TValue> where TKey : unmanaged where TValue : unmanaged
    {
        public TKey Key;
        public TValue Value;
        public SortedEntry(TKey key, TValue value) { Key = key; Value = value; }
    }
    public static class SortedListKernel<TKey, TValue, TComparer, TBuffer>
        where TKey : unmanaged where TValue : unmanaged where TComparer : unmanaged, IDeterministicComparer<TKey>
        where TBuffer : struct, IDeterministicBuffer<SortedEntry<TKey, TValue>>
    {
        public static int LowerBound(ref TBuffer buffer, int count, in TKey key, in TComparer comparer)
        {
            int low = 0, high = count;
            while (low < high) { int mid = low + (high - low) / 2; var current = buffer.Read(mid); if (comparer.Compare(current.Key, key) < 0) low = mid + 1; else high = mid; }
            return low;
        }
        public static DeterministicStatus TryGet(ref TBuffer buffer, in SequenceState state, in TKey key, in TComparer comparer, out TValue value)
        {
            value = default; if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            int index = LowerBound(ref buffer, state.Count, key, comparer);
            if (index == state.Count) return DeterministicStatus.NotFound;
            var current = buffer.Read(index); if (comparer.Compare(current.Key, key) != 0) return DeterministicStatus.NotFound;
            value = current.Value; return DeterministicStatus.None;
        }
        public static DeterministicStatus TryPut(ref TBuffer buffer, ref SequenceState state, in TKey key, in TValue value, in TComparer comparer, bool addOnly)
        {
            if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            int index = LowerBound(ref buffer, state.Count, key, comparer);
            if (index < state.Count && comparer.Compare(buffer.Read(index).Key, key) == 0)
            {
                if (addOnly) return DeterministicStatus.DuplicateKey;
                var entry = buffer.Read(index); entry.Value = value;
                return SequenceKernel<SortedEntry<TKey, TValue>, TBuffer>.TrySet(ref buffer, state, index, entry);
            }
            return SequenceKernel<SortedEntry<TKey, TValue>, TBuffer>.TryInsert(ref buffer, ref state, index, new SortedEntry<TKey, TValue>(key, value));
        }
        public static DeterministicStatus TryRemove(ref TBuffer buffer, ref SequenceState state, in TKey key, in TComparer comparer)
        {
            if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            int index = LowerBound(ref buffer, state.Count, key, comparer);
            if (index == state.Count || comparer.Compare(buffer.Read(index).Key, key) != 0) return DeterministicStatus.NotFound;
            return SequenceKernel<SortedEntry<TKey, TValue>, TBuffer>.TryRemoveAt(ref buffer, ref state, index);
        }
    }
}
