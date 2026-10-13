using System;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class GraphOptimizationValidationTests
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
        [TestCase(GraphDirection.Directed)] [TestCase(GraphDirection.Undirected)]
        public void ShorterAdjacencyAndTailInsertionPreserveDuplicatesSelfEdgesAndReusedOrder(GraphDirection direction)
        {
            using var graph = new DeterministicGraph<int, int>(new GraphOptions(130, 260, direction, allowSelfEdges: true));
            var hub = graph.AddNode(0); var leaves = new GraphNodeHandle[128]; var edges = new GraphEdgeHandle[128];
            for (int i = 0; i < leaves.Length; i++) { leaves[i] = graph.AddNode(i + 1); edges[i] = graph.AddEdge(hub, leaves[i], i); }
            byte[] original = Encode(graph, default(Int32Policy)); var view = graph.AsReadOnly();
            Assert.AreEqual(DeterministicStatus.DuplicateKey, graph.TryAddEdge(hub, leaves[127], -1, out _));
            Assert.AreEqual(DeterministicStatus.None, view.Check()); CollectionAssert.AreEqual(original, Encode(graph, default(Int32Policy)));
            graph.TryRemoveEdge(edges[0]).ThrowIfFailure(); var reused = graph.AddEdge(hub, leaves[0], 777);
            Assert.AreEqual(edges[0].Index, reused.Index); Assert.AreEqual(2u, reused.Generation);
            var self = graph.AddEdge(hub, hub, 9); original = Encode(graph, default(Int32Policy));
            Assert.AreEqual(DeterministicStatus.DuplicateKey, graph.TryAddEdge(hub, hub, 99, out _));
            if (direction == GraphDirection.Undirected) Assert.AreEqual(DeterministicStatus.DuplicateKey, graph.TryAddEdge(leaves[127], hub, -1, out _));
            CollectionAssert.AreEqual(original, Encode(graph, default(Int32Policy)));
            var e = graph.Outgoing(hub); GraphEdgeHandle previous = default; byte side = 0; int seen = 0;
            while (e.MoveNext())
            {
                var a = e.Current;
                if (seen != 0) Assert.IsTrue(previous.CompareTo(a.Edge) < 0 || previous == a.Edge && side < a.Side);
                previous = a.Edge; side = a.Side; seen++;
            }
            Assert.AreEqual(128 + (direction == GraphDirection.Undirected ? 2 : 1), seen);
            graph.TryGetEdge(self, out var selfData).ThrowIfFailure(); Assert.AreEqual(hub, selfData.Source); Assert.AreEqual(hub, selfData.Target);
        }
        [TestCase(false)] [TestCase(true)]
        public void FrozenReleasesWorkspaceAfterBuildAndCanonicalImport(bool import)
        {
            var options = new GraphOptions(4096, 64); using var graph = new DeterministicGraph<int, int>(options);
            var a = graph.AddNode(1); var b = graph.AddNode(2); graph.AddEdge(a, b, 3);
            using var frozen = new FrozenDeterministicGraph<int, int>(options, 2, 1);
            GraphScratch.TryMeasureFrozen(options.Nodes.MaxCapacity, 2, out int scratchSize).ThrowIfFailure();
            long before = frozen.MemoryStats.ReservedPayloadBytes;
            if (!import) graph.TryFreeze(frozen).ThrowIfFailure();
            else
            {
                using var first = new FrozenDeterministicGraph<int, int>(options, 2, 1); graph.TryFreeze(first).ThrowIfFailure();
                first.TryMeasureCanonical(default(Int32Policy), default(Int32Policy), out int size).ThrowIfFailure();
                var bytes = new byte[size]; var memory = new byte[32768];
                fixed (byte* p = bytes) fixed (byte* m = memory)
                {
                    var w = new DeterministicWriter(p, size); first.TryWriteCanonical(ref w, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
                    var r = new DeterministicReader(p, size); var s = new CanonicalScratch(m, memory.Length);
                    frozen.TryReadCanonical(ref r, ref s, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
                }
            }
            Assert.AreEqual(scratchSize, before - frozen.MemoryStats.ReservedPayloadBytes);
            frozen.AsReadOnly().TryGetNode(a, out int value).ThrowIfFailure(); Assert.AreEqual(1, value);
            Assert.AreEqual(DeterministicStatus.InvalidConfiguration, frozen.TryBuild(graph.AsReadOnly()));
        }
        [Test] public void FailedFreezeKeepsWorkspaceForRetry()
        {
            var options = new GraphOptions(64, 8); using var graph = new DeterministicGraph<int, int>(options);
            graph.AddNode(1); var extra = graph.AddNode(2); using var frozen = new FrozenDeterministicGraph<int, int>(options, 1, 0);
            long before = frozen.MemoryStats.ReservedPayloadBytes;
            Assert.AreEqual(DeterministicStatus.CapacityExceeded, graph.TryFreeze(frozen)); Assert.AreEqual(before, frozen.MemoryStats.ReservedPayloadBytes);
            graph.TryRemoveNode(extra).ThrowIfFailure(); graph.TryFreeze(frozen).ThrowIfFailure(); Assert.Less(frozen.MemoryStats.ReservedPayloadBytes, before);
        }
        [TestCase(GraphDirection.Directed, 0)] [TestCase(GraphDirection.Directed, 1)] [TestCase(GraphDirection.Directed, 2)]
        [TestCase(GraphDirection.Undirected, 0)] [TestCase(GraphDirection.Undirected, 1)] [TestCase(GraphDirection.Undirected, 2)]
        public void PayloadAndEmptyDeltasOnlyCopyChangedPagesAndKeepSnapshot(GraphDirection direction, int change)
        {
            var options = new GraphOptions(new BufferOptions(32, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 2, pageBytes: 128),
                new BufferOptions(64, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 2, pageBytes: 128), direction);
            using var source = new DeterministicGraph<int, int>(options); using var replica = new DeterministicGraph<int, int>(options);
            var a = source.AddNode(1); var b = source.AddNode(2); var edge = source.AddEdge(a, b, 3);
            var basis = Encode(source, default(Int32Policy)); Import(replica, basis, default(Int32Policy));
            replica.TryCapture(out var root).ThrowIfFailure();
            try
            {
                long before = replica.MemoryStats.LivePayloadBytes;
                if (change == 1) source.TrySetNode(a, 99).ThrowIfFailure(); if (change == 2) source.TrySetEdge(edge, 99).ThrowIfFailure();
                var expected = Encode(source, default(Int32Policy)); var packet = Delta(source, basis, default(Int32Policy));
                var borrowed = replica.AsReadOnly(); Apply(replica, packet, default(Int32Policy));
                int size = change == 1 ? sizeof(SlotMapNode<GraphNodeRecord<int>>) : sizeof(SlotMapNode<GraphEdgeRecord<int>>);
                long growth = change == 0 ? 0 : (128 / size) * size;
                Assert.AreEqual(growth, replica.MemoryStats.LivePayloadBytes - before);
                Assert.AreEqual(DeterministicStatus.Invalidated, borrowed.Check()); CollectionAssert.AreEqual(expected, Encode(replica, default(Int32Policy)));
                root.TryGetNode(a, out int old).ThrowIfFailure(); Assert.AreEqual(1, old); root.TryGetEdge(edge, out var oldEdge).ThrowIfFailure(); Assert.AreEqual(3, oldEdge.Data);
                Apply(replica, packet, default(Int32Policy), true); CollectionAssert.AreEqual(basis, Encode(replica, default(Int32Policy)));
            }
            finally { root.Dispose(); }
        }
        private struct NormalizedPayload { public int Value, Local; }
        private readonly struct NormalizingCodec : IDeterministicCodec<NormalizedPayload>
        {
            public uint SchemaId => 0xf001;
            public ushort Version => 1;
            public int Measure(in NormalizedPayload p) => default(Int32Policy).Measure(p.Value);
            public DeterministicStatus TryWrite(ref DeterministicWriter w, in NormalizedPayload p) => default(Int32Policy).TryWrite(ref w, p.Value);
            public DeterministicStatus TryRead(ref DeterministicReader r, out NormalizedPayload p)
            { p = default; var s = default(Int32Policy).TryRead(ref r, out int v); if (s.IsSuccess()) p.Value = v; return s; }
        }
        [Test] public void IncrementalPublicationStillNormalizesUnchangedPayloads()
        {
            var options = new GraphOptions(8, 8); var codec = default(NormalizingCodec);
            using var source = new DeterministicGraph<NormalizedPayload, int>(options); using var replica = new DeterministicGraph<NormalizedPayload, int>(options);
            var a = source.AddNode(new NormalizedPayload { Value = 1, Local = 99 }); var b = source.AddNode(new NormalizedPayload { Value = 2, Local = 88 });
            var basis = Encode(source, codec); Import(replica, basis, codec);
            replica.TrySetNode(a, new NormalizedPayload { Value = 1, Local = 777 }).ThrowIfFailure();
            source.TrySetNode(b, new NormalizedPayload { Value = 3, Local = 444 }).ThrowIfFailure();
            Apply(replica, Delta(source, basis, codec), codec);
            replica.TryGetNode(a, out var current).ThrowIfFailure(); Assert.AreEqual(1, current.Value); Assert.AreEqual(0, current.Local);
            replica.TryGetNode(b, out current).ThrowIfFailure(); Assert.AreEqual(3, current.Value); Assert.AreEqual(0, current.Local);
        }
        [Test] public void TinyFixedCowPagesDoNotReserveUnusedPageTails()
        {
            var options = new GraphOptions(new BufferOptions(8, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 64),
                new BufferOptions(1, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 64));
            using var graph = new DeterministicGraph<int, int>(options); var a = graph.AddNode(1);
            long buffers = (long)(8 * sizeof(SlotMapNode<GraphNodeRecord<int>>) + sizeof(SlotMapNode<GraphEdgeRecord<int>>) + 2 * sizeof(GraphIncidence)) * 65;
            GraphScratch.TryMeasure(8, 1, out int workspace).ThrowIfFailure(); Assert.AreEqual(buffers + workspace, graph.MemoryStats.ReservedPayloadBytes);
            graph.TryCapture(out var root).ThrowIfFailure();
            try { graph.TrySetNode(a, 2).ThrowIfFailure(); root.TryGetNode(a, out int old).ThrowIfFailure(); Assert.AreEqual(1, old); }
            finally { root.Dispose(); }
        }
        private struct FaultBuffer<T> : IDeterministicImportBuffer<T> where T : unmanaged
        {
            public T[] Items;
            public bool Fail;
            public int Writes, Prepares;
            public int Capacity => Items.Length;
            public ulong Revision { get; private set; }
            public bool IsCreated => Items != null;
            public T Read(int index) => Items[index];
            public void Write(int index, in T value) { Items[index] = value; Writes++; }
            public void CommitWrite() { Revision++; }
            public DeterministicStatus CanPrepareWrite(int required, int start, int count) => Fail ? DeterministicStatus.CapacityExceeded : DeterministicStatus.None;
            public DeterministicStatus PrepareWrite(int required, int start, int count) { Prepares++; return DeterministicStatus.None; }
            public DeterministicStatus CanReplaceCapacity(int count) => DeterministicStatus.None;
            public DeterministicStatus PrepareReplace(int count) => DeterministicStatus.None;
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void PublicationPreflightsEveryBufferBeforeAnyPrepareOrWrite(int failure)
        {
            var a = new GraphNodeHandle(new StableHandle(0, 1)); var b = new GraphNodeHandle(new StableHandle(1, 1));
            var c = new GraphNodeHandle(new StableHandle(2, 1)); var edge = new GraphEdgeHandle(new StableHandle(0, 1));
            var nodes = new FaultBuffer<SlotMapNode<GraphNodeRecord<int>>> { Items = new[]
            {
                new SlotMapNode<GraphNodeRecord<int>> { Active = 1, Generation = 1, Value = new GraphNodeRecord<int> { Data = 1, OutHead = 1, OutTail = 1, OutDegree = 1 } },
                new SlotMapNode<GraphNodeRecord<int>> { Active = 1, Generation = 1, Value = new GraphNodeRecord<int> { Data = 2, InHead = 2, InTail = 2, InDegree = 1 } },
                new SlotMapNode<GraphNodeRecord<int>> { Active = 1, Generation = 1, Value = new GraphNodeRecord<int> { Data = 3 } }
            }, Fail = failure == 0 };
            var edges = new FaultBuffer<SlotMapNode<GraphEdgeRecord<int>>> { Items = new[]
            { new SlotMapNode<GraphEdgeRecord<int>> { Active = 1, Generation = 1, Value = new GraphEdgeRecord<int> { Source = a, Target = b, Data = 4 } } }, Fail = failure == 1 };
            var arcs = new FaultBuffer<GraphIncidence> { Items = new[]
            { new GraphIncidence { Edge = edge, Neighbor = b, Side = 0 }, new GraphIncidence { Edge = edge, Neighbor = a, Side = 1 } }, Fail = failure == 2 };
            var options = new GraphOptions(3, 1); using var desired = new DeterministicGraph<int, int>(options);
            var da = desired.AddNode(9); desired.AddNode(2); var dc = desired.AddNode(3); desired.AddEdge(da, dc, 4);
            var encoded = Encode(desired, default(Int32Policy));
            var ns = new SlotMapState { Count = 3, Used = 3 }; var es = new SlotMapState { Count = 1, Used = 1 }; var memory = new byte[16384];
            fixed (byte* ep = encoded) fixed (byte* mp = memory)
            {
                var scratch = new CanonicalScratch(mp, memory.Length);
                var reader = new DeterministicReader(ep, encoded.Length);
                GraphWire<int, int, Int32Policy, Int32Policy>.TryReadStaged(ref reader, ref scratch, ref nodes, ref edges, options, default, default, out var stage).ThrowIfFailure();
                int before = scratch.Position;
                Assert.AreEqual(DeterministicStatus.CapacityExceeded, GraphPublicationKernel<int, int>.TryPublish(stage, ref nodes, ref edges, ref arcs, ref ns, ref es, ref scratch));
                Assert.AreEqual(before, scratch.Position);
            }
            Assert.AreEqual(0, nodes.Prepares + edges.Prepares + arcs.Prepares); Assert.AreEqual(0, nodes.Writes + edges.Writes + arcs.Writes);
            Assert.AreEqual(0, nodes.Revision + edges.Revision + arcs.Revision);
            Assert.AreEqual(1, nodes.Items[0].Value.Data); Assert.AreEqual(b, edges.Items[0].Value.Target); Assert.AreEqual(b, arcs.Items[0].Neighbor);
        }
    }
}
