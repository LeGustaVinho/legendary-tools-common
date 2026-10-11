using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class LinkedListTests
    {
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void HandlesAndBidirectionalLinksMatchListModelAcrossRestoration(SnapshotPolicy snapshots)
        {
            using (var list = new DeterministicLinkedList<int>(new BufferOptions(0, 37, AllocationPolicy.Grow, snapshots, 2, 64)))
            {
                var model = new List<SlotMapItem<int>>(); uint random = 49;
                for (int step = 0; step < 3000; step++)
                {
                    random = unchecked(random * 1664525u + 1013904223u);
                    if (model.Count != 0 && ((random & 7) < 3 || model.Count == 37))
                    { int i = (int)((random >> 8) % (uint)model.Count); list.Remove(model[i].Handle); model.RemoveAt(i); }
                    else
                    {
                        int i = (int)((random >> 8) % (uint)(model.Count + 1));
                        var handle = i == model.Count ? list.AddLast(step) : list.AddBefore(model[i].Handle, step); model.Insert(i, new SlotMapItem<int>(handle, step));
                    }
                    if (step % 53 == 0)
                    { list.TryCapture(out var snapshot).ThrowIfFailure(); list.Clear(); list.AddFirst(-99); list.TryRestore(snapshot).ThrowIfFailure(); snapshot.Dispose(); }
                    Assert.AreEqual(model.Count, list.Count); int rank = 0;
                    foreach (var value in list) Assert.AreEqual(model[rank++].Value, value);
                    var current = list.First; StableHandle previous = default;
                    for (int i = 0; i < model.Count; i++)
                    {
                        Assert.AreEqual(model[i].Handle, current); Assert.AreEqual(model[i].Value, list[current]);
                        list.TryGetLinks(current, out var prev, out var next).ThrowIfFailure(); Assert.AreEqual(previous, prev); previous = current; current = next;
                    }
                    Assert.IsFalse(current.IsValid); Assert.AreEqual(previous, list.Last);
                }
            }
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void FixedLinkedListDoesNotAllocateNodesOrSnapshots(SnapshotPolicy snapshots)
        {
            using (var list = new DeterministicLinkedList<int>(new BufferOptions(32, snapshots: snapshots, pageBytes: 64)))
            {
                for (int i = 0; i < 16; i++) list.AddLast(i); for (int i = 0; i < 20; i++) Cycle(list);
                long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 500; i++) Cycle(list);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before; Assert.AreEqual(0L, allocated);
            }
        }
        private static void Cycle(DeterministicLinkedList<int> list)
        { list.TryCapture(out var snapshot).ThrowIfFailure(); list.RemoveFirst(); list.AddAfter(list.First, 100); list.TryRestore(snapshot).ThrowIfFailure(); snapshot.Dispose(); }
        [Test]
        public void StaleNodesCannotBeInsertedRelativeToOrRemovedAgain()
        {
            using (var list = new DeterministicLinkedList<int>(2))
            {
                var old = list.AddLast(1); list.AddFirst(2); list.Remove(old); var newer = list.AddLast(3);
                Assert.AreEqual(new StableHandle(old.Index, old.Generation + 1), newer);
                Assert.AreEqual(DeterministicStatus.NotFound, list.TryAddAfter(old, 4, out _));
                Assert.AreEqual(DeterministicStatus.NotFound, list.TryRemove(old));
                CollectionAssert.AreEqual(new[] { 2, 3 }, list);
            }
        }
    }
}
