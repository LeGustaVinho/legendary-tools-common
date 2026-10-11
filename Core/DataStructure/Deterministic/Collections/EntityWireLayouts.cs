namespace LegendaryTools.Deterministic
{
    public unsafe struct LinkedListWireLayout<T> : IDeterministicWireLayout<SlotMapNode<LinkedListValue<T>>, LinkedListWireState> where T : unmanaged
    {
        public int ItemCount(in LinkedListWireState state) => state.Slots.Used;
        public int PhysicalIndex(in LinkedListWireState state, int capacity, int logicalIndex) => logicalIndex;
        public DeterministicStatus ValidateMetadata(in LinkedListWireState state, int capacity)
        {
            var status = default(SlotMapWireLayout<LinkedListValue<T>>).ValidateMetadata(state.Slots, capacity); if (!status.IsSuccess()) return status;
            if (state.Slots.Count == 0 ? state.Links.First.Packed != 0 || state.Links.Last.Packed != 0 :
                !state.Links.First.IsValid || !state.Links.Last.IsValid || state.Links.First.Index >= (uint)state.Slots.Used || state.Links.Last.Index >= (uint)state.Slots.Used)
                return DeterministicStatus.InvalidEncoding;
            return DeterministicStatus.None;
        }
        public DeterministicStatus Validate(SlotMapNode<LinkedListValue<T>>* staged, in LinkedListWireState state, int capacity, ref CanonicalScratch scratch)
        {
            var status = default(SlotMapWireLayout<LinkedListValue<T>>).Validate(staged, state.Slots, capacity, ref scratch); if (!status.IsSuccess()) return status;
            int visited = 0; StableHandle previous = default, current = state.Links.First;
            while (current.IsValid)
            {
                if (++visited > state.Slots.Count || current.Index >= (uint)state.Slots.Used) return DeterministicStatus.InvalidEncoding;
                var node = staged[current.Index];
                if (node.Active == 0 || node.Generation != current.Generation || node.Value.Previous != previous) return DeterministicStatus.InvalidEncoding;
                previous = current; current = node.Value.Next;
            }
            return current.Packed == 0 && visited == state.Slots.Count && previous == state.Links.Last ? DeterministicStatus.None : DeterministicStatus.InvalidEncoding;
        }
    }
    public unsafe struct SparseSetWireLayout<T> : IDeterministicWireLayout<SparseSetEntry<T>, SparseSetWireState> where T : unmanaged
    {
        private readonly BufferOptions entityOptions;
        public SparseSetWireLayout(in BufferOptions entityOptions) { this.entityOptions = entityOptions; }
        public int ItemCount(in SparseSetWireState state) => state.Count;
        public int PhysicalIndex(in SparseSetWireState state, int capacity, int logicalIndex) => logicalIndex;
        public DeterministicStatus ValidateMetadata(in SparseSetWireState state, int capacity)
        {
            if (state.Count < 0 || state.Count > capacity || state.EntitySlotCapacity < state.Count) return DeterministicStatus.InvalidEncoding;
            var status = entityOptions.TryCapacity(entityOptions.InitialCapacity, state.EntitySlotCapacity, out int normalized);
            return status.IsSuccess() && state.EntitySlotCapacity >= entityOptions.InitialCapacity && normalized == state.EntitySlotCapacity ? DeterministicStatus.None : DeterministicStatus.ProfileMismatch;
        }
        public DeterministicStatus Validate(SparseSetEntry<T>* staged, in SparseSetWireState state, int capacity, ref CanonicalScratch scratch)
        {
            if (state.Count == 0) return DeterministicStatus.None;
            int words = (int)(((long)state.EntitySlotCapacity + 63) / 64);
            var status = scratch.TryReserve<ulong>(words, out ulong* seen); if (!status.IsSuccess()) return status;
            for (int i = 0; i < words; i++) seen[i] = 0;
            for (int i = 0; i < state.Count; i++)
            {
                var handle = staged[i].Handle; if (!handle.IsValid || handle.Index >= (uint)state.EntitySlotCapacity) return DeterministicStatus.InvalidEncoding;
                int word = (int)(handle.Index >> 6); ulong mask = 1UL << (int)(handle.Index & 63);
                if ((seen[word] & mask) != 0) return DeterministicStatus.InvalidEncoding;
                seen[word] |= mask;
            }
            return DeterministicStatus.None;
        }
    }
    public unsafe struct BitSetWireLayout : IDeterministicWireLayout<ulong, BitSetWireState>
    {
        private readonly int length;
        public BitSetWireLayout(int length) { this.length = length; }
        public int ItemCount(in BitSetWireState state) => (int)(((long)state.Length + 63) / 64);
        public int PhysicalIndex(in BitSetWireState state, int capacity, int logicalIndex) => logicalIndex;
        public DeterministicStatus ValidateMetadata(in BitSetWireState state, int capacity)
            => state.Length == length && length >= 0 && capacity == ItemCount(state) ? DeterministicStatus.None : DeterministicStatus.ProfileMismatch;
        public DeterministicStatus Validate(ulong* staged, in BitSetWireState state, int capacity, ref CanonicalScratch scratch)
        {
            int bits = state.Length & 63;
            return bits != 0 && (staged[capacity - 1] >> bits) != 0 ? DeterministicStatus.InvalidEncoding : DeterministicStatus.None;
        }
    }
    public unsafe struct PriorityQueueWireLayout<T, TPriority, TComparer> : IDeterministicWireLayout<PriorityQueueNode<T, TPriority>, PriorityQueueState>
        where T : unmanaged where TPriority : unmanaged where TComparer : unmanaged, IDeterministicComparer<TPriority>
    {
        private readonly TComparer comparer;
        public PriorityQueueWireLayout(TComparer comparer) { this.comparer = comparer; }
        public int ItemCount(in PriorityQueueState state) => state.Count;
        public int PhysicalIndex(in PriorityQueueState state, int capacity, int logicalIndex) => logicalIndex;
        public DeterministicStatus ValidateMetadata(in PriorityQueueState state, int capacity)
            => !comparer.IsValid ? DeterministicStatus.InvalidConfiguration : state.Count >= 0 && state.Count <= capacity ? DeterministicStatus.None : DeterministicStatus.InvalidEncoding;
        public DeterministicStatus Validate(PriorityQueueNode<T, TPriority>* staged, in PriorityQueueState state, int capacity, ref CanonicalScratch scratch)
        {
            var status = scratch.TryReserve<ulong>(state.Count, out ulong* sequences); if (!status.IsSuccess()) return status;
            for (int i = 0; i < state.Count; i++)
            {
                var node = staged[i]; if (node.Sequence >= state.NextSequence) return DeterministicStatus.InvalidEncoding;
                sequences[i] = node.Sequence; if (i == 0) continue;
                var parent = staged[(i - 1) / 2]; int order = comparer.Compare(parent.Priority, node.Priority);
                if (order > 0 || order == 0 && parent.Sequence >= node.Sequence) return DeterministicStatus.InvalidEncoding;
            }
            Sort(sequences, state.Count);
            for (int i = 1; i < state.Count; i++) if (sequences[i - 1] == sequences[i]) return DeterministicStatus.InvalidEncoding;
            return DeterministicStatus.None;
        }
        private static void Sift(ulong* values, int root, int count)
        {
            ulong value = values[root];
            while (root < count / 2)
            {
                int child = root * 2 + 1; if (child + 1 < count && values[child] < values[child + 1]) child++;
                if (value >= values[child]) break; values[root] = values[child]; root = child;
            }
            values[root] = value;
        }
        private static void Sort(ulong* values, int count)
        {
            for (int root = count / 2 - 1; root >= 0; root--) Sift(values, root, count);
            for (int end = count - 1; end > 0; end--) { ulong tmp = values[0]; values[0] = values[end]; values[end] = tmp; Sift(values, 0, end); }
        }
    }
    public static unsafe class MultiMapWireValidation<TKey, TValue, TPolicy> where TKey : unmanaged where TValue : unmanaged where TPolicy : unmanaged, IDeterministicEquality<TKey>
    {
        public static DeterministicStatus Validate(OrderedHashEntry<TKey, MultiMapChain>* keys, in OrderedMapState map,
            SlotMapNode<MultiMapValue<TKey, TValue>>* values, in SlotMapState slots, in TPolicy policy)
        {
            long total = 0;
            for (int i = 0; i < map.Used; i++)
            {
                if (keys[i].Active == 0) continue;
                var chain = keys[i].Value; if (chain.Count <= 0 || chain.Count > slots.Count || !chain.First.IsValid || !chain.Last.IsValid) return DeterministicStatus.InvalidEncoding;
                int visited = 0; StableHandle previous = default, current = chain.First;
                while (current.IsValid)
                {
                    if (++visited > chain.Count || current.Index >= (uint)slots.Used) return DeterministicStatus.InvalidEncoding;
                    var node = values[current.Index];
                    if (node.Active == 0 || node.Generation != current.Generation || node.Value.Previous != previous || !policy.Equals(keys[i].Key, node.Value.Key)) return DeterministicStatus.InvalidEncoding;
                    previous = current; current = node.Value.Next;
                }
                if (current.Packed != 0 || visited != chain.Count || previous != chain.Last) return DeterministicStatus.InvalidEncoding;
                total += visited;
            }
            return total == slots.Count ? DeterministicStatus.None : DeterministicStatus.InvalidEncoding;
        }
    }
}
