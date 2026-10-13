using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class GraphDeterminismValidationTests
    {
        private const int Nodes = 24, Edges = 48;
        public static IEnumerable<TestCaseData> Profiles()
        {
            foreach (GraphDirection direction in Enum.GetValues(typeof(GraphDirection)))
            foreach (GraphCyclePolicy cycles in Enum.GetValues(typeof(GraphCyclePolicy)))
            foreach (SnapshotPolicy snapshots in Enum.GetValues(typeof(SnapshotPolicy)))
            foreach (AllocationPolicy allocation in Enum.GetValues(typeof(AllocationPolicy)))
            foreach (bool multi in new[] { false, true })
                yield return new TestCaseData(direction, cycles, snapshots, allocation, multi);
        }

        private static GraphOptions Options(GraphDirection direction, GraphCyclePolicy cycles, SnapshotPolicy snapshots, AllocationPolicy allocation, bool multi)
            => new GraphOptions(new BufferOptions(allocation == AllocationPolicy.Fixed ? Nodes : 1, Nodes, allocation, snapshots, 2, 64),
                new BufferOptions(allocation == AllocationPolicy.Fixed ? Edges : 0, Edges, allocation, snapshots, 2, 64), direction, cycles, multi, multi);

        private static uint Next(ref uint state)
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5; return state;
        }
        private static byte[] Encode(DeterministicGraph<int, int> graph)
        {
            graph.TryMeasureCanonical(default(Int32Policy), default(Int32Policy), out int length).ThrowIfFailure();
            var bytes = new byte[length];
            fixed (byte* p = bytes)
            {
                var writer = new DeterministicWriter(p, length);
                graph.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
            }
            return bytes;
        }
        private static ulong Hash(byte[] bytes)
        { fixed (byte* p = bytes) return DeterministicHash.Xxh64(p, bytes.Length); }

        // A dictionary/edge-list specification, independent from slot maps, incidence caches and graph kernels.
        private sealed class Model
        {
            internal readonly Dictionary<GraphNodeHandle, int> NodeData = new Dictionary<GraphNodeHandle, int>();
            internal readonly Dictionary<GraphEdgeHandle, GraphEdge<int>> EdgeData = new Dictionary<GraphEdgeHandle, GraphEdge<int>>();
            private readonly GraphOptions options;
            internal Model(GraphOptions options) { this.options = options; }
            internal List<GraphNodeHandle> NodeOrder()
            { var result = new List<GraphNodeHandle>(NodeData.Keys); result.Sort(); return result; }
            private List<GraphEdgeHandle> EdgeOrder()
            { var result = new List<GraphEdgeHandle>(EdgeData.Keys); result.Sort(); return result; }
            internal List<GraphArc> Adjacent(GraphNodeHandle node, GraphTraversal direction)
            {
                var result = new List<GraphArc>();
                foreach (var handle in EdgeOrder())
                {
                    var edge = EdgeData[handle];
                    if (options.Direction == GraphDirection.Undirected)
                    {
                        if (edge.Source == node) result.Add(new GraphArc(handle, edge.Target, 0));
                        if (edge.Target == node) result.Add(new GraphArc(handle, edge.Source, 1));
                    }
                    else if (direction == GraphTraversal.Outgoing && edge.Source == node) result.Add(new GraphArc(handle, edge.Target, 0));
                    else if (direction == GraphTraversal.Incoming && edge.Target == node) result.Add(new GraphArc(handle, edge.Source, 1));
                }
                return result;
            }
            private bool Connected(GraphNodeHandle source, GraphNodeHandle target)
            {
                var seen = new HashSet<GraphNodeHandle>(); var pending = new Queue<GraphNodeHandle>();
                seen.Add(source); pending.Enqueue(source);
                while (pending.Count != 0)
                {
                    var node = pending.Dequeue(); if (node == target) return true;
                    foreach (var arc in Adjacent(node, GraphTraversal.Outgoing))
                        if (seen.Add(arc.Neighbor)) pending.Enqueue(arc.Neighbor);
                }
                return false;
            }
            internal DeterministicStatus Expected(in GraphMutation<int, int> mutation)
            {
                switch (mutation.Kind)
                {
                    case GraphMutationKind.AddNode: return NodeData.Count == Nodes ? DeterministicStatus.CapacityExceeded : DeterministicStatus.None;
                    case GraphMutationKind.SetNode:
                    case GraphMutationKind.RemoveNode: return NodeData.ContainsKey(mutation.Node) ? DeterministicStatus.None : DeterministicStatus.NotFound;
                    case GraphMutationKind.SetEdge:
                    case GraphMutationKind.RemoveEdge: return EdgeData.ContainsKey(mutation.Edge) ? DeterministicStatus.None : DeterministicStatus.NotFound;
                    case GraphMutationKind.Clear: return DeterministicStatus.None;
                    case GraphMutationKind.AddEdge:
                        if (!NodeData.ContainsKey(mutation.Source) || !NodeData.ContainsKey(mutation.Target)) return DeterministicStatus.NotFound;
                        if (mutation.Source == mutation.Target && !options.AllowSelfEdges) return DeterministicStatus.InvalidConfiguration;
                        if (!options.AllowParallelEdges)
                            foreach (var edge in EdgeData.Values)
                                if (edge.Source == mutation.Source && edge.Target == mutation.Target ||
                                    options.Direction == GraphDirection.Undirected && edge.Source == mutation.Target && edge.Target == mutation.Source)
                                    return DeterministicStatus.DuplicateKey;
                        if (options.Cycles == GraphCyclePolicy.Acyclic && Connected(mutation.Target, mutation.Source)) return DeterministicStatus.CycleDetected;
                        return EdgeData.Count == Edges ? DeterministicStatus.CapacityExceeded : DeterministicStatus.None;
                    default: return DeterministicStatus.InvalidConfiguration;
                }
            }
            internal void Apply(in GraphMutation<int, int> mutation, in GraphMutationResult result)
            {
                switch (mutation.Kind)
                {
                    case GraphMutationKind.AddNode:
                        Assert.IsTrue(result.Node.IsValid); Assert.IsFalse(NodeData.ContainsKey(result.Node)); NodeData.Add(result.Node, mutation.NodeData); break;
                    case GraphMutationKind.AddEdge:
                        Assert.IsTrue(result.Edge.IsValid); Assert.IsFalse(EdgeData.ContainsKey(result.Edge));
                        var a = mutation.Source; var b = mutation.Target;
                        if (options.Direction == GraphDirection.Undirected && a.CompareTo(b) > 0) { var swap = a; a = b; b = swap; }
                        EdgeData.Add(result.Edge, new GraphEdge<int>(a, b, mutation.EdgeData)); break;
                    case GraphMutationKind.SetNode: NodeData[mutation.Node] = mutation.NodeData; break;
                    case GraphMutationKind.SetEdge:
                        var edge = EdgeData[mutation.Edge]; EdgeData[mutation.Edge] = new GraphEdge<int>(edge.Source, edge.Target, mutation.EdgeData); break;
                    case GraphMutationKind.RemoveEdge: EdgeData.Remove(mutation.Edge); break;
                    case GraphMutationKind.RemoveNode:
                        var remove = new List<GraphEdgeHandle>();
                        foreach (var pair in EdgeData)
                            if (pair.Value.Source == mutation.Node || pair.Value.Target == mutation.Node) remove.Add(pair.Key);
                        foreach (var handle in remove) EdgeData.Remove(handle);
                        NodeData.Remove(mutation.Node); break;
                    case GraphMutationKind.Clear: NodeData.Clear(); EdgeData.Clear(); break;
                }
            }
            internal GraphMutation<int, int> Mutation(int step, ref uint random)
            {
                var nodes = NodeOrder(); var edges = EdgeOrder(); uint choice = Next(ref random);
                var node = nodes.Count != 0 ? nodes[(int)(Next(ref random) % (uint)nodes.Count)] : default;
                var target = nodes.Count != 0 ? nodes[(int)(Next(ref random) % (uint)nodes.Count)] : default;
                var edge = edges.Count != 0 ? edges[(int)(Next(ref random) % (uint)edges.Count)] : default;
                if ((choice & 32) != 0 && (choice & 64) != 0) node = new GraphNodeHandle(new StableHandle(Nodes + 5, 1));
                if ((choice & 128) != 0 && (choice & 256) != 0) edge = new GraphEdgeHandle(new StableHandle(Edges + 5, 1));
                int kind = (int)(choice % 16);
                var mutation = new GraphMutation<int, int> { Node = node, Source = node, Target = target, Edge = edge,
                    NodeData = unchecked((int)Next(ref random)), EdgeData = unchecked((int)Next(ref random)) };
                mutation.Kind = kind < 3 ? GraphMutationKind.AddNode : kind < 8 ? GraphMutationKind.AddEdge :
                    kind < 10 ? GraphMutationKind.SetNode : kind == 10 ? GraphMutationKind.SetEdge :
                    kind < 13 ? GraphMutationKind.RemoveEdge : kind < 15 ? GraphMutationKind.RemoveNode :
                    step % 97 == 0 ? GraphMutationKind.Clear : GraphMutationKind.AddEdge;
                return mutation;
            }
            internal void CheckReader<TReader>(TReader reader) where TReader : struct, IDeterministicGraphReader<int, int>
            {
                Assert.AreEqual(NodeData.Count, reader.NodeCount); Assert.AreEqual(EdgeData.Count, reader.EdgeCount);
                var actualNodes = new List<GraphNodeHandle>(); var actualEdges = new List<GraphEdgeHandle>();
                for (int i = 0; i < reader.NodeExtent; i++)
                {
                    var status = reader.TryNodeAt(i, out var handle, out int data);
                    if (status == DeterministicStatus.NotFound) continue; status.ThrowIfFailure();
                    actualNodes.Add(handle); Assert.AreEqual(NodeData[handle], data);
                    foreach (GraphTraversal direction in Enum.GetValues(typeof(GraphTraversal)))
                    {
                        var expected = Adjacent(handle, direction); reader.TryFirstArc(handle, direction, out int cursor).ThrowIfFailure();
                        int arcIndex = 0;
                        while (cursor != 0)
                        {
                            Assert.Less(arcIndex, expected.Count, "Extra incidence or circular adjacency cache");
                            reader.TryReadArc(cursor, out var arc, out cursor).ThrowIfFailure();
                            Assert.AreEqual(expected[arcIndex].Edge, arc.Edge); Assert.AreEqual(expected[arcIndex].Neighbor, arc.Neighbor);
                            Assert.AreEqual(expected[arcIndex].Side, arc.Side); arcIndex++;
                        }
                        Assert.AreEqual(expected.Count, arcIndex);
                    }
                }
                for (int i = 0; i < reader.EdgeExtent; i++)
                {
                    var status = reader.TryEdgeAt(i, out var handle, out var edge);
                    if (status == DeterministicStatus.NotFound) continue; status.ThrowIfFailure();
                    actualEdges.Add(handle); var expected = EdgeData[handle];
                    Assert.AreEqual(expected.Source, edge.Source); Assert.AreEqual(expected.Target, edge.Target); Assert.AreEqual(expected.Data, edge.Data);
                }
                CollectionAssert.AreEqual(NodeOrder(), actualNodes); CollectionAssert.AreEqual(EdgeOrder(), actualEdges);
            }
            private List<GraphNodeHandle> Walk(GraphNodeHandle root, GraphTraversal direction, bool depth)
            {
                var result = new List<GraphNodeHandle>(); var seen = new HashSet<GraphNodeHandle>();
                if (depth) Visit(root, direction, seen, result);
                else
                {
                    var queue = new Queue<GraphNodeHandle>(); seen.Add(root); queue.Enqueue(root);
                    while (queue.Count != 0)
                    {
                        var node = queue.Dequeue(); result.Add(node);
                        foreach (var arc in Adjacent(node, direction)) if (seen.Add(arc.Neighbor)) queue.Enqueue(arc.Neighbor);
                    }
                }
                return result;
            }
            private void Visit(GraphNodeHandle node, GraphTraversal direction, HashSet<GraphNodeHandle> seen, List<GraphNodeHandle> result)
            {
                if (!seen.Add(node)) return; result.Add(node);
                foreach (var arc in Adjacent(node, direction)) Visit(arc.Neighbor, direction, seen, result);
            }
            internal void CheckAlgorithms<TReader>(TReader reader) where TReader : struct, IDeterministicGraphReader<int, int>
            {
                var nodes = NodeOrder(); int count = nodes.Count; var directed = new bool[count, count]; var weak = new bool[count, count];
                foreach (var edge in EdgeData.Values)
                {
                    int a = nodes.IndexOf(edge.Source), b = nodes.IndexOf(edge.Target);
                    directed[a, b] = true; if (options.Direction == GraphDirection.Undirected) directed[b, a] = true;
                    weak[a, b] = weak[b, a] = true;
                }
                // Transitive closure is an independent reachability/cycle oracle.
                for (int k = 0; k < count; k++) for (int i = 0; i < count; i++) for (int j = 0; j < count; j++)
                { directed[i, j] |= directed[i, k] && directed[k, j]; weak[i, j] |= weak[i, k] && weak[k, j]; }
                bool cyclic = false;
                if (options.Direction == GraphDirection.Directed)
                { for (int i = 0; i < count; i++) cyclic |= directed[i, i]; }
                else
                {
                    // A multigraph is a forest iff no logical edge joins vertices already connected.
                    var parent = new int[count]; for (int i = 0; i < count; i++) parent[i] = i;
                    foreach (var edge in EdgeData.Values)
                    {
                        int a = nodes.IndexOf(edge.Source), b = nodes.IndexOf(edge.Target);
                        while (parent[a] != a) a = parent[a]; while (parent[b] != b) b = parent[b];
                        if (a == b) cyclic = true; else parent[a] = b;
                    }
                }
                var bytes = new byte[16384]; var output = new GraphNodeHandle[Nodes]; var components = new GraphComponent[Nodes];
                fixed (byte* p = bytes) fixed (GraphNodeHandle* o = output) fixed (GraphComponent* c = components)
                {
                    var scratch = new CanonicalScratch(p, bytes.Length);
                    GraphAlgorithms<int, int>.TryHasCycle(reader, ref scratch, out bool actualCycle).ThrowIfFailure(); Assert.AreEqual(cyclic, actualCycle);
                    for (int i = 0; i < count; i++) for (int j = 0; j < count; j++)
                    {
                        scratch.Position = 0; GraphAlgorithms<int, int>.TryReachable(reader, nodes[i], nodes[j], ref scratch, out bool reachable).ThrowIfFailure();
                        Assert.AreEqual(i == j || directed[i, j], reachable);
                    }
                    var buffer = new GraphWorkBuffer<GraphNodeHandle>(o, Nodes);
                    if (count != 0) foreach (GraphTraversal direction in Enum.GetValues(typeof(GraphTraversal)))
                    {
                        scratch.Position = 0; GraphAlgorithms<int, int>.TryBreadthFirst(reader, nodes[0], ref scratch, ref buffer, out int n, direction).ThrowIfFailure();
                        CollectionAssert.AreEqual(Walk(nodes[0], direction, false), new ArraySegment<GraphNodeHandle>(output, 0, n));
                        scratch.Position = 0; GraphAlgorithms<int, int>.TryDepthFirst(reader, nodes[0], ref scratch, ref buffer, out n, direction).ThrowIfFailure();
                        CollectionAssert.AreEqual(Walk(nodes[0], direction, true), new ArraySegment<GraphNodeHandle>(output, 0, n));
                    }
                    scratch.Position = 0; var cb = new GraphWorkBuffer<GraphComponent>(c, Nodes);
                    GraphAlgorithms<int, int>.TryComponents(reader, ref scratch, ref cb, out int nc, out int cc).ThrowIfFailure();
                    Assert.AreEqual(count, nc); var labels = new int[count]; for (int i = 0; i < count; i++) labels[i] = -1;
                    int expectedComponents = 0;
                    for (int i = 0; i < count; i++) if (labels[i] == -1)
                    { for (int j = 0; j < count; j++) if (i == j || weak[i, j]) labels[j] = expectedComponents; expectedComponents++; }
                    Assert.AreEqual(expectedComponents, cc);
                    for (int i = 0; i < count; i++) { Assert.AreEqual(nodes[i], components[i].Node); Assert.AreEqual(labels[i], components[i].Component); }
                    scratch.Position = 0;
                    var topo = GraphAlgorithms<int, int>.TryTopologicalOrder(reader, ref scratch, ref buffer, out int nt);
                    if (options.Direction == GraphDirection.Undirected) Assert.AreEqual(DeterministicStatus.InvalidConfiguration, topo);
                    else if (cyclic) { Assert.AreEqual(DeterministicStatus.CycleDetected, topo); Assert.AreEqual(0, nt); }
                    else
                    {
                        topo.ThrowIfFailure(); var remaining = new List<GraphNodeHandle>(nodes); var expected = new List<GraphNodeHandle>();
                        while (remaining.Count != 0)
                        {
                            GraphNodeHandle next = default;
                            foreach (var candidate in remaining)
                            {
                                bool blocked = false;
                                foreach (var edge in EdgeData.Values) if (edge.Target == candidate && remaining.Contains(edge.Source)) blocked = true;
                                if (!blocked) { next = candidate; break; }
                            }
                            Assert.IsTrue(next.IsValid); expected.Add(next); remaining.Remove(next);
                        }
                        CollectionAssert.AreEqual(expected, new ArraySegment<GraphNodeHandle>(output, 0, nt));
                    }
                }
            }
        }

        [TestCaseSource(nameof(Profiles))]
        public void SeededMutationsMatchIndependentModelAndReplayExactly(GraphDirection direction, GraphCyclePolicy cycles,
            SnapshotPolicy snapshots, AllocationPolicy allocation, bool multi)
        {
            foreach (uint seed in new[] { 0x13579bdfU, 0xdeadbeefU, 0x80000001U })
            {
                var options = Options(direction, cycles, snapshots, allocation, multi);
                using var graph = new DeterministicGraph<int, int>(options); using var peer = new DeterministicGraph<int, int>(options);
                var model = new Model(options);
                for (int i = 0; i < 12; i++)
                {
                    var add = new GraphMutation<int, int> { Kind = GraphMutationKind.AddNode, NodeData = i };
                    graph.TryApply(add, out var result).ThrowIfFailure(); peer.TryApply(add, out var other).ThrowIfFailure();
                    Assert.AreEqual(result.Node, other.Node); model.Apply(add, result);
                }
                graph.TryCapture(out var baseline).ThrowIfFailure(); var baselineBytes = Encode(graph);
                const int steps = 384; uint random = seed;
                var mutations = new GraphMutation<int, int>[steps]; var statuses = new DeterministicStatus[steps];
                var results = new GraphMutationResult[steps]; var hashes = new ulong[steps]; var previous = baselineBytes;
                try
                {
                    for (int step = 0; step < steps; step++)
                    {
                        var mutation = model.Mutation(step, ref random); var expected = model.Expected(mutation); var borrowed = graph.AsReadOnly();
                        var actual = graph.TryApply(mutation, out var result); var other = peer.TryApply(mutation, out var peerResult);
                        string context = $"seed {seed:x8}, step {step}, {mutation.Kind}";
                        Assert.AreEqual(expected, actual, context); Assert.AreEqual(actual, other, context);
                        Assert.AreEqual(result.Node, peerResult.Node, context); Assert.AreEqual(result.Edge, peerResult.Edge, context);
                        var encoded = Encode(graph); CollectionAssert.AreEqual(encoded, Encode(peer), context);
                        if (actual.IsSuccess()) model.Apply(mutation, result);
                        else { CollectionAssert.AreEqual(previous, encoded, context); Assert.AreEqual(DeterministicStatus.None, borrowed.Check(), context); }
                        model.CheckReader(graph.AsReadOnly());
                        if (step % 64 == 0) model.CheckAlgorithms(graph.AsReadOnly());
                        mutations[step] = mutation; statuses[step] = actual; results[step] = result; hashes[step] = Hash(encoded); previous = encoded;
                    }
                    using var frozen = new FrozenDeterministicGraph<int, int>(options); graph.TryFreeze(frozen).ThrowIfFailure();
                    model.CheckReader(frozen.AsReadOnly()); model.CheckAlgorithms(frozen.AsReadOnly());
                    graph.TryRestore(baseline).ThrowIfFailure(); CollectionAssert.AreEqual(baselineBytes, Encode(graph));
                    for (int step = 0; step < steps; step++)
                    {
                        var status = graph.TryApply(mutations[step], out var result);
                        Assert.AreEqual(statuses[step], status, $"replay seed {seed:x8}, step {step}");
                        Assert.AreEqual(results[step].Node, result.Node); Assert.AreEqual(results[step].Edge, result.Edge);
                        Assert.AreEqual(hashes[step], Hash(Encode(graph)), $"replay seed {seed:x8}, step {step}");
                    }
                    CollectionAssert.AreEqual(previous, Encode(graph)); CollectionAssert.AreEqual(previous, Encode(peer));
                    model.CheckReader(graph.AsReadOnly()); model.CheckReader(frozen.AsReadOnly());
                }
                finally { baseline.Dispose(); }
            }
        }
    }
}
