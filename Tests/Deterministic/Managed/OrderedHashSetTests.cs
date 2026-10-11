using System;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class OrderedHashSetTests
    {
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void SetOperationsPreserveOrderAndHandleDuplicateInput(SnapshotPolicy snapshots)
        {
            using (var set = new DeterministicHashSet<int, Int32Policy>(new BufferOptions(8, snapshots: snapshots)))
            {
                Assert.IsTrue(set.Add(1)); Assert.IsTrue(set.Add(2)); Assert.IsFalse(set.Add(1));
                set.UnionWith(new[] { 3, 3, 4 }); CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, set);
                set.IntersectWith(new[] { 4, 2, 2, 999 }); CollectionAssert.AreEqual(new[] { 2, 4 }, set);
                Assert.IsTrue(set.SetEquals(new[] { 4, 2, 2 })); Assert.IsTrue(set.IsSubsetOf(new[] { 2, 4, 5 }));
                Assert.IsTrue(set.IsProperSubsetOf(new[] { 2, 4, 5 })); Assert.IsTrue(set.IsProperSupersetOf(new[] { 2, 2 }));
                Assert.IsFalse(set.IsSupersetOf(new[] { 2, 5 })); Assert.IsTrue(set.Overlaps(new[] { 999, 4 }));
                set.SymmetricExceptWith(new[] { 2, 2, 5, 5 }); CollectionAssert.AreEqual(new[] { 4, 5 }, set);
                set.TryCapture(out var snapshot).ThrowIfFailure(); set.Remove(4); set.Add(4); CollectionAssert.AreEqual(new[] { 5, 4 }, set);
                set.TryRestore(snapshot).ThrowIfFailure(); CollectionAssert.AreEqual(new[] { 4, 5 }, set); snapshot.Dispose();
                set.ExceptWith(set); Assert.AreEqual(0, set.Count);
            }
        }
        [Test]
        public void BulkCapacityFailuresLeaveTheWholeSetUnchanged()
        {
            using (var set = new DeterministicHashSet<int, Int32Policy>(2))
            {
                set.Add(1); var iterator = set.GetEnumerator();
                Assert.AreEqual(DeterministicStatus.CapacityExceeded, set.TryUnionWith(new int[] { 2, 3, 4 }.AsSpan()));
                CollectionAssert.AreEqual(new[] { 1 }, set); Assert.IsTrue(iterator.MoveNext());
                Assert.AreEqual(DeterministicStatus.CapacityExceeded, set.TrySymmetricExceptWith(new int[] { 2, 3, 4 }.AsSpan()));
                CollectionAssert.AreEqual(new[] { 1 }, set);
            }
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void SpanBulkOperationsUseTheReservedGcBuffersWithoutAllocation(SnapshotPolicy snapshots)
        {
            using (var set = new DeterministicHashSet<int, Int32Policy>(new BufferOptions(8, snapshots: snapshots)))
            {
                int[] input = { 1, 2, 2, 3 };
                for (int i = 0; i < 20; i++) { set.TryUnionWith(input.AsSpan()).ThrowIfFailure(); set.TryExceptWith(input.AsSpan()).ThrowIfFailure(); }
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 500; i++) { set.TryUnionWith(input.AsSpan()).ThrowIfFailure(); set.TryExceptWith(input.AsSpan()).ThrowIfFailure(); }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before; Assert.AreEqual(0L, allocated);
            }
        }
    }
}
