namespace LegendaryTools.Deterministic
{
    /// <summary>Workspace is exclusively borrowed until pair enumeration ends. Storage grows with records, not pairs.</summary>
    public readonly unsafe struct SpatialPairScratch
    {
        public readonly byte* Buffer;
        public readonly int Length;
        public SpatialPairScratch(byte* buffer, int length) { Buffer = buffer; Length = length; }
        public static DeterministicStatus TryMeasure<TKey>(int recordExtent, int nodeCapacity, out int bytes) where TKey : unmanaged
        {
            bytes = 0; var status = SpatialQueryScratch.TryMeasure<TKey>(recordExtent, nodeCapacity, out int query);
            if (!status.IsSuccess()) return status;
            long total = query + (long)recordExtent * sizeof(int) + 32;
            if (total > int.MaxValue) return DeterministicStatus.CapacityExceeded;
            bytes = (int)total; return DeterministicStatus.None;
        }
    }
    internal unsafe struct SpatialPairCursor<TKey, TData, TBounds, TPolicy, TStorage, TPredicate>
        where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
        where TPolicy : unmanaged, IDeterministicComparer<TKey>
        where TStorage : struct, ISpatialStorage<TKey, TData, TBounds>
        where TPredicate : unmanaged, ISpatialPairPredicate<TKey, TData, TBounds>
    {
        private TStorage storage;
        private TPolicy policy;
        private TPredicate predicate;
        private SpatialFilter filter;
        private SpatialQueryScratch scratch;
        private int* order;
        private SpatialCandidate<TKey>* neighbors;
        private int count, outer, next, neighborCount, dimensions;
        private ulong revision;
        private struct AboveKey : ISpatialPredicate<TKey, TData, TBounds>
        {
            internal TKey Minimum;
            internal TPolicy Policy;
            public bool Accept(in SpatialRecord<TKey, TData, TBounds> record) => Policy.Compare(record.Key, Minimum) > 0;
        }
        internal static DeterministicStatus Create(ref TStorage storage, SpatialPairScratch workspace, int dimensions, SpatialFilter filter,
            TPredicate predicate, TPolicy policy, out SpatialPairCursor<TKey, TData, TBounds, TPolicy, TStorage, TPredicate> cursor)
        {
            cursor = default;
            if (!storage.IsCreated) return DeterministicStatus.Disposed;
            int extent = storage.State.Records.Used;
            var allocator = new CanonicalScratch(workspace.Buffer, workspace.Length);
            var status = allocator.TryReserve<int>(extent, out int* order); if (!status.IsSuccess()) return status;
            var scratch = new SpatialQueryScratch(workspace.Buffer + allocator.Position, workspace.Length - allocator.Position);
            status = scratch.Prepare<TKey>(extent, storage.NodeCapacity, out var candidates, out _); if (!status.IsSuccess()) return status;
            int count = 0;
            for (int i = 0; i < extent; i++)
            {
                var entry = storage.ReadEntry(i);
                if (entry.Active != 0 && filter.Accept(entry.Value.Metadata)) candidates[count++] = new SpatialCandidate<TKey> { Slot = i, Key = entry.Key };
            }
            SpatialSort<TKey, TPolicy>.Sort(candidates, count, SpatialQueryKind.Aabb, policy);
            for (int i = 0; i < count; i++) order[i] = candidates[i].Slot;
            cursor = new SpatialPairCursor<TKey, TData, TBounds, TPolicy, TStorage, TPredicate>
            { storage = storage, dimensions = dimensions, filter = filter, predicate = predicate, policy = policy,
                order = order, scratch = scratch, count = count, outer = -1, revision = storage.State.Revision };
            return DeterministicStatus.None;
        }
        internal DeterministicStatus Next(out SpatialPair<TKey> pair, out bool hasValue)
        {
            pair = default; hasValue = false;
            if (!storage.IsCreated) return DeterministicStatus.Disposed;
            if (storage.State.Revision != revision) return DeterministicStatus.Invalidated;
            if (outer >= count) return DeterministicStatus.None;
            while (true)
            {
                if (outer >= 0 && outer < count)
                {
                    var left = storage.ReadEntry(order[outer]);
                    while (next < neighborCount)
                    {
                        var right = storage.ReadEntry(neighbors[next++].Slot);
                        if (!predicate.Accept(left.Value, right.Value)) continue;
                        pair = new SpatialPair<TKey>(left.Key, right.Key); hasValue = true; return DeterministicStatus.None;
                    }
                }
                // The final key cannot have a greater neighbor. No query or sort is needed for that row.
                if (++outer >= count - 1) { outer = count; return DeterministicStatus.None; }
                var query = SpatialQuery.FromEnvelope(storage.ReadPlacement(order[outer]).Bounds, dimensions);
                var status = SpatialQueryKernel<TKey, TData, TBounds, TPolicy, TStorage>.Collect(ref storage, query, dimensions,
                    scratch, filter, new AboveKey { Minimum = storage.ReadEntry(order[outer]).Key, Policy = policy }, policy, out neighbors, out neighborCount);
                if (!status.IsSuccess()) return status;
                next = 0;
            }
        }
    }
}
