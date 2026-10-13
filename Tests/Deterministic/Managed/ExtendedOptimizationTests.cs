using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    [Category("DeterministicExtendedOptimization")]
    public sealed unsafe class ExtendedOptimizationTests
    {
        [TestCase(63)]
        [TestCase(256)]
        [TestCase(257)]
        public void SparseClearPreservesFifoGenerationsRetirementAndRetainedRoot(int active)
        {
            const int used = 8192;
            using var map = new DeterministicSlotMap<int>(new BufferOptions(used, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 1, pageBytes: 256));
            var handles = new StableHandle[used]; var keep = new bool[used]; var free = new List<int>();
            for (int i = 0; i < used; i++) handles[i] = map.Add(i + 7);
            for (int i = 0; i < active; i++) keep[i * 31] = true;
            for (int i = 0; i < used; i++) if (!keep[i]) { map.Remove(handles[i]); free.Add(i); }
            // Exhausted generations retire instead of re-entering the free list.
            map.Storage.PrepareWrite(used, 0, 1).ThrowIfFailure(); var node = map.Storage.Read(0); node.Generation = uint.MaxValue;
            map.Storage.Write(0, node); map.Storage.CommitWrite(); handles[0] = new StableHandle(0, uint.MaxValue);
            for (int i = 1; i < used; i++) if (keep[i]) free.Add(i);
            map.TryCapture(out var root).ThrowIfFailure();
            try
            {
                map.TryClear().ThrowIfFailure(); Assert.AreEqual(0, map.Count); Assert.AreEqual(used, map.State.Used);
                Assert.AreEqual(1, map.Storage.Read(0).Retired); Assert.AreEqual(uint.MaxValue, map.Storage.Read(0).Generation);
                for (int i = 0; i < used; i++)
                {
                    Assert.AreEqual(DeterministicStatus.NotFound, map.TryGet(handles[i], out _));
                    var s = root.TryGet(handles[i], out int value);
                    if (keep[i]) { s.ThrowIfFailure(); Assert.AreEqual(i + 7, value); } else Assert.AreEqual(DeterministicStatus.NotFound, s);
                }
                for (int i = 0; i < free.Count; i++)
                { var h = map.Add(i); Assert.AreEqual((uint)free[i], h.Index); Assert.AreEqual(2u, h.Generation); }
                Assert.AreEqual(DeterministicStatus.CapacityExceeded, map.TryAdd(1, out _));
                map.TryRestore(root).ThrowIfFailure(); Assert.AreEqual(active, map.Count);
                for (int i = 0; i < used; i++) if (keep[i]) { map.TryGet(handles[i], out int value).ThrowIfFailure(); Assert.AreEqual(i + 7, value); }
            }
            finally { root.Dispose(); }
        }
        private sealed class BufferOwner
        {
            internal readonly SlotMapNode<int>[] Data = new SlotMapNode<int>[8192];
            internal readonly bool[] Prepared = new bool[8192];
            internal int Failure = -1, Writes, Commits;
        }
        private struct CheckedBuffer : IDeterministicBuffer<SlotMapNode<int>>
        {
            internal BufferOwner Owner;
            public int Capacity => Owner.Data.Length;
            public ulong Revision => (ulong)Owner.Commits;
            public bool IsCreated => Owner != null;
            public SlotMapNode<int> Read(int index) => Owner.Data[index];
            public void Write(int index, in SlotMapNode<int> value)
            { if (!Owner.Prepared[index]) throw new InvalidOperationException("Write without reservation: " + index); Owner.Data[index] = value; Owner.Writes++; }
            public DeterministicStatus CanPrepareWrite(int requiredCapacity, int start, int count)
                => Owner.Failure >= start && Owner.Failure < (long)start + count ? DeterministicStatus.SnapshotCapacityExceeded : DeterministicStatus.None;
            public DeterministicStatus PrepareWrite(int requiredCapacity, int start, int count)
            { var s = CanPrepareWrite(requiredCapacity, start, count); if (!s.IsSuccess()) return s; for (int i = start; i < start + count; i++) Owner.Prepared[i] = true; return s; }
            public void CommitWrite() => Owner.Commits++;
        }
        [Test]
        public void SparseClearPreflightsEveryActiveSlotAndFreeTailBeforeAnyWrite()
        {
            var owner = new BufferOwner(); var buffer = new CheckedBuffer { Owner = owner }; var state = default(SlotMapState);
            var handles = new StableHandle[8192];
            for (int i = 0; i < handles.Length; i++) SlotMapKernel<int, CheckedBuffer>.TryAdd(ref buffer, ref state, i, out handles[i]).ThrowIfFailure();
            for (int i = 0; i < handles.Length; i++) if (i % 128 != 0) SlotMapKernel<int, CheckedBuffer>.TryRemove(ref buffer, ref state, handles[i]).ThrowIfFailure();
            int writes = owner.Writes, commits = owner.Commits; var before = state; var bytes = (SlotMapNode<int>[])owner.Data.Clone();
            foreach (int failure in new[] { 0, 128 * 31, state.FreeTail - 1 })
            {
                Array.Clear(owner.Prepared, 0, owner.Prepared.Length); owner.Failure = failure;
                Assert.AreEqual(DeterministicStatus.SnapshotCapacityExceeded, SlotMapKernel<int, CheckedBuffer>.TryClear(ref buffer, ref state));
                Assert.AreEqual(writes, owner.Writes); Assert.AreEqual(commits, owner.Commits); Assert.AreEqual(before, state); CollectionAssert.AreEqual(bytes, owner.Data);
            }
            owner.Failure = -1; SlotMapKernel<int, CheckedBuffer>.TryClear(ref buffer, ref state).ThrowIfFailure(); Assert.AreEqual(0, state.Count);
        }
        [Test]
        public void MultiMapBatchRemovalPreservesFifoAndInsertionOrder()
        {
            var options = new BufferOptions(64, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 1, pageBytes: 64);
            using var map = new DeterministicMultiMap<int, int, Int32Policy>(options, options);
            var handles = new StableHandle[48];
            for (int i = 0; i < handles.Length; i++) handles[i] = map.Add(i % 3, i / 3);
            map.TryCapture(out var root).ThrowIfFailure();
            try
            {
                var view = map.GetValues(0).GetEnumerator(); Assert.IsTrue(view.MoveNext());
                map.TryRemoveAll(0).ThrowIfFailure(); Assert.AreEqual(DeterministicStatus.Invalidated, view.TryMoveNext(out _));
                Assert.AreEqual(32, map.Count); Assert.AreEqual(2, map.KeyCount);
                for (int i = 0; i < 16; i++) { var h = map.Add(0, 100 + i); Assert.AreEqual((uint)(i * 3), h.Index); Assert.AreEqual(2u, h.Generation); }
                var expected = new[] { 1, 2, 0 }; int keyOrdinal = 0; foreach (int key in map.Keys) Assert.AreEqual(expected[keyOrdinal++], key);
                Assert.AreEqual(3, keyOrdinal);
                for (int i = 0; i < handles.Length; i++) { root.TryGetPair(handles[i], out var pair).ThrowIfFailure(); Assert.AreEqual(i % 3, pair.Key); Assert.AreEqual(i / 3, pair.Value); }
                map.TryRestore(root).ThrowIfFailure(); Assert.AreEqual(48, map.Count);
            }
            finally { root.Dispose(); }
        }
        private struct CountingEquality : IDeterministicEquality<int>
        {
            internal static int Calls;
            internal bool Collision;
            public bool IsValid => true;
            public bool Equals(in int a, in int b) => a / 2 == b / 2;
            public ulong Hash(in int value) { Calls++; int key = value / 2; return Collision ? 1UL : default(Int32Policy).Hash(key); }
        }
        [TestCase(false)]
        [TestCase(true)]
        public void CanonicalImportReusesValidatedHashesAndHonorsCustomEqualityWithExcessCapacity(bool collisions)
        {
            const int count = 71, capacity = 257; var policy = new CountingEquality { Collision = collisions };
            using var source = new DeterministicDictionary<int, int, CountingEquality>(capacity, policy);
            using var target = new DeterministicDictionary<int, int, CountingEquality>(new BufferOptions(capacity, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 1, pageBytes: 256), policy);
            for (int i = 0; i < count; i++) { source.Add(i * 2, i + 3); target.Add(i * 2, -i - 1); }
            byte* packet = stackalloc byte[16384]; byte* workspace = stackalloc byte[32768];
            var writer = new DeterministicWriter(packet, 16384); source.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
            target.TryCapture(out var root).ThrowIfFailure();
            try
            {
                CountingEquality.Calls = 0; var reader = new DeterministicReader(packet, writer.Position); var scratch = new CanonicalScratch(workspace, 32768);
                target.TryReadCanonical(ref reader, ref scratch, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
                Assert.AreEqual(count, CountingEquality.Calls); Assert.AreEqual(writer.Position, reader.Position);
                for (int i = 0; i < count; i++)
                {
                    target.TryGet(i * 2 + 1, out int value).ThrowIfFailure(); Assert.AreEqual(i + 3, value);
                    root.Entries.TryGet(i, out var old).ThrowIfFailure(); Assert.AreEqual(-i - 1, old.Value);
                }
                var second = new DeterministicWriter(workspace, 32768); target.TryWriteCanonical(ref second, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
                Assert.AreEqual(writer.Position, second.Position); for (int i = 0; i < writer.Position; i++) Assert.AreEqual(packet[i], workspace[i]);
            }
            finally { root.Dispose(); }
        }
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void HubRemovalCoversDenseReservationAndSparseFallbacks(bool isolatedGap, bool edgeGap)
        {
            var options = new GraphOptions(new BufferOptions(130, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 1, pageBytes: 256),
                new BufferOptions(130, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 1, pageBytes: 256));
            using var graph = new DeterministicGraph<int, int>(options); var nodes = new GraphNodeHandle[66]; var edges = new GraphEdgeHandle[65];
            for (int i = 0; i < nodes.Length; i++) { if (isolatedGap && i == 32) graph.AddNode(-1); nodes[i] = graph.AddNode(i); }
            for (int i = 0; i < edges.Length; i++) edges[i] = graph.AddEdge(nodes[0], nodes[i + 1], i);
            if (edgeGap) graph.TryRemoveEdge(edges[17]).ThrowIfFailure();
            graph.TryCapture(out var root).ThrowIfFailure();
            try
            {
                graph.TryRemoveNode(nodes[0]).ThrowIfFailure(); Assert.AreEqual(0, graph.EdgeCount); Assert.AreEqual(isolatedGap ? 66 : 65, graph.NodeCount);
                for (int i = 0; i < edges.Length; i++)
                { Assert.AreEqual(DeterministicStatus.NotFound, graph.TryGetEdge(edges[i], out _)); if (!(edgeGap && i == 17)) root.TryGetEdge(edges[i], out _).ThrowIfFailure(); }
                var reused = graph.AddEdge(nodes[1], nodes[2], 99); Assert.AreEqual(edgeGap ? 17u : 0u, reused.Index); Assert.AreEqual(2u, reused.Generation);
                graph.TryRestore(root).ThrowIfFailure(); Assert.AreEqual(edgeGap ? 64 : 65, graph.EdgeCount);
            }
            finally { root.Dispose(); }
        }
    }
}
