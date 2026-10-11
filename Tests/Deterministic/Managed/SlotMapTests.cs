using System;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class SlotMapTests
    {
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void SnapshotPreservesFutureHandleReuseAndInvalidatesStaleHandles(SnapshotPolicy snapshots)
        {
            using (var map = new DeterministicSlotMap<int>(new BufferOptions(0, 5, AllocationPolicy.Grow, snapshots, 2, 64)))
            {
                var a = map.Add(10); var b = map.Add(20); var c = map.Add(30);
                Assert.IsTrue(map.Remove(b)); Assert.IsTrue(map.Remove(a));
                map.TryCapture(out var snapshot).ThrowIfFailure();
                var next = map.Add(40); var after = map.Add(50);
                Assert.AreEqual(new StableHandle(1, 2), next); Assert.AreEqual(new StableHandle(0, 2), after);
                Assert.IsFalse(map.ContainsKey(a)); Assert.IsFalse(map.ContainsKey(b));
                map.Clear(); map.TryRestore(snapshot).ThrowIfFailure();
                Assert.AreEqual(1, map.Count); Assert.AreEqual(30, map[c]);
                Assert.AreEqual(next, map.Add(40)); Assert.AreEqual(after, map.Add(50));
                snapshot.Dispose();
                var iterator = map.GetEnumerator(); iterator.MoveNext(); map.Remove(c);
                Assert.AreEqual(DeterministicStatus.Invalidated, iterator.TryMoveNext(out _));
            }
        }
        [Test]
        public void OverflowDoesNotConsumeHandlesOrInvalidateEnumeration()
        {
            using (var map = new DeterministicSlotMap<int>(1))
            {
                var first = map.Add(1); var iterator = map.GetEnumerator();
                Assert.AreEqual(DeterministicStatus.CapacityExceeded, map.TryAdd(2, out var failed)); Assert.IsFalse(failed.IsValid);
                Assert.IsTrue(iterator.MoveNext()); Assert.AreEqual(first, iterator.Current.Handle);
                map.Remove(first); Assert.AreEqual(new StableHandle(0, 2), map.Add(3));
            }
        }
        private struct TestBuffer : IDeterministicBuffer<SlotMapNode<int>>
        {
            internal SlotMapNode<int>[] Nodes;
            public int Capacity => Nodes.Length;
            public ulong Revision => 0;
            public bool IsCreated => Nodes != null;
            public SlotMapNode<int> Read(int i) => Nodes[i];
            public void Write(int i, in SlotMapNode<int> value) => Nodes[i] = value;
            public void CommitWrite() { }
            public DeterministicStatus CanPrepareWrite(int required, int start, int count) => required <= Capacity ? DeterministicStatus.None : DeterministicStatus.CapacityExceeded;
            public DeterministicStatus PrepareWrite(int required, int start, int count) => CanPrepareWrite(required, start, count);
        }
        [Test]
        public void GenerationExhaustionRetiresSlotInsteadOfRecyclingIdentity()
        {
            var buffer = new TestBuffer { Nodes = new[] { new SlotMapNode<int> { Active = 1, Generation = uint.MaxValue, Value = 7 }, default } };
            var state = new SlotMapState { Count = 1, Used = 1 };
            Assert.AreEqual(DeterministicStatus.None, SlotMapKernel<int, TestBuffer>.TryRemove(ref buffer, ref state, new StableHandle(0, uint.MaxValue)));
            Assert.AreEqual(1, buffer.Nodes[0].Retired); Assert.AreEqual(0, state.FreeHead);
            SlotMapKernel<int, TestBuffer>.TryAdd(ref buffer, ref state, 9, out var next).ThrowIfFailure();
            Assert.AreEqual(new StableHandle(1, 1), next);
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void FixedSlotMapSnapshotsAndMutationsDoNotAllocate(SnapshotPolicy snapshots)
        {
            using (var map = new DeterministicSlotMap<int>(new BufferOptions(16, snapshots: snapshots, pageBytes: 64)))
            {
                var first = map.Add(1); for (int i = 0; i < 20; i++) Cycle(map, first);
                long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 500; i++) Cycle(map, first);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.AreEqual(0L, allocated);
            }
        }
        private static void Cycle(DeterministicSlotMap<int> map, StableHandle first)
        { map.TryCapture(out var snapshot).ThrowIfFailure(); map.Remove(first); map.Add(2); map.TryRestore(snapshot).ThrowIfFailure(); snapshot.Dispose(); }
    }
}
