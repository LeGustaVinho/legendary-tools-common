using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class OrderedDictionaryTests
    {
        private struct CollisionPolicy : IDeterministicEquality<int>
        {
            public bool IsValid => true;
            public bool Equals(in int a, in int b) => a == b;
            public ulong Hash(in int value) => 0;
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void CollisionsGrowthRemovalAndSnapshotsPreserveInsertionOrder(SnapshotPolicy snapshots)
        {
            using (var map = new DeterministicDictionary<int, int, CollisionPolicy>(new BufferOptions(1, 37, AllocationPolicy.Grow, snapshots, 2, 64)))
            {
                var model = new Dictionary<int, int>(); var order = new List<int>(); uint random = 7331;
                for (int step = 0; step < 3000; step++)
                {
                    random = unchecked(random * 1664525u + 1013904223u);
                    int key = (int)((random >> 7) % 64), value = (int)(random >> 3), operation = (int)((random >> 27) % 6);
                    if (operation <= 1)
                    {
                        bool found = model.ContainsKey(key); var expected = found && operation == 0 ? DeterministicStatus.DuplicateKey :
                            !found && model.Count == 37 ? DeterministicStatus.CapacityExceeded : DeterministicStatus.None;
                        var status = operation == 0 ? map.TryAdd(key, value) : map.TrySet(key, value); Assert.AreEqual(expected, status);
                        if (status.IsSuccess()) { if (!found) order.Add(key); model[key] = value; }
                    }
                    else if (operation == 2)
                    {
                        bool found = model.Remove(key); if (found) order.Remove(key);
                        Assert.AreEqual(found ? DeterministicStatus.None : DeterministicStatus.NotFound, map.TryRemove(key));
                    }
                    else if (operation == 3 && step % 17 == 0)
                    {
                        map.TryCapture(out var snapshot).ThrowIfFailure(); int capacity = map.Capacity;
                        map.Clear(); for (int i = 0; i < 10; i++) map.Add(i, -i);
                        map.TryRestore(snapshot).ThrowIfFailure(); Assert.AreEqual(capacity, map.Capacity);
                        for (int i = 0; i < order.Count; i++)
                        { snapshot.TryGetAt(i, out var pair).ThrowIfFailure(); Assert.AreEqual(order[i], pair.Key); Assert.AreEqual(model[pair.Key], pair.Value); }
                        snapshot.Dispose();
                    }
                    else if (operation == 4)
                    {
                        bool found = model.TryGetValue(key, out int wanted);
                        Assert.AreEqual(found ? DeterministicStatus.None : DeterministicStatus.NotFound, map.TryGet(key, out int actual));
                        if (found) Assert.AreEqual(wanted, actual);
                    }
                    else if (operation == 5 && step % 53 == 0) { map.Clear(); model.Clear(); order.Clear(); }
                    Assert.AreEqual(model.Count, map.Count); int position = 0;
                    foreach (var pair in map) { Assert.AreEqual(order[position++], pair.Key); Assert.AreEqual(model[pair.Key], pair.Value); }
                    Assert.AreEqual(order.Count, position); CollectionAssert.AreEqual(order, map.Keys);
                }
            }
        }

        [Test]
        public void UpdatingPreservesPositionAndReinsertionAppendsAtTheEnd()
        {
            using (var map = new DeterministicDictionary<int, int, Int32Policy>(3))
            {
                map.Add(1, 10); map.Add(2, 20); map.Add(3, 30); map[2] = 99;
                CollectionAssert.AreEqual(new[] { 1, 2, 3 }, map.Keys);
                map.Remove(2); map.Add(2, 100); CollectionAssert.AreEqual(new[] { 1, 3, 2 }, map.Keys);
                var iterator = map.GetEnumerator(); Assert.AreEqual(DeterministicStatus.DuplicateKey, map.TryAdd(1, 12)); Assert.IsTrue(iterator.MoveNext());
                Assert.AreEqual(DeterministicStatus.CapacityExceeded, map.TryAdd(4, 40)); Assert.IsTrue(iterator.MoveNext());
                Assert.IsFalse(map.Remove(new KeyValuePair<int, int>(1, 999))); Assert.AreEqual(10, map[1]);
                Assert.IsTrue(map.Remove(new KeyValuePair<int, int>(1, 10))); Assert.IsFalse(map.ContainsKey(1));
            }
        }

        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void DictionaryFixedHotPathAndConcreteKeyViewsDoNotAllocate(SnapshotPolicy snapshots)
        {
            using (var map = new DeterministicDictionary<int, int, Int32Policy>(new BufferOptions(32, snapshots: snapshots, snapshotSlots: 2, pageBytes: 64)))
            {
                for (int i = 0; i < 16; i++) map.Add(i, i * 2);
                for (int i = 0; i < 20; i++) Cycle(map);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 500; i++) Cycle(map);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before; Assert.AreEqual(0L, allocated);
            }
        }
        private static void Cycle(DeterministicDictionary<int, int, Int32Policy> map)
        {
            map.TryCapture(out var snapshot).ThrowIfFailure(); map[2] = 999; map.Remove(1); map.Add(1, 88);
            map.TryRestore(snapshot).ThrowIfFailure(); snapshot.Dispose();
            int sum = 0; foreach (var key in map.Keys) sum += key;
            if (sum != 120 || map[2] != 4) throw new InvalidOperationException();
        }

        [Test]
        public void UnsupportedPrimitivePoliciesAreRejectedBeforeStorageCreation()
        {
            Assert.Throws<ArgumentException>(() => new DeterministicDictionary<StableHandle, int, PrimitiveEquality<StableHandle>>(4));
        }
    }
}
