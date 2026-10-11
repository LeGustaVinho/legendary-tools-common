namespace LegendaryTools.Deterministic
{
    internal static class WireChecks
    {
        internal static bool Reference(int encoded, int used) => (uint)encoded <= (uint)used;
        internal static bool Counts(int count, int used, int capacity) => count >= 0 && used >= count && used <= capacity;
    }
    public unsafe struct SequenceWireLayout<T> : IDeterministicWireLayout<T, SequenceState> where T : unmanaged
    {
        private readonly bool ring;
        private readonly int fixedLength;
        public SequenceWireLayout(bool ring, int fixedLength = -1) { this.ring = ring; this.fixedLength = fixedLength; }
        public int ItemCount(in SequenceState state) => state.Count;
        public int PhysicalIndex(in SequenceState state, int capacity, int logicalIndex) => ring ? (int)(((long)state.Head + logicalIndex) % capacity) : logicalIndex;
        public DeterministicStatus ValidateMetadata(in SequenceState state, int capacity)
        {
            if (state.Count < 0 || state.Count > capacity || fixedLength >= 0 && (state.Count != fixedLength || capacity != fixedLength) ||
                !ring && state.Head != 0 || ring && (capacity == 0 ? state.Head != 0 : (uint)state.Head >= (uint)capacity) || state.Count == 0 && state.Head != 0)
                return DeterministicStatus.InvalidEncoding;
            return DeterministicStatus.None;
        }
        public DeterministicStatus Validate(T* staged, in SequenceState state, int capacity, ref CanonicalScratch scratch) => DeterministicStatus.None;
    }
    public unsafe struct SlotMapWireLayout<T> : IDeterministicWireLayout<SlotMapNode<T>, SlotMapState> where T : unmanaged
    {
        public int ItemCount(in SlotMapState state) => state.Used;
        public int PhysicalIndex(in SlotMapState state, int capacity, int logicalIndex) => logicalIndex;
        public DeterministicStatus ValidateMetadata(in SlotMapState state, int capacity)
            => WireChecks.Counts(state.Count, state.Used, capacity) && WireChecks.Reference(state.FreeHead, state.Used) && WireChecks.Reference(state.FreeTail, state.Used) &&
                (state.FreeHead == 0) == (state.FreeTail == 0) ? DeterministicStatus.None : DeterministicStatus.InvalidEncoding;
        public DeterministicStatus Validate(SlotMapNode<T>* staged, in SlotMapState state, int capacity, ref CanonicalScratch scratch)
        {
            int active = 0, retired = 0;
            for (int i = 0; i < state.Used; i++)
            {
                var node = staged[i];
                if (node.Generation == 0 || node.Active > 1 || node.Retired > 1 || !WireChecks.Reference(node.NextFree, state.Used) ||
                    node.Active != 0 && (node.Retired != 0 || node.NextFree != 0) || node.Retired != 0 && (node.Generation != uint.MaxValue || node.NextFree != 0) ||
                    node.Active == 0 && node.Retired == 0 && node.Generation < 2) return DeterministicStatus.InvalidEncoding;
                active += node.Active; retired += node.Retired;
            }
            if (active != state.Count) return DeterministicStatus.InvalidEncoding;
            int expectedFree = state.Used - active - retired, free = 0, last = 0;
            for (int next = state.FreeHead; next != 0; next = staged[next - 1].NextFree)
            {
                if (++free > expectedFree || staged[next - 1].Active != 0 || staged[next - 1].Retired != 0) return DeterministicStatus.InvalidEncoding;
                last = next;
            }
            return free == expectedFree && last == state.FreeTail ? DeterministicStatus.None : DeterministicStatus.InvalidEncoding;
        }
    }
    public unsafe struct OrderedMapWireLayout<TKey, TValue, TPolicy> : IDeterministicWireLayout<OrderedHashEntry<TKey, TValue>, OrderedMapState>
        where TKey : unmanaged where TValue : unmanaged where TPolicy : unmanaged, IDeterministicEquality<TKey>
    {
        private readonly TPolicy policy;
        public OrderedMapWireLayout(TPolicy policy) { this.policy = policy; }
        public int ItemCount(in OrderedMapState state) => state.Used;
        public int PhysicalIndex(in OrderedMapState state, int capacity, int logicalIndex) => logicalIndex;
        public DeterministicStatus ValidateMetadata(in OrderedMapState state, int capacity)
        {
            if (!policy.IsValid || OrderedHashRules.BucketCapacity(capacity) == 0) return DeterministicStatus.InvalidConfiguration;
            if (!WireChecks.Counts(state.Count, state.Used, capacity) || !WireChecks.Reference(state.FreeHead, state.Used) ||
                !WireChecks.Reference(state.OrderHead, state.Used) || !WireChecks.Reference(state.OrderTail, state.Used) ||
                (state.Count == 0 ? state.OrderHead != 0 || state.OrderTail != 0 : state.OrderHead == 0 || state.OrderTail == 0)) return DeterministicStatus.InvalidEncoding;
            return DeterministicStatus.None;
        }
        public DeterministicStatus Validate(OrderedHashEntry<TKey, TValue>* staged, in OrderedMapState state, int capacity, ref CanonicalScratch scratch)
        {
            int active = 0;
            for (int i = 0; i < state.Used; i++)
            {
                var node = staged[i]; if (node.Active > 1 || !WireChecks.Reference(node.OrderNext, state.Used) || !WireChecks.Reference(node.OrderPrevious, state.Used) ||
                    node.Active == 0 && (!WireChecks.Reference(node.BucketNext, state.Used) || node.OrderNext != 0 || node.OrderPrevious != 0)) return DeterministicStatus.InvalidEncoding;
                active += node.Active;
            }
            if (active != state.Count) return DeterministicStatus.InvalidEncoding;
            int freeCount = 0;
            for (int next = state.FreeHead; next != 0; next = staged[next - 1].BucketNext)
                if (++freeCount > state.Used - active || staged[next - 1].Active != 0) return DeterministicStatus.InvalidEncoding;
            if (freeCount != state.Used - active) return DeterministicStatus.InvalidEncoding;
            int visited = 0, previous = 0;
            for (int next = state.OrderHead; next != 0; next = staged[next - 1].OrderNext)
            {
                if (++visited > state.Count || staged[next - 1].Active == 0 || staged[next - 1].OrderPrevious != previous) return DeterministicStatus.InvalidEncoding;
                previous = next;
            }
            if (visited != state.Count || previous != state.OrderTail) return DeterministicStatus.InvalidEncoding;
            if (active == 0) return DeterministicStatus.None;
            int bucketCount = OrderedHashRules.BucketCapacity(state.Count);
            var status = scratch.TryReserve<int>(bucketCount, out int* heads); if (!status.IsSuccess()) return status;
            for (int i = 0; i < bucketCount; i++) heads[i] = 0;
            for (int i = 0; i < state.Used; i++)
            {
                if (staged[i].Active == 0) continue;
                ulong hash = policy.Hash(staged[i].Key); int bucket = (int)(hash & (uint)(bucketCount - 1));
                for (int next = heads[bucket]; next != 0; next = staged[next - 1].BucketNext)
                    if (staged[next - 1].Hash == hash && policy.Equals(staged[next - 1].Key, staged[i].Key)) return DeterministicStatus.InvalidEncoding;
                staged[i].Hash = hash; staged[i].BucketNext = heads[bucket]; heads[bucket] = i + 1;
            }
            return DeterministicStatus.None;
        }
        /// <summary>Rebuild only derived bucket links after the validated primary buffer has been published.</summary>
        public void RebuildBuckets<TEntries, TBuckets>(ref TEntries entries, ref TBuckets buckets, in OrderedMapState state)
            where TEntries : struct, IDeterministicBuffer<OrderedHashEntry<TKey, TValue>> where TBuckets : struct, IDeterministicBuffer<int>
        {
            for (int i = 0; i < buckets.Capacity; i++) buckets.Write(i, 0);
            for (int i = 0; i < state.Used; i++)
            {
                var node = entries.Read(i); if (node.Active == 0) continue;
                node.Hash = policy.Hash(node.Key); int bucket = (int)(node.Hash & (uint)(buckets.Capacity - 1));
                node.BucketNext = buckets.Read(bucket); entries.Write(i, node); buckets.Write(bucket, i + 1);
            }
            buckets.CommitWrite();
        }
    }
    public unsafe struct SortedListWireLayout<TKey, TValue, TComparer> : IDeterministicWireLayout<SortedEntry<TKey, TValue>, SequenceState>
        where TKey : unmanaged where TValue : unmanaged where TComparer : unmanaged, IDeterministicComparer<TKey>
    {
        private readonly TComparer comparer;
        public SortedListWireLayout(TComparer comparer) { this.comparer = comparer; }
        public int ItemCount(in SequenceState state) => state.Count;
        public int PhysicalIndex(in SequenceState state, int capacity, int logicalIndex) => logicalIndex;
        public DeterministicStatus ValidateMetadata(in SequenceState state, int capacity) => !comparer.IsValid ? DeterministicStatus.InvalidConfiguration :
            state.Head == 0 && state.Count >= 0 && state.Count <= capacity ? DeterministicStatus.None : DeterministicStatus.InvalidEncoding;
        public DeterministicStatus Validate(SortedEntry<TKey, TValue>* staged, in SequenceState state, int capacity, ref CanonicalScratch scratch)
        {
            for (int i = 1; i < state.Count; i++) if (comparer.Compare(staged[i - 1].Key, staged[i].Key) >= 0) return DeterministicStatus.InvalidEncoding;
            return DeterministicStatus.None;
        }
    }
    public unsafe struct AvlWireLayout<TKey, TValue, TComparer> : IDeterministicWireLayout<AvlNode<TKey, TValue>, AvlState>
        where TKey : unmanaged where TValue : unmanaged where TComparer : unmanaged, IDeterministicComparer<TKey>
    {
        private readonly TComparer comparer;
        public AvlWireLayout(TComparer comparer) { this.comparer = comparer; }
        public int ItemCount(in AvlState state) => state.Used;
        public int PhysicalIndex(in AvlState state, int capacity, int logicalIndex) => logicalIndex;
        public DeterministicStatus ValidateMetadata(in AvlState state, int capacity) => !comparer.IsValid ? DeterministicStatus.InvalidConfiguration :
            WireChecks.Counts(state.Count, state.Used, capacity) && WireChecks.Reference(state.Root, state.Used) && WireChecks.Reference(state.FreeHead, state.Used) &&
            (state.Root == 0) == (state.Count == 0) ? DeterministicStatus.None : DeterministicStatus.InvalidEncoding;
        public DeterministicStatus Validate(AvlNode<TKey, TValue>* staged, in AvlState state, int capacity, ref CanonicalScratch scratch)
        {
            int active = 0;
            for (int i = 0; i < state.Used; i++)
            {
                var node = staged[i];
                if (node.Active > 1 || !WireChecks.Reference(node.Left, state.Used) || !WireChecks.Reference(node.Right, state.Used) || !WireChecks.Reference(node.Parent, state.Used) || !WireChecks.Reference(node.NextFree, state.Used)) return DeterministicStatus.InvalidEncoding;
                if (node.Active == 0)
                { if (node.Left != 0 || node.Right != 0 || node.Parent != 0 || node.Height != 0 || node.Size != 0) return DeterministicStatus.InvalidEncoding; continue; }
                active++;
                if ((node.Parent == 0) != (state.Root == i + 1) || node.NextFree != 0 || node.Height < 1 || node.Height > 64 || node.Size < 1 || node.Size > state.Count || node.Left != 0 && node.Left == node.Right) return DeterministicStatus.InvalidEncoding;
                var left = node.Left == 0 ? default : staged[node.Left - 1]; var right = node.Right == 0 ? default : staged[node.Right - 1];
                if (node.Left != 0 && (left.Active != 1 || left.Parent != i + 1) || node.Right != 0 && (right.Active != 1 || right.Parent != i + 1) ||
                    node.Height != 1 + (left.Height > right.Height ? left.Height : right.Height) || node.Size != 1L + left.Size + right.Size || left.Height - right.Height > 1 || right.Height - left.Height > 1) return DeterministicStatus.InvalidEncoding;
                if (node.Parent != 0)
                { var parent = staged[node.Parent - 1]; if (parent.Active != 1 || parent.Left != i + 1 && parent.Right != i + 1) return DeterministicStatus.InvalidEncoding; }
            }
            if (active != state.Count || state.Root != 0 && staged[state.Root - 1].Size != active) return DeterministicStatus.InvalidEncoding;
            int free = 0;
            for (int next = state.FreeHead; next != 0; next = staged[next - 1].NextFree)
                if (++free > state.Used - active || staged[next - 1].Active != 0) return DeterministicStatus.InvalidEncoding;
            if (free != state.Used - active) return DeterministicStatus.InvalidEncoding;
            if (active == 0) return DeterministicStatus.None;
            var status = scratch.TryReserve<int>(64, out int* stack); if (!status.IsSuccess()) return status;
            int current = state.Root, depth = 0, visited = 0; TKey previous = default; bool first = true;
            while (current != 0 || depth != 0)
            {
                while (current != 0) { if (depth == 64) return DeterministicStatus.InvalidEncoding; stack[depth++] = current; current = staged[current - 1].Left; }
                current = stack[--depth]; var node = staged[current - 1];
                if (++visited > active || !first && comparer.Compare(previous, node.Key) >= 0) return DeterministicStatus.InvalidEncoding;
                previous = node.Key; first = false; current = node.Right;
            }
            return visited == active ? DeterministicStatus.None : DeterministicStatus.InvalidEncoding;
        }
    }
}
