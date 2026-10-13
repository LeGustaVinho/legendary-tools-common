using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    // Shared with the real player harness. Raw padding is deliberately different in the two backends.
    public static unsafe class GraphBoundaryCorpus
    {
        [StructLayout(LayoutKind.Explicit, Size = 48)]
        public struct Payload
        {
            [FieldOffset(0)] public uint Flags;
            [FieldOffset(8)] public long Amount;
            [FieldOffset(24)] public byte Tag;
            [FieldOffset(32)] public ulong Mask;
        }
        public struct Codec : IDeterministicCodec<Payload>
        {
            public uint SchemaId => 0x47425032; public ushort Version => 7;
            public int Measure(in Payload value) { var writer = DeterministicWriter.CreateMeasure(); return TryWrite(ref writer, value).IsSuccess() ? writer.Position : -1; }
            public DeterministicStatus TryWrite(ref DeterministicWriter writer, in Payload value)
            {
                var w = writer; var s = w.TryUInt(value.Flags, 4); if (!s.IsSuccess()) return s;
                s = w.TryVarInt(value.Amount); if (!s.IsSuccess()) return s; s = w.TryUInt(value.Tag, 1); if (!s.IsSuccess()) return s;
                s = w.TryVarUInt(value.Mask); if (s.IsSuccess()) writer = w; return s;
            }
            public DeterministicStatus TryRead(ref DeterministicReader reader, out Payload value)
            {
                value = default; var r = reader; var s = r.TryUInt(4, out ulong flags); if (!s.IsSuccess()) return s;
                s = r.TryVarInt(out long amount); if (!s.IsSuccess()) return s; s = r.TryUInt(1, out ulong tag); if (!s.IsSuccess()) return s;
                s = r.TryVarUInt(out ulong mask); if (!s.IsSuccess()) return s;
                value = new Payload { Flags = (uint)flags, Amount = amount, Tag = (byte)tag, Mask = mask }; reader = r; return s;
            }
        }
        public struct Result { public ulong Hash; public long BytesCompared; public int Operations; }
        private const int Stride = 4096;
        private static Payload Value(int index, byte poison)
        {
            Payload value = default; byte* p = (byte*)&value; for (int i = 0; i < sizeof(Payload); i++) p[i] = poison;
            value.Flags = 0xf0f00000u + (uint)index; value.Amount = index % 2 == 0 ? long.MinValue + index : long.MaxValue - index;
            value.Tag = (byte)(index * 17); value.Mask = ulong.MaxValue - (uint)index; return value;
        }
        private static bool Equal(byte* a, byte* b, int length) { for (int i = 0; i < length; i++) if (a[i] != b[i]) return false; return true; }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static ulong ReaderHash<TReader>(TReader reader, out DeterministicStatus status)
            where TReader : struct, IDeterministicGraphReader<Payload, Payload>
        {
            ulong hash = 0; status = reader.Check(); if (!status.IsSuccess()) return 0;
            for (int i = 0; i < reader.NodeExtent; i++)
            {
                status = reader.TryNodeAt(i, out var node, out _); if (status == DeterministicStatus.NotFound) continue; if (!status.IsSuccess()) return 0;
                hash = DeterministicHash.Mix(hash ^ node.Value.Packed);
                for (int direction = 0; direction < 2; direction++)
                {
                    status = reader.TryFirstArc(node, (GraphTraversal)direction, out int cursor); if (!status.IsSuccess()) return 0; int traversed = 0;
                    while (cursor != 0)
                    {
                        if (++traversed > reader.EdgeCount * 2) { status = DeterministicStatus.InvalidEncoding; return 0; }
                        status = reader.TryReadArc(cursor, out var arc, out cursor); if (!status.IsSuccess()) return 0;
                        hash = DeterministicHash.Mix(hash ^ arc.Edge.Value.Packed); hash = DeterministicHash.Mix(hash ^ arc.Neighbor.Value.Packed ^ arc.Side);
                    }
                }
            }
            status = DeterministicStatus.None; return hash;
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct Job : IJob
        {
            public NativeDeterministicGraph<Payload, Payload> Graph, Replica;
            public NativeFrozenDeterministicGraph<Payload, Payload> Frozen;
            [ReadOnly] public NativeArray<byte> Initial;
            [ReadOnly] public NativeArray<GraphMutation<Payload, Payload>> Mutations;
            public NativeArray<byte> Bytes, Packet, FrozenBytes, Scratch, Temporary;
            public NativeArray<int> Statuses, Lengths, Control;
            public NativeArray<GraphMutationResult> Results;
            public NativeArray<ulong> ReaderHashes, Future;
            public int Alignment, InitialLength;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Control[0] = managed; Control[13] = -1;
                byte* initial = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(Initial) + Alignment;
                byte* memory = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Scratch) + Alignment;
                byte* bytes = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Bytes);
                byte* packet = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Packet) + Alignment;
                byte* temporary = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Temporary) + Alignment;
                var r = new DeterministicReader(initial, InitialLength); var scratch = new CanonicalScratch(memory, Scratch.Length - Alignment);
                Control[1] = (int)Graph.TryReadCanonical(ref r, ref scratch, default(Codec), default(Codec));
                Control[2] = (int)Graph.TryCapture(out var baseline); Control[3] = (int)Graph.TryCapture(out var second);
                Control[4] = (int)Graph.TryCapture(out var failed); Control[15] = failed.IsCreated ? 1 : 0;
                for (int i = 0; i < Mutations.Length; i++)
                {
                    Statuses[i] = (int)Graph.TryApply(Mutations[i], out var result); Results[i] = result;
                    var w = new DeterministicWriter(bytes + i * Stride, Stride);
                    var s = Graph.TryWriteCanonical(ref w, default(Codec), default(Codec)); Lengths[i] = w.Position;
                    ReaderHashes[i] = ReaderHash(Graph.AsReadOnly(), out var readStatus);
                    if (!s.IsSuccess() || !readStatus.IsSuccess()) { Control[13] = i; baseline.Dispose(); second.Dispose(); return; }
                }
                scratch.Position = 0; var writer = new DeterministicWriter(packet, Packet.Length - Alignment);
                Control[5] = (int)Graph.TryWriteDelta(new GraphCheckpoint(initial, InitialLength), ref writer, ref scratch, default(Codec), default(Codec), out var kind, GraphDeltaMode.Reversible);
                Control[12] = writer.Position; Control[14] = (int)kind;
                scratch.Position = 0; r = new DeterministicReader(initial, InitialLength);
                Control[6] = (int)Replica.TryReadCanonical(ref r, ref scratch, default(Codec), default(Codec));
                scratch.Position = 0; r = new DeterministicReader(packet, writer.Position);
                Control[7] = (int)Replica.TryApplyDelta(ref r, ref scratch, default(Codec), default(Codec));
                var comparison = new DeterministicWriter(temporary, Stride); Replica.TryWriteCanonical(ref comparison, default(Codec), default(Codec));
                if (comparison.Position != Lengths[Mutations.Length - 1] || !Equal(temporary, bytes + (Mutations.Length - 1) * Stride, comparison.Position))
                { Control[13] = Mutations.Length; baseline.Dispose(); second.Dispose(); return; }
                Control[8] = (int)Graph.TryFreeze(ref Frozen);
                var fw = new DeterministicWriter((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(FrozenBytes) + Alignment, FrozenBytes.Length - Alignment);
                Frozen.TryWriteCanonical(ref fw, default(Codec), default(Codec)); Control[16] = fw.Position;
                Graph.TrySetNode(new GraphNodeHandle(new StableHandle(3, 2)), Value(99, 0xcc));
                fw = new DeterministicWriter(temporary, Stride); Frozen.TryWriteCanonical(ref fw, default(Codec), default(Codec));
                if (fw.Position != Control[16] || !Equal(temporary, (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(FrozenBytes) + Alignment, fw.Position))
                { Control[13] = Mutations.Length + 1; baseline.Dispose(); second.Dispose(); return; }
                scratch.Position = 0; r = new DeterministicReader(packet, writer.Position);
                Control[9] = (int)Replica.TryApplyDelta(ref r, ref scratch, default(Codec), default(Codec), true);
                comparison = new DeterministicWriter(temporary, Stride); Replica.TryWriteCanonical(ref comparison, default(Codec), default(Codec));
                if (comparison.Position != InitialLength || !Equal(temporary, initial, InitialLength))
                { Control[13] = Mutations.Length + 2; baseline.Dispose(); second.Dispose(); return; }
                Replica.TryAddNode(Value(98, 0xcc), out var next); Future[0] = next.Value.Packed;
                Replica.TryAddEdge(next, new GraphNodeHandle(new StableHandle(1, 1)), Value(97, 0xcc), out var nextEdge); Future[1] = nextEdge.Value.Packed;
                Control[10] = (int)Graph.TryRestore(baseline);
                for (int i = 0; i < Mutations.Length; i++)
                {
                    var s = Graph.TryApply(Mutations[i], out var result); comparison = new DeterministicWriter(temporary, Stride);
                    var encode = Graph.TryWriteCanonical(ref comparison, default(Codec), default(Codec));
                    ulong readHash = ReaderHash(Graph.AsReadOnly(), out var readStatus);
                    if ((int)s != Statuses[i] || result.Node != Results[i].Node || result.Edge != Results[i].Edge ||
                        !encode.IsSuccess() || !readStatus.IsSuccess() || readHash != ReaderHashes[i] ||
                        comparison.Position != Lengths[i] || !Equal(temporary, bytes + i * Stride, comparison.Position))
                    { Control[13] = i; baseline.Dispose(); second.Dispose(); return; }
                }
                baseline.Dispose(); second.Dispose(); Control[11] = 1234;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        public static Result Run(GraphOptions options, int alignment)
        {
            Require(BurstCompiler.IsEnabled && alignment >= 0 && alignment < 8, "Invalid boundary corpus environment");
            var a = new GraphNodeHandle(new StableHandle(0, uint.MaxValue - 1)); var b = new GraphNodeHandle(new StableHandle(1, 1));
            var c = new GraphNodeHandle(new StableHandle(2, 3)); var lastA = new GraphNodeHandle(new StableHandle(0, uint.MaxValue));
            var d = new GraphNodeHandle(new StableHandle(3, 1)); var e0 = new GraphEdgeHandle(new StableHandle(0, uint.MaxValue - 1));
            var e1 = new GraphEdgeHandle(new StableHandle(1, uint.MaxValue)); var lastE0 = new GraphEdgeHandle(new StableHandle(0, uint.MaxValue));
            var e2 = new GraphEdgeHandle(new StableHandle(2, 1)); var laterE2 = new GraphEdgeHandle(new StableHandle(2, 2));
            int nc = options.Nodes.Allocation == AllocationPolicy.Fixed ? 8 : 4, ec = options.Edges.Allocation == AllocationPolicy.Fixed ? 16 : 4;
            var nodeSlots = new SlotMapNode<GraphNodeRecord<Payload>>[nc]; var edgeSlots = new SlotMapNode<GraphEdgeRecord<Payload>>[ec];
            nodeSlots[0] = new SlotMapNode<GraphNodeRecord<Payload>> { Active = 1, Generation = a.Generation, Value = new GraphNodeRecord<Payload> { Data = Value(0, 0x55) } };
            nodeSlots[1] = new SlotMapNode<GraphNodeRecord<Payload>> { Active = 1, Generation = 1, Value = new GraphNodeRecord<Payload> { Data = Value(1, 0x55) } };
            nodeSlots[2] = new SlotMapNode<GraphNodeRecord<Payload>> { Generation = 3 };
            edgeSlots[0] = new SlotMapNode<GraphEdgeRecord<Payload>> { Active = 1, Generation = e0.Generation, Value = new GraphEdgeRecord<Payload> { Source = a, Target = b, Data = Value(2, 0x55) } };
            edgeSlots[1] = new SlotMapNode<GraphEdgeRecord<Payload>> { Generation = uint.MaxValue };
            var initialBytes = new byte[4096 + 8]; int initialLength;
            fixed (SlotMapNode<GraphNodeRecord<Payload>>* n = nodeSlots) fixed (SlotMapNode<GraphEdgeRecord<Payload>>* e = edgeSlots) fixed (byte* p = initialBytes)
            {
                var nb = new GraphWorkBuffer<SlotMapNode<GraphNodeRecord<Payload>>>(n, nc); var eb = new GraphWorkBuffer<SlotMapNode<GraphEdgeRecord<Payload>>>(e, ec);
                var w = new DeterministicWriter(p + alignment, Stride);
                GraphWire<Payload, Payload, Codec, Codec>.TryWrite(ref w, ref nb, ref eb, new SlotMapState { Count = 2, Used = 3, FreeHead = 3, FreeTail = 3 },
                    new SlotMapState { Count = 1, Used = 2, FreeHead = 2, FreeTail = 2 }, options, default, default).ThrowIfFailure(); initialLength = w.Position;
            }
            var commands = new List<GraphMutation<Payload, Payload>>();
            commands.Add(new GraphMutation<Payload, Payload> { Kind = GraphMutationKind.RemoveEdge, Edge = e0 });
            commands.Add(new GraphMutation<Payload, Payload> { Kind = GraphMutationKind.AddEdge, Source = b, Target = a });
            commands.Add(new GraphMutation<Payload, Payload> { Kind = GraphMutationKind.RemoveEdge, Edge = e1 });
            commands.Add(new GraphMutation<Payload, Payload> { Kind = GraphMutationKind.AddEdge, Source = a, Target = b });
            commands.Add(new GraphMutation<Payload, Payload> { Kind = GraphMutationKind.RemoveNode, Node = a });
            commands.Add(new GraphMutation<Payload, Payload> { Kind = GraphMutationKind.AddNode });
            commands.Add(new GraphMutation<Payload, Payload> { Kind = GraphMutationKind.AddNode });
            commands.Add(new GraphMutation<Payload, Payload> { Kind = GraphMutationKind.AddEdge, Source = lastA, Target = b });
            commands.Add(new GraphMutation<Payload, Payload> { Kind = GraphMutationKind.RemoveNode, Node = lastA });
            commands.Add(new GraphMutation<Payload, Payload> { Kind = GraphMutationKind.AddNode });
            commands.Add(new GraphMutation<Payload, Payload> { Kind = GraphMutationKind.AddEdge, Source = d, Target = c });
            commands.Add(new GraphMutation<Payload, Payload> { Kind = GraphMutationKind.SetNode, Node = c });
            commands.Add(new GraphMutation<Payload, Payload> { Kind = GraphMutationKind.SetEdge, Edge = laterE2 });
            commands.Add(new GraphMutation<Payload, Payload> { Kind = GraphMutationKind.AddEdge, Source = d, Target = d });
            for (int i = 0; i < 6; i++) commands.Add(new GraphMutation<Payload, Payload> { Kind = GraphMutationKind.AddNode });
            commands.Add(new GraphMutation<Payload, Payload> { Kind = GraphMutationKind.SetNode, Node = lastA });
            commands.Add(new GraphMutation<Payload, Payload> { Kind = GraphMutationKind.RemoveEdge, Edge = lastE0 });
            commands.Add(new GraphMutation<Payload, Payload> { Kind = GraphMutationKind.SetEdge, Edge = e1 });
            commands.Add(new GraphMutation<Payload, Payload> { Kind = GraphMutationKind.Clear });
            commands.Add(new GraphMutation<Payload, Payload> { Kind = GraphMutationKind.AddNode });
            commands.Add(new GraphMutation<Payload, Payload> { Kind = GraphMutationKind.AddNode });
            var resetB = new GraphNodeHandle(new StableHandle(1, 2)); var resetC = new GraphNodeHandle(new StableHandle(2, 4));
            commands.Add(new GraphMutation<Payload, Payload> { Kind = GraphMutationKind.AddEdge, Source = resetB, Target = resetC });
            commands.Add(new GraphMutation<Payload, Payload> { Kind = GraphMutationKind.RemoveNode, Node = resetB });
            commands.Add(new GraphMutation<Payload, Payload> { Kind = GraphMutationKind.AddNode });
            commands.Add(new GraphMutation<Payload, Payload> { Kind = GraphMutationKind.AddEdge, Source = new GraphNodeHandle(new StableHandle(3, 2)), Target = resetC });
            int count = commands.Count; var nativeCommands = commands.ToArray();
            using var managed = new DeterministicGraph<Payload, Payload>(options); var memory = new byte[131072 + 8];
            var expectedBytes = new byte[count * Stride]; var statuses = new int[count]; var results = new GraphMutationResult[count]; var lengths = new int[count]; var hashes = new ulong[count];
            var expectedPacket = new byte[8192 + 8]; var expectedFrozen = new byte[4096 + 8]; int packetLength, frozenLength; GraphTransferKind transferKind;
            var report = new Result { Operations = count };
            fixed (byte* initial = initialBytes) fixed (byte* w = memory) fixed (byte* bytes = expectedBytes) fixed (byte* packet = expectedPacket) fixed (byte* frozenBytes = expectedFrozen)
            {
                var scratch = new CanonicalScratch(w + alignment, 131072); var reader = new DeterministicReader(initial + alignment, initialLength);
                managed.TryReadCanonical(ref reader, ref scratch, default(Codec), default(Codec)).ThrowIfFailure();
                for (int i = 0; i < count; i++)
                {
                    var operation = commands[i]; operation.NodeData = Value(i + 3, 0x55); operation.EdgeData = Value(i + 3, 0x55);
                    nativeCommands[i].NodeData = Value(i + 3, 0xaa); nativeCommands[i].EdgeData = Value(i + 3, 0xaa);
                    var status = managed.TryApply(operation, out results[i]); statuses[i] = (int)status;
                    var expected = i == 18 || i == 19 ? DeterministicStatus.CapacityExceeded : i >= 20 && i <= 22 ? DeterministicStatus.NotFound :
                        i == 13 && options.Cycles == GraphCyclePolicy.Acyclic ? DeterministicStatus.CycleDetected : DeterministicStatus.None;
                    Require(status == expected, $"Retirement specification mismatch at operation {i}: {status}");
                    var writer = new DeterministicWriter(bytes + i * Stride, Stride); managed.TryWriteCanonical(ref writer, default(Codec), default(Codec)).ThrowIfFailure();
                    lengths[i] = writer.Position; hashes[i] = ReaderHash(managed.AsReadOnly(), out var rs); rs.ThrowIfFailure();
                    report.Hash = DeterministicHash.Mix(report.Hash ^ DeterministicHash.Xxh64(writer.Buffer, writer.Position) ^ hashes[i]);
                    report.BytesCompared += (long)writer.Position * 2;
                }
                Require(results[1].Edge == e1 && results[3].Edge == lastE0 && results[5].Node == c && results[6].Node == lastA &&
                    results[7].Edge == e2 && results[9].Node == d && results[10].Edge == laterE2 && results[24].Node == resetB && results[25].Node == resetC,
                    "FIFO reuse or generation retirement did not follow the specified identities");
                scratch.Position = 0; var dw = new DeterministicWriter(packet + alignment, 8192);
                managed.TryWriteDelta(new GraphCheckpoint(initial + alignment, initialLength), ref dw, ref scratch, default(Codec), default(Codec), out transferKind, GraphDeltaMode.Reversible).ThrowIfFailure(); packetLength = dw.Position;
                using var frozen = new FrozenDeterministicGraph<Payload, Payload>(options); managed.TryFreeze(frozen).ThrowIfFailure();
                var fw = new DeterministicWriter(frozenBytes + alignment, Stride); frozen.TryWriteCanonical(ref fw, default(Codec), default(Codec)).ThrowIfFailure(); frozenLength = fw.Position;
            }
            using var graph = new NativeDeterministicGraph<Payload, Payload>(options, Allocator.Persistent); using var replica = new NativeDeterministicGraph<Payload, Payload>(options, Allocator.Persistent);
            using var frozenNative = new NativeFrozenDeterministicGraph<Payload, Payload>(options, Allocator.Persistent);
            using var initialNative = new NativeArray<byte>(initialBytes, Allocator.Persistent); using var mutations = new NativeArray<GraphMutation<Payload, Payload>>(nativeCommands, Allocator.Persistent);
            using var bytesNative = new NativeArray<byte>(expectedBytes.Length, Allocator.Persistent); using var packetNative = new NativeArray<byte>(8192 + 8, Allocator.Persistent);
            using var frozenNativeBytes = new NativeArray<byte>(Stride + 8, Allocator.Persistent); using var scratchNative = new NativeArray<byte>(131072 + 8, Allocator.Persistent);
            using var temporary = new NativeArray<byte>(Stride + 8, Allocator.Persistent); using var statusesNative = new NativeArray<int>(count, Allocator.Persistent);
            using var resultsNative = new NativeArray<GraphMutationResult>(count, Allocator.Persistent); using var lengthsNative = new NativeArray<int>(count, Allocator.Persistent);
            using var hashesNative = new NativeArray<ulong>(count, Allocator.Persistent); using var future = new NativeArray<ulong>(2, Allocator.Persistent); using var control = new NativeArray<int>(17, Allocator.Persistent);
            var writable = control; for (int i = 0; i < writable.Length; i++) writable[i] = -999;
            new Job { Graph = graph, Replica = replica, Frozen = frozenNative, Initial = initialNative, Mutations = mutations, Bytes = bytesNative,
                Packet = packetNative, FrozenBytes = frozenNativeBytes, Scratch = scratchNative, Temporary = temporary,
                Statuses = statusesNative, Lengths = lengthsNative, Results = resultsNative, ReaderHashes = hashesNative, Future = future, Control = control, Alignment = alignment, InitialLength = initialLength }.Schedule().Complete();
            Require(control[0] == 0 && control[4] == (int)DeterministicStatus.SnapshotCapacityExceeded && control[11] == 1234 && control[13] == -1 && control[15] == 0, "Boundary Burst job did not complete");
            foreach (int i in new[] { 1, 2, 3, 5, 6, 7, 8, 9, 10 }) Require(control[i] == 0, $"Boundary job status {i}: {control[i]}");
            byte* actual = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(bytesNative);
            fixed (byte* expected = expectedBytes)
            for (int i = 0; i < count; i++) Require(statuses[i] == statusesNative[i] && results[i].Node == resultsNative[i].Node && results[i].Edge == resultsNative[i].Edge &&
                lengths[i] == lengthsNative[i] && hashes[i] == hashesNative[i] && Equal(expected + i * Stride, actual + i * Stride, lengths[i]), $"Boundary backend divergence at operation {i}");
            fixed (byte* p = expectedPacket) fixed (byte* f = expectedFrozen)
            {
                Require(packetLength == control[12] && (int)transferKind == control[14] && Equal(p + alignment, (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(packetNative) + alignment, packetLength), "Custom payload delta differs");
                Require(frozenLength == control[16] && Equal(f + alignment, (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(frozenNativeBytes) + alignment, frozenLength), "Custom payload CSR differs");
            }
            Require(future[0] == c.Value.Packed && future[1] == e1.Value.Packed, "Reverse delta lost retired-generation allocator history");
            report.BytesCompared += packetLength + frozenLength + initialLength; return report;
        }
    }
}
