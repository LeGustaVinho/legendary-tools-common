namespace LegendaryTools.Deterministic
{
    internal struct SpatialCandidate<TKey> where TKey : unmanaged
    {
        internal int Slot;
        internal TKey Key;
        internal SpatialDistanceKey Distance;
        internal SpatialRayParameter Parameter;
        internal ulong HeapSequence;
    }
    internal unsafe struct SpatialSelectionBuffer<TKey> : IDeterministicBuffer<PriorityQueueNode<int, SpatialCandidate<TKey>>> where TKey : unmanaged
    {
        internal SpatialCandidate<TKey>* Pointer;
        internal int Limit;
        public int Capacity => Limit;
        public ulong Revision => 0;
        public bool IsCreated => Pointer != null || Limit == 0;
        public PriorityQueueNode<int, SpatialCandidate<TKey>> Read(int index)
        { var candidate = Pointer[index]; return new PriorityQueueNode<int, SpatialCandidate<TKey>> { Element = candidate.Slot, Priority = candidate, Sequence = candidate.HeapSequence }; }
        public void Write(int index, in PriorityQueueNode<int, SpatialCandidate<TKey>> node)
        { var candidate = node.Priority; candidate.HeapSequence = node.Sequence; Pointer[index] = candidate; }
        public DeterministicStatus CanPrepareWrite(int required, int start, int count) => required >= 0 && required <= Limit && start >= 0 && count >= 0 && count <= Limit - start
            ? DeterministicStatus.None : DeterministicStatus.CapacityExceeded;
        public DeterministicStatus PrepareWrite(int required, int start, int count) => CanPrepareWrite(required, start, count);
        public void CommitWrite() { }
    }
    internal struct SpatialSelectionComparer<TKey, TPolicy> : IDeterministicComparer<SpatialCandidate<TKey>>
        where TKey : unmanaged where TPolicy : unmanaged, IDeterministicComparer<TKey>
    {
        internal TPolicy Policy;
        public bool IsValid => Policy.IsValid;
        // Reverse the exact metric/key priority so the bounded queue exposes its worst retained hit.
        public int Compare(in SpatialCandidate<TKey> left, in SpatialCandidate<TKey> right)
            => SpatialSort<TKey, TPolicy>.Compare(right, left, SpatialQueryKind.Nearest, Policy);
    }
    /// <summary>Caller-owned workspace. Each simultaneous reader needs a different allocation.</summary>
    public unsafe struct SpatialQueryScratch
    {
        public readonly byte* Buffer;
        public readonly int Length;
        public SpatialQueryScratch(byte* buffer, int length) { Buffer = buffer; Length = length; }
        public static DeterministicStatus TryMeasure<TKey>(int recordExtent, int nodeCapacity, out int bytes) where TKey : unmanaged
        {
            bytes = 0; if (recordExtent < 0 || nodeCapacity < 0) return DeterministicStatus.OutOfRange;
            long size = (long)recordExtent * sizeof(SpatialCandidate<TKey>) + (long)nodeCapacity * sizeof(int) + 128;
            if (size > int.MaxValue) return DeterministicStatus.CapacityExceeded; bytes = (int)size; return DeterministicStatus.None;
        }
        internal DeterministicStatus Prepare<TKey>(int extent, int nodes, out SpatialCandidate<TKey>* candidates,
            out int* stack) where TKey : unmanaged
        {
            candidates = null; stack = null;
            var work = new CanonicalScratch(Buffer, Length);
            var s = work.TryReserve<SpatialCandidate<TKey>>(extent, out candidates); if (!s.IsSuccess()) return s;
            return work.TryReserve<int>(nodes, out stack);
        }
    }
    internal static unsafe class SpatialSort<TKey, TPolicy>
        where TKey : unmanaged where TPolicy : unmanaged, IDeterministicComparer<TKey>
    {
        internal static int Compare(in SpatialCandidate<TKey> a, in SpatialCandidate<TKey> b, SpatialQueryKind kind, TPolicy policy)
        {
            int c = kind == SpatialQueryKind.Nearest ? a.Distance.CompareTo(b.Distance) :
                kind == SpatialQueryKind.Ray || kind == SpatialQueryKind.Segment ? a.Parameter.CompareTo(b.Parameter) : 0;
            return c == 0 ? policy.Compare(a.Key, b.Key) : c;
        }
        internal static void Sort(SpatialCandidate<TKey>* values, int count, SpatialQueryKind kind, TPolicy policy)
        {
            for (int i = count / 2 - 1; i >= 0; i--) Sift(values, i, count, kind, policy);
            for (int end = count - 1; end > 0; end--)
            { var a = values[0]; values[0] = values[end]; values[end] = a; Sift(values, 0, end, kind, policy); }
        }
        private static void Sift(SpatialCandidate<TKey>* values, int root, int count, SpatialQueryKind kind, TPolicy policy)
        {
            while (root < count / 2)
            {
                int child = root * 2 + 1;
                if (child + 1 < count && Compare(values[child], values[child + 1], kind, policy) < 0) child++;
                if (Compare(values[root], values[child], kind, policy) >= 0) return;
                var a = values[root]; values[root] = values[child]; values[child] = a; root = child;
            }
        }
    }
}
