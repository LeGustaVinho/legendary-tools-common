using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class NativeGraphHotpathOptimizationValidationTests
    {
        [BurstDiscard] private static void ManagedMarker(ref int marker) => marker = 1;
        [BurstCompile(CompileSynchronously = true)] private struct ClearJob : IJob
        {
            public NativeDeterministicGraph<int, int> Graph;
            public NativeArray<int> Result;
            public void Execute()
            {
                int marker = 0; ManagedMarker(ref marker); Result[0] = marker;
                Result[1] = (int)Graph.TryClear(); Result[2] = 73951;
            }
        }
        [TestCase(GraphDirection.Directed, false, 64)] [TestCase(GraphDirection.Directed, false, 4096)]
        [TestCase(GraphDirection.Directed, true, 64)] [TestCase(GraphDirection.Directed, true, 4096)]
        [TestCase(GraphDirection.Undirected, false, 64)] [TestCase(GraphDirection.Undirected, false, 4096)]
        [TestCase(GraphDirection.Undirected, true, 64)] [TestCase(GraphDirection.Undirected, true, 4096)]
        public void SparseClearInBurstPreservesSnapshotAndPageBoundaries(GraphDirection direction, bool scattered, int pageBytes)
        {
            const int extent = 8192, active = 128;
            var buffer = new BufferOptions(extent, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 1, pageBytes: pageBytes);
            using var graph = new NativeDeterministicGraph<int, int>(new GraphOptions(buffer, buffer, direction, allowParallelEdges: true), Allocator.Persistent);
            var nodes = new GraphNodeHandle[extent]; var kept = new GraphNodeHandle[active];
            for (int i = 0; i < extent; i++) nodes[i] = graph.AddNode(i);
            int count = 0; for (int i = 0; i < extent; i++)
                if (scattered ? i % 64 == 63 : i >= extent - active) kept[count++] = nodes[i]; else graph.TryRemoveNode(nodes[i]).ThrowIfFailure();
            var edges = new GraphEdgeHandle[extent];
            for (int i = 0; i < extent; i++) edges[i] = graph.AddEdge(kept[i % active], kept[(i + 1) % active], i);
            for (int i = 0; i < extent; i++) if (!(scattered ? i % 64 == 63 : i >= extent - active)) graph.TryRemoveEdge(edges[i]).ThrowIfFailure();
            graph.TryCapture(out var snapshot).ThrowIfFailure();
            try
            {
                long before = graph.MemoryStats.LivePayloadBytes; using var result = new NativeArray<int>(3, Allocator.Persistent);
                var job = new ClearJob { Graph = graph, Result = result }; job.Schedule().Complete();
                Assert.IsTrue(BurstCompiler.IsEnabled); Assert.AreEqual(0, result[0]); Assert.AreEqual(0, result[1]); Assert.AreEqual(73951, result[2]);
                Assert.AreEqual(0, graph.NodeCount); Assert.AreEqual(0, graph.EdgeCount);
                snapshot.TryGetNode(kept[0], out int old).ThrowIfFailure(); Assert.AreEqual((int)kept[0].Index, old);
                Assert.AreEqual(active, snapshot.NodeCount); Assert.AreEqual(active, snapshot.EdgeCount);
                if (!scattered) Assert.Less(graph.MemoryStats.LivePayloadBytes - before, 45000);
                graph.TryRestore(snapshot).ThrowIfFailure(); Assert.AreEqual(active, graph.NodeCount); Assert.AreEqual(active, graph.EdgeCount);
                foreach (var node in graph.Nodes) Assert.AreEqual((int)node.Handle.Index, node.Data);
            }
            finally { snapshot.Dispose(); }
        }
    }
}
