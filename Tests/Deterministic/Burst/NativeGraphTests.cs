using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class NativeGraphTests
    {
        [BurstCompile(CompileSynchronously = true)]
        private struct GraphJob : IJob
        {
            internal NativeDeterministicGraph<int, int> Graph;
            internal NativeArray<ulong> Results;
            internal NativeArray<byte> Scratch;
            internal NativeArray<GraphNodeHandle> Output;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Results[0] = (ulong)managed;
                var a = Graph.AddNode(1); var b = Graph.AddNode(2); var c = Graph.AddNode(3);
                var edge = Graph.AddEdge(a, b, 5); Graph.AddEdge(b, c, 7);
                Results[1] = (ulong)Graph.TryAddEdge(c, a, 9, out _);
                Graph.TryCapture(out var root); Graph.TryRemoveEdge(edge);
                var next = Graph.AddEdge(a, c, 11); Graph.TrySetNode(a, 100);
                root.TryGetNode(a, out int old); Results[2] = (ulong)old;
                Graph.TryRestore(root); root.Dispose(); Graph.TryRemoveEdge(edge);
                Results[3] = next.Value.Packed; Results[4] = Graph.AddEdge(a, c, 11).Value.Packed;
                var scratch = new CanonicalScratch((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Scratch), Scratch.Length);
                var output = new GraphWorkBuffer<GraphNodeHandle>((GraphNodeHandle*)NativeArrayUnsafeUtility.GetUnsafePtr(Output), Output.Length);
                Results[5] = (ulong)GraphAlgorithms<int, int>.TryTopologicalOrder(Graph.AsReadOnly(), ref scratch, ref output, out int n);
                Results[6] = (ulong)n; Results[7] = Output[0].Value.Packed;
                Graph.TryRemoveNode(a); Results[8] = (ulong)Graph.EdgeCount; Results[9] = (ulong)Graph.NodeCount;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void MutationsSnapshotsAndAlgorithmsExecuteInBurst(SnapshotPolicy policy)
        {
            Assert.IsTrue(BurstCompiler.IsEnabled);
            var options = new GraphOptions(new BufferOptions(0, 4, AllocationPolicy.Grow, policy, 2, 64),
                new BufferOptions(0, 6, AllocationPolicy.Grow, policy, 2, 64), cycles: GraphCyclePolicy.Acyclic);
            using var graph = new NativeDeterministicGraph<int, int>(options, Allocator.Persistent);
            using var results = new NativeArray<ulong>(10, Allocator.TempJob);
            using var scratch = new NativeArray<byte>(4096, Allocator.TempJob);
            using var output = new NativeArray<GraphNodeHandle>(4, Allocator.TempJob);
            new GraphJob { Graph = graph, Results = results, Scratch = scratch, Output = output }.Schedule().Complete();
            CollectionAssert.AreEqual(new ulong[] { 0, (ulong)DeterministicStatus.CycleDetected, 1,
                new StableHandle(0, 2).Packed, new StableHandle(0, 2).Packed, 0, 3, new StableHandle(0, 1).Packed, 1, 2 }, results.ToArray());
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct ReadJob : IJob
        {
            internal NativeGraphReadOnly<int, int> Graph;
            internal NativeArray<int> Result;
            public void Execute()
            {
                int sum = 0; foreach (var n in Graph.Nodes) sum += n.Data; Result[0] = sum;
            }
        }
        [Test]
        public void PublishedSnapshotCanBeReadByAJobAfterLiveMutations()
        {
            using var graph = new NativeDeterministicGraph<int, int>(new GraphOptions(new BufferOptions(4, snapshots: SnapshotPolicy.CopyOnWrite), new BufferOptions(8, snapshots: SnapshotPolicy.CopyOnWrite)), Allocator.Persistent);
            var a = graph.AddNode(3); graph.AddNode(4); graph.TryCapture(out var root).ThrowIfFailure();
            graph.TrySetNode(a, 30).ThrowIfFailure(); using var result = new NativeArray<int>(1, Allocator.TempJob);
            new ReadJob { Graph = root.AsReadOnly(), Result = result }.Schedule().Complete(); Assert.AreEqual(7, result[0]); root.Dispose();
        }
    }
}
