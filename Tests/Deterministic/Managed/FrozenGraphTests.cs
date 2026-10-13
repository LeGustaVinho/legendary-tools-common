using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class FrozenGraphTests
    {
        [TestCase(GraphDirection.Directed)]
        [TestCase(GraphDirection.Undirected)]
        public void FrozenCsrPreservesIdentitiesAndOutlivesSource(GraphDirection direction)
        {
            var options = new GraphOptions(4, 8, direction, allowParallelEdges: true, allowSelfEdges: true);
            using var frozen = new FrozenDeterministicGraph<int, int>(options);
            var g = new DeterministicGraph<int, int>(options); var a = g.AddNode(1); var b = g.AddNode(2); var c = g.AddNode(3);
            g.TryRemoveNode(b).ThrowIfFailure(); var next = g.AddNode(4); var edge = g.AddEdge(a, next, 10); g.AddEdge(next, next, 20); g.AddEdge(c, a, 30);
            Assert.AreEqual(DeterministicStatus.Pending, frozen.AsReadOnly().Check()); g.TryFreeze(frozen).ThrowIfFailure();
            Assert.IsTrue(frozen.IsBuilt); var reader = frozen.AsReadOnly(); g.Dispose();
            reader.TryGetNode(next, out int payload).ThrowIfFailure(); Assert.AreEqual(4, payload);
            reader.TryGetEdge(edge, out var stored).ThrowIfFailure(); Assert.AreEqual(10, stored.Data);
            Assert.AreEqual(DeterministicStatus.NotFound, reader.TryGetNode(b, out _));
            int degree = 0; foreach (var arc in reader.Outgoing(next)) degree++; Assert.AreEqual(direction == GraphDirection.Directed ? 1 : 3, degree);
            Assert.AreEqual(DeterministicStatus.InvalidConfiguration, frozen.TryBuild(reader));
            var scratchBytes = new byte[4096]; var results = new GraphNodeHandle[4];
            fixed (byte* p = scratchBytes) fixed (GraphNodeHandle* r = results)
            { var scratch = new CanonicalScratch(p, scratchBytes.Length); var output = new GraphWorkBuffer<GraphNodeHandle>(r, results.Length); GraphAlgorithms<int, int>.TryBreadthFirst(reader, a, ref scratch, ref output, out int count).ThrowIfFailure(); Assert.AreEqual(direction == GraphDirection.Directed ? 2 : 3, count); }
        }
        [TestCase(GraphDirection.Directed)]
        [TestCase(GraphDirection.Undirected)]
        public void CanonicalFrozenRoundtripBuildsCsrAndRejectsTruncation(GraphDirection direction)
        {
            var options = new GraphOptions(3, 4, direction); using var g = new DeterministicGraph<int, int>(options);
            var a = g.AddNode(1); var b = g.AddNode(2); g.AddEdge(a, b, 3);
            using var frozen = new FrozenDeterministicGraph<int, int>(options); using var imported = new FrozenDeterministicGraph<int, int>(options);
            g.TryFreeze(frozen).ThrowIfFailure(); var bytes = new byte[4096]; var copy = new byte[4096]; var scratchBytes = new byte[8192];
            fixed (byte* p = bytes) fixed (byte* w = scratchBytes) fixed (byte* q = copy)
            {
                var writer = new DeterministicWriter(p, bytes.Length); frozen.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
                for (int length = 0; length < writer.Position; length++)
                { var truncated = new DeterministicReader(p, length); var work = new CanonicalScratch(w, scratchBytes.Length); Assert.AreNotEqual(DeterministicStatus.None, imported.TryReadCanonical(ref truncated, ref work, default(Int32Policy), default(Int32Policy))); Assert.IsFalse(imported.IsBuilt); Assert.AreEqual(0, truncated.Position); }
                var reader = new DeterministicReader(p, writer.Position); var scratch = new CanonicalScratch(w, scratchBytes.Length);
                imported.TryReadCanonical(ref reader, ref scratch, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
                var second = new DeterministicWriter(q, copy.Length); imported.TryWriteCanonical(ref second, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
                Assert.AreEqual(writer.Position, second.Position); Assert.AreEqual(DeterministicHash.Xxh64(p, writer.Position), DeterministicHash.Xxh64(q, second.Position));
            }
            var arcs = imported.AsReadOnly().Outgoing(a); Assert.IsTrue(arcs.MoveNext()); Assert.AreEqual(b, arcs.Current.Neighbor); Assert.IsFalse(arcs.MoveNext());
        }
        [Test]
        public void InsufficientDestinationCanBeRetriedWithoutPublishing()
        {
            var options = new GraphOptions(3, 4); using var g = new DeterministicGraph<int, int>(options); g.AddNode(1); var b = g.AddNode(2);
            using var frozen = new FrozenDeterministicGraph<int, int>(options, nodeCapacity: 1, edgeCapacity: 0);
            Assert.AreEqual(DeterministicStatus.CapacityExceeded, g.TryFreeze(frozen)); Assert.IsFalse(frozen.IsBuilt);
            g.TryRemoveNode(b).ThrowIfFailure(); g.TryFreeze(frozen).ThrowIfFailure(); Assert.AreEqual(1, frozen.NodeCount);
        }
    }
}
