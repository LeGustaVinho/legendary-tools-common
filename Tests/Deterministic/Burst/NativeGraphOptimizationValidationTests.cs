using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class NativeGraphOptimizationValidationTests
    {
        [BurstCompile(CompileSynchronously = true)] private struct FreezeJob : IJob
        {
            internal NativeDeterministicGraph<int, int> Graph;
            internal NativeFrozenDeterministicGraph<int, int> Frozen;
            internal NativeArray<long> Result;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed;
                Result[1] = (long)Frozen.TryBuild(Graph.AsReadOnly()); Result[2] = Frozen.MemoryStats.ReservedPayloadBytes; Result[3] = 1234;
            }
            [BurstDiscard] private static void MarkManaged(ref int value) => value = 1;
        }
        [Test] public void FrozenWorkspaceReleasedInJobIsVisibleToCopiedOwners()
        {
            var options = new GraphOptions(4096, 64);
            using var graph = new NativeDeterministicGraph<int, int>(options, Allocator.Persistent);
            using var frozen = new NativeFrozenDeterministicGraph<int, int>(options, Allocator.Persistent, 2, 1);
            using var result = new NativeArray<long>(4, Allocator.Persistent);
            var a = graph.AddNode(1); var b = graph.AddNode(2); graph.AddEdge(a, b, 3);
            var alias = frozen; long before = alias.MemoryStats.ReservedPayloadBytes;
            GraphScratch.TryMeasureFrozen(4096, 2, out int bytes).ThrowIfFailure();
            new FreezeJob { Graph = graph, Frozen = frozen, Result = result }.Schedule().Complete();
            Assert.AreEqual(0, result[0]); Assert.AreEqual(0, result[1]); Assert.AreEqual(1234, result[3]);
            Assert.IsTrue(alias.IsBuilt); Assert.AreEqual(bytes, before - result[2]);
            Assert.AreEqual(result[2], frozen.MemoryStats.ReservedPayloadBytes); Assert.AreEqual(result[2], alias.MemoryStats.ReservedPayloadBytes);
            alias.AsReadOnly().TryGetNode(a, out int value).ThrowIfFailure(); Assert.AreEqual(1, value);
            Assert.AreEqual(DeterministicStatus.InvalidConfiguration, alias.TryBuild(graph.AsReadOnly()));
            // Only the original owner is disposed, after both aliases have finished reading.
        }
        [BurstCompile(CompileSynchronously = true)] private struct DeltaJob : IJob
        {
            internal NativeDeterministicGraph<int, int> Graph;
            [ReadOnly] internal NativeArray<byte> Packet;
            internal NativeArray<byte> Scratch;
            internal NativeArray<long> Result;
            internal bool Reverse;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed;
                var r = new DeterministicReader((byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(Packet), Packet.Length);
                var s = new CanonicalScratch((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Scratch), Scratch.Length);
                Result[1] = (long)Graph.TryApplyDelta(ref r, ref s, default(Int32Policy), default(Int32Policy), Reverse);
                Result[2] = Graph.MemoryStats.LivePayloadBytes; Result[3] = r.Position; Result[4] = 1234;
            }
            [BurstDiscard] private static void MarkManaged(ref int value) => value = 1;
        }
        [TestCase(GraphDirection.Directed, 0)] [TestCase(GraphDirection.Directed, 1)] [TestCase(GraphDirection.Directed, 2)]
        [TestCase(GraphDirection.Undirected, 0)] [TestCase(GraphDirection.Undirected, 1)] [TestCase(GraphDirection.Undirected, 2)]
        public void BurstDeltaCopiesOnlyChangedPagesAndReversePreservesCanonicalState(GraphDirection direction, int change)
        {
            var options = new GraphOptions(new BufferOptions(32, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 2, pageBytes: 128),
                new BufferOptions(64, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 2, pageBytes: 128), direction);
            using var source = new DeterministicGraph<int, int>(options);
            using var graph = new NativeDeterministicGraph<int, int>(options, Allocator.Persistent);
            var a = source.AddNode(1); var b = source.AddNode(2); var edge = source.AddEdge(a, b, 3);
            var na = graph.AddNode(1); var nb = graph.AddNode(2); graph.AddEdge(na, nb, 3); Assert.AreEqual(a, na); Assert.AreEqual(b, nb);
            source.TryMeasureCanonical(default(Int32Policy), default(Int32Policy), out int basisSize).ThrowIfFailure();
            var basis = new byte[basisSize]; var transfer = new byte[16384]; var workspace = new byte[262144]; int length;
            fixed (byte* bp = basis) fixed (byte* tp = transfer) fixed (byte* wp = workspace)
            {
                var w = new DeterministicWriter(bp, basis.Length); source.TryWriteCanonical(ref w, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
                if (change == 1) source.TrySetNode(a, 99).ThrowIfFailure(); if (change == 2) source.TrySetEdge(edge, 99).ThrowIfFailure();
                w = new DeterministicWriter(tp, transfer.Length); var s = new CanonicalScratch(wp, workspace.Length);
                source.TryWriteDelta(new GraphCheckpoint(bp, basis.Length), ref w, ref s, default(Int32Policy), default(Int32Policy), out _, GraphDeltaMode.Reversible).ThrowIfFailure(); length = w.Position;
            }
            System.Array.Resize(ref transfer, length);
            using var packet = new NativeArray<byte>(transfer, Allocator.Persistent); using var scratch = new NativeArray<byte>(workspace.Length, Allocator.Persistent);
            using var result = new NativeArray<long>(5, Allocator.Persistent);
            graph.TryCapture(out var root).ThrowIfFailure();
            try
            {
                long before = graph.MemoryStats.LivePayloadBytes;
                new DeltaJob { Graph = graph, Packet = packet, Scratch = scratch, Result = result }.Schedule().Complete();
                Assert.AreEqual(0, result[0]); Assert.AreEqual(0, result[1]); Assert.AreEqual(length, result[3]); Assert.AreEqual(1234, result[4]);
                int size = change == 1 ? sizeof(SlotMapNode<GraphNodeRecord<int>>) : sizeof(SlotMapNode<GraphEdgeRecord<int>>);
                Assert.AreEqual(change == 0 ? 0 : (128 / size) * size, result[2] - before);
                root.TryGetNode(a, out int old).ThrowIfFailure(); Assert.AreEqual(1, old); root.TryGetEdge(edge, out var oldEdge).ThrowIfFailure(); Assert.AreEqual(3, oldEdge.Data);
                graph.TryGetNode(a, out int value).ThrowIfFailure(); Assert.AreEqual(change == 1 ? 99 : 1, value);
                graph.TryGetEdge(edge, out var currentEdge).ThrowIfFailure(); Assert.AreEqual(change == 2 ? 99 : 3, currentEdge.Data);
                new DeltaJob { Graph = graph, Packet = packet, Scratch = scratch, Result = result, Reverse = true }.Schedule().Complete();
                Assert.AreEqual(0, result[0]); Assert.AreEqual(0, result[1]); Assert.AreEqual(1234, result[4]);
                var after = new byte[basis.Length]; fixed (byte* p = after)
                { var w = new DeterministicWriter(p, after.Length); graph.TryWriteCanonical(ref w, default(Int32Policy), default(Int32Policy)).ThrowIfFailure(); }
                CollectionAssert.AreEqual(basis, after);
            }
            finally { root.Dispose(); }
        }
    }
}
