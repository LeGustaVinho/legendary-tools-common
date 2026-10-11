using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class SparseSetTests
    {
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void DenseSwapRemovalAndSortedViewsMatchIndependentModelAfterRollback(SnapshotPolicy snapshots)
        {
            using (var set = new DeterministicSparseSet<int>(new BufferOptions(0, 61, AllocationPolicy.Grow, snapshots, 2, 64), new BufferOptions(0, 103, AllocationPolicy.Grow, snapshots, 2, 64)))
            {
                var model = new List<SparseSetEntry<int>>(); uint random = 971; var generations = new uint[103];
                for (int i = 0; i < generations.Length; i++) generations[i] = 1;
                for (int step = 0; step < 3000; step++)
                {
                    random = unchecked(random * 1664525u + 1013904223u); int slot = (int)((random >> 10) % 103);
                    var handle = new StableHandle((uint)slot, generations[slot]); int found = model.FindIndex(x => x.Handle.Index == (uint)slot);
                    if ((random & 3) == 0 && found >= 0)
                    {
                        Assert.IsTrue(set.Remove(handle)); model[found] = model[model.Count - 1]; model.RemoveAt(model.Count - 1); generations[slot]++;
                        Assert.IsFalse(set.ContainsKey(handle));
                    }
                    else if (found >= 0)
                    { set[handle] = step; model[found] = new SparseSetEntry<int>(handle, step); }
                    else if (model.Count < 61)
                    { set.Add(handle, step); model.Add(new SparseSetEntry<int>(handle, step)); }
                    else Assert.AreEqual(DeterministicStatus.CapacityExceeded, set.TryAdd(handle, step));
                    if (step % 43 == 0)
                    {
                        set.TryCapture(out var snapshot).ThrowIfFailure(); set.Clear(); set.TryRestore(snapshot).ThrowIfFailure(); snapshot.Dispose();
                    }
                    Assert.AreEqual(model.Count, set.Count); int denseIndex = 0;
                    foreach (var item in set) { Assert.AreEqual(model[denseIndex].Handle, item.Handle); Assert.AreEqual(model[denseIndex++].Value, item.Value); }
                    uint previous = 0; bool first = true; int sortedCount = 0;
                    foreach (var item in set.OrderedByEntity)
                    { if (!first) Assert.Greater(item.Handle.Index, previous); first = false; previous = item.Handle.Index; Assert.AreEqual(item.Value, set[item.Handle]); sortedCount++; }
                    Assert.AreEqual(model.Count, sortedCount);
                }
            }
        }
        [Test]
        public void StaleGenerationCannotOverwriteLiveComponentAndCapacityFailuresAreAtomic()
        {
            using (var set = new DeterministicSparseSet<int>(1, 5))
            {
                var handle = new StableHandle(4, 7); set.Add(handle, 12); var iterator = set.GetEnumerator();
                Assert.AreEqual(DeterministicStatus.ProtocolConflict, set.TrySet(new StableHandle(4, 8), 99));
                Assert.AreEqual(DeterministicStatus.CapacityExceeded, set.TryAdd(new StableHandle(2, 1), 5));
                Assert.AreEqual(DeterministicStatus.OutOfRange, set.TryAdd(new StableHandle(5, 1), 5));
                Assert.IsTrue(iterator.MoveNext()); Assert.AreEqual(12, iterator.Current.Value);
                Assert.AreEqual(DeterministicStatus.NotFound, set.TryRemove(new StableHandle(4, 8)));
                set.Remove(handle); set.Add(new StableHandle(4, 8), 99); Assert.IsFalse(set.ContainsKey(handle));
            }
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void FixedSparseSetDoesNotAllocateDuringMutationOrSnapshot(SnapshotPolicy snapshots)
        {
            var options = new BufferOptions(32, snapshots: snapshots, pageBytes: 64);
            using (var set = new DeterministicSparseSet<int>(options, options))
            {
                for (uint i = 0; i < 16; i++) set.Add(new StableHandle(i, 1), (int)i);
                for (int i = 0; i < 20; i++) Cycle(set);
                long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 500; i++) Cycle(set);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before; Assert.AreEqual(0L, allocated);
            }
        }
        private static void Cycle(DeterministicSparseSet<int> set)
        {
            set.TryCapture(out var snapshot).ThrowIfFailure(); set.Remove(new StableHandle(3, 1)); set.Add(new StableHandle(20, 1), 100);
            set.TryRestore(snapshot).ThrowIfFailure(); snapshot.Dispose();
        }
    }
}
