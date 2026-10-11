using System.Collections.Generic;

namespace LegendaryTools.Deterministic
{
    public sealed partial class DeterministicSortedList<TKey, TValue, TComparer> : DeterministicSortedMapBase<TKey, TValue>
        where TKey : unmanaged where TValue : unmanaged where TComparer : unmanaged, IDeterministicComparer<TKey>
    {
        internal ManagedStorage<SortedEntry<TKey, TValue>> Storage;
        internal SequenceState State;
        private readonly TComparer comparer;
        public DeterministicSortedList(int capacity, TComparer comparer = default, IDeterministicEquality<TValue> valueEquality = null) : this(new BufferOptions(capacity), comparer, valueEquality) { }
        public DeterministicSortedList(BufferOptions options, TComparer comparer = default, IDeterministicEquality<TValue> valueEquality = null) : base(valueEquality)
        { if (!comparer.IsValid) DeterministicStatus.InvalidConfiguration.ThrowIfFailure(); this.comparer = comparer; Storage = new ManagedStorage<SortedEntry<TKey, TValue>>(options); }
        public override int Count => State.Count;
        public override int Capacity => Storage.Capacity;
        public override bool IsCreated => Storage.IsCreated;
        public override ulong Revision => Storage.Revision;
        public override DeterministicStatus TryAdd(in TKey key, in TValue value) => SortedListKernel<TKey, TValue, TComparer, ManagedStorage<SortedEntry<TKey, TValue>>>.TryPut(ref Storage, ref State, key, value, comparer, true);
        public override DeterministicStatus TrySet(in TKey key, in TValue value) => SortedListKernel<TKey, TValue, TComparer, ManagedStorage<SortedEntry<TKey, TValue>>>.TryPut(ref Storage, ref State, key, value, comparer, false);
        public override DeterministicStatus TryGet(in TKey key, out TValue value) => SortedListKernel<TKey, TValue, TComparer, ManagedStorage<SortedEntry<TKey, TValue>>>.TryGet(ref Storage, State, key, comparer, out value);
        public override DeterministicStatus TryRemove(in TKey key) => SortedListKernel<TKey, TValue, TComparer, ManagedStorage<SortedEntry<TKey, TValue>>>.TryRemove(ref Storage, ref State, key, comparer);
        public override DeterministicStatus TryClear() => SequenceKernel<SortedEntry<TKey, TValue>, ManagedStorage<SortedEntry<TKey, TValue>>>.TryClear(ref Storage, ref State);
        public override DeterministicStatus TryGetAt(int index, out KeyValuePair<TKey, TValue> item)
        { var status = SequenceKernel<SortedEntry<TKey, TValue>, ManagedStorage<SortedEntry<TKey, TValue>>>.TryGet(ref Storage, State, index, out var entry); item = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value); return status; }
        public int IndexOfKey(TKey key)
        { if (!IsCreated) DeterministicStatus.Disposed.ThrowIfFailure(); int index = SortedListKernel<TKey, TValue, TComparer, ManagedStorage<SortedEntry<TKey, TValue>>>.LowerBound(ref Storage, Count, key, comparer); return index < Count && comparer.Compare(Storage.Read(index).Key, key) == 0 ? index : -1; }
        public DeterministicStatus TryRemoveAt(int index) => SequenceKernel<SortedEntry<TKey, TValue>, ManagedStorage<SortedEntry<TKey, TValue>>>.TryRemoveAt(ref Storage, ref State, index);
        public void RemoveAt(int index) => TryRemoveAt(index).ThrowIfFailure();
        protected internal override int FirstCursor => 0;
        protected internal override bool ReadNext(ref int cursor, out KeyValuePair<TKey, TValue> item)
        { item = default; if (cursor >= Count) return false; var entry = Storage.Read(cursor++); item = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value); return true; }
        public DeterministicStatus TryCapture(out ManagedSequenceSnapshot<SortedEntry<TKey, TValue>> snapshot)
        { var status = Storage.TryCapture(out var buffer); snapshot = new ManagedSequenceSnapshot<SortedEntry<TKey, TValue>>(buffer, State, false); return status; }
        public DeterministicStatus TryRestore(in ManagedSequenceSnapshot<SortedEntry<TKey, TValue>> snapshot)
        { if (snapshot.Ring) return DeterministicStatus.InvalidOwner; var status = Storage.TryRestore(snapshot.Buffer); if (status.IsSuccess()) State = snapshot.State; return status; }
        public override void Dispose() { Storage.Dispose(); State = default; }
    }
}
