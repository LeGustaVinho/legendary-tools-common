using System;
using System.Diagnostics;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class GraphPerformanceTests
    {
        [Test]
        public void CowSharesPagesAndOnlyCopiesThePagesWritten()
        {
            var options = new GraphOptions(new BufferOptions(64, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 2, pageBytes: 64),
                new BufferOptions(64, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 2, pageBytes: 64));
            using var graph = new DeterministicGraph<int, int>(options); var first = graph.AddNode(1);
            var before = graph.MemoryStats; graph.TryCapture(out var root).ThrowIfFailure(); var captured = graph.MemoryStats;
            Assert.AreEqual(before.LivePages, captured.LivePages); Assert.Greater(captured.SharedLivePages, 0); Assert.AreEqual(3, captured.BufferSnapshotLeases);
            graph.TrySetNode(first, 2).ThrowIfFailure(); var changed = graph.MemoryStats;
            Assert.AreEqual(captured.LivePages + 1, changed.LivePages); Assert.AreEqual(before.ReservedPayloadBytes, changed.ReservedPayloadBytes);
            root.TryGetNode(first, out int old).ThrowIfFailure(); Assert.AreEqual(1, old); root.Dispose(); Assert.AreEqual(before.LivePages, graph.MemoryStats.LivePages);
        }
        [Test]
        public void FixedMutationsSerializationAndPersistentBranchesHaveNoSteadyGc()
        {
            var options = new GraphOptions(new BufferOptions(8, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 2, pageBytes: 64), new BufferOptions(16, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 2, pageBytes: 64));
            using var graph = new DeterministicGraph<int, int>(options); var a = graph.AddNode(1); var b = graph.AddNode(2);
            using var store = new PersistentDeterministicGraph<int, int>(new GraphOptions(4, 4), new GraphPersistenceOptions(4));
            store.TryCaptureCurrent(out var empty).ThrowIfFailure(); store.TryWithAddNode(empty, 1, out var basis, out var node).ThrowIfFailure(); empty.Dispose();
            var bytes = new byte[8192]; fixed (byte* p = bytes)
            {
                for (int i = 0; i < 16; i++) Exercise(graph, store, basis, a, b, node, i, p, bytes.Length);
                var stopwatch = new Stopwatch(); long start = GC.GetAllocatedBytesForCurrentThread(); stopwatch.Start();
                for (int i = 0; i < 1000; i++) Exercise(graph, store, basis, a, b, node, i, p, bytes.Length);
                stopwatch.Stop(); long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
                Assert.AreEqual(0, allocated);
                TestContext.Progress.WriteLine("Graph fixed Managed: " + stopwatch.Elapsed.TotalMilliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " ms/1000 iterations; reserved payload " + graph.MemoryStats.ReservedPayloadBytes + " bytes; GC " + allocated);
            }
            basis.Dispose();
        }
        private static void Exercise(DeterministicGraph<int, int> graph, PersistentDeterministicGraph<int, int> store,
            ManagedGraphVersion<int, int> basis, GraphNodeHandle a, GraphNodeHandle b, GraphNodeHandle node, int value, byte* bytes, int length)
        {
            graph.TryCapture(out var checkpoint).ThrowIfFailure(); graph.TrySetNode(a, value).ThrowIfFailure(); var edge = graph.AddEdge(a, b, value);
            var writer = new DeterministicWriter(bytes, length); graph.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
            graph.TryRemoveEdge(edge).ThrowIfFailure(); graph.TryRestore(checkpoint).ThrowIfFailure(); checkpoint.Dispose();
            store.TryWithSetNode(basis, node, value, out var version).ThrowIfFailure(); version.Dispose();
        }
        [Test]
        public void DeepGraphAlgorithmsUseExplicitWorkspaceAndFrozenStorageIsCompact()
        {
            const int count = 4096; var options = new GraphOptions(new BufferOptions(count, snapshotSlots: 0), new BufferOptions(count, snapshotSlots: 0), cycles: GraphCyclePolicy.Acyclic);
            using var graph = new DeterministicGraph<int, int>(options); GraphNodeHandle first = default, previous = default;
            for (int i = 0; i < count; i++) { var node = graph.AddNode(i); if (i == 0) first = node; else graph.AddEdge(previous, node, i); previous = node; }
            using var frozen = new FrozenDeterministicGraph<int, int>(options); graph.TryFreeze(frozen).ThrowIfFailure(); Assert.Less(frozen.MemoryStats.ReservedPayloadBytes, graph.MemoryStats.ReservedPayloadBytes);
            GraphScratch.TryMeasure(count, count, out int size).ThrowIfFailure(); var bytes = new byte[size]; var results = new GraphNodeHandle[count];
            fixed (byte* p = bytes) fixed (GraphNodeHandle* r = results)
            {
                var scratch = new CanonicalScratch(p, size); var output = new GraphWorkBuffer<GraphNodeHandle>(r, count);
                GraphAlgorithms<int, int>.TryDepthFirst(frozen.AsReadOnly(), first, ref scratch, ref output, out int n).ThrowIfFailure(); Assert.AreEqual(count, n);
                scratch.Position = 0; GraphAlgorithms<int, int>.TryHasCycle(frozen.AsReadOnly(), ref scratch, out bool cycle).ThrowIfFailure(); Assert.IsFalse(cycle);
                scratch.Position = 0; GraphAlgorithms<int, int>.TryTopologicalOrder(frozen.AsReadOnly(), ref scratch, ref output, out n).ThrowIfFailure(); Assert.AreEqual(count, n); Assert.AreEqual(previous, results[count - 1]);
            }
        }
    }
}
