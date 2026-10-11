using System;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public unsafe sealed class CanonicalCompoundTests
    {
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void DictionarySparseSetAndMultiMapRebuildDerivedIndexesAndPreserveOrdering(SnapshotPolicy snapshots)
        {
            var options = new BufferOptions(16, snapshots: snapshots, snapshotSlots: 2, pageBytes: 64); var codec = default(Int32Policy);
            byte* bytes = stackalloc byte[4096]; byte* work = stackalloc byte[16384];
            using (var dictionary = new DeterministicDictionary<int, int, Int32Policy>(options))
            using (var sparse = new DeterministicSparseSet<int>(options, options))
            using (var multi = new DeterministicMultiMap<int, int, Int32Policy>(options, options))
            {
                dictionary.Add(9, 90); dictionary.Add(2, 20); dictionary.Add(7, 70); dictionary.Remove(2); dictionary.Add(2, 21);
                sparse.Add(new StableHandle(9, 1), 90); sparse.Add(new StableHandle(2, 3), 20); sparse.Add(new StableHandle(7, 2), 70); sparse.Remove(new StableHandle(2, 3));
                var a = multi.Add(3, 31); multi.Add(1, 11); var removed = multi.Add(3, 32); multi.Remove(removed); multi.Add(3, 33);
                var writer = new DeterministicWriter(bytes, 4096);
                dictionary.TryWriteCanonical(ref writer, codec, codec).ThrowIfFailure(); sparse.TryWriteCanonical(ref writer, codec).ThrowIfFailure(); multi.TryWriteCanonical(ref writer, codec, codec).ThrowIfFailure();
                var next = multi.Add(9, 99); dictionary.Clear(); sparse.Clear(); multi.Clear();
                var reader = new DeterministicReader(bytes, writer.Position); var scratch = new CanonicalScratch(work, 16384);
                dictionary.TryReadCanonical(ref reader, ref scratch, codec, codec).ThrowIfFailure(); sparse.TryReadCanonical(ref reader, ref scratch, codec).ThrowIfFailure(); multi.TryReadCanonical(ref reader, ref scratch, codec, codec).ThrowIfFailure();
                CollectionAssert.AreEqual(new[] { 9, 7, 2 }, dictionary.Keys); Assert.AreEqual(21, dictionary[2]);
                Assert.AreEqual(90, sparse[new StableHandle(9, 1)]); Assert.AreEqual(70, sparse[new StableHandle(7, 2)]); Assert.IsFalse(sparse.ContainsKey(new StableHandle(2, 3)));
                CollectionAssert.AreEqual(new[] { 31, 33 }, multi[3]); Assert.AreEqual(next, multi.Add(9, 99)); multi.TryGetPair(a, out var pair).ThrowIfFailure(); Assert.AreEqual(31, pair.Value);
            }
        }
        [Test]
        public void TruncationInSecondMultiMapBufferCannotPublishFirstBuffer()
        {
            byte* bytes = stackalloc byte[2048]; byte* work = stackalloc byte[4096]; var codec = default(Int32Policy);
            using (var source = new DeterministicMultiMap<int, int, Int32Policy>(4, 8))
            using (var target = new DeterministicMultiMap<int, int, Int32Policy>(4, 8))
            {
                source.Add(1, 11); source.Add(2, 21); source.Add(1, 12); var existing = target.Add(9, 99); var iterator = target.GetEnumerator();
                var writer = new DeterministicWriter(bytes, 2048); source.TryWriteCanonical(ref writer, codec, codec).ThrowIfFailure();
                var reader = new DeterministicReader(bytes, writer.Position - 1); var scratch = new CanonicalScratch(work, 4096);
                Assert.AreEqual(DeterministicStatus.BufferTooSmall, target.TryReadCanonical(ref reader, ref scratch, codec, codec));
                Assert.AreEqual(0, reader.Position); Assert.AreEqual(0, scratch.Position); Assert.AreEqual(1, target.Count); Assert.AreEqual(1, target.KeyCount);
                target.TryGetPair(existing, out var pair).ThrowIfFailure(); Assert.AreEqual(99, pair.Value); Assert.IsTrue(iterator.MoveNext());
            }
        }
        [Test]
        public void WrappedQueueLinkedListBitSetAndSetsRoundTripCanonicalState()
        {
            byte* bytes = stackalloc byte[4096]; byte* work = stackalloc byte[8192]; var codec = default(Int32Policy);
            using (var queue = new DeterministicQueue<int>(5)) using (var linked = new DeterministicLinkedList<int>(5))
            using (var bits = new DeterministicBitSet(129)) using (var set = new DeterministicHashSet<int, Int32Policy>(5))
            using (var sorted = new DeterministicSortedSet<int, Int32Policy>(5))
            {
                for (int i = 1; i <= 5; i++) queue.Enqueue(i); queue.Dequeue(); queue.Dequeue(); queue.Enqueue(6); queue.Enqueue(7);
                var a = linked.AddLast(10); linked.AddFirst(5); linked.AddAfter(a, 15); bits[0] = true; bits[128] = true;
                set.Add(5); set.Add(1); sorted.Add(5); sorted.Add(1);
                var writer = new DeterministicWriter(bytes, 4096); queue.TryWriteCanonical(ref writer, codec).ThrowIfFailure(); linked.TryWriteCanonical(ref writer, codec).ThrowIfFailure();
                bits.TryWriteCanonical(ref writer).ThrowIfFailure(); set.TryWriteCanonical(ref writer, codec).ThrowIfFailure(); sorted.TryWriteCanonical(ref writer, codec).ThrowIfFailure();
                queue.Clear(); linked.Clear(); bits.Clear(); set.Clear(); sorted.Clear();
                var reader = new DeterministicReader(bytes, writer.Position); var scratch = new CanonicalScratch(work, 8192);
                queue.TryReadCanonical(ref reader, ref scratch, codec).ThrowIfFailure(); linked.TryReadCanonical(ref reader, ref scratch, codec).ThrowIfFailure(); bits.TryReadCanonical(ref reader, ref scratch).ThrowIfFailure();
                set.TryReadCanonical(ref reader, ref scratch, codec).ThrowIfFailure(); sorted.TryReadCanonical(ref reader, ref scratch, codec).ThrowIfFailure();
                CollectionAssert.AreEqual(new[] { 3, 4, 5, 6, 7 }, queue); CollectionAssert.AreEqual(new[] { 5, 10, 15 }, linked); Assert.AreEqual(10, linked[a]);
                Assert.AreEqual(2, bits.Count); Assert.IsTrue(bits[128]); CollectionAssert.AreEqual(new[] { 5, 1 }, set); CollectionAssert.AreEqual(new[] { 1, 5 }, sorted);
            }
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void FixedCompoundCheckpointIoAllocatesNoGarbage(SnapshotPolicy snapshots)
        {
            var options = new BufferOptions(16, snapshots: snapshots, pageBytes: 64); byte* bytes = stackalloc byte[8192]; byte* work = stackalloc byte[16384];
            using (var map = new DeterministicMultiMap<int, int, Int32Policy>(options, options))
            {
                for (int i = 0; i < 8; i++) map.Add(i % 3, i); for (int i = 0; i < 20; i++) Cycle(map, bytes, work);
                long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 500; i++) Cycle(map, bytes, work);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before; Assert.AreEqual(0L, allocated);
            }
        }
        private static void Cycle(DeterministicMultiMap<int, int, Int32Policy> map, byte* bytes, byte* work)
        {
            var writer = new DeterministicWriter(bytes, 8192); map.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
            var reader = new DeterministicReader(bytes, writer.Position); var scratch = new CanonicalScratch(work, 16384);
            map.Clear(); map.TryReadCanonical(ref reader, ref scratch, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
        }
    }
}
