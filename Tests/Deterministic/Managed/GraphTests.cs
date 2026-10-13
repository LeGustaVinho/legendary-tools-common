using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class GraphTests
    {
        [TestCase(GraphDirection.Directed)]
        [TestCase(GraphDirection.Undirected)]
        public void ParallelAndSelfEdgesEnumerateOnceAndCascadeRemovalPreservesFifo(GraphDirection direction)
        {
            using var g = new DeterministicGraph<int, int>(new GraphOptions(4, 8, direction, allowParallelEdges: true, allowSelfEdges: true));
            var a = g.AddNode(10); var b = g.AddNode(20); var c = g.AddNode(30);
            var ab = g.AddEdge(a, b, 1); var aa = g.AddEdge(a, a, 2); var ba = g.AddEdge(b, a, 3); var ac = g.AddEdge(a, c, 4);
            int incidences = 0; foreach (var arc in g.Outgoing(a)) incidences++;
            Assert.AreEqual(direction == GraphDirection.Directed ? 3 : 5, incidences);
            int logical = 0; foreach (var edge in g.Edges) logical++; Assert.AreEqual(4, logical);
            g.TryRemoveNode(a).ThrowIfFailure(); Assert.AreEqual(0, g.EdgeCount); Assert.AreEqual(2, g.NodeCount);
            foreach (var arc in g.Outgoing(b)) Assert.Fail("Dangling incidence");
            Assert.AreEqual(new StableHandle(a.Index, 2), g.AddNode(40).Value);
            var replacement = g.AddEdge(b, c, 5); Assert.AreEqual(new StableHandle(ab.Index, 2), replacement.Value);
            Assert.AreEqual(DeterministicStatus.NotFound, g.TryGetEdge(aa, out _));
        }
        [TestCase(GraphDirection.Directed)]
        [TestCase(GraphDirection.Undirected)]
        public void AcyclicRejectionDoesNotConsumeAnIdOrInvalidateViews(GraphDirection direction)
        {
            using var g = new DeterministicGraph<int, int>(new GraphOptions(3, 8, direction, GraphCyclePolicy.Acyclic, true, true));
            var a = g.AddNode(1); var b = g.AddNode(2); var c = g.AddNode(3);
            g.AddEdge(a, b, 1); g.AddEdge(b, c, 2); var view = g.AsReadOnly(); ulong revision = g.Revision;
            Assert.AreEqual(DeterministicStatus.CycleDetected, g.TryAddEdge(c, a, 3, out var failed)); Assert.IsFalse(failed.IsValid);
            Assert.AreEqual(DeterministicStatus.CycleDetected, g.TryAddEdge(a, a, 3, out _));
            Assert.AreEqual(revision, g.Revision); Assert.AreEqual(DeterministicStatus.None, view.Check());
            if (direction == GraphDirection.Undirected) Assert.AreEqual(DeterministicStatus.CycleDetected, g.TryAddEdge(a, b, 3, out _));
            else Assert.AreEqual(new StableHandle(2, 1), g.AddEdge(a, c, 3).Value);
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void SnapshotsPreserveReadersAndFutureIdentities(SnapshotPolicy policy)
        {
            var options = new GraphOptions(new BufferOptions(0, 5, AllocationPolicy.Grow, policy, 2, 64), new BufferOptions(0, 8, AllocationPolicy.Grow, policy, 2, 64));
            using var g = new DeterministicGraph<int, int>(options);
            var a = g.AddNode(1); var b = g.AddNode(2); var e = g.AddEdge(a, b, 4); g.TryRemoveEdge(e).ThrowIfFailure();
            g.TryCapture(out var root).ThrowIfFailure(); var frozenReader = root.AsReadOnly(); var borrowed = g.AsReadOnly();
            var next = g.AddEdge(a, b, 8); g.TrySetNode(a, 9).ThrowIfFailure();
            Assert.AreEqual(DeterministicStatus.Invalidated, borrowed.Check()); Assert.AreEqual(DeterministicStatus.None, frozenReader.Check());
            frozenReader.TryGetNode(a, out int old).ThrowIfFailure(); Assert.AreEqual(1, old);
            g.TryClear().ThrowIfFailure(); g.TryRestore(root).ThrowIfFailure(); Assert.AreEqual(next, g.AddEdge(a, b, 8));
            var copy = root; root.Dispose(); Assert.AreEqual(DeterministicStatus.Disposed, copy.AsReadOnly().Check());
            g.TryCapture(out var newer).ThrowIfFailure(); copy.Dispose(); Assert.IsTrue(newer.IsCreated); newer.Dispose();
        }
        [Test]
        public void FixedCapacityAndDuplicateFailuresLeaveTheTopologyUntouched()
        {
            using var g = new DeterministicGraph<int, int>(2, 1); var a = g.AddNode(1); var b = g.AddNode(2);
            var e = g.AddEdge(a, b, 10); var view = g.AsReadOnly();
            Assert.AreEqual(DeterministicStatus.CapacityExceeded, g.TryAddNode(3, out _));
            Assert.AreEqual(DeterministicStatus.DuplicateKey, g.TryAddEdge(a, b, 20, out _));
            Assert.AreEqual(DeterministicStatus.CapacityExceeded, g.TryAddEdge(b, a, 20, out _));
            Assert.AreEqual(DeterministicStatus.InvalidConfiguration, g.TryAddEdge(a, a, 20, out _));
            Assert.AreEqual(DeterministicStatus.None, view.Check());
            g.TryGetEdge(e, out var stored).ThrowIfFailure(); Assert.AreEqual(10, stored.Data);
            g.TryRemoveEdge(e).ThrowIfFailure(); Assert.AreEqual(new StableHandle(0, 2), g.AddEdge(b, a, 20).Value);
        }
        [Test]
        public void AlgorithmsHaveStableOrderingAndDoNotPublishPartialResults()
        {
            using var g = new DeterministicGraph<int, int>(5, 8); var a = g.AddNode(0); var b = g.AddNode(1); var c = g.AddNode(2); var d = g.AddNode(3); var isolated = g.AddNode(4);
            g.AddEdge(a, c, 1); g.AddEdge(a, b, 1); g.AddEdge(c, d, 1); g.AddEdge(b, d, 1);
            var scratchBytes = new byte[2048]; var output = new GraphNodeHandle[5]; var components = new GraphComponent[5];
            fixed (byte* p = scratchBytes) fixed (GraphNodeHandle* r = output) fixed (GraphComponent* comp = components)
            {
                var scratch = new CanonicalScratch(p, scratchBytes.Length); var buffer = new GraphWorkBuffer<GraphNodeHandle>(r, output.Length);
                GraphAlgorithms<int, int>.TryBreadthFirst(g.AsReadOnly(), a, ref scratch, ref buffer, out int count).ThrowIfFailure();
                Assert.AreEqual(4, count); CollectionAssert.AreEqual(new[] { a, c, b, d }, new ArraySegment<GraphNodeHandle>(output, 0, count));
                scratch.Position = 0; GraphAlgorithms<int, int>.TryDepthFirst(g.AsReadOnly(), a, ref scratch, ref buffer, out count).ThrowIfFailure();
                CollectionAssert.AreEqual(new[] { a, c, d, b }, new ArraySegment<GraphNodeHandle>(output, 0, count));
                scratch.Position = 0; GraphAlgorithms<int, int>.TryTopologicalOrder(g.AsReadOnly(), ref scratch, ref buffer, out count).ThrowIfFailure();
                CollectionAssert.AreEqual(new[] { a, b, c, d, isolated }, output);
                scratch.Position = 0; var componentBuffer = new GraphWorkBuffer<GraphComponent>(comp, components.Length);
                GraphAlgorithms<int, int>.TryComponents(g.AsReadOnly(), ref scratch, ref componentBuffer, out count, out int number).ThrowIfFailure();
                Assert.AreEqual(2, number); Assert.AreEqual(1, components[4].Component);
                scratch.Position = 0; GraphAlgorithms<int, int>.TryReachable(g.AsReadOnly(), d, a, ref scratch, out bool reachable).ThrowIfFailure(); Assert.IsFalse(reachable);
                g.AddEdge(d, a, 2); scratch.Position = 0;
                GraphAlgorithms<int, int>.TryHasCycle(g.AsReadOnly(), ref scratch, out bool cycle).ThrowIfFailure(); Assert.IsTrue(cycle);
                scratch.Position = 0; var before = (GraphNodeHandle[])output.Clone();
                Assert.AreEqual(DeterministicStatus.CycleDetected, GraphAlgorithms<int, int>.TryTopologicalOrder(g.AsReadOnly(), ref scratch, ref buffer, out count));
                Assert.AreEqual(0, count); CollectionAssert.AreEqual(before, output);
                scratch = new CanonicalScratch(p, 1);
                Assert.AreEqual(DeterministicStatus.BufferTooSmall, GraphAlgorithms<int, int>.TryBreadthFirst(g.AsReadOnly(), a, ref scratch, ref buffer, out count));
                CollectionAssert.AreEqual(before, output);
            }
        }
        [Test]
        public void UndirectedCycleDetectionDistinguishesParallelEdgesFromTheReverseIncidence()
        {
            using var g = new DeterministicGraph<int, int>(new GraphOptions(3, 4, GraphDirection.Undirected, allowParallelEdges: true, allowSelfEdges: true));
            var a = g.AddNode(1); var b = g.AddNode(2); var c = g.AddNode(3); g.AddEdge(a, b, 1); g.AddEdge(b, c, 1);
            var bytes = new byte[2048]; fixed (byte* p = bytes)
            {
                var scratch = new CanonicalScratch(p, bytes.Length); GraphAlgorithms<int, int>.TryHasCycle(g.AsReadOnly(), ref scratch, out bool cycle).ThrowIfFailure(); Assert.IsFalse(cycle);
                g.AddEdge(b, a, 1); scratch.Position = 0; GraphAlgorithms<int, int>.TryHasCycle(g.AsReadOnly(), ref scratch, out cycle).ThrowIfFailure(); Assert.IsTrue(cycle);
            }
        }
        [Test]
        public void ReusedEdgeSlotsReturnToSortedAdjacencyOrder()
        {
            using var g = new DeterministicGraph<int, int>(new GraphOptions(2, 3, allowParallelEdges: true));
            var a = g.AddNode(1); var b = g.AddNode(2); var first = g.AddEdge(a, b, 1); var second = g.AddEdge(a, b, 2);
            g.TryRemoveEdge(first).ThrowIfFailure(); var replaced = g.AddEdge(a, b, 3);
            var enumerator = g.Outgoing(a); Assert.IsTrue(enumerator.MoveNext()); Assert.AreEqual(replaced, enumerator.Current.Edge);
            Assert.IsTrue(enumerator.MoveNext()); Assert.AreEqual(second, enumerator.Current.Edge); Assert.IsFalse(enumerator.MoveNext());
        }
    }
}
