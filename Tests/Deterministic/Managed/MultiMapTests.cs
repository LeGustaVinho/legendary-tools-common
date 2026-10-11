using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class MultiMapTests
    {
        private struct CollisionPolicy : IDeterministicEquality<int>
        {
            public bool IsValid => true;
            public bool Equals(in int a, in int b) => a == b;
            public ulong Hash(in int value) => 0;
        }
        private struct ModelPair { internal StableHandle Handle; internal int Value; }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void CollidingKeysAndPerKeyValuesMatchModelThroughRemovalAndRollback(SnapshotPolicy snapshots)
        {
            using (var map = new DeterministicMultiMap<int, int, CollisionPolicy>(new BufferOptions(0, 5, AllocationPolicy.Grow, snapshots, 2, 64), new BufferOptions(0, 31, AllocationPolicy.Grow, snapshots, 2, 64)))
            {
                var order = new List<int>(); var model = new Dictionary<int, List<ModelPair>>(); uint random = 111; int count = 0;
                for (int step = 0; step < 3000; step++)
                {
                    random = unchecked(random * 1664525u + 1013904223u); int key = (int)((random >> 9) % 7), value = (int)((random >> 16) % 8);
                    model.TryGetValue(key, out var bucket);
                    if ((random & 7) < 4)
                    {
                        var status = map.TryAdd(key, value, out var handle);
                        if (count == 31 || bucket == null && model.Count == 5) Assert.AreEqual(DeterministicStatus.CapacityExceeded, status);
                        else
                        {
                            status.ThrowIfFailure(); if (bucket == null) { bucket = new List<ModelPair>(); model.Add(key, bucket); order.Add(key); }
                            bucket.Add(new ModelPair { Handle = handle, Value = value }); count++;
                        }
                    }
                    else if ((random & 7) == 4)
                    {
                        Assert.AreEqual(bucket != null, map.RemoveAll(key)); if (bucket != null) { count -= bucket.Count; model.Remove(key); order.Remove(key); }
                    }
                    else if (bucket != null)
                    {
                        int index = (int)((random >> 20) % (uint)bucket.Count); var pair = bucket[index];
                        if ((random & 7) == 5) { map.TrySetValue(pair.Handle, value).ThrowIfFailure(); pair.Value = value; bucket[index] = pair; }
                        else { Assert.IsTrue(map.Remove(pair.Handle)); Assert.IsFalse(map.Remove(pair.Handle)); bucket.RemoveAt(index); count--; if (bucket.Count == 0) { model.Remove(key); order.Remove(key); } }
                    }
                    if (step % 37 == 0)
                    { map.TryCapture(out var snapshot).ThrowIfFailure(); map.Clear(); map.Add(-1, -1); map.TryRestore(snapshot).ThrowIfFailure(); snapshot.Dispose(); }
                    Assert.AreEqual(count, map.Count); Assert.AreEqual(order.Count, map.KeyCount); CollectionAssert.AreEqual(order, map.Keys);
                    var iterator = map.GetEnumerator();
                    foreach (int orderedKey in order)
                    {
                        int i = 0; foreach (int actualValue in map[orderedKey]) Assert.AreEqual(model[orderedKey][i++].Value, actualValue);
                        Assert.AreEqual(model[orderedKey].Count, map[orderedKey].Count);
                        foreach (var pair in model[orderedKey]) { Assert.IsTrue(iterator.MoveNext()); Assert.AreEqual(new KeyValuePair<int, int>(orderedKey, pair.Value), iterator.Current); }
                    }
                    Assert.IsFalse(iterator.MoveNext());
                }
            }
        }
        [Test]
        public void CapacityFailuresNeverPublishEmptyKeysOrConsumeValueHandles()
        {
            using (var map = new DeterministicMultiMap<int, int, Int32Policy>(2, 1))
            {
                var first = map.Add(1, 10); var iterator = map.GetEnumerator();
                Assert.AreEqual(DeterministicStatus.CapacityExceeded, map.TryAdd(2, 20, out _)); Assert.AreEqual(1, map.KeyCount); Assert.IsFalse(map.ContainsKey(2));
                Assert.IsTrue(iterator.MoveNext()); map.Remove(first); Assert.AreEqual(new StableHandle(first.Index, first.Generation + 1), map.Add(2, 20));
            }
            using (var map = new DeterministicMultiMap<int, int, Int32Policy>(1, 3))
            {
                var first = map.Add(1, 10); Assert.AreEqual(DeterministicStatus.CapacityExceeded, map.TryAdd(2, 20, out _));
                var second = map.Add(1, 11); Assert.AreEqual(new StableHandle(1, 1), second); Assert.AreEqual(2, map.Count);
            }
        }
        [Test]
        public void ValueViewsInvalidateAndSnapshotRetainsPriorPairValue()
        {
            using (var map = new DeterministicMultiMap<int, int, Int32Policy>(3, 6))
            {
                var first = map.Add(1, 10); map.Add(1, 10); map.Add(1, 11); var view = map[1];
                map.TryCapture(out var snapshot).ThrowIfFailure(); map.TrySetValue(first, 99).ThrowIfFailure();
                Assert.AreEqual(DeterministicStatus.Invalidated, view.GetEnumerator().TryMoveNext(out _));
                snapshot.TryGetPair(first, out var old).ThrowIfFailure(); Assert.AreEqual(10, old.Value);
                map.TryRestore(snapshot).ThrowIfFailure(); snapshot.Dispose(); Assert.IsTrue(map.Remove(1, 10));
                CollectionAssert.AreEqual(new[] { 10, 11 }, map[1]); Assert.IsFalse(map.Remove(first));
            }
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void FixedMultiMapDoesNotAllocateDuringLinkedValueUpdatesOrSnapshots(SnapshotPolicy snapshots)
        {
            using (var map = new DeterministicMultiMap<int, int, Int32Policy>(new BufferOptions(8, snapshots: snapshots, pageBytes: 64), new BufferOptions(32, snapshots: snapshots, pageBytes: 64)))
            {
                for (int i = 0; i < 16; i++) map.Add(i % 4, i); for (int i = 0; i < 20; i++) Cycle(map);
                long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 500; i++) Cycle(map);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before; Assert.AreEqual(0L, allocated);
            }
        }
        private static void Cycle(DeterministicMultiMap<int, int, Int32Policy> map)
        { map.TryCapture(out var snapshot).ThrowIfFailure(); map.RemoveAll(1); map.Add(7, 77); map.TryRestore(snapshot).ThrowIfFailure(); snapshot.Dispose(); }
    }
}
