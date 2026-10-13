using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    [Category("DeterministicExtendedOptimization")]
    public sealed unsafe class NativeExtendedOptimizationTests
    {
        [BurstCompile(CompileSynchronously = true)]
        private struct MutationJob : IJob
        {
            internal NativeDeterministicSlotMap<int> Slots;
            internal NativeDeterministicMultiMap<int, int, Int32Policy> Map;
            internal NativeDeterministicGraph<int, int> Graph;
            internal NativeDeterministicDictionary<int, int, Int32Policy> Dictionary;
            internal GraphNodeHandle Hub, A, B;
            [ReadOnly] internal NativeArray<byte> Packet;
            internal NativeArray<byte> Scratch;
            internal NativeArray<int> Result;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed;
                var status = Slots.TryClear(); if (!status.IsSuccess()) { Result[1] = (int)status; return; }
                status = Slots.TryAdd(900, out var slot); if (!status.IsSuccess()) { Result[1] = (int)status; return; }
                Result[2] = (int)slot.Index; Result[3] = (int)slot.Generation;
                status = Map.TryRemoveAll(0); if (!status.IsSuccess()) { Result[1] = (int)status; return; }
                status = Map.TryAdd(0, 900, out var pair); if (!status.IsSuccess()) { Result[1] = (int)status; return; }
                Result[4] = (int)pair.Index; Result[5] = (int)pair.Generation;
                status = Graph.TryRemoveNode(Hub); if (!status.IsSuccess()) { Result[1] = (int)status; return; }
                status = Graph.TryAddEdge(A, B, 900, out var edge); if (!status.IsSuccess()) { Result[1] = (int)status; return; }
                Result[6] = (int)edge.Index; Result[7] = (int)edge.Generation;
                var reader = new DeterministicReader((byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(Packet), Packet.Length);
                var scratch = new CanonicalScratch((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Scratch), Scratch.Length);
                status = Dictionary.TryReadCanonical(ref reader, ref scratch, default(Int32Policy), default(Int32Policy));
                Result[1] = (int)status; Result[8] = reader.Position; Result[9] = 12345;
            }
            [BurstDiscard] private static void MarkManaged(ref int value) => value = 1;
        }
        [Test]
        public void OptimizedMutationsAndImportRunInBurstAndPreserveAllRetainedRoots()
        {
            var options = new BufferOptions(8192, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 1, pageBytes: 256);
            using var slots = new NativeDeterministicSlotMap<int>(options, Allocator.Persistent);
            using var map = new NativeDeterministicMultiMap<int, int, Int32Policy>(options, options, Allocator.Persistent);
            var graphOptions = new GraphOptions(new BufferOptions(66, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 1, pageBytes: 256),
                new BufferOptions(65, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 1, pageBytes: 256));
            using var graph = new NativeDeterministicGraph<int, int>(graphOptions, Allocator.Persistent);
            using var dictionary = new NativeDeterministicDictionary<int, int, Int32Policy>(new BufferOptions(257, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 1), Allocator.Persistent);
            var sh = new StableHandle[8192]; var mh = new StableHandle[48]; var nodes = new GraphNodeHandle[66];
            for (int i = 0; i < sh.Length; i++) sh[i] = slots.Add(i);
            for (int i = 0; i < sh.Length; i++) if (i % 128 != 0) slots.TryRemove(sh[i]).ThrowIfFailure();
            for (int i = 0; i < mh.Length; i++) mh[i] = map.Add(i % 3, i / 3);
            for (int i = 0; i < nodes.Length; i++) nodes[i] = graph.AddNode(i);
            for (int i = 1; i < nodes.Length; i++) graph.AddEdge(nodes[0], nodes[i], i);
            for (int i = 0; i < 71; i++) dictionary.Add(i, -i - 1);
            using var source = new DeterministicDictionary<int, int, Int32Policy>(257); for (int i = 0; i < 71; i++) source.Add(i, i + 7);
            var measure = DeterministicWriter.CreateMeasure(); source.TryWriteCanonical(ref measure, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
            using var packet = new NativeArray<byte>(measure.Position, Allocator.Persistent); using var scratch = new NativeArray<byte>(32768, Allocator.Persistent); using var result = new NativeArray<int>(10, Allocator.Persistent);
            var writer = new DeterministicWriter((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(packet), packet.Length); source.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
            slots.TryCapture(out var sr).ThrowIfFailure(); map.TryCapture(out var mr).ThrowIfFailure(); graph.TryCapture(out var gr).ThrowIfFailure(); dictionary.TryCapture(out var dr).ThrowIfFailure();
            try
            {
                new MutationJob { Slots = slots, Map = map, Graph = graph, Dictionary = dictionary, Hub = nodes[0], A = nodes[1], B = nodes[2], Packet = packet, Scratch = scratch, Result = result }.Schedule().Complete();
                Assert.IsTrue(BurstCompiler.IsEnabled); Assert.AreEqual(0, result[0]); Assert.AreEqual(0, result[1]); Assert.AreEqual(12345, result[9]); Assert.AreEqual(packet.Length, result[8]);
                Assert.AreEqual(1, slots.Count); Assert.AreEqual(1, result[2]); Assert.AreEqual(2, result[3]);
                Assert.AreEqual(33, map.Count); Assert.AreEqual(0, result[4]); Assert.AreEqual(2, result[5]);
                Assert.AreEqual(65, graph.NodeCount); Assert.AreEqual(1, graph.EdgeCount); Assert.AreEqual(0, result[6]); Assert.AreEqual(2, result[7]);
                for (int i = 0; i < sh.Length; i += 128) { sr.TryGet(sh[i], out int value).ThrowIfFailure(); Assert.AreEqual(i, value); }
                for (int i = 0; i < mh.Length; i++) { mr.TryGetPair(mh[i], out var pair).ThrowIfFailure(); Assert.AreEqual(i % 3, pair.Key); Assert.AreEqual(i / 3, pair.Value); }
                Assert.AreEqual(66, gr.NodeCount); Assert.AreEqual(65, gr.EdgeCount); gr.TryGetNode(nodes[0], out int hub).ThrowIfFailure(); Assert.AreEqual(0, hub);
                for (int i = 0; i < 71; i++)
                { dictionary.TryGet(i, out int value).ThrowIfFailure(); Assert.AreEqual(i + 7, value); Assert.AreEqual(-i - 1, dr.Entries.ReadSnapshot(dr.EntrySlot, i).Value); }
                slots.TryRestore(sr).ThrowIfFailure(); map.TryRestore(mr).ThrowIfFailure(); graph.TryRestore(gr).ThrowIfFailure(); dictionary.TryRestore(dr).ThrowIfFailure();
                Assert.AreEqual(64, slots.Count); Assert.AreEqual(48, map.Count); Assert.AreEqual(65, graph.EdgeCount); dictionary.TryGet(0, out int restored).ThrowIfFailure(); Assert.AreEqual(-1, restored);
            }
            finally { sr.Dispose(); mr.Dispose(); gr.Dispose(); dr.Dispose(); }
        }
    }
}
