namespace LegendaryTools.Deterministic
{
    public struct MultiMapChain { public StableHandle First, Last; public int Count; }
    public struct MultiMapValue<TKey, TValue> where TKey : unmanaged where TValue : unmanaged
    {
        public TKey Key;
        public TValue Value;
        public StableHandle Previous, Next;
    }
    /// <summary>Insertion-ordered keys, then insertion-ordered values for each key. Pair handles are generational.</summary>
    public static class MultiMapKernel<TKey, TValue, TPolicy, TEntries, TBuckets, TValues>
        where TKey : unmanaged where TValue : unmanaged where TPolicy : unmanaged, IDeterministicEquality<TKey>
        where TEntries : struct, IDeterministicBuffer<OrderedHashEntry<TKey, MultiMapChain>>
        where TBuckets : struct, IDeterministicBuffer<int>
        where TValues : struct, IDeterministicBuffer<SlotMapNode<MultiMapValue<TKey, TValue>>>
    {
        public static DeterministicStatus TryGetChain(ref TEntries entries, ref TBuckets buckets, in TKey key, in TPolicy policy, out MultiMapChain chain)
            => OrderedHashKernel<TKey, MultiMapChain, TPolicy, TEntries, TBuckets>.TryGet(ref entries, ref buckets, key, policy, out chain);
        private static DeterministicStatus CheckMapCapacity(ref TEntries entries, ref TBuckets buckets, in OrderedMapState state, BufferOptions options, bool newKey)
        {
            if (newKey && state.FreeHead == 0 && state.Used == int.MaxValue) return DeterministicStatus.CapacityExceeded;
            int required = newKey && state.FreeHead == 0 ? state.Used + 1 : entries.Capacity;
            var status = options.TryCapacity(entries.Capacity, required, out int next); if (!status.IsSuccess()) return status;
            status = entries.CanPrepareWrite(next, 0, next); if (!status.IsSuccess()) return status;
            int bucketCount = OrderedHashRules.BucketCapacity(next);
            return buckets.CanPrepareWrite(bucketCount, 0, bucketCount);
        }
        private static DeterministicStatus ReserveValue(ref TValues values, int required, StableHandle handle, bool prepare)
        { if (!handle.IsValid) return DeterministicStatus.None; return prepare ? values.PrepareWrite(required, (int)handle.Index, 1) : values.CanPrepareWrite(required, (int)handle.Index, 1); }
        private static void Link(ref TValues values, StableHandle at, StableHandle target, bool next)
        { if (!at.IsValid) return; var node = values.Read((int)at.Index); if (next) node.Value.Next = target; else node.Value.Previous = target; values.Write((int)at.Index, node); }
        public static DeterministicStatus TryAdd(ref TEntries entries, ref TBuckets buckets, ref TValues values,
            ref OrderedMapState map, ref SlotMapState slots, BufferOptions options, in TKey key, in TValue value, in TPolicy policy, out StableHandle handle)
        {
            handle = default; if (!values.IsCreated) return DeterministicStatus.Disposed;
            var status = TryGetChain(ref entries, ref buckets, key, policy, out var chain);
            bool newKey = status == DeterministicStatus.NotFound; if (!status.IsSuccess() && !newKey) return status;
            if (chain.Count == int.MaxValue || slots.FreeHead == 0 && slots.Used == int.MaxValue) return DeterministicStatus.CapacityExceeded;
            status = CheckMapCapacity(ref entries, ref buckets, map, options, newKey); if (!status.IsSuccess()) return status;
            int index = slots.FreeHead == 0 ? slots.Used : slots.FreeHead - 1, required = slots.FreeHead == 0 ? slots.Used + 1 : values.Capacity;
            for (int phase = 0; phase < 2; phase++)
            {
                status = phase == 0 ? values.CanPrepareWrite(required, index, 1) : values.PrepareWrite(required, index, 1); if (!status.IsSuccess()) return status;
                status = ReserveValue(ref values, required, chain.Last, phase != 0); if (!status.IsSuccess()) return status;
            }
            var predicted = new StableHandle((uint)index, slots.FreeHead == 0 ? 1 : values.Read(index).Generation);
            var nodeValue = new MultiMapValue<TKey, TValue> { Key = key, Value = value, Previous = chain.Last };
            var nextChain = chain; nextChain.Last = predicted; if (chain.Count == 0) nextChain.First = predicted; nextChain.Count++;
            // Map reserves its affected buckets/order links before committing. All value writes have already been reserved.
            status = OrderedHashKernel<TKey, MultiMapChain, TPolicy, TEntries, TBuckets>.TryPut(ref entries, ref buckets, ref map, options, key, nextChain, policy, false);
            if (!status.IsSuccess()) return status;
            status = SlotMapKernel<MultiMapValue<TKey, TValue>, TValues>.TryAdd(ref values, ref slots, nodeValue, out handle); if (!status.IsSuccess()) return status;
            Link(ref values, chain.Last, handle, true); values.CommitWrite(); return DeterministicStatus.None;
        }
        public static DeterministicStatus TryRemove(ref TEntries entries, ref TBuckets buckets, ref TValues values,
            ref OrderedMapState map, ref SlotMapState slots, BufferOptions options, StableHandle handle, in TPolicy policy)
        {
            var status = SlotMapKernel<MultiMapValue<TKey, TValue>, TValues>.TryGet(ref values, slots, handle, out var node); if (!status.IsSuccess()) return status;
            status = TryGetChain(ref entries, ref buckets, node.Key, policy, out var chain); if (!status.IsSuccess()) return DeterministicStatus.ProtocolConflict;
            status = CheckMapCapacity(ref entries, ref buckets, map, options, false); if (!status.IsSuccess()) return status;
            uint generation = values.Read((int)handle.Index).Generation;
            for (int phase = 0; phase < 2; phase++)
            {
                status = ReserveValue(ref values, values.Capacity, handle, phase != 0); if (!status.IsSuccess()) return status;
                status = ReserveValue(ref values, values.Capacity, node.Previous, phase != 0); if (!status.IsSuccess()) return status;
                status = ReserveValue(ref values, values.Capacity, node.Next, phase != 0); if (!status.IsSuccess()) return status;
                if (generation != uint.MaxValue && slots.FreeTail != 0)
                { status = phase == 0 ? values.CanPrepareWrite(values.Capacity, slots.FreeTail - 1, 1) : values.PrepareWrite(values.Capacity, slots.FreeTail - 1, 1); if (!status.IsSuccess()) return status; }
            }
            if (chain.Count == 1) status = OrderedHashKernel<TKey, MultiMapChain, TPolicy, TEntries, TBuckets>.TryRemove(ref entries, ref buckets, ref map, node.Key, policy);
            else
            {
                chain.Count--; if (!node.Previous.IsValid) chain.First = node.Next; if (!node.Next.IsValid) chain.Last = node.Previous;
                status = OrderedHashKernel<TKey, MultiMapChain, TPolicy, TEntries, TBuckets>.TryPut(ref entries, ref buckets, ref map, options, node.Key, chain, policy, false);
            }
            if (!status.IsSuccess()) return status;
            status = SlotMapKernel<MultiMapValue<TKey, TValue>, TValues>.TryRemove(ref values, ref slots, handle); if (!status.IsSuccess()) return status;
            Link(ref values, node.Previous, node.Next, true); Link(ref values, node.Next, node.Previous, false); values.CommitWrite(); return DeterministicStatus.None;
        }
        public static DeterministicStatus TryRemoveAll(ref TEntries entries, ref TBuckets buckets, ref TValues values,
            ref OrderedMapState map, ref SlotMapState slots, BufferOptions options, in TKey key, in TPolicy policy)
        {
            var status = TryGetChain(ref entries, ref buckets, key, policy, out var chain); if (!status.IsSuccess()) return status;
            status = CheckMapCapacity(ref entries, ref buckets, map, options, false); if (!status.IsSuccess()) return status;
            for (int phase = 0; phase < 2; phase++)
            {
                for (var current = chain.First; current.IsValid; current = values.Read((int)current.Index).Value.Next)
                { status = ReserveValue(ref values, values.Capacity, current, phase != 0); if (!status.IsSuccess()) return status; }
                if (slots.FreeTail != 0)
                { status = phase == 0 ? values.CanPrepareWrite(values.Capacity, slots.FreeTail - 1, 1) : values.PrepareWrite(values.Capacity, slots.FreeTail - 1, 1); if (!status.IsSuccess()) return status; }
            }
            status = OrderedHashKernel<TKey, MultiMapChain, TPolicy, TEntries, TBuckets>.TryRemove(ref entries, ref buckets, ref map, key, policy); if (!status.IsSuccess()) return status;
            for (var current = chain.First; current.IsValid;)
            {
                int index = (int)current.Index; var node = values.Read(index); var next = node.Value.Next;
                SlotMapKernel<MultiMapValue<TKey, TValue>, TValues>.RemoveReserved(ref values, ref slots, index, node); current = next;
            }
            values.CommitWrite(); return DeterministicStatus.None;
        }
        public static DeterministicStatus TryClear(ref TEntries entries, ref TBuckets buckets, ref TValues values, ref OrderedMapState map, ref SlotMapState slots)
        {
            if (!entries.IsCreated || !buckets.IsCreated || !values.IsCreated) return DeterministicStatus.Disposed;
            var status = entries.CanPrepareWrite(entries.Capacity, 0, map.Used); if (!status.IsSuccess()) return status;
            status = buckets.CanPrepareWrite(buckets.Capacity, 0, buckets.Capacity); if (!status.IsSuccess()) return status;
            status = values.PrepareWrite(values.Capacity, 0, slots.Used); if (!status.IsSuccess()) return status;
            status = OrderedHashKernel<TKey, MultiMapChain, TPolicy, TEntries, TBuckets>.TryClear(ref entries, ref buckets, ref map); if (!status.IsSuccess()) return status;
            return SlotMapKernel<MultiMapValue<TKey, TValue>, TValues>.TryClear(ref values, ref slots);
        }
    }
}
