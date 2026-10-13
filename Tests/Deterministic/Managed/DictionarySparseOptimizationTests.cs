using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    [Category("DeterministicDictionarySparseOptimization")]
    public sealed unsafe class DictionarySparseOptimizationTests
    {
        private struct HashPolicy : IDeterministicEquality<int>
        {
            internal bool Collisions;
            public bool IsValid => true;
            public bool Equals(in int a, in int b) => a == b;
            public ulong Hash(in int key) => Collisions ? 1UL : default(Int32Policy).Hash(key);
        }
        private struct Payload
        {
            internal fixed ulong Words[32];
            internal static Payload Create(int key)
            {
                Payload value = default;
                for (int i = 0; i < 32; i++) value.Words[i] = Word(key, i);
                return value;
            }
            internal static ulong Word(int key, int word) => unchecked((ulong)(uint)key * 0x9e3779b97f4a7c15UL ^ (ulong)(word + 1) * 0xd6e8feb86659fd93UL);
        }
        private static void Verify(Payload value, int key)
        { for (int i = 0; i < 32; i++) Assert.AreEqual(Payload.Word(key, i), value.Words[i], "word " + i); }
        private static IEnumerable<object[]> ClearCases()
        {
            foreach (SnapshotPolicy policy in new[] { SnapshotPolicy.FullPerTick, SnapshotPolicy.KeyframesAndDeltas, SnapshotPolicy.CopyOnWrite })
                foreach (bool collisions in new[] { false, true })
                    for (int mode = 0; mode < 4; mode++) yield return new object[] { policy, collisions, mode };
        }
        [TestCaseSource(nameof(ClearCases))]
        public void ClearPreservesRootsAcrossDensityModesAndReuse(SnapshotPolicy policy, bool collisions, int mode)
        {
            const int capacity = 512;
            using var map = new DeterministicDictionary<int, Payload, HashPolicy>(
                new BufferOptions(capacity, snapshots: policy, snapshotSlots: 2, pageBytes: 256),
                new HashPolicy { Collisions = collisions });
            int used = mode == 2 ? 8 : capacity;
            var order = new List<int>();
            for (int i = 0; i < used; i++) { map.Add(i, Payload.Create(i)); order.Add(i); }
            for (int i = 0; i < used; i++)
                if (mode == 3 || mode == 1 && i % 64 != 0) { map.Remove(i); order.Remove(i); }
            map.TryCapture(out var root).ThrowIfFailure();
            var iterator = map.GetEnumerator();
            long liveBefore = map.Entries.Owner.MemoryStats.LivePayloadBytes + map.Buckets.Owner.MemoryStats.LivePayloadBytes;
            try
            {
                map.TryClear().ThrowIfFailure();
                Assert.AreEqual(0, map.Count); Assert.AreEqual(0, map.State.Used);
                Assert.AreEqual(DeterministicStatus.Invalidated, iterator.TryMoveNext(out _));
                for (int i = 0; i < capacity; i++) Assert.AreEqual(0, map.Entries.Read(i).Active);
                for (int i = 0; i < map.Buckets.Capacity; i++) Assert.AreEqual(0, map.Buckets.Read(i));
                if (mode == 3 && policy == SnapshotPolicy.CopyOnWrite)
                    Assert.AreEqual(liveBefore, map.Entries.Owner.MemoryStats.LivePayloadBytes + map.Buckets.Owner.MemoryStats.LivePayloadBytes);
                if (mode == 1 && policy == SnapshotPolicy.CopyOnWrite)
                    Assert.Less(map.Entries.Owner.MemoryStats.LivePayloadBytes + map.Buckets.Owner.MemoryStats.LivePayloadBytes - liveBefore, capacity * sizeof(Payload) / 4);
                for (int i = 0; i < order.Count; i++)
                {
                    root.TryGetAt(i, out var pair).ThrowIfFailure(); Assert.AreEqual(order[i], pair.Key); Verify(pair.Value, pair.Key);
                }
                map.TryCapture(out var emptyRoot).ThrowIfFailure();
                try
                {
                    for (int i = 0; i < capacity; i++) map.Add(1000 + i, Payload.Create(1000 + i));
                    Assert.AreEqual(capacity, map.State.Used);
                    for (int i = 0; i < used; i++) Assert.IsFalse(map.ContainsKey(i));
                    map.TryRestore(emptyRoot).ThrowIfFailure(); Assert.AreEqual(0, map.Count);
                    map.Add(-7, Payload.Create(-7)); CollectionAssert.AreEqual(new[] { -7 }, map.Keys);
                    map.TryRestore(root).ThrowIfFailure(); CollectionAssert.AreEqual(order, map.Keys);
                    foreach (int key in order) Verify(map[key], key);
                }
                finally { emptyRoot.Dispose(); }
            }
            finally { root.Dispose(); }
        }
        private static IEnumerable<object[]> SnapshotCases()
        {
            foreach (SnapshotPolicy policy in new[] { SnapshotPolicy.FullPerTick, SnapshotPolicy.KeyframesAndDeltas, SnapshotPolicy.CopyOnWrite })
                foreach (int count in new[] { 0, 1, 31, 32 }) yield return new object[] { policy, count };
        }
        [TestCaseSource(nameof(SnapshotCases))]
        public void SnapshotOrdinalsPreserveAllPayloadWordsAfterHolesReinsertionAndLeaseReuse(SnapshotPolicy policy, int count)
        {
            using var map = new DeterministicDictionary<int, Payload, Int32Policy>(
                new BufferOptions(0, 64, AllocationPolicy.Grow, policy, 1, 256));
            var order = new List<int>();
            for (int i = 0; i < count; i++) { map.Add(i, Payload.Create(i)); order.Add(i); }
            for (int i = 1; i < count; i += 3) { map.Remove(i); order.Remove(i); }
            if (count > 1) for (int i = 0; i < 7; i++) { map.Add(100 + i, Payload.Create(100 + i)); order.Add(100 + i); }
            map.TryCapture(out var root).ThrowIfFailure(); var stale = root;
            try
            {
                map.Clear();
                for (int i = 0; i < 64; i++) map.Add(-i - 1, Payload.Create(-i - 1));
                for (int pass = 0; pass < 2; pass++)
                    for (int ordinal = 0; ordinal < order.Count; ordinal++)
                    {
                        int index = pass == 0 ? ordinal : order.Count - 1 - ordinal;
                        root.TryGetAt(index, out var pair).ThrowIfFailure();
                        Assert.AreEqual(order[index], pair.Key); Verify(pair.Value, pair.Key);
                    }
                Assert.AreEqual(DeterministicStatus.OutOfRange, root.TryGetAt(-1, out var invalid));
                Assert.AreEqual(default(KeyValuePair<int, Payload>), invalid);
                Assert.AreEqual(DeterministicStatus.OutOfRange, root.TryGetAt(order.Count, out _));
                map.TryRestore(root).ThrowIfFailure(); CollectionAssert.AreEqual(order, map.Keys);
            }
            finally { root.Dispose(); }
            map.TryCapture(out var reused).ThrowIfFailure();
            try
            {
                stale.Dispose();
                Assert.AreEqual(DeterministicStatus.Disposed, stale.TryGetAt(0, out _));
                Assert.IsTrue(reused.IsCreated);
            }
            finally { reused.Dispose(); }
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void SparseMembershipRemovalAndGenerationReusePreservePayloadsAndRoot(SnapshotPolicy policy)
        {
            var dense = new BufferOptions(4, snapshots: policy, snapshotSlots: 1, pageBytes: 256);
            var sparse = new BufferOptions(1024, snapshots: policy, snapshotSlots: 1, pageBytes: 256);
            using var set = new DeterministicSparseSet<Payload>(dense, sparse);
            var first = new StableHandle(1, 7); var middle = new StableHandle(513, 9); var last = new StableHandle(1023, uint.MaxValue);
            set.Add(first, Payload.Create(1)); set.Add(middle, Payload.Create(513)); set.Add(last, Payload.Create(1023));
            set.TryCapture(out var root).ThrowIfFailure();
            try
            {
                var invalid = new[] { default(StableHandle), new StableHandle(1, 6), new StableHandle(512, 1), new StableHandle(1024, 1), new StableHandle(uint.MaxValue, 1) };
                ulong revision = set.Revision;
                foreach (var handle in invalid)
                {
                    Assert.IsFalse(set.ContainsKey(handle));
                    Assert.AreEqual(DeterministicStatus.NotFound, set.TryGet(handle, out var zero));
                    Assert.AreEqual(default(Payload), zero);
                    Assert.AreEqual(DeterministicStatus.NotFound, set.TryRemove(handle));
                }
                Assert.AreEqual(revision, set.Revision);
                Assert.IsTrue(set.ContainsKey(first)); Assert.IsTrue(set.ContainsKey(last));
                set.TryRemove(first).ThrowIfFailure();
                Assert.AreEqual(last, set.Dense.Read(0).Handle); Verify(set.Dense.Read(0).Value, 1023);
                set.TryRemove(middle).ThrowIfFailure();
                Assert.IsFalse(set.ContainsKey(middle)); Assert.AreEqual(1, set.Count);
                set.Add(new StableHandle(513, 10), Payload.Create(514));
                Assert.IsFalse(set.ContainsKey(middle)); Assert.IsTrue(set.ContainsKey(new StableHandle(513, 10)));
                Assert.AreEqual(DeterministicStatus.ProtocolConflict, set.TrySet(middle, Payload.Create(-1)));
                foreach (var handle in new[] { first, middle, last }) { root.TryGet(handle, out var value).ThrowIfFailure(); Verify(value, (int)handle.Index); }
                set.TryRestore(root).ThrowIfFailure();
                Assert.IsTrue(set.ContainsKey(middle)); Assert.IsFalse(set.ContainsKey(new StableHandle(513, 10)));
            }
            finally { root.Dispose(); }
        }
        private sealed class Owner<T> where T : unmanaged
        {
            internal readonly T[] Data;
            internal readonly bool[] Prepared;
            internal int Failure = -1, Writes, Commits, Reads;
            internal bool DenyReads;
            internal Owner(int count) { Data = new T[count]; Prepared = new bool[count]; }
        }
        private struct CheckedBuffer<T> : IDeterministicBuffer<T> where T : unmanaged
        {
            internal Owner<T> Owner;
            public int Capacity => Owner.Data.Length;
            public ulong Revision => (ulong)Owner.Commits;
            public bool IsCreated => Owner != null;
            public T Read(int index) { if (Owner.DenyReads) throw new InvalidOperationException("Unexpected dense payload read"); Owner.Reads++; return Owner.Data[index]; }
            public void Write(int index, in T value)
            { if (!Owner.Prepared[index]) throw new InvalidOperationException("Unprepared write"); Owner.Data[index] = value; Owner.Writes++; }
            public DeterministicStatus CanPrepareWrite(int required, int start, int count)
                => Owner.Failure >= start && Owner.Failure < (long)start + count ? DeterministicStatus.SnapshotCapacityExceeded : DeterministicStatus.None;
            public DeterministicStatus PrepareWrite(int required, int start, int count)
            { var status = CanPrepareWrite(required, start, count); if (status.IsSuccess()) for (int i = start; i < start + count; i++) Owner.Prepared[i] = true; return status; }
            public void CommitWrite() => Owner.Commits++;
        }
        [TestCase(false, 0)]
        [TestCase(false, 64)]
        [TestCase(false, 128)]
        [TestCase(true, 0)]
        [TestCase(true, 64)]
        [TestCase(true, 128)]
        public void FragmentedClearPreflightsEveryActiveEntryAndBucketBeforeWriting(bool failBucket, int failingKey)
        {
            var entriesOwner = new Owner<OrderedHashEntry<int, int>>(256); var bucketsOwner = new Owner<int>(512);
            var entries = new CheckedBuffer<OrderedHashEntry<int, int>> { Owner = entriesOwner };
            var buckets = new CheckedBuffer<int> { Owner = bucketsOwner }; var state = default(OrderedMapState);
            var options = new BufferOptions(256); var policy = default(Int32Policy);
            for (int i = 0; i < 256; i++) OrderedHashKernel<int, int, Int32Policy, CheckedBuffer<OrderedHashEntry<int, int>>, CheckedBuffer<int>>.TryPut(ref entries, ref buckets, ref state, options, i, i + 1, policy, true).ThrowIfFailure();
            for (int i = 0; i < 256; i++) if (i % 64 != 0)
                OrderedHashKernel<int, int, Int32Policy, CheckedBuffer<OrderedHashEntry<int, int>>, CheckedBuffer<int>>.TryRemove(ref entries, ref buckets, ref state, i, policy).ThrowIfFailure();
            var oldState = state; var oldEntries = (OrderedHashEntry<int, int>[])entriesOwner.Data.Clone(); var oldBuckets = (int[])bucketsOwner.Data.Clone();
            int writes = entriesOwner.Writes + bucketsOwner.Writes, commits = entriesOwner.Commits + bucketsOwner.Commits;
            Array.Clear(entriesOwner.Prepared, 0, entriesOwner.Prepared.Length); Array.Clear(bucketsOwner.Prepared, 0, bucketsOwner.Prepared.Length);
            if (failBucket) bucketsOwner.Failure = (int)(policy.Hash(failingKey) & 511); else entriesOwner.Failure = failingKey;
            Assert.AreEqual(DeterministicStatus.SnapshotCapacityExceeded,
                OrderedHashKernel<int, int, Int32Policy, CheckedBuffer<OrderedHashEntry<int, int>>, CheckedBuffer<int>>.TryClear(ref entries, ref buckets, ref state));
            Assert.AreEqual(oldState, state); CollectionAssert.AreEqual(oldEntries, entriesOwner.Data); CollectionAssert.AreEqual(oldBuckets, bucketsOwner.Data);
            Assert.AreEqual(writes, entriesOwner.Writes + bucketsOwner.Writes); Assert.AreEqual(commits, entriesOwner.Commits + bucketsOwner.Commits);
            entriesOwner.Failure = bucketsOwner.Failure = -1;
            OrderedHashKernel<int, int, Int32Policy, CheckedBuffer<OrderedHashEntry<int, int>>, CheckedBuffer<int>>.TryClear(ref entries, ref buckets, ref state).ThrowIfFailure();
            Assert.AreEqual(0, state.Count); Assert.AreEqual(0, state.Used);
        }
        [Test]
        public void SparseContainsAndLastRemovalRequireNoDensePayloadRead()
        {
            var denseOwner = new Owner<SparseSetEntry<Payload>>(2); var sparseOwner = new Owner<SparseSetIndex>(32);
            var dense = new CheckedBuffer<SparseSetEntry<Payload>> { Owner = denseOwner };
            var sparse = new CheckedBuffer<SparseSetIndex> { Owner = sparseOwner }; var state = default(SequenceState); var handle = new StableHandle(31, 9);
            SparseSetKernel<Payload, CheckedBuffer<SparseSetEntry<Payload>>, CheckedBuffer<SparseSetIndex>>.TryPut(ref dense, ref sparse, ref state, 32, handle, Payload.Create(31), true).ThrowIfFailure();
            denseOwner.DenyReads = true;
            Assert.IsTrue(SparseSetKernel<Payload, CheckedBuffer<SparseSetEntry<Payload>>, CheckedBuffer<SparseSetIndex>>.ContainsKey(ref dense, ref sparse, handle));
            Assert.IsFalse(SparseSetKernel<Payload, CheckedBuffer<SparseSetEntry<Payload>>, CheckedBuffer<SparseSetIndex>>.ContainsKey(ref dense, ref sparse, new StableHandle(31, 8)));
            SparseSetKernel<Payload, CheckedBuffer<SparseSetEntry<Payload>>, CheckedBuffer<SparseSetIndex>>.TryRemove(ref dense, ref sparse, ref state, handle).ThrowIfFailure();
            Assert.AreEqual(0, state.Count); Assert.AreEqual(default(SparseSetEntry<Payload>), denseOwner.Data[0]); Assert.AreEqual(default(SparseSetIndex), sparseOwner.Data[31]);
        }
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void SparseSwapRemovalPreflightsBothDenseAndSparseTargets(int failure)
        {
            var denseOwner = new Owner<SparseSetEntry<Payload>>(3); var sparseOwner = new Owner<SparseSetIndex>(32);
            var dense = new CheckedBuffer<SparseSetEntry<Payload>> { Owner = denseOwner }; var sparse = new CheckedBuffer<SparseSetIndex> { Owner = sparseOwner };
            var state = default(SequenceState); var first = new StableHandle(1, 7); var last = new StableHandle(31, 9);
            foreach (var handle in new[] { first, new StableHandle(16, 2), last })
                SparseSetKernel<Payload, CheckedBuffer<SparseSetEntry<Payload>>, CheckedBuffer<SparseSetIndex>>.TryPut(ref dense, ref sparse, ref state, 32, handle, Payload.Create((int)handle.Index), true).ThrowIfFailure();
            var oldState = state; var oldDense = (SparseSetEntry<Payload>[])denseOwner.Data.Clone(); var oldSparse = (SparseSetIndex[])sparseOwner.Data.Clone();
            int writes = denseOwner.Writes + sparseOwner.Writes, commits = denseOwner.Commits + sparseOwner.Commits;
            Array.Clear(denseOwner.Prepared, 0, denseOwner.Prepared.Length); Array.Clear(sparseOwner.Prepared, 0, sparseOwner.Prepared.Length);
            if (failure < 2) denseOwner.Failure = failure == 0 ? 0 : 2; else sparseOwner.Failure = failure == 2 ? 1 : 31;
            Assert.AreEqual(DeterministicStatus.SnapshotCapacityExceeded,
                SparseSetKernel<Payload, CheckedBuffer<SparseSetEntry<Payload>>, CheckedBuffer<SparseSetIndex>>.TryRemove(ref dense, ref sparse, ref state, first));
            Assert.AreEqual(oldState, state); CollectionAssert.AreEqual(oldDense, denseOwner.Data); CollectionAssert.AreEqual(oldSparse, sparseOwner.Data);
            Assert.AreEqual(writes, denseOwner.Writes + sparseOwner.Writes); Assert.AreEqual(commits, denseOwner.Commits + sparseOwner.Commits);
            denseOwner.Failure = sparseOwner.Failure = -1;
            SparseSetKernel<Payload, CheckedBuffer<SparseSetEntry<Payload>>, CheckedBuffer<SparseSetIndex>>.TryRemove(ref dense, ref sparse, ref state, first).ThrowIfFailure();
            Assert.AreEqual(2, state.Count); Assert.AreEqual(last, denseOwner.Data[0].Handle); Verify(denseOwner.Data[0].Value, 31);
        }
    }
}

