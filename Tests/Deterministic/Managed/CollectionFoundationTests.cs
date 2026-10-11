using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class CollectionFoundationTests
    {
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void RandomizedListMatchesOrderedCSharpSemantics(SnapshotPolicy snapshots)
        {
            using (var actual = new DeterministicList<int>(new BufferOptions(0, 512, AllocationPolicy.Grow, snapshots, 3, 64)))
            {
                var expected = new List<int>(); uint random = 1337;
                for (int step = 0; step < 6000; step++)
                {
                    random = unchecked(random * 1664525u + 1013904223u);
                    int operation = (int)(random % 6), index = expected.Count == 0 ? 0 : (int)((random >> 8) % (uint)expected.Count);
                    int value = unchecked((int)random);
                    if (operation <= 1 && expected.Count < 512)
                    { expected.Insert(index, value); Assert.AreEqual(DeterministicStatus.None, actual.TryInsert(index, value)); }
                    else if (operation == 2 && expected.Count != 0)
                    { expected[index] = value; actual[index] = value; }
                    else if (operation == 3 && expected.Count != 0)
                    { expected.RemoveAt(index); actual.RemoveAt(index); }
                    else if (operation == 4 && expected.Count != 0)
                    { expected[index] = expected[expected.Count - 1]; expected.RemoveAt(expected.Count - 1); actual.RemoveAtSwapBack(index); }
                    else if (operation == 5 && step % 61 == 0)
                    {
                        actual.TryCapture(out var snapshot).ThrowIfFailure();
                        var before = expected.ToArray(); int capacity = actual.Capacity;
                        actual.Clear(); actual.Add(123);
                        actual.TryRestore(snapshot).ThrowIfFailure();
                        snapshot.Dispose(); Assert.AreEqual(capacity, actual.Capacity); CollectionAssert.AreEqual(before, actual);
                    }
                    Assert.AreEqual(expected.Count, actual.Count);
                    for (int i = 0; i < expected.Count; i++) Assert.AreEqual(expected[i], actual[i], "step=" + step + " index=" + i);
                }
            }
        }

        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void SnapshotsPreserveValuesCapacityAndLeaseIdentity(SnapshotPolicy snapshots)
        {
            using (var list = new DeterministicList<int>(new BufferOptions(4, 17, AllocationPolicy.Grow, snapshots, 2, 64)))
            {
                list.Add(10); list.Add(20); list.TryCapture(out var first).ThrowIfFailure();
                list[0] = 90; list.Add(30); list.TryCapture(out var second).ThrowIfFailure();
                Assert.AreEqual(DeterministicStatus.SnapshotCapacityExceeded, list.TryCapture(out var failed));
                Assert.IsFalse(failed.IsCreated);
                first.TryGet(0, out int oldValue).ThrowIfFailure(); Assert.AreEqual(10, oldValue);
                second.TryGet(0, out oldValue).ThrowIfFailure(); Assert.AreEqual(90, oldValue);
                for (int i = 0; i < 10; i++) list.Add(i);
                list.TryRestore(first).ThrowIfFailure(); Assert.AreEqual(4, list.Capacity); CollectionAssert.AreEqual(new[] { 10, 20 }, list);
                first.Dispose(); list.TryCapture(out var reused).ThrowIfFailure(); first.Dispose();
                Assert.IsTrue(reused.IsCreated); Assert.IsFalse(first.IsCreated);
                list[1] = 999; list.TryRestore(second).ThrowIfFailure(); CollectionAssert.AreEqual(new[] { 90, 20, 30 }, list);
                second.Dispose(); reused.Dispose();
            }
        }

        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void RingGrowthPreservesWrappedOrderWithNonPowerOfTwoLimit(SnapshotPolicy snapshots)
        {
            using (var queue = new DeterministicQueue<int>(new BufferOptions(3, 5, AllocationPolicy.Grow, snapshots, 2, 64)))
            {
                queue.Enqueue(1); queue.Enqueue(2); queue.Enqueue(3);
                Assert.AreEqual(1, queue.Dequeue()); Assert.AreEqual(2, queue.Dequeue());
                queue.Enqueue(4); queue.Enqueue(5); queue.TryCapture(out var snapshot).ThrowIfFailure();
                queue.Enqueue(6); queue.Enqueue(7);
                CollectionAssert.AreEqual(new[] { 3, 4, 5, 6, 7 }, queue);
                Assert.AreEqual(DeterministicStatus.CapacityExceeded, queue.TryEnqueue(8));
                CollectionAssert.AreEqual(new[] { 3, 4, 5, 6, 7 }, queue);
                queue.TryRestore(snapshot).ThrowIfFailure();
                CollectionAssert.AreEqual(new[] { 3, 4, 5 }, queue); Assert.AreEqual(3, queue.Capacity);
                snapshot.Dispose(); Assert.AreEqual(3, queue.Dequeue()); Assert.AreEqual(4, queue.Dequeue()); Assert.AreEqual(5, queue.Dequeue());
                Assert.AreEqual(DeterministicStatus.Empty, queue.TryDequeue(out _));
            }
        }

        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void DequeMatchesReferenceThroughWrappingAndGrowth(SnapshotPolicy snapshots)
        {
            using (var deque = new DeterministicDeque<int>(new BufferOptions(1, 37, AllocationPolicy.Grow, snapshots, 2, 64)))
            {
                var model = new List<int>(); uint random = 9;
                for (int i = 0; i < 2000; i++)
                {
                    random = unchecked(random * 1664525u + 1013904223u);
                    int op = (int)(random >> 26) % 4, value = (int)(random >> 3);
                    if (op == 0 && model.Count < 37) { deque.AddFirst(value); model.Insert(0, value); }
                    else if (op == 1 && model.Count < 37) { deque.AddLast(value); model.Add(value); }
                    else if (op == 2 && model.Count != 0) { Assert.AreEqual(model[0], deque.RemoveFirst()); model.RemoveAt(0); }
                    else if (op == 3 && model.Count != 0) { Assert.AreEqual(model[model.Count - 1], deque.RemoveLast()); model.RemoveAt(model.Count - 1); }
                    CollectionAssert.AreEqual(model, deque);
                }
            }
        }

        [Test]
        public void FixedCapacityFailuresDoNotMutateOrInvalidateEnumerators()
        {
            using (var list = new DeterministicList<int>(2))
            {
                list.Add(4); list.Add(5); var enumerator = list.GetEnumerator();
                Assert.AreEqual(DeterministicStatus.CapacityExceeded, list.TryInsert(1, 6));
                Assert.AreEqual(DeterministicStatus.OutOfRange, list.TrySet(-1, 7));
                Assert.AreEqual(DeterministicStatus.OutOfRange, list.TryRemoveAt(2));
                Assert.IsTrue(enumerator.MoveNext()); Assert.AreEqual(4, enumerator.Current);
                list[0] = 8; Assert.AreEqual(DeterministicStatus.Invalidated, enumerator.TryMoveNext(out _));
                CollectionAssert.AreEqual(new[] { 8, 5 }, list);
                list.TryCapture(out var snapshot).ThrowIfFailure(); var beforeRestore = list.GetEnumerator();
                list.TryRestore(snapshot).ThrowIfFailure(); Assert.AreEqual(DeterministicStatus.Invalidated, beforeRestore.TryMoveNext(out _));
                snapshot.Dispose();
            }
        }

        [Test]
        public void StackArrayCellRingAndBitSetExposeTheirLogicalOrders()
        {
            using (var stack = new DeterministicStack<int>(3))
            { stack.Push(1); stack.Push(2); stack.Push(3); CollectionAssert.AreEqual(new[] { 3, 2, 1 }, stack); Assert.AreEqual(3, stack.Pop()); }
            using (var ring = new DeterministicRingBuffer<int>(2))
            {
                ring.Enqueue(1); ring.Enqueue(2); Assert.AreEqual(DeterministicStatus.CapacityExceeded, ring.TryEnqueue(3));
                ring.Enqueue(3, overwriteOldest: true); CollectionAssert.AreEqual(new[] { 2, 3 }, ring);
            }
            using (var array = new DeterministicArray<int>(2))
            { array[1] = 7; CollectionAssert.AreEqual(new[] { 0, 7 }, array); Assert.AreEqual(DeterministicStatus.OutOfRange, array.TrySet(2, 1)); }
            using (var cell = new DeterministicCell<int>(9))
            { cell.TryCapture(out var snapshot).ThrowIfFailure(); cell.Value = 3; cell.TryRestore(snapshot).ThrowIfFailure(); Assert.AreEqual(9, cell.Value); snapshot.Dispose(); }
            using (var bits = new DeterministicBitSet(130, SnapshotPolicy.CopyOnWrite))
            {
                bits[0] = true; bits[64] = true; bits[129] = true; Assert.AreEqual(3, bits.Count);
                Assert.AreEqual(0, bits.NextSetBit(0)); Assert.AreEqual(64, bits.NextSetBit(1)); Assert.AreEqual(129, bits.NextSetBit(65));
                Assert.AreEqual(-1, bits.NextSetBit(130)); Assert.AreEqual(DeterministicStatus.OutOfRange, bits.TrySet(130, true));
                bits.TryCapture(out var snapshot).ThrowIfFailure(); bits.Clear(); bits.TryRestore(snapshot).ThrowIfFailure();
                Assert.AreEqual(3, bits.Count); snapshot.Dispose();
            }
        }

        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void FixedListAndSnapshotHotPathDoNotAllocateAfterConstruction(SnapshotPolicy snapshots)
        {
            using (var list = new DeterministicList<int>(new BufferOptions(64, snapshots: snapshots, snapshotSlots: 2, pageBytes: 64)))
            {
                for (int i = 0; i < 32; i++) list.Add(i);
                for (int i = 0; i < 50; i++) Cycle(list);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 1000; i++) Cycle(list);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.AreEqual(0L, allocated);
            }
        }
        private static void Cycle(DeterministicList<int> list)
        {
            list.TryCapture(out var snapshot).ThrowIfFailure(); list[16] = 9;
            list.Insert(4, 20); list.RemoveAt(7); list.TryRestore(snapshot).ThrowIfFailure(); snapshot.Dispose();
            int total = 0; foreach (var value in list) total += value;
            if (total != 496) throw new InvalidOperationException();
        }

        [Test]
        public void DisposedSnapshotsAndCollectionsRejectAccessAndForeignRestore()
        {
            var first = new DeterministicList<int>(2); var second = new DeterministicList<int>(2);
            first.Add(10); first.TryCapture(out var snapshot).ThrowIfFailure();
            Assert.AreEqual(DeterministicStatus.InvalidOwner, second.TryRestore(snapshot));
            first.Dispose(); Assert.AreEqual(DeterministicStatus.Disposed, first.TryAdd(1));
            Assert.AreEqual(DeterministicStatus.Disposed, snapshot.TryGet(0, out _)); snapshot.Dispose(); second.Dispose();
        }
    }
}
