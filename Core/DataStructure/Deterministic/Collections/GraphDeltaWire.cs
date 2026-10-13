namespace LegendaryTools.Deterministic
{
    /// <summary>Borrowed canonical graph bytes. The owner must keep this region alive for the whole operation.</summary>
    public readonly unsafe struct GraphCheckpoint
    {
        public readonly byte* Buffer;
        public readonly int Length;
        public GraphCheckpoint(byte* buffer, int length) { Buffer = buffer; Length = length; }
        public bool IsValid => Length >= 0 && (Buffer != null || Length == 0);
    }
    internal readonly struct GraphImportTarget<T> : IDeterministicImportBuffer<T> where T : unmanaged
    {
        private readonly BufferOptions options;
        internal GraphImportTarget(BufferOptions options) { this.options = options; }
        public int Capacity => options.MaxCapacity;
        public ulong Revision => 0;
        public bool IsCreated => options.IsValid;
        public T Read(int index) => default;
        public void Write(int index, in T value) { }
        public void CommitWrite() { }
        public DeterministicStatus CanPrepareWrite(int required, int start, int count) => DeterministicStatus.InvalidConfiguration;
        public DeterministicStatus PrepareWrite(int required, int start, int count) => DeterministicStatus.InvalidConfiguration;
        public DeterministicStatus CanReplaceCapacity(int capacity)
        {
            var s = options.TryCapacity(options.InitialCapacity, capacity, out int normalized);
            return s.IsSuccess() && capacity >= options.InitialCapacity && capacity == normalized ? DeterministicStatus.None : DeterministicStatus.ProfileMismatch;
        }
        public DeterministicStatus PrepareReplace(int capacity) => DeterministicStatus.InvalidConfiguration;
    }
    /// <summary>Slot postimages plus allocator metadata. Reversible transfers also carry preimages.</summary>
    public static unsafe class GraphDeltaWire<TNode, TEdge, TNodeCodec, TEdgeCodec>
        where TNode : unmanaged where TEdge : unmanaged
        where TNodeCodec : unmanaged, IDeterministicCodec<TNode> where TEdgeCodec : unmanaged, IDeterministicCodec<TEdge>
    {
        public const uint Magic = 0x3144474c; // LGD1
        public const ushort Version = 1;
        public static DeterministicStatus TryMeasureScratch(in GraphOptions options, int sourceBytes, int targetBytes, out int bytes)
        {
            bytes = 0; if (sourceBytes < 0 || targetBytes < 0) return DeterministicStatus.InvalidConfiguration;
            var s = GraphWire<TNode, TEdge, TNodeCodec, TEdgeCodec>.TryMeasureScratch(options, out int stage); if (!s.IsSuccess()) return s;
            long publication = (((long)options.Nodes.MaxCapacity + 63) / 64 + ((long)options.Edges.MaxCapacity + 63) / 64 +
                ((long)options.Edges.MaxCapacity * 2 + 63) / 64) * sizeof(ulong) + 48;
            long size = (long)stage * 2 + sourceBytes + targetBytes + ((long)options.Nodes.MaxCapacity + options.Edges.MaxCapacity) * 4 + publication + 1024;
            if (size > int.MaxValue) return DeterministicStatus.CapacityExceeded; bytes = (int)size; return DeterministicStatus.None;
        }
        private static DeterministicStatus Decode(in GraphCheckpoint checkpoint, ref CanonicalScratch scratch, in GraphOptions options,
            TNodeCodec nc, TEdgeCodec ec, out GraphCanonicalStage<TNode, TEdge> stage)
        {
            stage = default; if (!checkpoint.IsValid) return DeterministicStatus.InvalidConfiguration;
            var n = new GraphImportTarget<SlotMapNode<GraphNodeRecord<TNode>>>(options.Nodes);
            var e = new GraphImportTarget<SlotMapNode<GraphEdgeRecord<TEdge>>>(options.Edges);
            var reader = new DeterministicReader(checkpoint.Buffer, checkpoint.Length);
            var s = GraphWire<TNode, TEdge, TNodeCodec, TEdgeCodec>.TryReadStaged(ref reader, ref scratch, ref n, ref e, options, nc, ec, out stage);
            if (s.IsSuccess() && reader.Position != checkpoint.Length) return DeterministicStatus.InvalidEncoding; return s;
        }
        private static DeterministicStatus Equal<T, TCodec>(bool hasA, in T a, bool hasB, in T b,
            TCodec codec, in CanonicalScratch scratch, out bool equal) where T : unmanaged where TCodec : unmanaged, IDeterministicCodec<T>
        {
            equal = false; if (hasA != hasB) return DeterministicStatus.None; if (!hasA) { equal = true; return DeterministicStatus.None; }
            var am = DeterministicWriter.CreateMeasure(); var bm = DeterministicWriter.CreateMeasure();
            var s = codec.TryWrite(ref am, a); if (!s.IsSuccess()) return s; s = codec.TryWrite(ref bm, b); if (!s.IsSuccess()) return s;
            if (am.Position != bm.Position) return DeterministicStatus.None;
            var work = scratch; s = work.TryReserve<byte>(am.Position, out var ap); if (!s.IsSuccess()) return s;
            s = work.TryReserve<byte>(bm.Position, out var bp); if (!s.IsSuccess()) return s;
            var aw = new DeterministicWriter(ap, am.Position); var bw = new DeterministicWriter(bp, bm.Position);
            s = codec.TryWrite(ref aw, a); if (!s.IsSuccess()) return s; s = codec.TryWrite(ref bw, b); if (!s.IsSuccess()) return s;
            equal = true; for (int i = 0; i < am.Position; i++) if (ap[i] != bp[i]) { equal = false; break; } return DeterministicStatus.None;
        }
        private static DeterministicStatus Changes<T, TCodec>(in CanonicalBufferStage<T, SlotMapState> a,
            in CanonicalBufferStage<T, SlotMapState> b, TCodec codec, ref CanonicalScratch scratch, out int* indices, out int count)
            where T : unmanaged where TCodec : unmanaged, IDeterministicCodec<T>
        {
            indices = null; count = 0; int used = a.State.Used > b.State.Used ? a.State.Used : b.State.Used;
            var s = scratch.TryReserve<int>(used, out indices); if (!s.IsSuccess()) return s;
            for (int i = 0; i < used; i++)
            {
                bool ha = i < a.State.Used, hb = i < b.State.Used; T av = ha ? a.Items[i] : default, bv = hb ? b.Items[i] : default;
                s = Equal(ha, av, hb, bv, codec, scratch, out bool equal); if (!s.IsSuccess()) return s; if (!equal) indices[count++] = i;
            }
            return DeterministicStatus.None;
        }
        private static DeterministicStatus Meta(ref DeterministicWriter w, in GraphCanonicalStage<TNode, TEdge> stage)
        {
            var s = w.TryVarUInt((uint)stage.Nodes.Capacity); if (!s.IsSuccess()) return s;
            s = default(SlotMapStateCodec).TryWrite(ref w, stage.Nodes.State); if (!s.IsSuccess()) return s;
            s = w.TryVarUInt((uint)stage.Edges.Capacity); if (!s.IsSuccess()) return s;
            return default(SlotMapStateCodec).TryWrite(ref w, stage.Edges.State);
        }
        private static DeterministicStatus Images<T, TCodec>(ref DeterministicWriter w, in CanonicalBufferStage<T, SlotMapState> source,
            in CanonicalBufferStage<T, SlotMapState> target, int* indices, int count, GraphDeltaMode mode, TCodec codec)
            where T : unmanaged where TCodec : unmanaged, IDeterministicCodec<T>
        {
            var s = w.TryVarUInt((uint)count); if (!s.IsSuccess()) return s; int previous = -1;
            for (int i = 0; i < count; i++)
            {
                int index = indices[i]; s = w.TryVarUInt((uint)(index - previous - 1)); if (!s.IsSuccess()) return s; previous = index;
                bool exists = index < target.State.Used; s = w.TryUInt(exists ? 1UL : 0, 1); if (!s.IsSuccess()) return s;
                if (exists) { s = codec.TryWrite(ref w, target.Items[index]); if (!s.IsSuccess()) return s; }
                if (mode == GraphDeltaMode.Reversible)
                { exists = index < source.State.Used; s = w.TryUInt(exists ? 1UL : 0, 1); if (!s.IsSuccess()) return s; if (exists) { s = codec.TryWrite(ref w, source.Items[index]); if (!s.IsSuccess()) return s; } }
            }
            return DeterministicStatus.None;
        }
        private static DeterministicStatus Body(ref DeterministicWriter w, in GraphCheckpoint sourceBytes, in GraphCheckpoint targetBytes,
            in GraphCanonicalStage<TNode, TEdge> source, in GraphCanonicalStage<TNode, TEdge> target, in GraphOptions options,
            TNodeCodec nc, TEdgeCodec ec, GraphDeltaMode mode, GraphTransferKind kind, int* nodes, int nodeCount, int* edges, int edgeCount)
        {
            var s = w.TryUInt(Magic, 4); if (!s.IsSuccess()) return s; s = w.TryUInt(Version, 2); if (!s.IsSuccess()) return s;
            s = w.TryUInt((byte)mode, 1); if (!s.IsSuccess()) return s; s = w.TryUInt((byte)kind, 1); if (!s.IsSuccess()) return s;
            s = w.TryUInt(DeterministicHash.Xxh64(sourceBytes.Buffer, sourceBytes.Length), 8); if (!s.IsSuccess()) return s;
            s = w.TryUInt(DeterministicHash.Xxh64(targetBytes.Buffer, targetBytes.Length), 8); if (!s.IsSuccess()) return s;
            s = w.TryVarUInt((uint)sourceBytes.Length); if (!s.IsSuccess()) return s; s = w.TryVarUInt((uint)targetBytes.Length); if (!s.IsSuccess()) return s;
            s = GraphWire<TNode, TEdge, TNodeCodec, TEdgeCodec>.Header(ref w, options, nc, ec); if (!s.IsSuccess()) return s;
            if (kind == GraphTransferKind.Checkpoint)
            {
                s = w.TryBytes(targetBytes.Buffer, targetBytes.Length); if (!s.IsSuccess()) return s;
                return mode == GraphDeltaMode.Reversible ? w.TryBytes(sourceBytes.Buffer, sourceBytes.Length) : DeterministicStatus.None;
            }
            s = Meta(ref w, target); if (!s.IsSuccess()) return s;
            if (mode == GraphDeltaMode.Reversible) { s = Meta(ref w, source); if (!s.IsSuccess()) return s; }
            var ncodec = new SlotMapNodeCodec<GraphNodeRecord<TNode>, GraphNodeRecordCodec<TNode, TNodeCodec>>(new GraphNodeRecordCodec<TNode, TNodeCodec>(nc));
            var ecodec = new SlotMapNodeCodec<GraphEdgeRecord<TEdge>, GraphEdgeRecordCodec<TEdge, TEdgeCodec>>(new GraphEdgeRecordCodec<TEdge, TEdgeCodec>(ec));
            s = Images(ref w, source.Nodes, target.Nodes, nodes, nodeCount, mode, ncodec); if (!s.IsSuccess()) return s;
            return Images(ref w, source.Edges, target.Edges, edges, edgeCount, mode, ecodec);
        }
        public static DeterministicStatus TryWriteDelta(ref DeterministicWriter writer, in GraphCheckpoint sourceBytes,
            in GraphCheckpoint targetBytes, ref CanonicalScratch scratch, in GraphOptions options, TNodeCodec nc, TEdgeCodec ec,
            out GraphTransferKind kind, GraphDeltaMode mode = GraphDeltaMode.Forward)
        {
            kind = default; if (mode > GraphDeltaMode.Reversible || !sourceBytes.IsValid || !targetBytes.IsValid) return DeterministicStatus.InvalidConfiguration;
            if (scratch.Overlaps(sourceBytes.Buffer, sourceBytes.Length) || scratch.Overlaps(targetBytes.Buffer, targetBytes.Length) ||
                !writer.IsMeasuring && scratch.Overlaps(writer.Buffer, writer.Capacity)) return DeterministicStatus.InvalidConfiguration;
            var work = scratch; var s = Decode(sourceBytes, ref work, options, nc, ec, out var source); if (!s.IsSuccess()) return s;
            s = Decode(targetBytes, ref work, options, nc, ec, out var target); if (!s.IsSuccess()) return s;
            var ncodec = new SlotMapNodeCodec<GraphNodeRecord<TNode>, GraphNodeRecordCodec<TNode, TNodeCodec>>(new GraphNodeRecordCodec<TNode, TNodeCodec>(nc));
            var ecodec = new SlotMapNodeCodec<GraphEdgeRecord<TEdge>, GraphEdgeRecordCodec<TEdge, TEdgeCodec>>(new GraphEdgeRecordCodec<TEdge, TEdgeCodec>(ec));
            s = Changes(source.Nodes, target.Nodes, ncodec, ref work, out var nodes, out int n); if (!s.IsSuccess()) return s;
            s = Changes(source.Edges, target.Edges, ecodec, ref work, out var edges, out int e); if (!s.IsSuccess()) return s;
            var patch = DeterministicWriter.CreateMeasure(); var full = DeterministicWriter.CreateMeasure();
            s = Body(ref patch, sourceBytes, targetBytes, source, target, options, nc, ec, mode, GraphTransferKind.Patch, nodes, n, edges, e); if (!s.IsSuccess()) return s;
            s = Body(ref full, sourceBytes, targetBytes, source, target, options, nc, ec, mode, GraphTransferKind.Checkpoint, nodes, n, edges, e); if (!s.IsSuccess()) return s;
            kind = patch.Position < full.Position ? GraphTransferKind.Patch : GraphTransferKind.Checkpoint;
            int size = kind == GraphTransferKind.Patch ? patch.Position : full.Position;
            if (!writer.Has(size)) return DeterministicStatus.BufferTooSmall;
            var trial = writer; s = Body(ref trial, sourceBytes, targetBytes, source, target, options, nc, ec, mode, kind, nodes, n, edges, e);
            if (s.IsSuccess()) { writer = trial; scratch = work; } return s;
        }
        private static DeterministicStatus ReadMeta(ref DeterministicReader r, in GraphOptions options,
            out int nodeCapacity, out SlotMapState ns, out int edgeCapacity, out SlotMapState es)
        {
            nodeCapacity = edgeCapacity = 0; ns = es = default;
            var s = r.TryVarUInt(out ulong n); if (!s.IsSuccess()) return s; if (n > int.MaxValue) return DeterministicStatus.InvalidEncoding;
            s = default(SlotMapStateCodec).TryRead(ref r, out ns); if (!s.IsSuccess()) return s;
            s = r.TryVarUInt(out ulong e); if (!s.IsSuccess()) return s; if (e > int.MaxValue) return DeterministicStatus.InvalidEncoding;
            s = default(SlotMapStateCodec).TryRead(ref r, out es); if (!s.IsSuccess()) return s; nodeCapacity = (int)n; edgeCapacity = (int)e;
            var nt = new GraphImportTarget<SlotMapNode<GraphNodeRecord<TNode>>>(options.Nodes); var et = new GraphImportTarget<SlotMapNode<GraphEdgeRecord<TEdge>>>(options.Edges);
            s = nt.CanReplaceCapacity(nodeCapacity); if (!s.IsSuccess()) return s; s = et.CanReplaceCapacity(edgeCapacity); if (!s.IsSuccess()) return s;
            s = default(SlotMapWireLayout<GraphNodeRecord<TNode>>).ValidateMetadata(ns, nodeCapacity); if (!s.IsSuccess()) return s;
            return default(SlotMapWireLayout<GraphEdgeRecord<TEdge>>).ValidateMetadata(es, edgeCapacity);
        }
        private static bool Same(in SlotMapState a, in SlotMapState b) => a.Count == b.Count && a.Used == b.Used && a.FreeHead == b.FreeHead && a.FreeTail == b.FreeTail;
        private static DeterministicStatus ReadImages<T, TCodec>(ref DeterministicReader r,
            in CanonicalBufferStage<T, SlotMapState> current, in CanonicalBufferStage<T, SlotMapState> destination,
            int beforeUsed, int afterUsed, bool reverse, GraphDeltaMode mode, TCodec codec, in CanonicalScratch scratch)
            where T : unmanaged where TCodec : unmanaged, IDeterministicCodec<T>
        {
            int maximum = beforeUsed > afterUsed ? beforeUsed : afterUsed;
            var s = r.TryVarUInt(out ulong count); if (!s.IsSuccess()) return s; if (count > (uint)maximum) return DeterministicStatus.InvalidEncoding;
            int previous = -1;
            for (ulong i = 0; i < count; i++)
            {
                s = r.TryVarUInt(out ulong gap); if (!s.IsSuccess()) return s;
                if (gap >= (uint)maximum || (ulong)(previous + 1) + gap >= (uint)maximum) return DeterministicStatus.InvalidEncoding;
                int index = previous + 1 + (int)gap; previous = index;
                s = r.TryUInt(1, out ulong hasAfter); if (!s.IsSuccess()) return s;
                if (hasAfter != (index < afterUsed ? 1UL : 0)) return DeterministicStatus.InvalidEncoding;
                T after = default, before = default;
                if (hasAfter != 0) { s = codec.TryRead(ref r, out after); if (!s.IsSuccess()) return s; }
                bool hasBefore = index < beforeUsed;
                if (mode == GraphDeltaMode.Reversible)
                {
                    s = r.TryUInt(1, out ulong present); if (!s.IsSuccess()) return s; if (present != (hasBefore ? 1UL : 0)) return DeterministicStatus.InvalidEncoding;
                    if (hasBefore) { s = codec.TryRead(ref r, out before); if (!s.IsSuccess()) return s; }
                    bool hasCurrent = index < current.State.Used; T actual = hasCurrent ? current.Items[index] : default;
                    T expected = reverse ? after : before; bool hasExpected = reverse ? hasAfter != 0 : hasBefore;
                    s = Equal(hasCurrent, actual, hasExpected, expected, codec, scratch, out bool matches); if (!s.IsSuccess()) return s;
                    if (!matches) return DeterministicStatus.ProtocolConflict;
                }
                else before = hasBefore ? current.Items[index] : default;
                s = Equal(hasBefore, before, hasAfter != 0, after, codec, scratch, out bool redundant); if (!s.IsSuccess()) return s;
                if (redundant) return DeterministicStatus.InvalidEncoding;
                if (index < destination.State.Used) destination.Items[index] = reverse ? before : after;
            }
            return DeterministicStatus.None;
        }
        private static DeterministicStatus Hash(in GraphCanonicalStage<TNode, TEdge> stage, in GraphOptions options,
            TNodeCodec nc, TEdgeCodec ec, in CanonicalScratch scratch, int expectedLength, out ulong hash)
        {
            hash = 0; var nodes = new GraphWorkBuffer<SlotMapNode<GraphNodeRecord<TNode>>>(stage.Nodes.Items, stage.Nodes.Capacity);
            var edges = new GraphWorkBuffer<SlotMapNode<GraphEdgeRecord<TEdge>>>(stage.Edges.Items, stage.Edges.Capacity);
            var measure = DeterministicWriter.CreateMeasure(); var s = GraphWire<TNode, TEdge, TNodeCodec, TEdgeCodec>.TryWrite(ref measure, ref nodes, ref edges, stage.Nodes.State, stage.Edges.State, options, nc, ec); if (!s.IsSuccess()) return s;
            if (measure.Position != expectedLength) return DeterministicStatus.InvalidEncoding;
            var work = scratch; s = work.TryReserve<byte>(measure.Position, out var bytes); if (!s.IsSuccess()) return s;
            var writer = new DeterministicWriter(bytes, measure.Position) { PremeasuredEnd = measure.Position };
            s = GraphWire<TNode, TEdge, TNodeCodec, TEdgeCodec>.TryWrite(ref writer, ref nodes, ref edges, stage.Nodes.State, stage.Edges.State, options, nc, ec);
            if (s.IsSuccess()) hash = DeterministicHash.Xxh64(bytes, writer.Position); return s;
        }
        public static DeterministicStatus TryReadStaged(ref DeterministicReader reader, in GraphCheckpoint currentBytes,
            ref CanonicalScratch scratch, in GraphOptions options, TNodeCodec nc, TEdgeCodec ec,
            out GraphCanonicalStage<TNode, TEdge> stage, bool reverse = false)
        {
            stage = default; if (!currentBytes.IsValid || scratch.Overlaps(reader.Buffer, reader.Length) || scratch.Overlaps(currentBytes.Buffer, currentBytes.Length)) return DeterministicStatus.InvalidConfiguration;
            var r = reader; var work = scratch;
            var s = r.TryUInt(4, out ulong magic); if (!s.IsSuccess()) return s; s = r.TryUInt(2, out ulong version); if (!s.IsSuccess()) return s;
            s = r.TryUInt(1, out ulong modeValue); if (!s.IsSuccess()) return s; s = r.TryUInt(1, out ulong kindValue); if (!s.IsSuccess()) return s;
            if (magic != Magic || version != Version || modeValue > (byte)GraphDeltaMode.Reversible || kindValue > (byte)GraphTransferKind.Checkpoint) return DeterministicStatus.InvalidEncoding;
            var mode = (GraphDeltaMode)modeValue; var kind = (GraphTransferKind)kindValue; if (reverse && mode != GraphDeltaMode.Reversible) return DeterministicStatus.InvalidConfiguration;
            s = r.TryUInt(8, out ulong baseHash); if (!s.IsSuccess()) return s; s = r.TryUInt(8, out ulong targetHash); if (!s.IsSuccess()) return s;
            s = r.TryVarUInt(out ulong sourceLength); if (!s.IsSuccess()) return s; s = r.TryVarUInt(out ulong targetLength); if (!s.IsSuccess()) return s;
            if (sourceLength > int.MaxValue || targetLength > int.MaxValue) return DeterministicStatus.InvalidEncoding;
            s = GraphWire<TNode, TEdge, TNodeCodec, TEdgeCodec>.ReadHeader(ref r, options, nc, ec); if (!s.IsSuccess()) return s;
            ulong expectedCurrent = reverse ? targetHash : baseHash, expectedResult = reverse ? baseHash : targetHash;
            int expectedCurrentLength = (int)(reverse ? targetLength : sourceLength), expectedResultLength = (int)(reverse ? sourceLength : targetLength);
            if (currentBytes.Length != expectedCurrentLength || DeterministicHash.Xxh64(currentBytes.Buffer, currentBytes.Length) != expectedCurrent) return DeterministicStatus.ProtocolConflict;
            GraphCanonicalStage<TNode, TEdge> candidate;
            if (kind == GraphTransferKind.Checkpoint)
            {
                long payload = (long)targetLength + (mode == GraphDeltaMode.Reversible ? (long)sourceLength : 0);
                if (payload > int.MaxValue || !r.Has((int)payload)) return DeterministicStatus.BufferTooSmall;
                var after = new GraphCheckpoint(r.Buffer + r.Position, (int)targetLength);
                if (DeterministicHash.Xxh64(after.Buffer, after.Length) != targetHash) return DeterministicStatus.InvalidEncoding;
                var before = new GraphCheckpoint(after.Buffer + after.Length, (int)sourceLength);
                if (mode == GraphDeltaMode.Reversible && DeterministicHash.Xxh64(before.Buffer, before.Length) != baseHash) return DeterministicStatus.InvalidEncoding;
                s = Decode(reverse ? before : after, ref work, options, nc, ec, out candidate); if (!s.IsSuccess()) return s;
                // Validate the opposite full image as well, without retaining its staging allocation.
                if (mode == GraphDeltaMode.Reversible) { var temporary = work; s = Decode(reverse ? after : before, ref temporary, options, nc, ec, out _); if (!s.IsSuccess()) return s; }
                r.Position += (int)payload;
            }
            else
            {
                s = Decode(currentBytes, ref work, options, nc, ec, out var current); if (!s.IsSuccess()) return s;
                s = ReadMeta(ref r, options, out int an, out var ans, out int ae, out var aes); if (!s.IsSuccess()) return s;
                int bn = current.Nodes.Capacity, be = current.Edges.Capacity; var bns = current.Nodes.State; var bes = current.Edges.State;
                if (mode == GraphDeltaMode.Reversible) { s = ReadMeta(ref r, options, out bn, out bns, out be, out bes); if (!s.IsSuccess()) return s; }
                if (mode == GraphDeltaMode.Reversible && (current.Nodes.Capacity != (reverse ? an : bn) || current.Edges.Capacity != (reverse ? ae : be) ||
                    !Same(current.Nodes.State, reverse ? ans : bns) || !Same(current.Edges.State, reverse ? aes : bes))) return DeterministicStatus.ProtocolConflict;
                int dn = reverse ? bn : an, de = reverse ? be : ae; var dns = reverse ? bns : ans; var des = reverse ? bes : aes;
                s = work.TryReserve<SlotMapNode<GraphNodeRecord<TNode>>>(dn, out var nodes); if (!s.IsSuccess()) return s;
                s = work.TryReserve<SlotMapNode<GraphEdgeRecord<TEdge>>>(de, out var edges); if (!s.IsSuccess()) return s;
                s = work.TryReserve<GraphIncidence>(de * 2, out var arcs); if (!s.IsSuccess()) return s;
                for (int i = 0; i < dn; i++) nodes[i] = i < current.Nodes.State.Used ? current.Nodes.Items[i] : default;
                for (int i = 0; i < de; i++) edges[i] = i < current.Edges.State.Used ? current.Edges.Items[i] : default;
                candidate = new GraphCanonicalStage<TNode, TEdge>(new CanonicalBufferStage<SlotMapNode<GraphNodeRecord<TNode>>, SlotMapState>(nodes, dn, dns), new CanonicalBufferStage<SlotMapNode<GraphEdgeRecord<TEdge>>, SlotMapState>(edges, de, des), arcs);
                var ncodec = new SlotMapNodeCodec<GraphNodeRecord<TNode>, GraphNodeRecordCodec<TNode, TNodeCodec>>(new GraphNodeRecordCodec<TNode, TNodeCodec>(nc));
                var ecodec = new SlotMapNodeCodec<GraphEdgeRecord<TEdge>, GraphEdgeRecordCodec<TEdge, TEdgeCodec>>(new GraphEdgeRecordCodec<TEdge, TEdgeCodec>(ec));
                s = ReadImages(ref r, current.Nodes, candidate.Nodes, bns.Used, ans.Used, reverse, mode, ncodec, work); if (!s.IsSuccess()) return s;
                s = ReadImages(ref r, current.Edges, candidate.Edges, bes.Used, aes.Used, reverse, mode, ecodec, work); if (!s.IsSuccess()) return s;
                s = default(SlotMapWireLayout<GraphNodeRecord<TNode>>).Validate(nodes, dns, dn, ref work); if (!s.IsSuccess()) return s;
                s = default(SlotMapWireLayout<GraphEdgeRecord<TEdge>>).Validate(edges, des, de, ref work); if (!s.IsSuccess()) return s;
                s = GraphWire<TNode, TEdge, TNodeCodec, TEdgeCodec>.Validate(candidate, options, ref work); if (!s.IsSuccess()) return s;
                s = Hash(candidate, options, nc, ec, work, expectedResultLength, out ulong actualHash); if (!s.IsSuccess()) return s;
                if (actualHash != expectedResult) return DeterministicStatus.InvalidEncoding;
            }
            stage = candidate; reader = r; scratch = work; return DeterministicStatus.None;
        }
    }
}
