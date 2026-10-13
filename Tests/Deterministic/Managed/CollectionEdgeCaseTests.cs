using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    [Category("DeterministicEdgeCase")]
    public sealed unsafe class CollectionEdgeCaseTests
    {
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void ZeroCapacityFamiliesRejectWritesAndCanRestoreEmptySnapshots(SnapshotPolicy policy)
        {
            var options = new BufferOptions(0, snapshots: policy, snapshotSlots: 1, pageBytes: 64);
            using (var array = new DeterministicArray<int>(options))
            using (var list = new DeterministicList<int>(options))
            using (var queue = new DeterministicQueue<int>(options))
            using (var deque = new DeterministicDeque<int>(options))
            using (var stack = new DeterministicStack<int>(options))
            using (var ring = new DeterministicRingBuffer<int>(options))
            using (var slots = new DeterministicSlotMap<int>(options))
            using (var linked = new DeterministicLinkedList<int>(options))
            using (var map = new DeterministicMultiMap<int, int, Int32Policy>(options, options))
            using (var heap = new DeterministicPriorityQueue<int, int, Int32Policy>(options))
            {
                Assert.AreEqual(DeterministicStatus.OutOfRange, array.TrySet(0, 1));
                Assert.AreEqual(DeterministicStatus.CapacityExceeded, list.TryAdd(1));
                Assert.AreEqual(DeterministicStatus.CapacityExceeded, queue.TryEnqueue(1));
                Assert.AreEqual(DeterministicStatus.CapacityExceeded, deque.TryAddFirst(1));
                Assert.AreEqual(DeterministicStatus.CapacityExceeded, stack.TryPush(1));
                Assert.AreEqual(DeterministicStatus.CapacityExceeded, ring.TryEnqueue(1, true));
                Assert.AreEqual(DeterministicStatus.CapacityExceeded, slots.TryAdd(1, out var slot)); Assert.IsFalse(slot.IsValid);
                Assert.AreEqual(DeterministicStatus.CapacityExceeded, linked.TryAddLast(1, out var node)); Assert.IsFalse(node.IsValid);
                Assert.AreEqual(DeterministicStatus.CapacityExceeded, map.TryAdd(1, 1, out var pair)); Assert.IsFalse(pair.IsValid);
                Assert.AreEqual(DeterministicStatus.CapacityExceeded, heap.TryEnqueue(1, 1)); Assert.AreEqual(0UL, heap.NextSequence);
                Assert.AreEqual(DeterministicStatus.Empty, queue.TryDequeue(out _));
                Assert.AreEqual(DeterministicStatus.Empty, deque.TryRemoveLast(out _));
                Assert.AreEqual(DeterministicStatus.Empty, stack.TryPop(out _));
                Assert.AreEqual(DeterministicStatus.Empty, ring.TryDequeue(out _));
                Assert.AreEqual(0, map.KeyCount); Assert.AreEqual(0, map.Count);
                map.TryCapture(out var snapshot).ThrowIfFailure();
                try { Assert.AreEqual(0, snapshot.Count); map.TryRestore(snapshot).ThrowIfFailure(); }
                finally { snapshot.Dispose(); }
                array.TryCapture(out var empty).ThrowIfFailure();
                try { Assert.AreEqual(0, empty.Count); array.TryRestore(empty).ThrowIfFailure(); }
                finally { empty.Dispose(); }
            }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(63)] [TestCase(64)]
        [TestCase(65)] [TestCase(127)] [TestCase(128)] [TestCase(129)]
        public void BitSetWordBoundariesAndUnalignedWireOffsetsMatchBooleanModel(int length)
        {
            using (var bits = new DeterministicBitSet(length, SnapshotPolicy.CopyOnWrite))
            using (var loaded = new DeterministicBitSet(length, SnapshotPolicy.CopyOnWrite))
            {
                var model = new bool[length]; int count = 0;
                for (int i = 0; i < length; i++) { model[i] = i % 3 == 0 || i == length - 1; bits[i] = model[i]; if (model[i]) count++; }
                Assert.AreEqual(count, bits.Count);
                Assert.AreEqual(DeterministicStatus.OutOfRange, bits.TrySet(-1, true));
                Assert.AreEqual(DeterministicStatus.OutOfRange, bits.TrySet(length, true));
                Assert.AreEqual(DeterministicStatus.OutOfRange, bits.TryGet(int.MaxValue, out _));
                for (int start = 0; start <= length; start++)
                {
                    int expected = -1; for (int i = start; i < length; i++) if (model[i]) { expected = i; break; }
                    Assert.AreEqual(expected, bits.NextSetBit(start));
                }
                Assert.AreEqual(-1, bits.NextSetBit(int.MaxValue));
                byte* bytes = stackalloc byte[512]; byte* work = stackalloc byte[2048];
                var writer = new DeterministicWriter(bytes + 3, 509); bits.TryWriteCanonical(ref writer).ThrowIfFailure();
                var reader = new DeterministicReader(bytes + 3, writer.Position); var scratch = new CanonicalScratch(work, 2048);
                loaded.TryReadCanonical(ref reader, ref scratch).ThrowIfFailure(); Assert.AreEqual(writer.Position, reader.Position);
                Assert.AreEqual(count, loaded.Count); for (int i = 0; i < length; i++) Assert.AreEqual(model[i], loaded[i]);
                Assert.AreEqual(-1, loaded.NextSetBit(length)); Assert.AreEqual(count, bits.Count);
            }
        }

        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void CompoundSnapshotAliasesCannotReleaseReusedLeasesOrRestoreForeignOwners(SnapshotPolicy policy)
        {
            var options = new BufferOptions(4, snapshots: policy, snapshotSlots: 2, pageBytes: 64);
            using (var map = new DeterministicMultiMap<int, int, Int32Policy>(options, options))
            using (var foreign = new DeterministicMultiMap<int, int, Int32Policy>(options, options))
            {
                var firstHandle = map.Add(1, 10); map.TryCapture(out var first).ThrowIfFailure(); var alias = first;
                map.TrySetValue(firstHandle, 20).ThrowIfFailure(); map.TryCapture(out var second).ThrowIfFailure();
                try
                {
                    var iterator = map.GetEnumerator();
                    Assert.AreEqual(DeterministicStatus.SnapshotCapacityExceeded, map.TryCapture(out var exhausted)); Assert.IsFalse(exhausted.IsCreated);
                    Assert.IsTrue(iterator.MoveNext()); Assert.AreEqual(20, iterator.Current.Value);
                    Assert.AreEqual(DeterministicStatus.InvalidOwner, foreign.TryRestore(first)); Assert.AreEqual(0, foreign.Count);
                    first.Dispose(); Assert.IsFalse(alias.IsCreated); map.TryCapture(out var reused).ThrowIfFailure();
                    try
                    {
                        alias.Dispose(); Assert.IsTrue(reused.IsCreated);
                        Assert.AreEqual(DeterministicStatus.Disposed, map.TryRestore(alias));
                        map.Clear(); map.Add(3, 30); map.TryRestore(second).ThrowIfFailure();
                        map.TryGetPair(firstHandle, out var pair).ThrowIfFailure(); Assert.AreEqual(20, pair.Value);
                        map.TryRestore(reused).ThrowIfFailure(); Assert.AreEqual(1, map.KeyCount);
                    }
                    finally { reused.Dispose(); }
                }
                finally { first.Dispose(); second.Dispose(); }
            }
        }

        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void ExtremeAndConflictingHandlesNeverGrowOrMutateSparseStorage(SnapshotPolicy policy)
        {
            var dense = new BufferOptions(0, 4, AllocationPolicy.Grow, policy, 1, 64);
            var sparse = new BufferOptions(0, 130, AllocationPolicy.Grow, policy, 1, 64);
            using (var set = new DeterministicSparseSet<int>(dense, sparse))
            {
                var valid = new StableHandle(0, 1); set.Add(valid, 7);
                int capacity = set.Capacity, entityCapacity = set.EntitySlotCapacity; ulong revision = set.Revision;
                var iterator = set.GetEnumerator();
                Assert.AreEqual(DeterministicStatus.InvalidConfiguration, set.TryAdd(new StableHandle(uint.MaxValue, 0), 99));
                Assert.AreEqual(DeterministicStatus.OutOfRange, set.TrySet(new StableHandle(uint.MaxValue, uint.MaxValue), 99));
                Assert.AreEqual(DeterministicStatus.OutOfRange, set.TryAdd(new StableHandle(130, 1), 99));
                Assert.AreEqual(DeterministicStatus.ProtocolConflict, set.TrySet(new StableHandle(0, 2), 99));
                Assert.AreEqual(DeterministicStatus.NotFound, set.TryRemove(new StableHandle(uint.MaxValue, 1)));
                Assert.AreEqual(revision, set.Revision); Assert.AreEqual(capacity, set.Capacity); Assert.AreEqual(entityCapacity, set.EntitySlotCapacity);
                Assert.IsTrue(iterator.MoveNext()); Assert.AreEqual(7, iterator.Current.Value); Assert.AreEqual(1, set.Count);
                var highest = new StableHandle(129, uint.MaxValue); set.Add(highest, -7);
                Assert.AreEqual(-7, set[highest]); Assert.AreEqual(130, set.EntitySlotCapacity); Assert.IsTrue(set.Remove(highest));
                Assert.AreEqual(7, set[valid]);
            }
        }

        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void SignedPriorityExtremesAndDuplicateDeadlinesKeepTotalOrder(SnapshotPolicy policy)
        {
            var options = new BufferOptions(12, snapshots: policy, snapshotSlots: 1, pageBytes: 64);
            using (var heap = new DeterministicPriorityQueue<int, long, Int64Policy>(options))
            using (var sorted = new DeterministicSortedDictionary<long, int, Int64Policy>(options))
            {
                long[] priorities = { long.MaxValue, 0, long.MinValue, -1, 1, long.MinValue, long.MaxValue, 0 };
                var oracle = new List<int>();
                for (int i = 0; i < priorities.Length; i++) { heap.Enqueue(i, priorities[i]); sorted.TrySet(priorities[i], i).ThrowIfFailure(); oracle.Add(i); }
                oracle.Sort((a, b) => { int c = priorities[a].CompareTo(priorities[b]); return c == 0 ? a.CompareTo(b) : c; });
                foreach (int expected in oracle)
                { heap.TryDequeue(out int actual, out long priority).ThrowIfFailure(); Assert.AreEqual(expected, actual); Assert.AreEqual(priorities[expected], priority); }
                long[] keys = { long.MinValue, -1, 0, 1, long.MaxValue }; int[] values = { 5, 3, 7, 4, 6 };
                for (int rank = 0; rank < keys.Length; rank++)
                { sorted.TryGetAt(rank, out var pair).ThrowIfFailure(); Assert.AreEqual(keys[rank], pair.Key); Assert.AreEqual(values[rank], pair.Value); }
                Assert.AreEqual(DeterministicStatus.Empty, heap.TryDequeue(out _, out _)); Assert.AreEqual(8UL, heap.NextSequence);
            }
        }

        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void SingleElementRingCanOverwriteRepeatedlyAndRestoreOriginalHead(SnapshotPolicy policy)
        {
            using (var ring = new DeterministicRingBuffer<int>(1, policy, 1))
            {
                ring.TryEnqueue(int.MinValue).ThrowIfFailure(); ring.TryCapture(out var snapshot).ThrowIfFailure();
                try
                {
                    for (int value = 0; value < 257; value++)
                    { ring.TryEnqueue(value, true).ThrowIfFailure(); Assert.AreEqual(1, ring.Count); Assert.AreEqual(value, ring[0]); }
                    Assert.AreEqual(DeterministicStatus.CapacityExceeded, ring.TryEnqueue(int.MaxValue)); Assert.AreEqual(256, ring.Dequeue());
                    ring.TryRestore(snapshot).ThrowIfFailure(); Assert.AreEqual(int.MinValue, ring.Dequeue());
                    Assert.AreEqual(DeterministicStatus.Empty, ring.TryDequeue(out _));
                }
                finally { snapshot.Dispose(); }
            }
        }

        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void ZeroSnapshotBudgetStillAllowsCompoundMutationsAndCanonicalIo(SnapshotPolicy policy)
        {
            var options = new BufferOptions(4, snapshots: policy, snapshotSlots: 0, pageBytes: 64);
            using (var source = new DeterministicMultiMap<int, int, Int32Policy>(options, options))
            using (var target = new DeterministicMultiMap<int, int, Int32Policy>(options, options))
            {
                source.Add(1, 11); source.Add(1, 12);
                Assert.AreEqual(DeterministicStatus.SnapshotCapacityExceeded, source.TryCapture(out var snapshot)); Assert.IsFalse(snapshot.IsCreated);
                source.Add(2, 21); byte* bytes = stackalloc byte[4096]; byte* work = stackalloc byte[16384];
                var writer = new DeterministicWriter(bytes, 4096); source.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
                var reader = new DeterministicReader(bytes, writer.Position); var scratch = new CanonicalScratch(work, 16384);
                target.TryReadCanonical(ref reader, ref scratch, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
                Assert.AreEqual(writer.Position, reader.Position); CollectionAssert.AreEqual(new[] { 11, 12 }, target[1]);
                CollectionAssert.AreEqual(new[] { 21 }, target[2]); Assert.AreEqual(3, target.Count);
                target.RemoveAll(1); Assert.AreEqual(new StableHandle(0, 2), target.Add(3, 31));
            }
        }
    }
}
