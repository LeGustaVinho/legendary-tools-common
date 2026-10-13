using System;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class GraphBoundaryValidationTests
    {
        private static byte[] Encode(DeterministicGraph<int, int> graph)
        {
            var bytes = new byte[8192]; int length;
            fixed (byte* p = bytes) { var writer = new DeterministicWriter(p, bytes.Length); graph.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)).ThrowIfFailure(); length = writer.Position; }
            Array.Resize(ref bytes, length); return bytes;
        }
        private static byte[] Encode(ManagedGraphSnapshot<int, int> snapshot)
        {
            var bytes = new byte[8192]; int length;
            fixed (byte* p = bytes) { var writer = new DeterministicWriter(p, bytes.Length); snapshot.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)).ThrowIfFailure(); length = writer.Position; }
            Array.Resize(ref bytes, length); return bytes;
        }
        private static byte[] Encode(PersistentDeterministicGraph<int, int> graph)
        {
            var bytes = new byte[8192]; int length;
            fixed (byte* p = bytes) { var writer = new DeterministicWriter(p, bytes.Length); graph.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)).ThrowIfFailure(); length = writer.Position; }
            Array.Resize(ref bytes, length); return bytes;
        }
        private static bool Reach(bool[,] adjacency, int source, int target, bool identity = true)
        {
            var closure = (bool[,])adjacency.Clone();
            for (int k = 0; k < 3; k++) for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++)
                closure[i, j] |= closure[i, k] && closure[k, j];
            return identity && source == target || closure[source, target];
        }
        [TestCase(GraphDirection.Directed, GraphCyclePolicy.Allow)]
        [TestCase(GraphDirection.Directed, GraphCyclePolicy.Acyclic)]
        [TestCase(GraphDirection.Undirected, GraphCyclePolicy.Allow)]
        [TestCase(GraphDirection.Undirected, GraphCyclePolicy.Acyclic)]
        public void EveryThreeVertexTopologyMatchesReachabilityAndCycleOracle(GraphDirection direction, GraphCyclePolicy cycles)
        {
            int candidates = direction == GraphDirection.Directed ? 9 : 6, masks = 1 << candidates;
            var options = new GraphOptions(3, 9, direction, cycles, allowSelfEdges: true);
            var memory = new byte[4096];
            for (int mask = 0; mask < masks; mask++)
            {
                using var graph = new DeterministicGraph<int, int>(options);
                var nodes = new[] { graph.AddNode(0), graph.AddNode(1), graph.AddNode(2) };
                var adjacency = new bool[3, 3]; int bit = 0, expectedEdges = 0; bool undirectedCycle = false;
                for (int a = 0; a < 3; a++) for (int b = direction == GraphDirection.Directed ? 0 : a; b < 3; b++, bit++)
                {
                    if ((mask & (1 << bit)) == 0) continue;
                    bool createsCycle = Reach(adjacency, b, a);
                    var expected = cycles == GraphCyclePolicy.Acyclic && createsCycle ? DeterministicStatus.CycleDetected : DeterministicStatus.None;
                    var before = Encode(graph); var view = graph.AsReadOnly();
                    Assert.AreEqual(expected, graph.TryAddEdge(nodes[a], nodes[b], bit, out _), $"mask {mask}, arc {a}->{b}");
                    if (expected.IsSuccess())
                    {
                        if (direction == GraphDirection.Undirected) undirectedCycle |= createsCycle;
                        adjacency[a, b] = true; if (direction == GraphDirection.Undirected) adjacency[b, a] = true; expectedEdges++;
                    }
                    else { CollectionAssert.AreEqual(before, Encode(graph)); Assert.AreEqual(DeterministicStatus.None, view.Check()); }
                }
                Assert.AreEqual(expectedEdges, graph.EdgeCount);
                fixed (byte* p = memory)
                {
                    var scratch = new CanonicalScratch(p, memory.Length); var reader = graph.AsReadOnly();
                    GraphAlgorithms<int, int>.TryHasCycle(reader, ref scratch, out bool cyclic).ThrowIfFailure();
                    bool expectedCycle = direction == GraphDirection.Undirected ? undirectedCycle :
                        Reach(adjacency, 0, 0, false) || Reach(adjacency, 1, 1, false) || Reach(adjacency, 2, 2, false);
                    Assert.AreEqual(expectedCycle, cyclic, $"cycle mask {mask}");
                    for (int a = 0; a < 3; a++) for (int b = 0; b < 3; b++)
                    {
                        scratch.Position = 0; GraphAlgorithms<int, int>.TryReachable(reader, nodes[a], nodes[b], ref scratch, out bool actual).ThrowIfFailure();
                        Assert.AreEqual(Reach(adjacency, a, b), actual, $"reachability mask {mask}, {a}->{b}");
                    }
                }
            }
            TestContext.WriteLine($"topology masks={masks}; direction={direction}; cycles={cycles}");
        }

        [TestCase(GraphDirection.Directed, SnapshotPolicy.FullPerTick)]
        [TestCase(GraphDirection.Undirected, SnapshotPolicy.FullPerTick)]
        [TestCase(GraphDirection.Directed, SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(GraphDirection.Undirected, SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(GraphDirection.Directed, SnapshotPolicy.CopyOnWrite)]
        [TestCase(GraphDirection.Undirected, SnapshotPolicy.CopyOnWrite)]
        public void FullSnapshotPoolAndStaleLeaseCopiesPreserveBothRoots(GraphDirection direction, SnapshotPolicy policy)
        {
            var options = new GraphOptions(new BufferOptions(3, snapshots: policy, snapshotSlots: 2, pageBytes: 64),
                new BufferOptions(6, snapshots: policy, snapshotSlots: 2, pageBytes: 64), direction, allowParallelEdges: true, allowSelfEdges: true);
            using var graph = new DeterministicGraph<int, int>(options);
            var a = graph.AddNode(1); var b = graph.AddNode(2); graph.AddEdge(a, b, 3);
            graph.TryCapture(out var first).ThrowIfFailure(); var firstBytes = Encode(first);
            graph.TrySetNode(a, 99).ThrowIfFailure(); graph.AddEdge(b, b, 8);
            graph.TryCapture(out var second).ThrowIfFailure(); var secondBytes = Encode(second);
            var before = Encode(graph); var view = graph.AsReadOnly();
            Assert.AreEqual(DeterministicStatus.SnapshotCapacityExceeded, graph.TryCapture(out var failed));
            Assert.IsFalse(failed.IsCreated); CollectionAssert.AreEqual(before, Encode(graph)); Assert.AreEqual(DeterministicStatus.None, view.Check());
            for (int i = 0; i < 64; i++)
            {
                graph.TryClear().ThrowIfFailure(); var n = graph.AddNode(i); graph.AddEdge(n, n, i);
                CollectionAssert.AreEqual(firstBytes, Encode(first)); CollectionAssert.AreEqual(secondBytes, Encode(second));
                graph.TryRestore(i % 2 == 0 ? first : second).ThrowIfFailure();
                CollectionAssert.AreEqual(i % 2 == 0 ? firstBytes : secondBytes, Encode(graph));
            }
            var stale = first; first.Dispose(); graph.TryCapture(out var reused).ThrowIfFailure();
            stale.Dispose(); Assert.IsTrue(reused.IsCreated); Assert.IsTrue(second.IsCreated);
            second.Dispose(); reused.Dispose();
        }

        [TestCase(GraphDirection.Directed, SnapshotPolicy.FullPerTick)]
        [TestCase(GraphDirection.Undirected, SnapshotPolicy.FullPerTick)]
        [TestCase(GraphDirection.Directed, SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(GraphDirection.Undirected, SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(GraphDirection.Directed, SnapshotPolicy.CopyOnWrite)]
        [TestCase(GraphDirection.Undirected, SnapshotPolicy.CopyOnWrite)]
        public void FailedPersistentBatchRollsBackCascadeAndDoesNotConsumeFutureIds(GraphDirection direction, SnapshotPolicy policy)
        {
            var options = new GraphOptions(new BufferOptions(2, snapshots: policy), new BufferOptions(2, snapshots: policy), direction);
            using var source = new DeterministicGraph<int, int>(options);
            var a = source.AddNode(1); var b = source.AddNode(2); var edge = source.AddEdge(a, b, 3); var original = Encode(source);
            using var store = new PersistentDeterministicGraph<int, int>(options, new GraphPersistenceOptions(4));
            var memory = new byte[16384];
            fixed (byte* bytes = original) fixed (byte* p = memory)
            { var reader = new DeterministicReader(bytes, original.Length); var scratch = new CanonicalScratch(p, memory.Length); store.TryReadCanonical(ref reader, ref scratch, default(Int32Policy), default(Int32Policy)).ThrowIfFailure(); }
            store.TryCaptureCurrent(out var basis).ThrowIfFailure();
            var mutations = new[] {
                new GraphMutation<int, int> { Kind = GraphMutationKind.RemoveNode, Node = a },
                new GraphMutation<int, int> { Kind = GraphMutationKind.AddNode, NodeData = 4 },
                new GraphMutation<int, int> { Kind = GraphMutationKind.AddNode, NodeData = 5 } };
            fixed (GraphMutation<int, int>* p = mutations)
            {
                var buffer = new GraphWorkBuffer<GraphMutation<int, int>>(p, mutations.Length);
                Assert.AreEqual(DeterministicStatus.CapacityExceeded, store.TryWithBatch(basis, ref buffer, mutations.Length, out var failed));
                Assert.IsFalse(failed.IsCreated); Assert.AreEqual(1, store.VersionCount); CollectionAssert.AreEqual(original, Encode(store));
            }
            basis.AsReadOnly().TryGetEdge(edge, out _).ThrowIfFailure();
            store.TryWithRemoveNode(basis, a, out var removed).ThrowIfFailure();
            store.TryWithAddNode(removed, 7, out var added, out var replacement).ThrowIfFailure();
            Assert.AreEqual(new StableHandle(0, 2), replacement.Value);
            store.TryWithAddEdge(added, replacement, b, 8, out var connected, out var replacementEdge).ThrowIfFailure();
            Assert.AreEqual(new StableHandle(0, 2), replacementEdge.Value); Assert.AreEqual(1, connected.AsReadOnly().EdgeCount);
            CollectionAssert.AreEqual(original, Encode(store));
            connected.Dispose(); added.Dispose(); removed.Dispose(); basis.Dispose();
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void MalformedTopologyAndFreeListsAreRejectedBeforePublication(int corruption)
        {
            var options = new GraphOptions(3, 3, corruption == 3 ? GraphDirection.Undirected : GraphDirection.Directed);
            var a = new GraphNodeHandle(new StableHandle(0, 1)); var b = new GraphNodeHandle(new StableHandle(1, 1));
            var nodes = new SlotMapNode<GraphNodeRecord<int>>[3]; var edges = new SlotMapNode<GraphEdgeRecord<int>>[3];
            nodes[0] = new SlotMapNode<GraphNodeRecord<int>> { Active = 1, Generation = 1, Value = new GraphNodeRecord<int> { Data = 1 } };
            nodes[1] = new SlotMapNode<GraphNodeRecord<int>> { Active = 1, Generation = 1, Value = new GraphNodeRecord<int> { Data = 2 } };
            edges[0] = new SlotMapNode<GraphEdgeRecord<int>> { Active = 1, Generation = 1, Value = new GraphEdgeRecord<int> { Source = a, Target = b, Data = 3 } };
            var ns = new SlotMapState { Count = 2, Used = 2 }; var es = new SlotMapState { Count = 1, Used = 1 };
            if (corruption == 0) edges[0].Value.Target = new GraphNodeHandle(new StableHandle(1, 2));
            if (corruption == 1) { edges[1] = edges[0]; es.Count = es.Used = 2; }
            if (corruption == 2) edges[0].Value.Target = a;
            if (corruption == 3) { edges[0].Value.Source = b; edges[0].Value.Target = a; }
            if (corruption == 4)
            { nodes[2] = new SlotMapNode<GraphNodeRecord<int>> { Generation = 2, NextFree = 3 }; ns.Used = 3; ns.FreeHead = ns.FreeTail = 3; }
            if (corruption == 5) { nodes[2] = new SlotMapNode<GraphNodeRecord<int>> { Generation = 2 }; ns.Used = 3; }
            using var destination = new DeterministicGraph<int, int>(options); destination.AddNode(88); var before = Encode(destination); var view = destination.AsReadOnly();
            var bytes = new byte[8192]; var memory = new byte[16384];
            fixed (SlotMapNode<GraphNodeRecord<int>>* n = nodes) fixed (SlotMapNode<GraphEdgeRecord<int>>* e = edges) fixed (byte* p = bytes) fixed (byte* w = memory)
            {
                var nb = new GraphWorkBuffer<SlotMapNode<GraphNodeRecord<int>>>(n, nodes.Length); var eb = new GraphWorkBuffer<SlotMapNode<GraphEdgeRecord<int>>>(e, edges.Length);
                var writer = new DeterministicWriter(p, bytes.Length);
                GraphWire<int, int, Int32Policy, Int32Policy>.TryWrite(ref writer, ref nb, ref eb, ns, es, options, default, default).ThrowIfFailure();
                var reader = new DeterministicReader(p, writer.Position); var scratch = new CanonicalScratch(w, memory.Length);
                Assert.AreEqual(DeterministicStatus.InvalidEncoding, destination.TryReadCanonical(ref reader, ref scratch, default(Int32Policy), default(Int32Policy)));
                Assert.AreEqual(0, reader.Position); Assert.AreEqual(0, scratch.Position);
            }
            CollectionAssert.AreEqual(before, Encode(destination)); Assert.AreEqual(DeterministicStatus.None, view.Check());
        }
    }
}
