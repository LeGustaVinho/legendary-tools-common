using System;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class GraphAdditionalValidationTests
    {
        private static byte[] Encode(DeterministicGraph<int, int> graph)
        {
            graph.TryMeasureCanonical(default(Int32Policy), default(Int32Policy), out int length).ThrowIfFailure(); var bytes = new byte[length];
            fixed (byte* p = bytes) { var writer = new DeterministicWriter(p, length); graph.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)).ThrowIfFailure(); } return bytes;
        }
        [Test]
        public void GrowDeltaCanBeReversedAndPublishedAsAPersistentBranch()
        {
            var options = new GraphOptions(new BufferOptions(0, 16, AllocationPolicy.Grow), new BufferOptions(0, 32, AllocationPolicy.Grow));
            using var source = new DeterministicGraph<int, int>(options); using var replica = new DeterministicGraph<int, int>(options);
            using var persistent = new PersistentDeterministicGraph<int, int>(options, new GraphPersistenceOptions(4)); persistent.TryCaptureCurrent(out var empty).ThrowIfFailure();
            var original = Encode(source); var nodes = new GraphNodeHandle[12];
            for (int i = 0; i < nodes.Length; i++) { nodes[i] = source.AddNode(i); if (i != 0) source.AddEdge(nodes[i - 1], nodes[i], i); }
            var target = Encode(source); var delta = new byte[8192]; var scratchBytes = new byte[131072];
            fixed (byte* b = original) fixed (byte* d = delta) fixed (byte* w = scratchBytes)
            {
                var writer = new DeterministicWriter(d, delta.Length); var scratch = new CanonicalScratch(w, scratchBytes.Length);
                source.TryWriteDelta(new GraphCheckpoint(b, original.Length), ref writer, ref scratch, default(Int32Policy), default(Int32Policy), out _, GraphDeltaMode.Reversible).ThrowIfFailure();
                var reader = new DeterministicReader(d, writer.Position); scratch.Position = 0;
                replica.TryApplyDelta(ref reader, ref scratch, default(Int32Policy), default(Int32Policy)).ThrowIfFailure(); CollectionAssert.AreEqual(target, Encode(replica));
                reader.Position = 0; scratch.Position = 0;
                persistent.TryWithDelta(empty, ref reader, ref scratch, default(Int32Policy), default(Int32Policy), out var version).ThrowIfFailure();
                Assert.AreEqual(0, persistent.Current.NodeCount); Assert.AreEqual(nodes.Length, version.AsReadOnly().NodeCount);
                persistent.TrySelectCurrent(version).ThrowIfFailure();
                reader.Position = 0; scratch.Position = 0; persistent.TryApplyDelta(ref reader, ref scratch, default(Int32Policy), default(Int32Policy), true).ThrowIfFailure();
                Assert.AreEqual(0, persistent.Current.NodeCount); Assert.AreEqual(nodes.Length, version.AsReadOnly().NodeCount);
                reader.Position = 0; scratch.Position = 0; replica.TryApplyDelta(ref reader, ref scratch, default(Int32Policy), default(Int32Policy), true).ThrowIfFailure();
                CollectionAssert.AreEqual(original, Encode(replica)); Assert.AreEqual(0, replica.NodeCapacity); Assert.AreEqual(0, replica.EdgeCapacity); version.Dispose();
            }
            empty.Dispose();
        }
        [Test]
        public void CanonicalImportRejectsDanglingEndpointsAndCyclesWithoutPublishing()
        {
            var options = new GraphOptions(3, 3, cycles: GraphCyclePolicy.Acyclic); using var destination = new DeterministicGraph<int, int>(options); destination.AddNode(99); var before = Encode(destination);
            var nodeSlots = new SlotMapNode<GraphNodeRecord<int>>[3]; var edgeSlots = new SlotMapNode<GraphEdgeRecord<int>>[3];
            for (int i = 0; i < 3; i++) nodeSlots[i] = new SlotMapNode<GraphNodeRecord<int>> { Active = 1, Generation = 1, Value = new GraphNodeRecord<int> { Data = i } };
            var a = new GraphNodeHandle(new StableHandle(0, 1)); var b = new GraphNodeHandle(new StableHandle(1, 1));
            edgeSlots[0] = new SlotMapNode<GraphEdgeRecord<int>> { Active = 1, Generation = 1, Value = new GraphEdgeRecord<int> { Source = a, Target = new GraphNodeHandle(new StableHandle(1, 2)), Data = 1 } };
            var bytes = new byte[8192]; var scratchBytes = new byte[16384];
            fixed (SlotMapNode<GraphNodeRecord<int>>* n = nodeSlots) fixed (SlotMapNode<GraphEdgeRecord<int>>* e = edgeSlots) fixed (byte* p = bytes) fixed (byte* w = scratchBytes)
            {
                var nb = new GraphWorkBuffer<SlotMapNode<GraphNodeRecord<int>>>(n, 3); var eb = new GraphWorkBuffer<SlotMapNode<GraphEdgeRecord<int>>>(e, 3);
                for (int pass = 0; pass < 2; pass++)
                {
                    var writer = new DeterministicWriter(p, bytes.Length);
                    GraphWire<int, int, Int32Policy, Int32Policy>.TryWrite(ref writer, ref nb, ref eb, new SlotMapState { Count = 3, Used = 3 }, new SlotMapState { Count = pass == 0 ? 1 : 2, Used = pass == 0 ? 1 : 2 }, options, default, default).ThrowIfFailure();
                    var reader = new DeterministicReader(p, writer.Position); var scratch = new CanonicalScratch(w, scratchBytes.Length); var view = destination.AsReadOnly();
                    Assert.AreEqual(DeterministicStatus.InvalidEncoding, destination.TryReadCanonical(ref reader, ref scratch, default(Int32Policy), default(Int32Policy)));
                    Assert.AreEqual(0, reader.Position); Assert.AreEqual(DeterministicStatus.None, view.Check()); CollectionAssert.AreEqual(before, Encode(destination));
                    edgeSlots[0].Value.Target = b; edgeSlots[1] = new SlotMapNode<GraphEdgeRecord<int>> { Active = 1, Generation = 1, Value = new GraphEdgeRecord<int> { Source = b, Target = a, Data = 2 } };
                }
            }
        }
        [Test]
        public void GraphOwnerMismatchAndFixedVersionExhaustionKeepExistingRootsValid()
        {
            using var a = new PersistentDeterministicGraph<int, int>(new GraphOptions(3, 3), new GraphPersistenceOptions(1));
            using var b = new PersistentDeterministicGraph<int, int>(new GraphOptions(3, 3), new GraphPersistenceOptions(1));
            a.TryCaptureCurrent(out var av).ThrowIfFailure(); b.TryCaptureCurrent(out var bv).ThrowIfFailure();
            Assert.AreEqual(DeterministicStatus.InvalidOwner, b.TrySelectCurrent(av)); Assert.AreEqual(DeterministicStatus.InvalidOwner, b.TryWithAddNode(av, 1, out _, out _));
            Assert.AreEqual(DeterministicStatus.SnapshotCapacityExceeded, a.TryWithAddNode(av, 1, out _, out _)); Assert.IsTrue(av.IsCreated); Assert.IsTrue(bv.IsCreated);
            av.Dispose(); bv.Dispose();
        }
    }
}
