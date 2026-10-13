using System;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe partial class GraphHotpathOptimizationValidationTests
    {
        private static byte[] Encode<TNode, TCodec>(DeterministicGraph<TNode, int> graph, TCodec codec)
            where TNode : unmanaged where TCodec : unmanaged, IDeterministicCodec<TNode>
        {
            graph.TryMeasureCanonical(codec, default(Int32Policy), out int count).ThrowIfFailure(); var result = new byte[count];
            fixed (byte* p = result) { var w = new DeterministicWriter(p, count); graph.TryWriteCanonical(ref w, codec, default(Int32Policy)).ThrowIfFailure(); }
            return result;
        }
        private static byte[] Delta<TNode, TCodec>(DeterministicGraph<TNode, int> source, byte[] basis, TCodec codec)
            where TNode : unmanaged where TCodec : unmanaged, IDeterministicCodec<TNode>
        {
            var packet = new byte[16384]; var memory = new byte[262144]; int length;
            fixed (byte* b = basis) fixed (byte* p = packet) fixed (byte* m = memory)
            {
                var w = new DeterministicWriter(p, packet.Length); var s = new CanonicalScratch(m, memory.Length);
                source.TryWriteDelta(new GraphCheckpoint(b, basis.Length), ref w, ref s, codec, default(Int32Policy), out _, GraphDeltaMode.Reversible).ThrowIfFailure(); length = w.Position;
            }
            Array.Resize(ref packet, length); return packet;
        }
        private static void Import<TNode, TCodec>(DeterministicGraph<TNode, int> graph, byte[] bytes, TCodec codec)
            where TNode : unmanaged where TCodec : unmanaged, IDeterministicCodec<TNode>
        {
            var memory = new byte[262144]; fixed (byte* b = bytes) fixed (byte* m = memory)
            { var r = new DeterministicReader(b, bytes.Length); var s = new CanonicalScratch(m, memory.Length); graph.TryReadCanonical(ref r, ref s, codec, default(Int32Policy)).ThrowIfFailure(); }
        }
        private static void Apply<TNode, TCodec>(DeterministicGraph<TNode, int> graph, byte[] bytes, TCodec codec, bool reverse = false)
            where TNode : unmanaged where TCodec : unmanaged, IDeterministicCodec<TNode>
        {
            var memory = new byte[262144]; fixed (byte* b = bytes) fixed (byte* m = memory)
            { var r = new DeterministicReader(b, bytes.Length); var s = new CanonicalScratch(m, memory.Length); graph.TryApplyDelta(ref r, ref s, codec, default(Int32Policy), reverse).ThrowIfFailure(); Assert.AreEqual(bytes.Length, r.Position); }
        }
        [TestCase(false, 64)] [TestCase(false, 4096)] [TestCase(true, 64)] [TestCase(true, 4096)]
        public void SparseClearMatchesScalarSlotRemovalAndCopiesOnlyTouchedPages(bool scattered, int pageBytes)
        {
            const int extent = 8192, active = 128;
            var buffer = new BufferOptions(extent, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 1, pageBytes: pageBytes);
            using var graph = new DeterministicGraph<int, int>(new GraphOptions(buffer, buffer, allowParallelEdges: true));
            var all = new GraphNodeHandle[extent]; var kept = new GraphNodeHandle[active];
            for (int i = 0; i < extent; i++) all[i] = graph.AddNode(i);
            int k = 0; for (int i = 0; i < extent; i++)
                if (scattered ? i % 64 == 63 : i >= extent - active) kept[k++] = all[i]; else graph.TryRemoveNode(all[i]).ThrowIfFailure();
            var edges = new GraphEdgeHandle[extent]; k = 0;
            for (int i = 0; i < extent; i++) edges[i] = graph.AddEdge(kept[i % active], kept[(i + 1) % active], i);
            for (int i = 0; i < extent; i++) if (!(scattered ? i % 64 == 63 : i >= extent - active)) graph.TryRemoveEdge(edges[i]).ThrowIfFailure();
            var encoded = Encode(graph, default(Int32Policy)); var memory = new byte[4194304]; var expected = new byte[encoded.Length];
            fixed (byte* ep = encoded) fixed (byte* mp = memory) fixed (byte* outp = expected)
            {
                var nodes = new ImportBuffer<SlotMapNode<GraphNodeRecord<int>>> { Items = new SlotMapNode<GraphNodeRecord<int>>[extent], Capacity = extent };
                var es = new ImportBuffer<SlotMapNode<GraphEdgeRecord<int>>> { Items = new SlotMapNode<GraphEdgeRecord<int>>[extent], Capacity = extent };
                var reader = new DeterministicReader(ep, encoded.Length); var scratch = new CanonicalScratch(mp, memory.Length);
                GraphWire<int, int, Int32Policy, Int32Policy>.TryReadStaged(ref reader, ref scratch, ref nodes, ref es, graph.Options, default, default, out var stage).ThrowIfFailure();
                var n = new GraphWorkBuffer<SlotMapNode<GraphNodeRecord<int>>>(stage.Nodes.Items, extent);
                var e = new GraphWorkBuffer<SlotMapNode<GraphEdgeRecord<int>>>(stage.Edges.Items, extent);
                var ns = stage.Nodes.State; var state = stage.Edges.State;
                // Independent scalar oracle: same ascending removals and FIFO updates as the old clear.
                for (int i = 0; i < ns.Used; i++) if (n.Read(i).Active != 0) SlotMapKernel<GraphNodeRecord<int>, GraphWorkBuffer<SlotMapNode<GraphNodeRecord<int>>>>.TryRemove(ref n, ref ns, new StableHandle((uint)i, n.Read(i).Generation)).ThrowIfFailure();
                for (int i = 0; i < state.Used; i++) if (e.Read(i).Active != 0) SlotMapKernel<GraphEdgeRecord<int>, GraphWorkBuffer<SlotMapNode<GraphEdgeRecord<int>>>>.TryRemove(ref e, ref state, new StableHandle((uint)i, e.Read(i).Generation)).ThrowIfFailure();
                var writer = new DeterministicWriter(outp, expected.Length);
                GraphWire<int, int, Int32Policy, Int32Policy>.TryWrite(ref writer, ref n, ref e, ns, state, graph.Options, default, default).ThrowIfFailure();
                Array.Resize(ref expected, writer.Position);
            }
            graph.TryCapture(out var root).ThrowIfFailure();
            try
            {
                long before = graph.MemoryStats.LivePayloadBytes; graph.TryClear().ThrowIfFailure();
                CollectionAssert.AreEqual(expected, Encode(graph, default(Int32Policy)));
                root.TryGetNode(kept[0], out int old).ThrowIfFailure(); Assert.AreEqual((int)kept[0].Index, old);
                Assert.AreEqual(active, root.NodeCount); Assert.AreEqual(active, root.EdgeCount);
                if (!scattered) Assert.Less(graph.MemoryStats.LivePayloadBytes - before, 45000);
                graph.TryRestore(root).ThrowIfFailure(); CollectionAssert.AreEqual(encoded, Encode(graph, default(Int32Policy)));
            }
            finally { root.Dispose(); }
        }
        [TestCase(false, 32)] [TestCase(false, 127)] [TestCase(true, 32)] [TestCase(true, 127)]
        public void FrozenLookupHintHandlesGapsAndReusedGenerations(bool irregular, int count)
        {
            int extent = count * 8; var options = new GraphOptions(extent, extent);
            using var graph = new DeterministicGraph<int, int>(options); var nodes = new GraphNodeHandle[extent];
            for (int i = 0; i < extent; i++) nodes[i] = graph.AddNode(i);
            for (int i = 0; i < extent; i++) if (irregular ? i % 13 != 0 && i < extent - count / 2 : i % 8 != 7) graph.TryRemoveNode(nodes[i]).ThrowIfFailure();
            var reused = graph.AddNode(-777); nodes[reused.Index] = reused;
            GraphNodeHandle previous = default; foreach (var node in graph.Nodes) { if (previous.IsValid) graph.AddEdge(previous, node.Handle, node.Data); previous = node.Handle; }
            var enumerator = graph.Edges; enumerator.MoveNext(); var firstEdge = enumerator.Current.Handle;
            graph.TryGetEdge(firstEdge, out var oldEdge).ThrowIfFailure(); graph.TryRemoveEdge(firstEdge).ThrowIfFailure(); var newEdge = graph.AddEdge(oldEdge.Source, oldEdge.Target, 123);
            using var frozen = new FrozenDeterministicGraph<int, int>(options, graph.NodeCount, graph.EdgeCount); graph.TryFreeze(frozen).ThrowIfFailure();
            var reader = frozen.AsReadOnly();
            for (int i = 0; i <= extent; i++) for (uint generation = 0; generation <= 3; generation++)
            {
                var h = new GraphNodeHandle(new StableHandle((uint)i, generation));
                var a = graph.TryGetNode(h, out int expected); var b = reader.TryGetNode(h, out int actual); Assert.AreEqual(a, b); if (a.IsSuccess()) Assert.AreEqual(expected, actual);
                var edge = new GraphEdgeHandle(new StableHandle((uint)i, generation));
                a = graph.TryGetEdge(edge, out var ee); b = reader.TryGetEdge(edge, out var ae); Assert.AreEqual(a, b);
                if (a.IsSuccess()) { Assert.AreEqual(ee.Source, ae.Source); Assert.AreEqual(ee.Target, ae.Target); Assert.AreEqual(ee.Data, ae.Data); }
            }
            Assert.AreEqual(DeterministicStatus.NotFound, reader.TryGetNode(new GraphNodeHandle(new StableHandle(uint.MaxValue, 1)), out _));
            Assert.AreEqual(2u, newEdge.Generation);
        }
        [TestCase(GraphDirection.Directed, GraphTraversal.Outgoing)] [TestCase(GraphDirection.Directed, GraphTraversal.Incoming)]
        [TestCase(GraphDirection.Undirected, GraphTraversal.Outgoing)] [TestCase(GraphDirection.Undirected, GraphTraversal.Incoming)]
        public void DirectDfsStackPreservesBranchOrderAndAtomicOutput(GraphDirection direction, GraphTraversal traversal)
        {
            var options = new GraphOptions(new BufferOptions(6, snapshotSlots: 0), new BufferOptions(6, snapshotSlots: 0), direction);
            using var graph = new DeterministicGraph<int, int>(options); var nodes = new GraphNodeHandle[6]; for (int i = 0; i < 6; i++) nodes[i] = graph.AddNode(i);
            graph.AddEdge(nodes[0], nodes[3], 0); graph.AddEdge(nodes[0], nodes[1], 0); graph.AddEdge(nodes[3], nodes[2], 0);
            graph.AddEdge(nodes[3], nodes[5], 0); graph.AddEdge(nodes[2], nodes[4], 0); graph.AddEdge(nodes[5], nodes[0], 0);
            int[] expected = direction == GraphDirection.Directed && traversal == GraphTraversal.Incoming ? new[] { 0, 5, 3 } : new[] { 0, 3, 2, 4, 5, 1 };
            GraphNodeHandle* result = stackalloc GraphNodeHandle[6]; byte* memory = stackalloc byte[8192]; var scratch = new CanonicalScratch(memory, 8192);
            var output = new GraphWorkBuffer<GraphNodeHandle>(result, 6); GraphAlgorithms<int, int>.TryDepthFirst(graph.AsReadOnly(), nodes[0], ref scratch, ref output, out int count, traversal).ThrowIfFailure();
            Assert.AreEqual(expected.Length, count); for (int i = 0; i < count; i++) Assert.AreEqual(nodes[expected[i]], result[i]);
            result[0] = new GraphNodeHandle(new StableHandle(99, 99)); output = new GraphWorkBuffer<GraphNodeHandle>(result, 1); scratch = new CanonicalScratch(memory, 8192);
            Assert.AreEqual(DeterministicStatus.BufferTooSmall, GraphAlgorithms<int, int>.TryDepthFirst(graph.AsReadOnly(), nodes[0], ref scratch, ref output, out count, traversal));
            Assert.AreEqual(0, count); Assert.AreEqual(0, scratch.Position); Assert.AreEqual(99u, result[0].Index);
        }
        [TestCase(GraphDirection.Directed, SnapshotPolicy.FullPerTick)] [TestCase(GraphDirection.Directed, SnapshotPolicy.CopyOnWrite)]
        [TestCase(GraphDirection.Undirected, SnapshotPolicy.FullPerTick)] [TestCase(GraphDirection.Undirected, SnapshotPolicy.CopyOnWrite)]
        public void GrowingDeltaPreservesRootAndReverseCheckpoint(GraphDirection direction, SnapshotPolicy policy)
        {
            var buffer = new BufferOptions(4, 8, AllocationPolicy.Grow, policy, 2, 64); var options = new GraphOptions(buffer, buffer, direction);
            using var source = new DeterministicGraph<int, int>(options); using var replica = new DeterministicGraph<int, int>(options);
            var nodes = new GraphNodeHandle[4]; for (int i = 0; i < 4; i++) nodes[i] = source.AddNode(i);
            for (int i = 0; i < 4; i++) source.AddEdge(nodes[i], nodes[(i + 1) % 4], i);
            var basis = Encode(source, default(Int32Policy)); Import(replica, basis, default(Int32Policy)); replica.TryCapture(out var root).ThrowIfFailure();
            try
            {
                var next = source.AddNode(9); source.AddEdge(nodes[3], next, 9); var packet = Delta(source, basis, default(Int32Policy));
                long before = replica.MemoryStats.LivePayloadBytes; Apply(replica, packet, default(Int32Policy));
                CollectionAssert.AreEqual(Encode(source, default(Int32Policy)), Encode(replica, default(Int32Policy)));
                Assert.AreEqual(8, replica.NodeCapacity); Assert.AreEqual(8, replica.EdgeCapacity); Assert.AreEqual(4, root.NodeCount);
                root.TryGetNode(nodes[3], out int data).ThrowIfFailure(); Assert.AreEqual(3, data);
                if (policy == SnapshotPolicy.CopyOnWrite) Assert.Less(replica.MemoryStats.LivePayloadBytes - before, 960);
                Apply(replica, packet, default(Int32Policy), true); CollectionAssert.AreEqual(basis, Encode(replica, default(Int32Policy)));
            }
            finally { root.Dispose(); }
        }
        private struct ImportBuffer<T> : IDeterministicImportBuffer<T> where T : unmanaged
        {
            public T[] Items; public bool Fail, ReplaceOnly; public int Writes, Prepares;
            public int Capacity { get; set; }
            public ulong Revision { get; private set; }
            public bool IsCreated => Items != null;
            public T Read(int index) => Items[index];
            public void Write(int index, in T value) { Items[index] = value; Writes++; }
            public void CommitWrite() { Revision++; }
            public DeterministicStatus CanPrepareWrite(int required, int start, int count)
                => Fail ? DeterministicStatus.CapacityExceeded : ReplaceOnly && required > Capacity ? DeterministicStatus.BufferTooSmall
                    : required > Items.Length || (long)start + count > required ? DeterministicStatus.BufferTooSmall : DeterministicStatus.None;
            public DeterministicStatus PrepareWrite(int required, int start, int count) { Prepares++; Capacity = Math.Max(Capacity, required); return DeterministicStatus.None; }
            public DeterministicStatus CanReplaceCapacity(int capacity) => capacity > Items.Length ? DeterministicStatus.BufferTooSmall : DeterministicStatus.None;
            public DeterministicStatus PrepareReplace(int capacity) { Array.Clear(Items, 0, Items.Length); Capacity = capacity; return DeterministicStatus.None; }
        }
        [TestCase(-1, false)] [TestCase(0, false)] [TestCase(1, false)] [TestCase(2, false)] [TestCase(-1, true)]
        public void CapacityOnlyGrowthPreflightsAndSupportsReplaceOnlyBuffers(int failure, bool replaceOnly)
        {
            var buffer = new BufferOptions(4, 8, AllocationPolicy.Grow, snapshotSlots: 0); var options = new GraphOptions(buffer, buffer);
            var n = new ImportBuffer<SlotMapNode<GraphNodeRecord<int>>> { Items = new SlotMapNode<GraphNodeRecord<int>>[8], Capacity = 8 };
            var e = new ImportBuffer<SlotMapNode<GraphEdgeRecord<int>>> { Items = new SlotMapNode<GraphEdgeRecord<int>>[8], Capacity = 8 };
            for (int i = 0; i < 3; i++) n.Items[i] = new SlotMapNode<GraphNodeRecord<int>> { Active = 1, Generation = 1, Value = new GraphNodeRecord<int> { Data = i } };
            var ns = new SlotMapState { Count = 3, Used = 3 }; var es = default(SlotMapState);
            byte* encoded = stackalloc byte[8192]; byte* memory = stackalloc byte[32768]; var writer = new DeterministicWriter(encoded, 8192);
            GraphWire<int, int, Int32Policy, Int32Policy>.TryWrite(ref writer, ref n, ref e, ns, es, options, default, default).ThrowIfFailure();
            var targetN = new ImportBuffer<SlotMapNode<GraphNodeRecord<int>>> { Items = (SlotMapNode<GraphNodeRecord<int>>[])n.Items.Clone(), Capacity = 4, Fail = failure == 0, ReplaceOnly = replaceOnly };
            var targetE = new ImportBuffer<SlotMapNode<GraphEdgeRecord<int>>> { Items = new SlotMapNode<GraphEdgeRecord<int>>[8], Capacity = 4, Fail = failure == 1, ReplaceOnly = replaceOnly };
            var targetA = new ImportBuffer<GraphIncidence> { Items = new GraphIncidence[16], Capacity = 8, Fail = failure == 2, ReplaceOnly = replaceOnly };
            var reader = new DeterministicReader(encoded, writer.Position); var scratch = new CanonicalScratch(memory, 32768);
            GraphWire<int, int, Int32Policy, Int32Policy>.TryReadStaged(ref reader, ref scratch, ref targetN, ref targetE, options, default, default, out var stage).ThrowIfFailure();
            int before = scratch.Position;
            Assert.AreEqual(failure < 0 ? DeterministicStatus.None : DeterministicStatus.CapacityExceeded,
                GraphPublicationKernel<int, int>.TryPublish(stage, ref targetN, ref targetE, ref targetA, ref ns, ref es, ref scratch));
            if (failure >= 0)
            { Assert.AreEqual(before, scratch.Position); Assert.AreEqual(0, targetN.Prepares + targetE.Prepares + targetA.Prepares + targetN.Writes + targetE.Writes + targetA.Writes); Assert.AreEqual(4, targetN.Capacity); }
            else
            { Assert.AreEqual(8, targetN.Capacity); Assert.AreEqual(8, targetE.Capacity); Assert.AreEqual(16, targetA.Capacity); if (!replaceOnly) Assert.AreEqual(0, targetN.Writes + targetE.Writes + targetA.Writes); }
            for (int i = 0; i < 3; i++) Assert.AreEqual(i, targetN.Items[i].Value.Data);
        }
    }
}
