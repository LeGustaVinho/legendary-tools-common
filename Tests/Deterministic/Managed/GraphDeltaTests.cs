using System;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class GraphDeltaTests
    {
        private static byte[] Encode(DeterministicGraph<int, int> graph)
        {
            graph.TryMeasureCanonical(default(Int32Policy), default(Int32Policy), out int length).ThrowIfFailure(); var bytes = new byte[length];
            fixed (byte* p = bytes) { var w = new DeterministicWriter(p, length); graph.TryWriteCanonical(ref w, default(Int32Policy), default(Int32Policy)).ThrowIfFailure(); } return bytes;
        }
        private static void Import(DeterministicGraph<int, int> graph, byte[] bytes)
        {
            var workBytes = new byte[131072]; fixed (byte* p = bytes) fixed (byte* w = workBytes)
            { var r = new DeterministicReader(p, bytes.Length); var scratch = new CanonicalScratch(w, workBytes.Length); graph.TryReadCanonical(ref r, ref scratch, default(Int32Policy), default(Int32Policy)).ThrowIfFailure(); }
        }
        private static byte[] Delta(DeterministicGraph<int, int> target, byte[] source, GraphDeltaMode mode, out GraphTransferKind kind)
        {
            var output = new byte[65536]; var workspace = new byte[262144]; int length;
            fixed (byte* p = source) fixed (byte* o = output) fixed (byte* w = workspace)
            { var writer = new DeterministicWriter(o, output.Length); var scratch = new CanonicalScratch(w, workspace.Length); target.TryWriteDelta(new GraphCheckpoint(p, source.Length), ref writer, ref scratch, default(Int32Policy), default(Int32Policy), out kind, mode).ThrowIfFailure(); length = writer.Position; }
            Array.Resize(ref output, length); return output;
        }
        private static DeterministicStatus Apply(DeterministicGraph<int, int> graph, byte[] delta, bool reverse = false)
        {
            var workspace = new byte[262144]; fixed (byte* d = delta) fixed (byte* w = workspace)
            { var reader = new DeterministicReader(d, delta.Length); var scratch = new CanonicalScratch(w, workspace.Length); return graph.TryApplyDelta(ref reader, ref scratch, default(Int32Policy), default(Int32Policy), reverse); }
        }
        [TestCase(GraphDeltaMode.Forward)]
        [TestCase(GraphDeltaMode.Reversible)]
        public void SparseUpdatesUsePatchAndReversibleModeRestoresExactSource(GraphDeltaMode mode)
        {
            var options = new GraphOptions(100, 200); using var source = new DeterministicGraph<int, int>(options); using var target = new DeterministicGraph<int, int>(options); using var replica = new DeterministicGraph<int, int>(options);
            GraphNodeHandle first = default, previous = default;
            for (int i = 0; i < 80; i++) { var node = source.AddNode(i); if (i == 0) first = node; else source.AddEdge(previous, node, i); previous = node; }
            var original = Encode(source); Import(target, original); Import(replica, original); target.TrySetNode(first, 1000).ThrowIfFailure();
            var delta = Delta(target, original, mode, out var kind); Assert.AreEqual(GraphTransferKind.Patch, kind); Assert.Less(delta.Length, original.Length);
            Apply(replica, delta).ThrowIfFailure(); CollectionAssert.AreEqual(Encode(target), Encode(replica)); Assert.AreEqual(DeterministicStatus.ProtocolConflict, Apply(replica, delta));
            if (mode == GraphDeltaMode.Reversible) { Apply(replica, delta, true).ThrowIfFailure(); CollectionAssert.AreEqual(original, Encode(replica)); }
            else Assert.AreEqual(DeterministicStatus.InvalidConfiguration, Apply(replica, delta, true));
        }
        [Test]
        public void StructuralDeltaPreservesFreeQueuesGenerationsAndFutureIds()
        {
            var options = new GraphOptions(40, 80, allowParallelEdges: true, allowSelfEdges: true);
            using var source = new DeterministicGraph<int, int>(options); using var target = new DeterministicGraph<int, int>(options); using var replica = new DeterministicGraph<int, int>(options);
            var nodes = new GraphNodeHandle[30]; for (int i = 0; i < nodes.Length; i++) nodes[i] = source.AddNode(i);
            for (int i = 1; i < nodes.Length; i++) source.AddEdge(nodes[0], nodes[i], i);
            var original = Encode(source); Import(target, original); Import(replica, original);
            target.TryRemoveNode(nodes[5]).ThrowIfFailure(); target.TryRemoveNode(nodes[2]).ThrowIfFailure(); var added = target.AddNode(200); target.AddEdge(added, added, 42);
            var delta = Delta(target, original, GraphDeltaMode.Reversible, out _); Apply(replica, delta).ThrowIfFailure(); CollectionAssert.AreEqual(Encode(target), Encode(replica));
            var sourceFuture = target.AddNode(300); var replicaFuture = replica.AddNode(300); Assert.AreEqual(sourceFuture, replicaFuture);
            Assert.AreEqual(target.AddEdge(sourceFuture, nodes[0], 7), replica.AddEdge(replicaFuture, nodes[0], 7));
            // Restore the transfer destination before undoing; later operations have a different base hash.
            Import(replica, Encode(target)); Assert.AreEqual(DeterministicStatus.ProtocolConflict, Apply(replica, delta, true));
            using var originalDestination = new DeterministicGraph<int, int>(options); Import(originalDestination, original); Apply(originalDestination, delta).ThrowIfFailure(); Apply(originalDestination, delta, true).ThrowIfFailure();
            CollectionAssert.AreEqual(original, Encode(originalDestination)); Assert.AreEqual(source.AddNode(99), originalDestination.AddNode(99));
        }
        [Test]
        public void DenseChangesChooseFullCheckpointAndCanStillBeUndone()
        {
            var options = new GraphOptions(100, 4); using var source = new DeterministicGraph<int, int>(options); using var target = new DeterministicGraph<int, int>(options); using var replica = new DeterministicGraph<int, int>(options);
            for (int i = 0; i < 100; i++) source.AddNode(i); var original = Encode(source); Import(target, original); Import(replica, original);
            foreach (var item in source.Nodes) target.TrySetNode(item.Handle, item.Data + 100).ThrowIfFailure();
            var delta = Delta(target, original, GraphDeltaMode.Reversible, out var kind); Assert.AreEqual(GraphTransferKind.Checkpoint, kind);
            Apply(replica, delta).ThrowIfFailure(); CollectionAssert.AreEqual(Encode(target), Encode(replica)); Apply(replica, delta, true).ThrowIfFailure(); CollectionAssert.AreEqual(original, Encode(replica));
        }
        [Test]
        public void TruncationWrongHashAndInsufficientScratchDoNotPublish()
        {
            var options = new GraphOptions(12, 4); using var source = new DeterministicGraph<int, int>(options); using var target = new DeterministicGraph<int, int>(options); using var replica = new DeterministicGraph<int, int>(options);
            var first = source.AddNode(1); for (int i = 0; i < 10; i++) source.AddNode(i); var original = Encode(source); Import(target, original); Import(replica, original); target.TrySetNode(first, 88).ThrowIfFailure();
            var delta = Delta(target, original, GraphDeltaMode.Reversible, out _); var workspace = new byte[131072]; var borrowed = replica.AsReadOnly();
            fixed (byte* p = delta) fixed (byte* w = workspace)
            {
                for (int i = 0; i < delta.Length; i++)
                { var r = new DeterministicReader(p, i); var work = new CanonicalScratch(w, workspace.Length); Assert.AreNotEqual(DeterministicStatus.None, replica.TryApplyDelta(ref r, ref work, default(Int32Policy), default(Int32Policy))); Assert.AreEqual(0, r.Position); Assert.AreEqual(0, work.Position); }
                var reader = new DeterministicReader(p, delta.Length); var scratch = new CanonicalScratch(w, 4);
                Assert.AreEqual(DeterministicStatus.BufferTooSmall, replica.TryApplyDelta(ref reader, ref scratch, default(Int32Policy), default(Int32Policy)));
                delta[8] ^= 1; reader = new DeterministicReader(p, delta.Length); scratch = new CanonicalScratch(w, workspace.Length);
                Assert.AreEqual(DeterministicStatus.ProtocolConflict, replica.TryApplyDelta(ref reader, ref scratch, default(Int32Policy), default(Int32Policy))); delta[8] ^= 1;
            }
            Assert.AreEqual(DeterministicStatus.None, borrowed.Check()); CollectionAssert.AreEqual(original, Encode(replica));
        }
    }
}
