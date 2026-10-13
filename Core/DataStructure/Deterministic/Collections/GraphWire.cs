namespace LegendaryTools.Deterministic
{
    public readonly struct GraphNodeRecordCodec<T, TCodec> : IDeterministicCodec<GraphNodeRecord<T>>
        where T : unmanaged where TCodec : unmanaged, IDeterministicCodec<T>
    {
        private readonly TCodec codec;
        public GraphNodeRecordCodec(TCodec codec) { this.codec = codec; }
        public uint SchemaId => CanonicalFields.CombineSchema(0xd301u, codec.SchemaId, codec.Version);
        public ushort Version => 1;
        public int Measure(in GraphNodeRecord<T> value) => codec.Measure(value.Data);
        public DeterministicStatus TryWrite(ref DeterministicWriter writer, in GraphNodeRecord<T> value) => codec.TryWrite(ref writer, value.Data);
        public DeterministicStatus TryRead(ref DeterministicReader reader, out GraphNodeRecord<T> value)
        { value = default; var s = codec.TryRead(ref reader, out var data); if (s.IsSuccess()) value.Data = data; return s; }
    }
    public readonly struct GraphEdgeRecordCodec<T, TCodec> : IDeterministicCodec<GraphEdgeRecord<T>>
        where T : unmanaged where TCodec : unmanaged, IDeterministicCodec<T>
    {
        private readonly TCodec codec;
        public GraphEdgeRecordCodec(TCodec codec) { this.codec = codec; }
        public uint SchemaId => CanonicalFields.CombineSchema(0xd302u, codec.SchemaId, codec.Version);
        public ushort Version => 1;
        public int Measure(in GraphEdgeRecord<T> value)
        { var w = DeterministicWriter.CreateMeasure(); return TryWrite(ref w, value).IsSuccess() ? w.Position : -1; }
        public DeterministicStatus TryWrite(ref DeterministicWriter writer, in GraphEdgeRecord<T> value)
        {
            var s = default(StableHandlePolicy).TryWrite(ref writer, value.Source.Value); if (!s.IsSuccess()) return s;
            s = default(StableHandlePolicy).TryWrite(ref writer, value.Target.Value); if (!s.IsSuccess()) return s;
            return codec.TryWrite(ref writer, value.Data);
        }
        public DeterministicStatus TryRead(ref DeterministicReader reader, out GraphEdgeRecord<T> value)
        {
            value = default; var r = reader;
            var s = default(StableHandlePolicy).TryRead(ref r, out var source); if (!s.IsSuccess()) return s;
            s = default(StableHandlePolicy).TryRead(ref r, out var target); if (!s.IsSuccess()) return s;
            s = codec.TryRead(ref r, out var data); if (!s.IsSuccess()) return s;
            value = new GraphEdgeRecord<T> { Source = new GraphNodeHandle(source), Target = new GraphNodeHandle(target), Data = data }; reader = r; return s;
        }
    }
    /// <summary>Borrowed validated replacement. Valid only while its scratch memory remains alive.</summary>
    public readonly unsafe struct GraphCanonicalStage<TNode, TEdge> where TNode : unmanaged where TEdge : unmanaged
    {
        public readonly CanonicalBufferStage<SlotMapNode<GraphNodeRecord<TNode>>, SlotMapState> Nodes;
        public readonly CanonicalBufferStage<SlotMapNode<GraphEdgeRecord<TEdge>>, SlotMapState> Edges;
        public readonly GraphIncidence* Arcs;
        public GraphCanonicalStage(CanonicalBufferStage<SlotMapNode<GraphNodeRecord<TNode>>, SlotMapState> nodes,
            CanonicalBufferStage<SlotMapNode<GraphEdgeRecord<TEdge>>, SlotMapState> edges, GraphIncidence* arcs)
        { Nodes = nodes; Edges = edges; Arcs = arcs; }
    }
    /// <summary>Compound canonical state. Pool history is semantic; adjacency caches are rebuilt.</summary>
    public static unsafe class GraphWire<TNode, TEdge, TNodeCodec, TEdgeCodec>
        where TNode : unmanaged where TEdge : unmanaged
        where TNodeCodec : unmanaged, IDeterministicCodec<TNode> where TEdgeCodec : unmanaged, IDeterministicCodec<TEdge>
    {
        public const uint Magic = 0x3147444c; // LDG1
        public const ushort Version = 1;
        public static DeterministicStatus TryMeasureScratch(in GraphOptions options, out int bytes)
        {
            bytes = 0; if (!options.IsValid) return DeterministicStatus.InvalidConfiguration;
            GraphScratch.TryMeasure(options.Nodes.MaxCapacity, options.Edges.MaxCapacity, out int algorithms);
            long size = (long)options.Nodes.MaxCapacity * (sizeof(SlotMapNode<GraphNodeRecord<TNode>>) + 8) +
                (long)options.Edges.MaxCapacity * (sizeof(SlotMapNode<GraphEdgeRecord<TEdge>>) + 2 * sizeof(GraphIncidence)) + algorithms + 256;
            if (size > int.MaxValue) return DeterministicStatus.CapacityExceeded; bytes = (int)size; return DeterministicStatus.None;
        }
        internal static DeterministicStatus Header(ref DeterministicWriter w, in GraphOptions options, in TNodeCodec nc, in TEdgeCodec ec, DeterministicCollectionKind kind = DeterministicCollectionKind.Graph)
        {
            if (!options.IsValid || nc.SchemaId == 0 || nc.Version == 0 || ec.SchemaId == 0 || ec.Version == 0) return DeterministicStatus.InvalidConfiguration;
            var s = w.TryUInt(Magic, 4); if (!s.IsSuccess()) return s;
            s = w.TryUInt(Version, 2); if (!s.IsSuccess()) return s;
            s = w.TryUInt((byte)kind, 1); if (!s.IsSuccess()) return s;
            s = w.TryUInt(options.SemanticFingerprint, 8); if (!s.IsSuccess()) return s;
            s = w.TryUInt(nc.SchemaId, 4); if (!s.IsSuccess()) return s; s = w.TryUInt(nc.Version, 2); if (!s.IsSuccess()) return s;
            s = w.TryUInt(ec.SchemaId, 4); if (!s.IsSuccess()) return s; return w.TryUInt(ec.Version, 2);
        }
        internal static DeterministicStatus ReadHeader(ref DeterministicReader r, in GraphOptions options, in TNodeCodec nc, in TEdgeCodec ec, DeterministicCollectionKind expectedKind = DeterministicCollectionKind.Graph)
        {
            var s = r.TryUInt(4, out ulong magic); if (!s.IsSuccess()) return s;
            s = r.TryUInt(2, out ulong version); if (!s.IsSuccess()) return s;
            s = r.TryUInt(1, out ulong kind); if (!s.IsSuccess()) return s;
            if (magic != Magic || version != Version || kind != (byte)expectedKind) return DeterministicStatus.InvalidEncoding;
            s = r.TryUInt(8, out ulong semantics); if (!s.IsSuccess()) return s;
            s = r.TryUInt(4, out ulong ns); if (!s.IsSuccess()) return s; s = r.TryUInt(2, out ulong nv); if (!s.IsSuccess()) return s;
            s = r.TryUInt(4, out ulong es); if (!s.IsSuccess()) return s; s = r.TryUInt(2, out ulong ev); if (!s.IsSuccess()) return s;
            return semantics == options.SemanticFingerprint && ns == nc.SchemaId && nv == nc.Version && es == ec.SchemaId && ev == ec.Version ? DeterministicStatus.None : DeterministicStatus.ProfileMismatch;
        }
        private static DeterministicStatus WriteBody<TNodes, TEdges>(ref DeterministicWriter w, ref TNodes nodes, ref TEdges edges,
            in SlotMapState ns, in SlotMapState es, in GraphOptions options, in TNodeCodec nc, in TEdgeCodec ec)
            where TNodes : struct, IDeterministicImportBuffer<SlotMapNode<GraphNodeRecord<TNode>>>
            where TEdges : struct, IDeterministicImportBuffer<SlotMapNode<GraphEdgeRecord<TEdge>>>
        {
            var s = Header(ref w, options, nc, ec); if (!s.IsSuccess()) return s;
            var ncodec = new SlotMapNodeCodec<GraphNodeRecord<TNode>, GraphNodeRecordCodec<TNode, TNodeCodec>>(new GraphNodeRecordCodec<TNode, TNodeCodec>(nc));
            var ecodec = new SlotMapNodeCodec<GraphEdgeRecord<TEdge>, GraphEdgeRecordCodec<TEdge, TEdgeCodec>>(new GraphEdgeRecordCodec<TEdge, TEdgeCodec>(ec));
            s = StagedBufferWire<SlotMapNode<GraphNodeRecord<TNode>>, SlotMapState, SlotMapNodeCodec<GraphNodeRecord<TNode>, GraphNodeRecordCodec<TNode, TNodeCodec>>, SlotMapStateCodec, SlotMapWireLayout<GraphNodeRecord<TNode>>, TNodes>
                .TryWrite(ref w, ref nodes, ns, DeterministicCollectionKind.SlotMap, ncodec, default, default); if (!s.IsSuccess()) return s;
            return StagedBufferWire<SlotMapNode<GraphEdgeRecord<TEdge>>, SlotMapState, SlotMapNodeCodec<GraphEdgeRecord<TEdge>, GraphEdgeRecordCodec<TEdge, TEdgeCodec>>, SlotMapStateCodec, SlotMapWireLayout<GraphEdgeRecord<TEdge>>, TEdges>
                .TryWrite(ref w, ref edges, es, DeterministicCollectionKind.SlotMap, ecodec, default, default);
        }
        public static DeterministicStatus TryWrite<TNodes, TEdges>(ref DeterministicWriter writer, ref TNodes nodes, ref TEdges edges,
            in SlotMapState ns, in SlotMapState es, in GraphOptions options, TNodeCodec nc, TEdgeCodec ec)
            where TNodes : struct, IDeterministicImportBuffer<SlotMapNode<GraphNodeRecord<TNode>>>
            where TEdges : struct, IDeterministicImportBuffer<SlotMapNode<GraphEdgeRecord<TEdge>>>
        {
            if (writer.IsMeasuring) return WriteBody(ref writer, ref nodes, ref edges, ns, es, options, nc, ec);
            if (writer.HasPremeasuredCapacity)
            {
                var measured = writer; var status = WriteBody(ref measured, ref nodes, ref edges, ns, es, options, nc, ec);
                if (status.IsSuccess()) writer = measured; return status;
            }
            var measure = DeterministicWriter.CreateMeasure(); var s = WriteBody(ref measure, ref nodes, ref edges, ns, es, options, nc, ec); if (!s.IsSuccess()) return s;
            if (!writer.Has(measure.Position)) return DeterministicStatus.BufferTooSmall;
            var trial = writer; trial.PremeasuredEnd = trial.Position + measure.Position;
            s = WriteBody(ref trial, ref nodes, ref edges, ns, es, options, nc, ec);
            trial.PremeasuredEnd = writer.PremeasuredEnd;
            if (s.IsSuccess()) writer = trial; return s;
        }
        public static DeterministicStatus TryReadStaged<TNodes, TEdges>(ref DeterministicReader reader, ref CanonicalScratch scratch,
            ref TNodes nodes, ref TEdges edges, in GraphOptions options, TNodeCodec nc, TEdgeCodec ec, out GraphCanonicalStage<TNode, TEdge> stage)
            where TNodes : struct, IDeterministicImportBuffer<SlotMapNode<GraphNodeRecord<TNode>>>
            where TEdges : struct, IDeterministicImportBuffer<SlotMapNode<GraphEdgeRecord<TEdge>>>
        {
            stage = default; if (!nodes.IsCreated || !edges.IsCreated) return DeterministicStatus.Disposed;
            if (!options.IsValid || nc.SchemaId == 0 || nc.Version == 0 || ec.SchemaId == 0 || ec.Version == 0) return DeterministicStatus.InvalidConfiguration;
            if (scratch.Overlaps(reader.Buffer, reader.Length)) return DeterministicStatus.InvalidConfiguration;
            var r = reader; var work = scratch;
            var s = ReadHeader(ref r, options, nc, ec); if (!s.IsSuccess()) return s;
            var ncodec = new SlotMapNodeCodec<GraphNodeRecord<TNode>, GraphNodeRecordCodec<TNode, TNodeCodec>>(new GraphNodeRecordCodec<TNode, TNodeCodec>(nc));
            var ecodec = new SlotMapNodeCodec<GraphEdgeRecord<TEdge>, GraphEdgeRecordCodec<TEdge, TEdgeCodec>>(new GraphEdgeRecordCodec<TEdge, TEdgeCodec>(ec));
            s = StagedBufferWire<SlotMapNode<GraphNodeRecord<TNode>>, SlotMapState, SlotMapNodeCodec<GraphNodeRecord<TNode>, GraphNodeRecordCodec<TNode, TNodeCodec>>, SlotMapStateCodec, SlotMapWireLayout<GraphNodeRecord<TNode>>, TNodes>
                .TryReadStaged(ref r, ref work, ref nodes, DeterministicCollectionKind.SlotMap, ncodec, default, default, out var nstage); if (!s.IsSuccess()) return s;
            s = StagedBufferWire<SlotMapNode<GraphEdgeRecord<TEdge>>, SlotMapState, SlotMapNodeCodec<GraphEdgeRecord<TEdge>, GraphEdgeRecordCodec<TEdge, TEdgeCodec>>, SlotMapStateCodec, SlotMapWireLayout<GraphEdgeRecord<TEdge>>, TEdges>
                .TryReadStaged(ref r, ref work, ref edges, DeterministicCollectionKind.SlotMap, ecodec, default, default, out var estage); if (!s.IsSuccess()) return s;
            s = work.TryReserve<GraphIncidence>(estage.Capacity * 2, out var arcs); if (!s.IsSuccess()) return s;
            var candidate = new GraphCanonicalStage<TNode, TEdge>(nstage, estage, arcs);
            s = Validate(candidate, options, ref work); if (!s.IsSuccess()) return s;
            stage = candidate; reader = r; scratch = work; return DeterministicStatus.None;
        }
        public static DeterministicStatus Validate(in GraphCanonicalStage<TNode, TEdge> stage, in GraphOptions options, ref CanonicalScratch scratch)
        {
            var n = stage.Nodes; var e = stage.Edges;
            for (int i = 0; i < e.State.Used; i++)
            {
                var slot = e.Items[i]; if (slot.Active == 0) continue; var from = slot.Value.Source; var to = slot.Value.Target;
                if (!Endpoint(n, from) || !Endpoint(n, to) || from == to && !options.AllowSelfEdges ||
                    options.Direction == GraphDirection.Undirected && from.CompareTo(to) > 0) return DeterministicStatus.InvalidEncoding;
            }
            var nb = new GraphWorkBuffer<SlotMapNode<GraphNodeRecord<TNode>>>(n.Items, n.Capacity);
            var eb = new GraphWorkBuffer<SlotMapNode<GraphEdgeRecord<TEdge>>>(e.Items, e.Capacity);
            var ab = new GraphWorkBuffer<GraphIncidence>(stage.Arcs, e.Capacity * 2);
            GraphKernel<TNode, TEdge, GraphWorkBuffer<SlotMapNode<GraphNodeRecord<TNode>>>, GraphWorkBuffer<SlotMapNode<GraphEdgeRecord<TEdge>>>, GraphWorkBuffer<GraphIncidence>>.Rebuild(ref nb, ref eb, ref ab, n.State, e.State, options);
            if (!options.AllowParallelEdges)
            {
                var s = scratch.TryReserve<int>(n.State.Used, out var stamps); if (!s.IsSuccess()) return s;
                s = scratch.TryReserve<int>(n.State.Used, out var previous); if (!s.IsSuccess()) return s;
                for (int i = 0; i < n.State.Used; i++) stamps[i] = 0;
                for (int i = 0; i < n.State.Used; i++)
                    for (int cursor = n.Items[i].Value.OutHead; cursor != 0; cursor = stage.Arcs[cursor - 1].Next)
                    {
                        var a = stage.Arcs[cursor - 1]; int target = (int)a.Neighbor.Index;
                        if (stamps[target] == i + 1 && previous[target] != (int)a.Edge.Index) return DeterministicStatus.InvalidEncoding;
                        stamps[target] = i + 1; previous[target] = (int)a.Edge.Index;
                    }
            }
            if (options.Cycles == GraphCyclePolicy.Acyclic)
            {
                var reader = new GraphBufferReader<TNode, TEdge, GraphWorkBuffer<SlotMapNode<GraphNodeRecord<TNode>>>, GraphWorkBuffer<SlotMapNode<GraphEdgeRecord<TEdge>>>, GraphWorkBuffer<GraphIncidence>>(nb, eb, ab, n.State, e.State, options);
                var s = GraphAlgorithms<TNode, TEdge>.TryHasCycle(reader, ref scratch, out bool cyclic); if (!s.IsSuccess()) return s;
                if (cyclic) return DeterministicStatus.InvalidEncoding;
            }
            return DeterministicStatus.None;
        }
        private static bool Endpoint(in CanonicalBufferStage<SlotMapNode<GraphNodeRecord<TNode>>, SlotMapState> nodes, GraphNodeHandle h)
            => h.IsValid && h.Index < (uint)nodes.State.Used && nodes.Items[h.Index].Active == 1 && nodes.Items[h.Index].Generation == h.Generation;
        public static DeterministicStatus TryPublish<TNodes, TEdges, TArcs>(in GraphCanonicalStage<TNode, TEdge> stage,
            ref TNodes nodes, ref TEdges edges, ref TArcs arcs, ref SlotMapState ns, ref SlotMapState es)
            where TNodes : struct, IDeterministicImportBuffer<SlotMapNode<GraphNodeRecord<TNode>>>
            where TEdges : struct, IDeterministicImportBuffer<SlotMapNode<GraphEdgeRecord<TEdge>>>
            where TArcs : struct, IDeterministicImportBuffer<GraphIncidence>
            => GraphPublicationKernel<TNode, TEdge>.TryPublishFull(stage, ref nodes, ref edges, ref arcs, ref ns, ref es);
    }
}
