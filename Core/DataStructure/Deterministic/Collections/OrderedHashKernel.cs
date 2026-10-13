namespace LegendaryTools.Deterministic
{
    public struct OrderedHashEntry<TKey, TValue> where TKey : unmanaged where TValue : unmanaged
    {
        public TKey Key;
        public TValue Value;
        public ulong Hash;
        public int BucketNext, OrderPrevious, OrderNext;
        public byte Active;
    }
    public struct OrderedMapState
    {
        public int Count, Used, FreeHead, OrderHead, OrderTail;
    }

    public static class OrderedHashRules
    {
        public static int BucketCapacity(int entries)
        {
            if (entries < 0 || entries > (1 << 29)) return 0;
            long required = (long)entries * 2; int capacity = 1;
            while (capacity < required) capacity <<= 1;
            return capacity;
        }
        public static BufferOptions BucketOptions(in BufferOptions entries) => new BufferOptions(
            BucketCapacity(entries.InitialCapacity), BucketCapacity(entries.MaxCapacity), entries.Allocation,
            entries.Snapshots, entries.SnapshotSlots, entries.PageBytes);
    }

    /// <summary>Bucket and order links are independent. Encoded indices use zero as the end marker.</summary>
    public static class OrderedHashKernel<TKey, TValue, TPolicy, TEntries, TBuckets>
        where TKey : unmanaged where TValue : unmanaged
        where TPolicy : unmanaged, IDeterministicEquality<TKey>
        where TEntries : struct, IDeterministicBuffer<OrderedHashEntry<TKey, TValue>>
        where TBuckets : struct, IDeterministicBuffer<int>
    {
        private static int Bucket(ulong hash, int capacity) => (int)(hash & (uint)(capacity - 1));
        public static int Find(ref TEntries entries, ref TBuckets buckets, in TKey key, TPolicy policy, out int previous)
            => FindHashed(ref entries, ref buckets, key, policy, policy.Hash(key), out previous, out _);
        private static int FindHashed(ref TEntries entries, ref TBuckets buckets, in TKey key, TPolicy policy,
            ulong hash, out int previous, out OrderedHashEntry<TKey, TValue> found)
        {
            previous = -1; found = default;
            int encoded = buckets.Read(Bucket(hash, buckets.Capacity));
            while (encoded != 0)
            {
                int index = encoded - 1; var entry = entries.Read(index);
                if (entry.Hash == hash && policy.Equals(entry.Key, key)) { found = entry; return index; }
                previous = index; encoded = entry.BucketNext;
            }
            return -1;
        }
        public static DeterministicStatus TryGet(ref TEntries entries, ref TBuckets buckets, in TKey key, TPolicy policy, out TValue value)
        {
            value = default;
            if (!entries.IsCreated || !buckets.IsCreated) return DeterministicStatus.Disposed;
            if (!policy.IsValid) return DeterministicStatus.UnsupportedType;
            int index = FindHashed(ref entries, ref buckets, key, policy, policy.Hash(key), out _, out var found);
            if (index < 0) return DeterministicStatus.NotFound;
            value = found.Value; return DeterministicStatus.None;
        }

        public static DeterministicStatus TryPut(ref TEntries entries, ref TBuckets buckets, ref OrderedMapState state,
            in BufferOptions options, in TKey key, in TValue value, TPolicy policy, bool addOnly)
        {
            if (!entries.IsCreated || !buckets.IsCreated) return DeterministicStatus.Disposed;
            if (!policy.IsValid) return DeterministicStatus.UnsupportedType;
            ulong hash = policy.Hash(key);
            int existing = FindHashed(ref entries, ref buckets, key, policy, hash, out _, out var replacement);
            if (existing >= 0)
            {
                if (addOnly) return DeterministicStatus.DuplicateKey;
                var status = entries.PrepareWrite(entries.Capacity, existing, 1); if (!status.IsSuccess()) return status;
                replacement.Value = value; entries.Write(existing, replacement);
                entries.CommitWrite(); return DeterministicStatus.None;
            }
            bool reused = state.FreeHead != 0;
            if (!reused && state.Used == int.MaxValue) return DeterministicStatus.CapacityExceeded;
            int index = reused ? state.FreeHead - 1 : state.Used;
            int required = reused ? state.Used : state.Used + 1;
            var capacityStatus = options.TryCapacity(entries.Capacity, required, out int nextCapacity);
            if (!capacityStatus.IsSuccess()) return capacityStatus;
            int bucketCapacity = OrderedHashRules.BucketCapacity(nextCapacity);
            if (bucketCapacity == 0) return DeterministicStatus.InvalidConfiguration;
            bool rehash = bucketCapacity != buckets.Capacity;
            int bucket = Bucket(hash, bucketCapacity);
            var statusEntry = entries.CanPrepareWrite(required, rehash ? 0 : index, rehash ? required : 1);
            if (!statusEntry.IsSuccess()) return statusEntry;
            var statusBucket = buckets.CanPrepareWrite(bucketCapacity, rehash ? 0 : bucket, rehash ? bucketCapacity : 1);
            if (!statusBucket.IsSuccess()) return statusBucket;
            if (state.OrderTail != 0)
            {
                var tailStatus = entries.CanPrepareWrite(required, state.OrderTail - 1, 1);
                if (!tailStatus.IsSuccess()) return tailStatus;
            }
            statusEntry = entries.PrepareWrite(required, rehash ? 0 : index, rehash ? required : 1);
            if (!statusEntry.IsSuccess()) return statusEntry;
            statusBucket = buckets.PrepareWrite(bucketCapacity, rehash ? 0 : bucket, rehash ? bucketCapacity : 1);
            if (!statusBucket.IsSuccess()) return statusBucket;
            if (state.OrderTail != 0)
            {
                var tailStatus = entries.PrepareWrite(required, state.OrderTail - 1, 1);
                if (!tailStatus.IsSuccess()) return tailStatus;
            }
            int freeNext = reused ? entries.Read(index).BucketNext : 0;
            if (rehash)
            {
                int zero = 0;
                for (int i = 0; i < buckets.Capacity; i++) buckets.Write(i, zero);
                for (int i = 0; i < state.Used; i++)
                {
                    var old = entries.Read(i); if (old.Active == 0) continue;
                    int b = Bucket(old.Hash, buckets.Capacity); old.BucketNext = buckets.Read(b);
                    int encoded = i + 1; entries.Write(i, old); buckets.Write(b, encoded);
                }
            }
            var added = new OrderedHashEntry<TKey, TValue> { Key = key, Value = value, Hash = hash, Active = 1,
                BucketNext = buckets.Read(bucket), OrderPrevious = state.OrderTail };
            int nextEncoded = index + 1;
            if (state.OrderTail != 0)
            { var tail = entries.Read(state.OrderTail - 1); tail.OrderNext = nextEncoded; entries.Write(state.OrderTail - 1, tail); }
            else state.OrderHead = nextEncoded;
            entries.Write(index, added); buckets.Write(bucket, nextEncoded);
            state.OrderTail = nextEncoded; state.Count++;
            if (reused) state.FreeHead = freeNext; else state.Used++;
            entries.CommitWrite(); buckets.CommitWrite(); return DeterministicStatus.None;
        }

        private static DeterministicStatus ReserveIndex(ref TEntries entries, int index, bool prepare)
        {
            if (index < 0) return DeterministicStatus.None;
            return prepare ? entries.PrepareWrite(entries.Capacity, index, 1) : entries.CanPrepareWrite(entries.Capacity, index, 1);
        }
        public static DeterministicStatus TryRemove(ref TEntries entries, ref TBuckets buckets, ref OrderedMapState state,
            in TKey key, TPolicy policy)
        {
            if (!entries.IsCreated || !buckets.IsCreated) return DeterministicStatus.Disposed;
            if (!policy.IsValid) return DeterministicStatus.UnsupportedType;
            int index = FindHashed(ref entries, ref buckets, key, policy, policy.Hash(key), out int previousBucket, out var removed);
            if (index < 0) return DeterministicStatus.NotFound;
            int bucket = Bucket(removed.Hash, buckets.Capacity);
            for (int phase = 0; phase < 2; phase++)
            {
                bool prepare = phase != 0;
                var status = ReserveIndex(ref entries, index, prepare); if (!status.IsSuccess()) return status;
                status = ReserveIndex(ref entries, previousBucket, prepare); if (!status.IsSuccess()) return status;
                int orderPrevious = removed.OrderPrevious - 1, orderNext = removed.OrderNext - 1;
                if (orderPrevious != previousBucket)
                { status = ReserveIndex(ref entries, orderPrevious, prepare); if (!status.IsSuccess()) return status; }
                if (orderNext != previousBucket)
                { status = ReserveIndex(ref entries, orderNext, prepare); if (!status.IsSuccess()) return status; }
                status = prepare ? buckets.PrepareWrite(buckets.Capacity, bucket, 1) : buckets.CanPrepareWrite(buckets.Capacity, bucket, 1);
                if (!status.IsSuccess()) return status;
            }
            if (previousBucket >= 0)
            { var previous = entries.Read(previousBucket); previous.BucketNext = removed.BucketNext; entries.Write(previousBucket, previous); }
            else buckets.Write(bucket, removed.BucketNext);
            if (removed.OrderPrevious != 0)
            { var previous = entries.Read(removed.OrderPrevious - 1); previous.OrderNext = removed.OrderNext; entries.Write(removed.OrderPrevious - 1, previous); }
            else state.OrderHead = removed.OrderNext;
            if (removed.OrderNext != 0)
            { var next = entries.Read(removed.OrderNext - 1); next.OrderPrevious = removed.OrderPrevious; entries.Write(removed.OrderNext - 1, next); }
            else state.OrderTail = removed.OrderPrevious;
            var free = new OrderedHashEntry<TKey, TValue> { BucketNext = state.FreeHead };
            entries.Write(index, free); state.FreeHead = index + 1; state.Count--;
            entries.CommitWrite(); buckets.CommitWrite(); return DeterministicStatus.None;
        }
        public static DeterministicStatus TryClear(ref TEntries entries, ref TBuckets buckets, ref OrderedMapState state)
        {
            if (!entries.IsCreated || !buckets.IsCreated) return DeterministicStatus.Disposed;
            if (state.Count == 0 && state.Used == 0) return DeterministicStatus.None;
            // Removed entries already contain no key/value. Only live links own nonempty buckets.
            // Keep contiguous preparation for dense maps; avoid copying holes and empty bucket pages.
            bool denseEntries = state.Count > state.Used / 2;
            bool denseBuckets = state.Count > 0 && (buckets.Capacity <= 4096 || state.Count >= buckets.Capacity / 4);
            for (int phase = 0; phase < 2; phase++)
            {
                bool prepare = phase != 0;
                DeterministicStatus status;
                if (denseEntries)
                {
                    status = prepare ? entries.PrepareWrite(entries.Capacity, 0, state.Used) : entries.CanPrepareWrite(entries.Capacity, 0, state.Used);
                    if (!status.IsSuccess()) return status;
                }
                if (denseBuckets)
                {
                    status = prepare ? buckets.PrepareWrite(buckets.Capacity, 0, buckets.Capacity) : buckets.CanPrepareWrite(buckets.Capacity, 0, buckets.Capacity);
                    if (!status.IsSuccess()) return status;
                }
                if (denseEntries && denseBuckets) continue;
                int encoded = state.OrderHead;
                while (encoded != 0)
                {
                    int index = encoded - 1; var entry = entries.Read(index);
                    if (!denseEntries)
                    { status = ReserveIndex(ref entries, index, prepare); if (!status.IsSuccess()) return status; }
                    if (!denseBuckets)
                    {
                        int bucket = Bucket(entry.Hash, buckets.Capacity);
                        status = prepare ? buckets.PrepareWrite(buckets.Capacity, bucket, 1) : buckets.CanPrepareWrite(buckets.Capacity, bucket, 1);
                        if (!status.IsSuccess()) return status;
                    }
                    encoded = entry.OrderNext;
                }
            }
            if (!denseEntries || !denseBuckets)
            {
                int encoded = state.OrderHead;
                while (encoded != 0)
                {
                    int index = encoded - 1; var entry = entries.Read(index);
                    if (!denseBuckets) buckets.Write(Bucket(entry.Hash, buckets.Capacity), 0);
                    if (!denseEntries) entries.Write(index, default);
                    encoded = entry.OrderNext;
                }
            }
            if (denseEntries) for (int i = 0; i < state.Used; i++) entries.Write(i, default);
            if (denseBuckets) for (int i = 0; i < buckets.Capacity; i++) buckets.Write(i, 0);
            state = default; entries.CommitWrite(); buckets.CommitWrite(); return DeterministicStatus.None;
        }
    }
}
