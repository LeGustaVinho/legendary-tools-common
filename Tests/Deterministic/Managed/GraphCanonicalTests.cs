using System;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class GraphCanonicalTests
    {
        private static byte[] Encode(DeterministicGraph<int, int> graph)
        {
            graph.TryMeasureCanonical(default(Int32Policy), default(Int32Policy), out int size).ThrowIfFailure(); var bytes = new byte[size];
            fixed (byte* p = bytes) { var writer = new DeterministicWriter(p, size); graph.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)).ThrowIfFailure(); }
            return bytes;
        }
        [TestCase(GraphDirection.Directed, SnapshotPolicy.FullPerTick)]
        [TestCase(GraphDirection.Undirected, SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(GraphDirection.Directed, SnapshotPolicy.CopyOnWrite)]
        [TestCase(GraphDirection.Undirected, SnapshotPolicy.CopyOnWrite)]
        public void CheckpointPreservesAllocatorHistoryAndRebuildsAdjacency(GraphDirection direction, SnapshotPolicy policy)
        {
            var options = new GraphOptions(new BufferOptions(0, 8, AllocationPolicy.Grow, policy, 3, 64), new BufferOptions(0, 8, AllocationPolicy.Grow, policy, 3, 64), direction, allowParallelEdges: true, allowSelfEdges: true);
            using var source = new DeterministicGraph<int, int>(options); using var target = new DeterministicGraph<int, int>(options);
            var a = source.AddNode(10); var b = source.AddNode(20); var c = source.AddNode(30);
            var first = source.AddEdge(a, b, 1); var removed = source.AddEdge(a, a, 2); source.AddEdge(c, a, 3);
            source.TryRemoveEdge(removed).ThrowIfFailure(); source.TryRemoveNode(b).ThrowIfFailure();
            var bytes = Encode(source); GraphWire<int, int, Int32Policy, Int32Policy>.TryMeasureScratch(options, out int length).ThrowIfFailure(); var scratch = new byte[length];
            target.AddNode(99);
            fixed (byte* p = bytes) fixed (byte* w = scratch)
            { var reader = new DeterministicReader(p, bytes.Length); var work = new CanonicalScratch(w, length); target.TryReadCanonical(ref reader, ref work, default(Int32Policy), default(Int32Policy)).ThrowIfFailure(); Assert.AreEqual(bytes.Length, reader.Position); }
            CollectionAssert.AreEqual(bytes, Encode(target));
            var newSource = source.AddNode(40); var newTarget = target.AddNode(40); Assert.AreEqual(newSource, newTarget);
            Assert.AreEqual(source.AddEdge(a, newSource, 5), target.AddEdge(a, newTarget, 5)); CollectionAssert.AreEqual(Encode(source), Encode(target));
            int outgoing = 0; foreach (var arc in target.Outgoing(a)) { target.TryGetEdge(arc.Edge, out _).ThrowIfFailure(); outgoing++; }
            Assert.AreEqual(direction == GraphDirection.Directed ? 1 : 2, outgoing);
        }
        [Test]
        public void InvalidReadDoesNotChangeStateCursorOrBorrowedViews()
        {
            var options = new GraphOptions(3, 4); using var source = new DeterministicGraph<int, int>(options); using var target = new DeterministicGraph<int, int>(options);
            var a = source.AddNode(1); var b = source.AddNode(2); source.AddEdge(a, b, 3); target.AddNode(88);
            var bytes = Encode(source); var before = Encode(target); var view = target.AsReadOnly(); var scratch = new byte[8192];
            fixed (byte* p = bytes) fixed (byte* w = scratch)
            {
                for (int length = 0; length < bytes.Length; length++)
                { var reader = new DeterministicReader(p, length); var work = new CanonicalScratch(w, scratch.Length); Assert.AreNotEqual(DeterministicStatus.None, target.TryReadCanonical(ref reader, ref work, default(Int32Policy), default(Int32Policy))); Assert.AreEqual(0, reader.Position); Assert.AreEqual(0, work.Position); }
                bytes[15] ^= 1; var invalid = new DeterministicReader(p, bytes.Length); var staging = new CanonicalScratch(w, scratch.Length);
                Assert.AreEqual(DeterministicStatus.ProfileMismatch, target.TryReadCanonical(ref invalid, ref staging, default(Int32Policy), default(Int32Policy))); bytes[15] ^= 1;
                invalid = new DeterministicReader(p, bytes.Length); staging = new CanonicalScratch(w, 1);
                Assert.AreEqual(DeterministicStatus.BufferTooSmall, target.TryReadCanonical(ref invalid, ref staging, default(Int32Policy), default(Int32Policy)));
            }
            Assert.AreEqual(DeterministicStatus.None, view.Check()); CollectionAssert.AreEqual(before, Encode(target));
        }
        [Test]
        public void StoragePolicyAndSnapshotLeasesDoNotEnterCanonicalBytes()
        {
            using var a = new DeterministicGraph<int, int>(new GraphOptions(new BufferOptions(3, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 4, pageBytes: 64), new BufferOptions(4, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 4, pageBytes: 64)));
            using var b = new DeterministicGraph<int, int>(new GraphOptions(new BufferOptions(3, snapshotSlots: 0), new BufferOptions(4, snapshotSlots: 0)));
            var n = a.AddNode(10); var m = a.AddNode(20); a.AddEdge(n, m, 30); b.AddEdge(b.AddNode(10), b.AddNode(20), 30);
            a.TryCapture(out var root).ThrowIfFailure(); CollectionAssert.AreEqual(Encode(a), Encode(b));
            var snapshotBytes = new byte[1024]; fixed (byte* p = snapshotBytes)
            { var writer = new DeterministicWriter(p, snapshotBytes.Length); root.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)).ThrowIfFailure(); CollectionAssert.AreEqual(Encode(b), new ArraySegment<byte>(snapshotBytes, 0, writer.Position)); }
            root.Dispose();
        }
        [Test]
        public void ExhaustedGenerationIsRetiredAfterImport()
        {
            var options = new GraphOptions(2, 2); var n = new SlotMapNode<GraphNodeRecord<int>>[2]; var e = new SlotMapNode<GraphEdgeRecord<int>>[2];
            n[0] = new SlotMapNode<GraphNodeRecord<int>> { Active = 1, Generation = uint.MaxValue, Value = new GraphNodeRecord<int> { Data = 5 } };
            var bytes = new byte[1024]; var scratch = new byte[8192]; using var graph = new DeterministicGraph<int, int>(options);
            fixed (SlotMapNode<GraphNodeRecord<int>>* np = n) fixed (SlotMapNode<GraphEdgeRecord<int>>* ep = e) fixed (byte* p = bytes) fixed (byte* w = scratch)
            {
                var nb = new GraphWorkBuffer<SlotMapNode<GraphNodeRecord<int>>>(np, n.Length); var eb = new GraphWorkBuffer<SlotMapNode<GraphEdgeRecord<int>>>(ep, e.Length);
                var writer = new DeterministicWriter(p, bytes.Length); var state = new SlotMapState { Count = 1, Used = 1 };
                GraphWire<int, int, Int32Policy, Int32Policy>.TryWrite(ref writer, ref nb, ref eb, state, default, options, default, default).ThrowIfFailure();
                var reader = new DeterministicReader(p, writer.Position); var work = new CanonicalScratch(w, scratch.Length);
                graph.TryReadCanonical(ref reader, ref work, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
            }
            graph.TryRemoveNode(new GraphNodeHandle(new StableHandle(0, uint.MaxValue))).ThrowIfFailure();
            Assert.AreEqual(new StableHandle(1, 1), graph.AddNode(7).Value);
        }
    }
}
