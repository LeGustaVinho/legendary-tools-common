using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class GraphNetworkDeterminismValidationTests
    {
        private static byte[] Encode(DeterministicGraph<int, int> graph)
        {
            var bytes = new byte[8192]; int length;
            fixed (byte* p = bytes)
            { var writer = new DeterministicWriter(p, bytes.Length); graph.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)).ThrowIfFailure(); length = writer.Position; }
            Array.Resize(ref bytes, length); return bytes;
        }
        private static void Import(DeterministicGraph<int, int> graph, byte[] bytes)
        {
            var memory = new byte[262144];
            fixed (byte* b = bytes) fixed (byte* p = memory)
            { var reader = new DeterministicReader(b, bytes.Length); var scratch = new CanonicalScratch(p, memory.Length); graph.TryReadCanonical(ref reader, ref scratch, default(Int32Policy), default(Int32Policy)).ThrowIfFailure(); Assert.AreEqual(bytes.Length, reader.Position); }
        }
        private static byte[] Transfer(DeterministicGraph<int, int> graph, byte[] basis, GraphDeltaMode mode)
        {
            var bytes = new byte[16384]; var memory = new byte[262144]; int length;
            fixed (byte* b = basis) fixed (byte* p = bytes) fixed (byte* w = memory)
            {
                var writer = new DeterministicWriter(p, bytes.Length); var scratch = new CanonicalScratch(w, memory.Length);
                graph.TryWriteDelta(new GraphCheckpoint(b, basis.Length), ref writer, ref scratch, default(Int32Policy), default(Int32Policy), out _, mode).ThrowIfFailure();
                length = writer.Position;
            }
            Array.Resize(ref bytes, length); return bytes;
        }
        private static DeterministicStatus Apply(DeterministicGraph<int, int> graph, byte[] bytes, bool reverse = false)
        {
            var memory = new byte[262144];
            fixed (byte* b = bytes) fixed (byte* w = memory)
            {
                var reader = new DeterministicReader(b, bytes.Length); var scratch = new CanonicalScratch(w, memory.Length);
                var status = graph.TryApplyDelta(ref reader, ref scratch, default(Int32Policy), default(Int32Policy), reverse);
                Assert.AreEqual(status.IsSuccess() ? bytes.Length : 0, reader.Position);
                if (!status.IsSuccess()) Assert.AreEqual(0, scratch.Position);
                return status;
            }
        }
        private static uint Next(ref uint random)
        { random ^= random << 13; random ^= random >> 17; random ^= random << 5; return random; }
        private static List<GraphNodeHandle> Nodes(DeterministicGraph<int, int> graph)
        { var nodes = new List<GraphNodeHandle>(); foreach (var item in graph.Nodes) nodes.Add(item.Handle); return nodes; }
        private static List<GraphEdgeHandle> Edges(DeterministicGraph<int, int> graph)
        { var edges = new List<GraphEdgeHandle>(); foreach (var item in graph.Edges) edges.Add(item.Handle); return edges; }
        public static IEnumerable<TestCaseData> TransferProfiles()
        {
            foreach (GraphDirection direction in Enum.GetValues(typeof(GraphDirection)))
            foreach (GraphCyclePolicy cycles in Enum.GetValues(typeof(GraphCyclePolicy)))
            foreach (GraphDeltaMode mode in Enum.GetValues(typeof(GraphDeltaMode)))
                yield return new TestCaseData(direction, cycles, mode);
        }
        [TestCaseSource(nameof(TransferProfiles))]
        public void LongDeltaChainPreservesExactStatesAndCanRewindAllocatorHistory(GraphDirection direction, GraphCyclePolicy cycles, GraphDeltaMode mode)
        {
            var options = new GraphOptions(new BufferOptions(1, 16, AllocationPolicy.Grow, SnapshotPolicy.CopyOnWrite, 2, 64),
                new BufferOptions(0, 32, AllocationPolicy.Grow, SnapshotPolicy.CopyOnWrite, 2, 64), direction, cycles, true, true);
            using var source = new DeterministicGraph<int, int>(options); using var replica = new DeterministicGraph<int, int>(options);
            for (int i = 0; i < 5; i++) source.AddNode(i);
            var checkpoints = new List<byte[]> { Encode(source) }; var deltas = new List<byte[]>(); Import(replica, checkpoints[0]); uint random = 0x10203041;
            for (int epoch = 0; epoch < 12; epoch++)
            {
                for (int i = 0; i < 64; i++)
                {
                    var nodes = Nodes(source); var edges = Edges(source); uint choice = Next(ref random);
                    if (nodes.Count == 0) { source.TryAddNode(i, out _).ThrowIfFailure(); continue; }
                    var node = nodes[(int)(choice % (uint)nodes.Count)];
                    switch (choice % 6)
                    {
                        case 0:
                            var add = source.TryAddNode(unchecked((int)choice), out _);
                            Assert.That(add == DeterministicStatus.None || add == DeterministicStatus.CapacityExceeded); break;
                        case 1: source.TryRemoveNode(node).ThrowIfFailure(); break;
                        case 2:
                            var edge = source.TryAddEdge(node, nodes[(int)(Next(ref random) % (uint)nodes.Count)], unchecked((int)choice), out _);
                            Assert.That(edge == DeterministicStatus.None || edge == DeterministicStatus.CycleDetected || edge == DeterministicStatus.CapacityExceeded); break;
                        case 3: if (edges.Count != 0) source.TrySetEdge(edges[(int)(choice % (uint)edges.Count)], i).ThrowIfFailure(); break;
                        case 4: source.TrySetNode(node, i).ThrowIfFailure(); break;
                        case 5: if (edges.Count != 0) source.TryRemoveEdge(edges[(int)(choice % (uint)edges.Count)]).ThrowIfFailure(); break;
                    }
                }
                var live = Nodes(source); if (live.Count == 0) live.Add(source.AddNode(0));
                source.TrySetNode(live[0], 0x10203000 + epoch).ThrowIfFailure();
                var expected = Encode(source); var delta = Transfer(source, checkpoints[epoch], mode);
                Apply(replica, delta).ThrowIfFailure(); CollectionAssert.AreEqual(expected, Encode(replica));
                var borrowed = replica.AsReadOnly();
                Assert.AreEqual(DeterministicStatus.ProtocolConflict, Apply(replica, delta));
                Assert.AreEqual(DeterministicStatus.None, borrowed.Check()); CollectionAssert.AreEqual(expected, Encode(replica));
                deltas.Add(delta); checkpoints.Add(expected);
            }
            if (mode == GraphDeltaMode.Reversible)
            {
                var finalView = replica.AsReadOnly();
                Assert.AreEqual(DeterministicStatus.ProtocolConflict, Apply(replica, deltas[0], true));
                Assert.AreEqual(DeterministicStatus.None, finalView.Check());
                for (int epoch = deltas.Count - 1; epoch >= 0; epoch--)
                { Apply(replica, deltas[epoch], true).ThrowIfFailure(); CollectionAssert.AreEqual(checkpoints[epoch], Encode(replica)); }
                using var original = new DeterministicGraph<int, int>(options); Import(original, checkpoints[0]);
                var a = original.AddNode(77); var b = replica.AddNode(77); Assert.AreEqual(a, b);
                var endpoint = Nodes(original)[0]; Assert.AreEqual(original.AddEdge(a, endpoint, 88), replica.AddEdge(b, endpoint, 88));
                CollectionAssert.AreEqual(Encode(original), Encode(replica));
            }
            else Assert.AreEqual(DeterministicStatus.InvalidConfiguration, Apply(replica, deltas[0], true));
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)]
        public void UnalignedPacketsRejectEverySingleBitCorruptionWithoutPublishing(int alignment)
        {
            var options = new GraphOptions(16, 32, GraphDirection.Undirected, allowParallelEdges: true, allowSelfEdges: true);
            using var source = new DeterministicGraph<int, int>(options); using var target = new DeterministicGraph<int, int>(options); using var replica = new DeterministicGraph<int, int>(options);
            GraphNodeHandle first = default, previous = default;
            for (int i = 0; i < 12; i++) { var node = source.AddNode(i); if (i == 0) first = node; else source.AddEdge(previous, node, i); previous = node; }
            var before = Encode(source); Import(target, before);
            target.TrySetNode(first, int.MaxValue).ThrowIfFailure(); target.TrySetEdge(Edges(target)[0], int.MinValue).ThrowIfFailure();
            var expected = Encode(target); var packet = Transfer(target, before, GraphDeltaMode.Reversible);
            var input = new byte[before.Length + 8]; var encoded = new byte[packet.Length + 8]; var memory = new byte[262144 + 8];
            Buffer.BlockCopy(before, 0, input, alignment, before.Length); Buffer.BlockCopy(packet, 0, encoded, alignment, packet.Length);
            fixed (byte* b = input) fixed (byte* d = encoded) fixed (byte* w = memory)
            {
                var reader = new DeterministicReader(b + alignment, before.Length); var scratch = new CanonicalScratch(w + alignment, 262144);
                replica.TryReadCanonical(ref reader, ref scratch, default(Int32Policy), default(Int32Policy)).ThrowIfFailure(); CollectionAssert.AreEqual(before, Encode(replica));
                var borrowed = replica.AsReadOnly();
                for (int bit = 0; bit < packet.Length * 8; bit++)
                {
                    int index = alignment + bit / 8; byte mask = (byte)(1 << (bit % 8)); encoded[index] ^= mask;
                    reader = new DeterministicReader(d + alignment, packet.Length); scratch = new CanonicalScratch(w + alignment, 262144);
                    var status = replica.TryValidateDelta(ref reader, ref scratch, default(Int32Policy), default(Int32Policy));
                    Assert.AreNotEqual(DeterministicStatus.None, status, $"validation accepted bit {bit}");
                    Assert.AreEqual(0, reader.Position); Assert.AreEqual(0, scratch.Position);
                    reader = new DeterministicReader(d + alignment, packet.Length); scratch = new CanonicalScratch(w + alignment, 262144);
                    status = replica.TryApplyDelta(ref reader, ref scratch, default(Int32Policy), default(Int32Policy));
                    Assert.AreNotEqual(DeterministicStatus.None, status, $"application accepted bit {bit}");
                    Assert.AreEqual(0, reader.Position); Assert.AreEqual(0, scratch.Position);
                    Assert.AreEqual(DeterministicStatus.None, borrowed.Check()); encoded[index] ^= mask;
                }
                CollectionAssert.AreEqual(before, Encode(replica));
                reader = new DeterministicReader(d + alignment, packet.Length); scratch = new CanonicalScratch(w + alignment, 262144);
                replica.TryApplyDelta(ref reader, ref scratch, default(Int32Policy), default(Int32Policy)).ThrowIfFailure(); CollectionAssert.AreEqual(expected, Encode(replica));
                reader.Position = 0; scratch.Position = 0;
                replica.TryApplyDelta(ref reader, ref scratch, default(Int32Policy), default(Int32Policy), true).ThrowIfFailure(); CollectionAssert.AreEqual(before, Encode(replica));
            }
        }

        [StructLayout(LayoutKind.Explicit, Size = 32)]
        private struct PaddedPayload
        {
            [FieldOffset(0)] public byte Tag;
            [FieldOffset(8)] public long Amount;
            [FieldOffset(24)] public int Flags;
        }
        private struct PayloadCodec : IDeterministicCodec<PaddedPayload>
        {
            public uint SchemaId => 0x50414444; public ushort Version => 1;
            public int Measure(in PaddedPayload value) => 1 + CanonicalBinary.VarUIntBytes(unchecked(((ulong)value.Amount << 1) ^ (ulong)(value.Amount >> 63))) + default(Int32Policy).Measure(value.Flags);
            public DeterministicStatus TryWrite(ref DeterministicWriter writer, in PaddedPayload value)
            {
                var trial = writer; var s = trial.TryUInt(value.Tag, 1); if (!s.IsSuccess()) return s;
                s = trial.TryVarInt(value.Amount); if (!s.IsSuccess()) return s; s = default(Int32Policy).TryWrite(ref trial, value.Flags);
                if (s.IsSuccess()) writer = trial; return s;
            }
            public DeterministicStatus TryRead(ref DeterministicReader reader, out PaddedPayload value)
            {
                value = default; var trial = reader; var s = trial.TryUInt(1, out ulong tag); if (!s.IsSuccess()) return s;
                s = trial.TryVarInt(out long amount); if (!s.IsSuccess()) return s; s = default(Int32Policy).TryRead(ref trial, out int flags); if (!s.IsSuccess()) return s;
                value = new PaddedPayload { Tag = (byte)tag, Amount = amount, Flags = flags }; reader = trial; return s;
            }
        }
        [Test]
        public void DifferentPayloadPaddingProducesIdenticalCheckpointsAndDeltas()
        {
            PaddedPayload a = default, b = default; byte* pa = (byte*)&a; byte* pb = (byte*)&b;
            for (int i = 0; i < sizeof(PaddedPayload); i++) { pa[i] = 0xaa; pb[i] = 0x55; }
            a.Tag = b.Tag = 127; a.Amount = b.Amount = long.MinValue; a.Flags = b.Flags = int.MaxValue;
            var options = new GraphOptions(2, 2, allowSelfEdges: true); using var first = new DeterministicGraph<PaddedPayload, PaddedPayload>(options);
            using var second = new DeterministicGraph<PaddedPayload, PaddedPayload>(options);
            var n = first.AddNode(a); var m = second.AddNode(b); first.AddEdge(n, n, a); second.AddEdge(m, m, b);
            var bytesA = new byte[4096]; var bytesB = new byte[4096]; var deltaA = new byte[8192]; var deltaB = new byte[8192]; var workspace = new byte[65536];
            fixed (byte* p = bytesA) fixed (byte* q = bytesB) fixed (byte* d = deltaA) fixed (byte* e = deltaB) fixed (byte* w = workspace)
            {
                var aw = new DeterministicWriter(p, bytesA.Length); var bw = new DeterministicWriter(q, bytesB.Length);
                first.TryWriteCanonical(ref aw, default(PayloadCodec), default(PayloadCodec)).ThrowIfFailure(); second.TryWriteCanonical(ref bw, default(PayloadCodec), default(PayloadCodec)).ThrowIfFailure();
                Assert.AreEqual(aw.Position, bw.Position); CollectionAssert.AreEqual(new ArraySegment<byte>(bytesA, 0, aw.Position), new ArraySegment<byte>(bytesB, 0, bw.Position));
                a.Amount = b.Amount = long.MaxValue; first.TrySetNode(n, a).ThrowIfFailure(); second.TrySetNode(m, b).ThrowIfFailure();
                var scratch = new CanonicalScratch(w, workspace.Length); var ad = new DeterministicWriter(d, deltaA.Length); var bd = new DeterministicWriter(e, deltaB.Length);
                first.TryWriteDelta(new GraphCheckpoint(p, aw.Position), ref ad, ref scratch, default(PayloadCodec), default(PayloadCodec), out var ak, GraphDeltaMode.Reversible).ThrowIfFailure();
                scratch.Position = 0; second.TryWriteDelta(new GraphCheckpoint(q, bw.Position), ref bd, ref scratch, default(PayloadCodec), default(PayloadCodec), out var bk, GraphDeltaMode.Reversible).ThrowIfFailure();
                Assert.AreEqual(ak, bk); Assert.AreEqual(ad.Position, bd.Position); CollectionAssert.AreEqual(new ArraySegment<byte>(deltaA, 0, ad.Position), new ArraySegment<byte>(deltaB, 0, bd.Position));
            }
        }
    }
}
