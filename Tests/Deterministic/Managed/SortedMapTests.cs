using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class SortedMapTests
    {
        [TestCase(false, SnapshotPolicy.FullPerTick)]
        [TestCase(false, SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(false, SnapshotPolicy.CopyOnWrite)]
        [TestCase(true, SnapshotPolicy.FullPerTick)]
        [TestCase(true, SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(true, SnapshotPolicy.CopyOnWrite)]
        public void SortedMapsMatchBclAcrossGrowthUpdatesDeletionAndSnapshots(bool tree, SnapshotPolicy snapshots)
        {
            var options = new BufferOptions(0, 113, AllocationPolicy.Grow, snapshots, 2, 64);
            using (DeterministicSortedMapBase<int, int> map = tree ? (DeterministicSortedMapBase<int, int>)new DeterministicSortedDictionary<int, int, Int32Policy>(options) : new DeterministicSortedList<int, int, Int32Policy>(options))
            {
                var model = new SortedDictionary<int, int>(); uint random = 99;
                for (int step = 0; step < 7000; step++)
                {
                    random = unchecked(random * 1664525u + 1013904223u); int key = (int)((random >> 8) % 113);
                    if ((random & 7) < 3) Assert.AreEqual(model.Remove(key), map.Remove(key));
                    else { map[key] = step; model[key] = step; }
                    if (step % 59 == 0)
                    {
                        if (tree)
                        {
                            var owner = (DeterministicSortedDictionary<int, int, Int32Policy>)map;
                            owner.TryCapture(out var snapshot).ThrowIfFailure(); owner.Clear(); owner.Add(-1, 99); owner.TryRestore(snapshot).ThrowIfFailure(); snapshot.Dispose();
                        }
                        else
                        {
                            var owner = (DeterministicSortedList<int, int, Int32Policy>)map;
                            owner.TryCapture(out var snapshot).ThrowIfFailure(); owner.Clear(); owner.Add(-1, 99); owner.TryRestore(snapshot).ThrowIfFailure(); snapshot.Dispose();
                        }
                    }
                    Assert.AreEqual(model.Count, map.Count); var iterator = map.GetEnumerator(); int rank = 0;
                    foreach (var item in model)
                    {
                        Assert.IsTrue(iterator.MoveNext()); Assert.AreEqual(item, iterator.Current);
                        map.TryGetAt(rank++, out var ranked).ThrowIfFailure(); Assert.AreEqual(item, ranked); Assert.AreEqual(item.Value, map[item.Key]);
                    }
                    Assert.IsFalse(iterator.MoveNext());
                }
            }
        }
        [Test]
        public void AvlHeightRemainsLogarithmicForMonotonicInsertAndRepeatedRootDeletion()
        {
            using (var map = new DeterministicSortedDictionary<int, int, Int32Policy>(4096))
            {
                for (int i = 0; i < 4096; i++) map.Add(i, i);
                Assert.LessOrEqual(map.Height, 18);
                for (int i = 0; i < 4096; i++) { Assert.AreEqual(i, map[i]); Assert.IsTrue(map.Remove(i)); }
                Assert.AreEqual(0, map.Height); Assert.AreEqual(0, map.Count);
            }
        }
        [Test]
        public void FailuresPreserveTreeAndEnumerator()
        {
            using (var map = new DeterministicSortedDictionary<int, int, Int32Policy>(2))
            {
                map.Add(2, 20); map.Add(1, 10); var iterator = map.GetEnumerator();
                Assert.AreEqual(DeterministicStatus.CapacityExceeded, map.TryAdd(3, 30));
                Assert.AreEqual(DeterministicStatus.DuplicateKey, map.TryAdd(2, 99));
                Assert.AreEqual(DeterministicStatus.NotFound, map.TryRemove(0));
                Assert.IsTrue(iterator.MoveNext()); Assert.AreEqual(1, iterator.Current.Key);
                map[2] = 21; Assert.AreEqual(DeterministicStatus.Invalidated, iterator.TryMoveNext(out _));
            }
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void AvlMutationSnapshotsAndConcreteViewsAllocateNoGarbage(SnapshotPolicy snapshots)
        {
            using (var map = new DeterministicSortedDictionary<int, int, Int32Policy>(new BufferOptions(64, snapshots: snapshots, pageBytes: 64)))
            {
                for (int i = 0; i < 32; i++) map.Add(i, i);
                for (int i = 0; i < 20; i++) Cycle(map);
                long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 500; i++) Cycle(map);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before; Assert.AreEqual(0L, allocated);
            }
        }
        private static void Cycle(DeterministicSortedDictionary<int, int, Int32Policy> map)
        {
            map.TryCapture(out var snapshot).ThrowIfFailure(); map.Remove(17); map.Add(50, 100); map.TryRestore(snapshot).ThrowIfFailure(); snapshot.Dispose();
            int sum = 0; foreach (var key in map.Keys) sum += key; foreach (var value in map.Values) sum += value;
            if (sum != 992) throw new InvalidOperationException();
        }
        [Test]
        public void SortedSetUsesComparerOrderAndDuplicateAwareBulkOperations()
        {
            using (var set = new DeterministicSortedSet<int, Int32Policy>(6))
            {
                set.Add(7); set.Add(2); set.Add(5); set.SymmetricExceptWith(new[] { 7, 7, 1, 1, 3 });
                CollectionAssert.AreEqual(new[] { 1, 2, 3, 5 }, set); Assert.AreEqual(1, set.Min); Assert.AreEqual(5, set.Max);
                Assert.IsTrue(set.SetEquals(new[] { 5, 3, 1, 2, 2 }));
                set.IntersectWith(new[] { 5, 5, 1 }); CollectionAssert.AreEqual(new[] { 1, 5 }, set);
            }
        }
    }
}
