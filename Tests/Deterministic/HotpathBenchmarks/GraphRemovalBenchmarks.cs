using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.PerformanceTesting;

namespace LegendaryTools.Deterministic.Tests.HotpathBenchmarks
{
    public sealed unsafe partial class ExtendedHotpathBenchmarks
    {
        public enum HubTopology { DirectedStar, UndirectedParallelAndSelf }
        private static int HubEdges(int count, HubTopology topology) => topology == HubTopology.DirectedStar ? count - 1 : 2 * (count - 1) + 4;
        private static GraphOptions HubOptions(int count, HubTopology topology, SnapshotPolicy policy) => new GraphOptions(
            Options(count, policy, 1), Options(HubEdges(count, topology), policy, 1),
            direction: topology == HubTopology.DirectedStar ? GraphDirection.Directed : GraphDirection.Undirected,
            allowParallelEdges: topology == HubTopology.UndirectedParallelAndSelf, allowSelfEdges: topology == HubTopology.UndirectedParallelAndSelf);
        private static IEnumerable<TestCaseData> GraphRemovalCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int count in new[] { 256, 2048 })
            foreach (HubTopology topology in new[] { HubTopology.DirectedStar, HubTopology.UndirectedParallelAndSelf })
            foreach (SnapshotPolicy policy in new[] { SnapshotPolicy.FullPerTick, SnapshotPolicy.CopyOnWrite })
                yield return new TestCaseData(backend, count, topology, policy).SetName($"H13_GraphRemoveHub_{backend}_{count}_{topology}_{policy}");
        }
        private static void PopulateHub(int count, HubTopology topology, Func<int, GraphNodeHandle> addNode,
            Func<GraphNodeHandle, GraphNodeHandle, int, GraphEdgeHandle> addEdge, out GraphNodeHandle[] nodes, out GraphEdgeHandle[] edges)
        {
            nodes = new GraphNodeHandle[count]; edges = new GraphEdgeHandle[HubEdges(count, topology)];
            for (int i = 0; i < count; i++) nodes[i] = addNode(SlotValue(i));
            int e = 0;
            for (int i = 1; i < count; i++)
            {
                edges[e] = i % 2 == 0 ? addEdge(nodes[0], nodes[i], SlotValue(e)) : addEdge(nodes[i], nodes[0], SlotValue(e)); e++;
                if (topology == HubTopology.UndirectedParallelAndSelf) { edges[e] = addEdge(nodes[0], nodes[i], SlotValue(e)); e++; }
            }
            if (topology == HubTopology.UndirectedParallelAndSelf)
                for (int i = 0; i < 4; i++) { edges[e] = addEdge(nodes[0], nodes[0], SlotValue(e)); e++; }
            Assert.AreEqual(edges.Length, e);
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct HubRemovalJob : IJob
        {
            internal NativeDeterministicGraph<int, int> Graph;
            internal GraphNodeHandle Hub;
            internal NativeArray<int> Result;
            public void Execute() { int managed = 0; MarkManaged(ref managed); Result[0] = managed; Result[1] = (int)Graph.TryRemoveNode(Hub); }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [TestCaseSource(nameof(GraphRemovalCases)), Performance]
        public void RemoveHighDegreeGraphNode(Backend backend, int count, HubTopology topology, SnapshotPolicy policy)
        {
            var options = HubOptions(count, topology, policy); int edgeCount = HubEdges(count, topology);
            GraphScratch.TryMeasure(count, edgeCount, out int scratchBytes).ThrowIfFailure();
            string distribution = topology + "_" + policy + "_1Root";
            if (backend == Backend.Managed)
            {
                using var graph = new DeterministicGraph<int, int>(options);
                PopulateHub(count, topology, graph.AddNode, graph.AddEdge, out var nodes, out var edges);
                graph.TryCapture(out var root).ThrowIfFailure();
                try
                {
                    long baseline = graph.MemoryStats.LivePayloadBytes; int status = 0;
                    Benchmark("H13", "Graph.RemoveHub", backend, distribution, count, edgeCount, 0, () => status = (int)graph.TryRemoveNode(nodes[0]),
                        () =>
                        {
                            Assert.AreEqual(0, status); Assert.AreEqual(count - 1, graph.NodeCount); Assert.AreEqual(0, graph.EdgeCount);
                            Assert.AreEqual(count, root.NodeCount); Assert.AreEqual(edgeCount, root.EdgeCount);
                            for (int i = 0; i < count; i++)
                            {
                                var a = graph.TryGetNode(nodes[i], out int value); var b = root.TryGetNode(nodes[i], out int retained);
                                if (i == 0 ? a != DeterministicStatus.NotFound : !a.IsSuccess() || value != SlotValue(i)) Assert.Fail("Graph node " + i);
                                if (!b.IsSuccess() || retained != SlotValue(i)) Assert.Fail("Graph retained node " + i);
                                if (i != 0)
                                {
                                    var outgoing = graph.Outgoing(nodes[i]).GetEnumerator(); var incoming = graph.Incoming(nodes[i]).GetEnumerator();
                                    if (outgoing.MoveNext() || incoming.MoveNext()) Assert.Fail("Unlinked incidence " + i);
                                }
                            }
                            for (int i = 0; i < edgeCount; i++)
                            {
                                if (graph.TryGetEdge(edges[i], out _) != DeterministicStatus.NotFound) Assert.Fail("Graph edge " + i);
                                var s = root.TryGetEdge(edges[i], out var edge); if (!s.IsSuccess() || edge.Data != SlotValue(i)) Assert.Fail("Graph retained edge " + i);
                            }
                        }, () => graph.TryRestore(root).ThrowIfFailure(),
                        () => { var d = Stats(graph.MemoryStats); d.baselineLivePayloadBytes = baseline; d.payloadGrowthBytes = d.livePayloadBytes - baseline; d.scratchBytes = scratchBytes; d.active = count; d.universe = edgeCount; d.retainedRoots = 1; return d; });
                }
                finally { root.Dispose(); }
            }
            else
            {
                using var graph = new NativeDeterministicGraph<int, int>(options, Allocator.Persistent);
                using var result = new NativeArray<int>(2, Allocator.Persistent);
                PopulateHub(count, topology, graph.AddNode, graph.AddEdge, out var nodes, out var edges);
                graph.TryCapture(out var root).ThrowIfFailure();
                try
                {
                    long baseline = graph.MemoryStats.LivePayloadBytes; var job = new HubRemovalJob { Graph = graph, Hub = nodes[0], Result = result };
                    Benchmark("H13", "Graph.RemoveHub", backend, distribution, count, edgeCount, 0, () => job.Schedule().Complete(),
                        () =>
                        {
                            CheckBurst(result[0]); Assert.AreEqual(0, result[1]); Assert.AreEqual(count - 1, graph.NodeCount); Assert.AreEqual(0, graph.EdgeCount);
                            Assert.AreEqual(count, root.NodeCount); Assert.AreEqual(edgeCount, root.EdgeCount);
                            for (int i = 0; i < count; i++)
                            {
                                var a = graph.TryGetNode(nodes[i], out int value); var b = root.TryGetNode(nodes[i], out int retained);
                                if (i == 0 ? a != DeterministicStatus.NotFound : !a.IsSuccess() || value != SlotValue(i)) Assert.Fail("Native graph node " + i);
                                if (!b.IsSuccess() || retained != SlotValue(i)) Assert.Fail("Native graph retained node " + i);
                                if (i != 0)
                                {
                                    var outgoing = graph.Outgoing(nodes[i]).GetEnumerator(); var incoming = graph.Incoming(nodes[i]).GetEnumerator();
                                    if (outgoing.MoveNext() || incoming.MoveNext()) Assert.Fail("Native unlinked incidence " + i);
                                }
                            }
                            for (int i = 0; i < edgeCount; i++)
                            {
                                if (graph.TryGetEdge(edges[i], out _) != DeterministicStatus.NotFound) Assert.Fail("Native graph edge " + i);
                                var s = root.TryGetEdge(edges[i], out var edge); if (!s.IsSuccess() || edge.Data != SlotValue(i)) Assert.Fail("Native graph retained edge " + i);
                            }
                        }, () => graph.TryRestore(root).ThrowIfFailure(),
                        () =>
                        {
                            var d = Stats(graph.MemoryStats); d.reservedNativeMetadataBytes = NativeMetadata(graph.NodesStorage) + NativeMetadata(graph.EdgesStorage) + NativeMetadata(graph.ArcsStorage);
                            d.baselineLivePayloadBytes = baseline; d.payloadGrowthBytes = d.livePayloadBytes - baseline; d.scratchBytes = scratchBytes; d.active = count; d.universe = edgeCount; d.retainedRoots = 1; return d;
                        });
                }
                finally { root.Dispose(); }
            }
        }
    }
}
