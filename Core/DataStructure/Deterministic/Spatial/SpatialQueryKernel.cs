namespace LegendaryTools.Deterministic
{
    internal static unsafe class SpatialQueryKernel<TKey, TData, TBounds, TPolicy, TStorage>
        where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
        where TPolicy : unmanaged, IDeterministicComparer<TKey>
        where TStorage : struct, ISpatialStorage<TKey, TData, TBounds>
    {
        private static void Offer(SpatialCandidate<TKey>* candidates, ref int count, in SpatialCandidate<TKey> candidate, int k, TPolicy policy, ref PriorityQueueState selection)
        {
            if (k == 0) return;
            if (count == k && SpatialSort<TKey, TPolicy>.Compare(candidate, candidates[0], SpatialQueryKind.Nearest, policy) >= 0) return;
            var buffer = new SpatialSelectionBuffer<TKey> { Pointer = candidates, Limit = k };
            var comparer = new SpatialSelectionComparer<TKey, TPolicy> { Policy = policy };
            // Capacity, comparer and sequence exhaustion were bounded by the validated record extent.
            if (count == k) PriorityQueueKernel<int, SpatialCandidate<TKey>, SpatialSelectionComparer<TKey, TPolicy>, SpatialSelectionBuffer<TKey>>.TryDequeue(ref buffer, ref selection, comparer, out _, out _);
            PriorityQueueKernel<int, SpatialCandidate<TKey>, SpatialSelectionComparer<TKey, TPolicy>, SpatialSelectionBuffer<TKey>>.TryEnqueue(ref buffer, ref selection, candidate.Slot, candidate, comparer);
            count = selection.Count;
        }
        private static void Visit<TPredicate>(ref TStorage store, int slot, in SpatialPreparedQuery query, SpatialFilter filter,
            TPredicate predicate, SpatialCandidate<TKey>* candidates, ref int count, TPolicy policy, ref PriorityQueueState selection)
            where TPredicate : unmanaged, ISpatialPredicate<TKey, TData, TBounds>
        {
            var entry = store.ReadEntry(slot); if (entry.Active == 0 || !filter.Accept(entry.Value.Metadata) || !predicate.Accept(entry.Value)) return;
            if (!SpatialGeometry.Match(store.ReadPlacement(slot).Bounds, query, out var distance, out var parameter)) return;
            var candidate = new SpatialCandidate<TKey> { Slot = slot, Key = entry.Key, Distance = distance, Parameter = parameter };
            if (query.Kind == SpatialQueryKind.Nearest) Offer(candidates, ref count, candidate, query.K, policy, ref selection);
            else candidates[count++] = candidate;
        }
        private static bool CanVisit(ref TStorage store, int nodeId, in SpatialPreparedQuery query,
            SpatialCandidate<TKey>* candidates, int count, TPolicy policy)
        {
            var node = store.ReadNode(nodeId);
            if (!SpatialGeometry.Match(node.Bounds, query, out var distance, out _)) return false;
            if (query.Kind != SpatialQueryKind.Nearest || count < query.K || count == 0) return true;
            int c = distance.CompareTo(candidates[0].Distance);
            return c < 0 || c == 0 && policy.Compare(node.MinimumKey, candidates[0].Key) <= 0;
        }
        private static void Scan<TPredicate>(ref TStorage store, in SpatialPreparedQuery query, SpatialFilter filter,
            TPredicate predicate, SpatialCandidate<TKey>* candidates, ref int count, TPolicy policy, ref PriorityQueueState selection)
            where TPredicate : unmanaged, ISpatialPredicate<TKey, TData, TBounds>
        {
            if (store.State.Records.Count < store.State.Records.Used / 2)
            {
                int encoded = store.State.Records.OrderHead;
                while (encoded != 0)
                {
                    int slot = encoded - 1; encoded = store.ReadEntry(slot).OrderNext;
                    Visit(ref store, slot, query, filter, predicate, candidates, ref count, policy, ref selection);
                }
            }
            else
                for (int i = 0; i < store.State.Records.Used; i++)
                    Visit(ref store, i, query, filter, predicate, candidates, ref count, policy, ref selection);
        }
        internal static DeterministicStatus Collect<TPredicate>(ref TStorage store, in SpatialQuery query, int dimensions,
            SpatialQueryScratch scratch, SpatialFilter filter, TPredicate predicate, TPolicy policy,
            out SpatialCandidate<TKey>* candidates, out int count, bool sort = true)
            where TPredicate : unmanaged, ISpatialPredicate<TKey, TData, TBounds>
        {
            candidates = null; count = 0;
            if (!store.IsCreated) return DeterministicStatus.Disposed;
            if (store.Options.Ownership == SpatialStateOwnership.Derived && !store.State.SourceSynchronized) return DeterministicStatus.Invalidated;
            var s = SpatialGeometry.Prepare(query, dimensions, out var prepared); if (!s.IsSuccess()) return s;
            prepared.K = System.Math.Min(prepared.K, store.State.Records.Count);
            var selection = default(PriorityQueueState);
            s = scratch.Prepare<TKey>(store.State.Records.Used, store.NodeCapacity, out candidates, out var stack);
            if (!s.IsSuccess()) return s;
            if (query.Kind == SpatialQueryKind.Nearest && query.K == 0) return DeterministicStatus.None;
            var strategy = store.Options.Strategy;
            if (store.State.Dirty)
            {
                Scan(ref store, prepared, filter, predicate, candidates, ref count, policy, ref selection);
            }
            else if (strategy == SpatialIndexStrategy.SparseGrid && prepared.HasEnvelope)
            {
                long size = SpatialGeometry.Normalize(unchecked((ulong)store.Options.CellSize.RawValue), LegendaryTools.FixedPoint.FixedRuntime.State);
                if (!SpatialGridKernel<TKey, TData, TBounds, TStorage>.Range(prepared.Envelope, size, dimensions, store.Options.MaxCellsPerQuery, out var min, out var max))
                {
                    Scan(ref store, prepared, filter, predicate, candidates, ref count, policy, ref selection);
                }
                else
                {
                    for (long x = min.X; ; x++)
                    {
                        for (long y = min.Y; ; y++)
                        {
                            for (long z = min.Z; ; z++)
                            {
                                int cell = store.FindCell(new SpatialCellKey(x, y, z));
                                if (cell >= 0)
                                {
                                    int link = store.ReadCell(cell).Value;
                                    while (link >= 0)
                                    {
                                        var node = store.ReadLink(link);
                                        var bounds = store.ReadPlacement(node.RecordSlot).Bounds;
                                        // Every regular object has links in every covered cell. Only the first
                                        // cell shared with this query owns the visit, without a per-slot bitmap.
                                        if (x == System.Math.Max(min.X, SpatialGeometry.Cell(bounds.MinX, size)) &&
                                            y == System.Math.Max(min.Y, SpatialGeometry.Cell(bounds.MinY, size)) &&
                                            (dimensions == 2 || z == System.Math.Max(min.Z, SpatialGeometry.Cell(bounds.MinZ, size))))
                                            Visit(ref store, node.RecordSlot, prepared, filter, predicate, candidates, ref count, policy, ref selection);
                                        link = node.NextCell;
                                    }
                                }
                                if (z == max.Z) break;
                            }
                            if (y == max.Y) break;
                        }
                        if (x == max.X) break;
                    }
                    int overflow = store.State.OverflowHead;
                    while (overflow >= 0)
                    {
                        var link = store.ReadLink(overflow);
                        Visit(ref store, link.RecordSlot, prepared, filter, predicate, candidates, ref count, policy, ref selection);
                        overflow = link.NextCell;
                    }
                }
            }
            else if (store.State.Root >= 0)
            {
                int top = 0; stack[top++] = store.State.Root;
                while (top != 0)
                {
                    int nodeId = stack[--top]; if (!CanVisit(ref store, nodeId, prepared, candidates, count, policy)) continue;
                    var node = store.ReadNode(nodeId);
                    if (node.IsLeaf) Visit(ref store, node.RecordSlot, prepared, filter, predicate, candidates, ref count, policy, ref selection);
                    else
                    {
                        int a = node.Left, b = node.Right;
                        if (query.Kind == SpatialQueryKind.Nearest)
                        {
                            var da = SpatialGeometry.Distance(store.ReadNode(a).Bounds, prepared.Origin, dimensions);
                            var db = SpatialGeometry.Distance(store.ReadNode(b).Bounds, prepared.Origin, dimensions);
                            if (da.CompareTo(db) > 0) { int temp = a; a = b; b = temp; }
                        }
                        stack[top++] = b; stack[top++] = a;
                    }
                }
            }
            if (sort) SpatialSort<TKey, TPolicy>.Sort(candidates, count, query.Kind, policy);
            return DeterministicStatus.None;
        }
        internal static DeterministicStatus Query<TPredicate>(ref TStorage store, in SpatialQuery query, int dimensions,
            SpatialQueryScratch scratch, SpatialHit<TKey>* output, int capacity, out int written,
            SpatialFilter filter, TPredicate predicate, TPolicy policy)
            where TPredicate : unmanaged, ISpatialPredicate<TKey, TData, TBounds>
        {
            written = 0;
            if (capacity < 0 || output == null && capacity != 0) return DeterministicStatus.OutOfRange;
            var s = Collect(ref store, query, dimensions, scratch, filter, predicate, policy, out var candidates, out int count, false);
            if (!s.IsSuccess()) return s; if (count > capacity) return DeterministicStatus.BufferTooSmall;
            SpatialSort<TKey, TPolicy>.Sort(candidates, count, query.Kind, policy);
            for (int i = 0; i < count; i++)
            {
                var hit = new SpatialHit<TKey> { Key = candidates[i].Key };
                if (query.Kind == SpatialQueryKind.Nearest || query.Kind == SpatialQueryKind.Sphere)
                    candidates[i].Distance.TryGetSquaredDistance(out hit.Metric, out hit.MetricStatus);
                else if (query.Kind == SpatialQueryKind.Ray || query.Kind == SpatialQueryKind.Segment)
                    candidates[i].Parameter.TryGetParameter(out hit.Metric, out hit.MetricStatus);
                output[i] = hit;
            }
            written = count; return DeterministicStatus.None;
        }
    }
}
