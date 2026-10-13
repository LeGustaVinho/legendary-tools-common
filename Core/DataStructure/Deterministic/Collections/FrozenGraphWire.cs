namespace LegendaryTools.Deterministic
{
    public readonly unsafe struct FrozenGraphStage<TNode, TEdge> where TNode : unmanaged where TEdge : unmanaged
    {
        public readonly GraphNodeItem<TNode>* Nodes;
        public readonly GraphEdgeItem<TEdge>* Edges;
        public readonly int* Outgoing, Incoming;
        public readonly GraphIncidence* Arcs;
        public readonly int NodeCount, EdgeCount;
        public FrozenGraphStage(GraphNodeItem<TNode>* nodes, GraphEdgeItem<TEdge>* edges, int* outgoing, int* incoming,
            GraphIncidence* arcs, int nc, int ec)
        { Nodes = nodes; Edges = edges; Outgoing = outgoing; Incoming = incoming; Arcs = arcs; NodeCount = nc; EdgeCount = ec; }
    }
    public static unsafe class FrozenGraphWire<TNode, TEdge, TNodeCodec, TEdgeCodec>
        where TNode : unmanaged where TEdge : unmanaged
        where TNodeCodec : unmanaged, IDeterministicCodec<TNode> where TEdgeCodec : unmanaged, IDeterministicCodec<TEdge>
    {
        private static DeterministicStatus Body<TReader>(ref DeterministicWriter w, TReader reader, TNodeCodec nc, TEdgeCodec ec)
            where TReader : struct, IDeterministicGraphReader<TNode, TEdge>
        {
            var s = reader.Check(); if (!s.IsSuccess()) return s;
            s = GraphWire<TNode, TEdge, TNodeCodec, TEdgeCodec>.Header(ref w, reader.Options, nc, ec, DeterministicCollectionKind.FrozenGraph); if (!s.IsSuccess()) return s;
            s = w.TryVarUInt((uint)reader.NodeCount); if (!s.IsSuccess()) return s; s = w.TryVarUInt((uint)reader.EdgeCount); if (!s.IsSuccess()) return s;
            for (int i = 0; i < reader.NodeExtent; i++)
            {
                s = reader.TryNodeAt(i, out var h, out var data); if (s == DeterministicStatus.NotFound) continue; if (!s.IsSuccess()) return s;
                s = default(StableHandlePolicy).TryWrite(ref w, h.Value); if (!s.IsSuccess()) return s; s = nc.TryWrite(ref w, data); if (!s.IsSuccess()) return s;
            }
            var edgeCodec = new GraphEdgeRecordCodec<TEdge, TEdgeCodec>(ec);
            for (int i = 0; i < reader.EdgeExtent; i++)
            {
                s = reader.TryEdgeAt(i, out var h, out var edge); if (s == DeterministicStatus.NotFound) continue; if (!s.IsSuccess()) return s;
                s = default(StableHandlePolicy).TryWrite(ref w, h.Value); if (!s.IsSuccess()) return s;
                var record = new GraphEdgeRecord<TEdge> { Source = edge.Source, Target = edge.Target, Data = edge.Data };
                s = edgeCodec.TryWrite(ref w, record); if (!s.IsSuccess()) return s;
            }
            return reader.Check();
        }
        public static DeterministicStatus TryWrite<TReader>(ref DeterministicWriter writer, TReader reader, TNodeCodec nc, TEdgeCodec ec)
            where TReader : struct, IDeterministicGraphReader<TNode, TEdge>
        {
            if (writer.IsMeasuring) return Body(ref writer, reader, nc, ec);
            var measure = DeterministicWriter.CreateMeasure(); var s = Body(ref measure, reader, nc, ec); if (!s.IsSuccess()) return s;
            if (!writer.Has(measure.Position)) return DeterministicStatus.BufferTooSmall;
            var trial = writer; s = Body(ref trial, reader, nc, ec); if (s.IsSuccess()) writer = trial; return s;
        }
        public static DeterministicStatus TryReadStaged(ref DeterministicReader reader, ref CanonicalScratch scratch,
            in GraphOptions options, int nodeCapacity, int edgeCapacity, TNodeCodec nc, TEdgeCodec ec, out FrozenGraphStage<TNode, TEdge> stage)
        {
            stage = default; if (!options.IsValid) return DeterministicStatus.InvalidConfiguration;
            if (scratch.Overlaps(reader.Buffer, reader.Length)) return DeterministicStatus.InvalidConfiguration;
            var r = reader; var work = scratch;
            var s = GraphWire<TNode, TEdge, TNodeCodec, TEdgeCodec>.ReadHeader(ref r, options, nc, ec, DeterministicCollectionKind.FrozenGraph); if (!s.IsSuccess()) return s;
            s = r.TryVarUInt(out ulong nodeCount); if (!s.IsSuccess()) return s; s = r.TryVarUInt(out ulong edgeCount); if (!s.IsSuccess()) return s;
            if (nodeCount > (uint)nodeCapacity || edgeCount > (uint)edgeCapacity) return DeterministicStatus.CapacityExceeded;
            int n = (int)nodeCount, e = (int)edgeCount;
            s = work.TryReserve<GraphNodeItem<TNode>>(n, out var nodes); if (!s.IsSuccess()) return s;
            s = work.TryReserve<GraphEdgeItem<TEdge>>(e, out var edges); if (!s.IsSuccess()) return s;
            s = work.TryReserve<int>(n + 1, out var outgoing); if (!s.IsSuccess()) return s; s = work.TryReserve<int>(n + 1, out var incoming); if (!s.IsSuccess()) return s;
            s = work.TryReserve<GraphIncidence>(e * 2, out var arcs); if (!s.IsSuccess()) return s;
            for (int i = 0; i < n; i++)
            {
                s = default(StableHandlePolicy).TryRead(ref r, out var h); if (!s.IsSuccess()) return s;
                if (!h.IsValid || i != 0 && nodes[i - 1].Handle.Value.CompareTo(h) >= 0) return DeterministicStatus.InvalidEncoding;
                s = nc.TryRead(ref r, out var data); if (!s.IsSuccess()) return s; nodes[i] = new GraphNodeItem<TNode>(new GraphNodeHandle(h), data);
            }
            var edgeCodec = new GraphEdgeRecordCodec<TEdge, TEdgeCodec>(ec);
            for (int i = 0; i < e; i++)
            {
                s = default(StableHandlePolicy).TryRead(ref r, out var h); if (!s.IsSuccess()) return s;
                if (!h.IsValid || i != 0 && edges[i - 1].Handle.Value.CompareTo(h) >= 0) return DeterministicStatus.InvalidEncoding;
                s = edgeCodec.TryRead(ref r, out var edge); if (!s.IsSuccess()) return s;
                if (edge.Source == edge.Target && !options.AllowSelfEdges || options.Direction == GraphDirection.Undirected && edge.Source.CompareTo(edge.Target) > 0) return DeterministicStatus.InvalidEncoding;
                edges[i] = new GraphEdgeItem<TEdge>(new GraphEdgeHandle(h), new GraphEdge<TEdge>(edge.Source, edge.Target, edge.Data));
            }
            var nb = new GraphWorkBuffer<GraphNodeItem<TNode>>(nodes, n); var eb = new GraphWorkBuffer<GraphEdgeItem<TEdge>>(edges, e);
            var ob = new GraphWorkBuffer<int>(outgoing, n + 1); var ib = new GraphWorkBuffer<int>(incoming, n + 1); var ab = new GraphWorkBuffer<GraphIncidence>(arcs, e * 2);
            var source = new FrozenGraphBufferReader<TNode, TEdge, GraphWorkBuffer<GraphNodeItem<TNode>>, GraphWorkBuffer<GraphEdgeItem<TEdge>>, GraphWorkBuffer<int>, GraphWorkBuffer<GraphIncidence>>(nb, eb, ob, ib, ab, n, e, true, options);
            s = FrozenGraphKernel<TNode, TEdge, GraphWorkBuffer<GraphNodeItem<TNode>>, GraphWorkBuffer<GraphEdgeItem<TEdge>>, GraphWorkBuffer<int>, GraphWorkBuffer<GraphIncidence>>
                .TryBuild(source, options, ref nb, ref eb, ref ob, ref ib, ref ab, ref work, out _, out _); if (!s.IsSuccess()) return s == DeterministicStatus.NotFound ? DeterministicStatus.InvalidEncoding : s;
            var rebuilt = new FrozenGraphBufferReader<TNode, TEdge, GraphWorkBuffer<GraphNodeItem<TNode>>, GraphWorkBuffer<GraphEdgeItem<TEdge>>, GraphWorkBuffer<int>, GraphWorkBuffer<GraphIncidence>>(nb, eb, ob, ib, ab, n, e, true, options);
            if (!options.AllowParallelEdges)
            {
                s = work.TryReserve<int>(n, out var stamps); if (!s.IsSuccess()) return s; s = work.TryReserve<GraphEdgeHandle>(n, out var previous); if (!s.IsSuccess()) return s;
                for (int i = 0; i < n; i++) stamps[i] = 0;
                for (int row = 0; row < n; row++)
                    for (int i = outgoing[row]; i < outgoing[row + 1]; i++)
                    {
                        rebuilt.TryNodeOrdinal(arcs[i].Neighbor, out int target);
                        if (stamps[target] == row + 1 && previous[target] != arcs[i].Edge) return DeterministicStatus.InvalidEncoding;
                        stamps[target] = row + 1; previous[target] = arcs[i].Edge;
                    }
            }
            if (options.Cycles == GraphCyclePolicy.Acyclic)
            {
                s = GraphAlgorithms<TNode, TEdge>.TryHasCycle(rebuilt, ref work, out bool cycle); if (!s.IsSuccess()) return s; if (cycle) return DeterministicStatus.InvalidEncoding;
            }
            stage = new FrozenGraphStage<TNode, TEdge>(nodes, edges, outgoing, incoming, arcs, n, e); reader = r; scratch = work; return DeterministicStatus.None;
        }
        public static DeterministicStatus TryPublish<TNodes, TEdges, TOffsets, TArcs>(in FrozenGraphStage<TNode, TEdge> stage,
            ref TNodes nodes, ref TEdges edges, ref TOffsets outgoing, ref TOffsets incoming, ref TArcs arcs)
            where TNodes : struct, IDeterministicBuffer<GraphNodeItem<TNode>> where TEdges : struct, IDeterministicBuffer<GraphEdgeItem<TEdge>>
            where TOffsets : struct, IDeterministicBuffer<int> where TArcs : struct, IDeterministicBuffer<GraphIncidence>
        {
            int nc = stage.NodeCount, ec = stage.EdgeCount;
            var s = nodes.CanPrepareWrite(nc, 0, nc); if (!s.IsSuccess()) return s; s = edges.CanPrepareWrite(ec, 0, ec); if (!s.IsSuccess()) return s;
            s = outgoing.CanPrepareWrite(nc + 1, 0, nc + 1); if (!s.IsSuccess()) return s; s = incoming.CanPrepareWrite(nc + 1, 0, nc + 1); if (!s.IsSuccess()) return s;
            s = arcs.CanPrepareWrite(ec * 2, 0, ec * 2); if (!s.IsSuccess()) return s;
            nodes.PrepareWrite(nc, 0, nc); edges.PrepareWrite(ec, 0, ec); outgoing.PrepareWrite(nc + 1, 0, nc + 1); incoming.PrepareWrite(nc + 1, 0, nc + 1); arcs.PrepareWrite(ec * 2, 0, ec * 2);
            for (int i = 0; i < nc; i++) nodes.Write(i, stage.Nodes[i]); for (int i = 0; i < ec; i++) edges.Write(i, stage.Edges[i]);
            for (int i = 0; i <= nc; i++) { outgoing.Write(i, stage.Outgoing[i]); incoming.Write(i, stage.Incoming[i]); }
            for (int i = 0; i < ec * 2; i++) arcs.Write(i, stage.Arcs[i]);
            nodes.CommitWrite(); edges.CommitWrite(); outgoing.CommitWrite(); incoming.CommitWrite(); arcs.CommitWrite(); return DeterministicStatus.None;
        }
    }
}
