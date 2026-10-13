namespace LegendaryTools.Deterministic
{
    public readonly struct FrozenGraphBufferReader<TNode, TEdge, TNodes, TEdges, TOffsets, TArcs> : IDeterministicGraphReader<TNode, TEdge>
        where TNode : unmanaged where TEdge : unmanaged
        where TNodes : struct, IDeterministicBuffer<GraphNodeItem<TNode>>
        where TEdges : struct, IDeterministicBuffer<GraphEdgeItem<TEdge>>
        where TOffsets : struct, IDeterministicBuffer<int> where TArcs : struct, IDeterministicBuffer<GraphIncidence>
    {
        private readonly TNodes nodes;
        private readonly TEdges edges;
        private readonly TOffsets outgoing, incoming;
        private readonly TArcs arcs;
        private readonly ulong revision;
        private readonly bool built;
        public GraphOptions Options { get; }
        public int NodeCount { get; }
        public int EdgeCount { get; }
        public int NodeExtent => NodeCount;
        public int EdgeExtent => EdgeCount;
        public FrozenGraphBufferReader(TNodes nodes, TEdges edges, TOffsets outgoing, TOffsets incoming, TArcs arcs,
            int nodeCount, int edgeCount, bool built, GraphOptions options)
        { this.nodes = nodes; this.edges = edges; this.outgoing = outgoing; this.incoming = incoming; this.arcs = arcs; NodeCount = nodeCount; EdgeCount = edgeCount; this.built = built; Options = options; revision = nodes.Revision; }
        public DeterministicStatus Check() => !nodes.IsCreated || !edges.IsCreated || !arcs.IsCreated ? DeterministicStatus.Disposed :
            !built ? DeterministicStatus.Pending : nodes.Revision != revision ? DeterministicStatus.Invalidated : DeterministicStatus.None;
        public DeterministicStatus TryNodeOrdinal(GraphNodeHandle h, out int ordinal)
        {
            ordinal = -1; var s = Check(); if (!s.IsSuccess()) return s;
            int low = 0, high = NodeCount - 1;
            if (high >= 15)
            {
                var first = nodes.Read(0).Handle; var last = nodes.Read(high).Handle;
                if (h.Index < first.Index || h.Index > last.Index) return DeterministicStatus.NotFound;
                if (last.Index > first.Index)
                {
                    // A single interpolation hint is exact for regular index spacing. Every
                    // comparison still narrows the sorted range; irregular layouts use binary search.
                    int hint = (int)((ulong)(h.Index - first.Index) * (uint)high / (last.Index - first.Index));
                    var entry = nodes.Read(hint).Handle; int c = entry.CompareTo(h);
                    if (c == 0) { ordinal = hint; return DeterministicStatus.None; }
                    if (c < 0)
                    { low = hint + 1; if (low > high || nodes.Read(low).Handle.Index > h.Index) return DeterministicStatus.NotFound; }
                    else
                    { high = hint - 1; if (high < low || nodes.Read(high).Handle.Index < h.Index) return DeterministicStatus.NotFound; }
                }
            }
            while (low <= high) { int mid = low + (high - low) / 2; int c = nodes.Read(mid).Handle.CompareTo(h);
                if (c == 0) { ordinal = mid; return DeterministicStatus.None; } if (c < 0) low = mid + 1; else high = mid - 1; }
            return DeterministicStatus.NotFound;
        }
        public DeterministicStatus TryGetNode(GraphNodeHandle h, out TNode data)
        { data = default; var s = TryNodeOrdinal(h, out int ordinal); if (s.IsSuccess()) data = nodes.Read(ordinal).Data; return s; }
        public DeterministicStatus TryGetEdge(GraphEdgeHandle h, out GraphEdge<TEdge> edge)
        {
            edge = default; var s = Check(); if (!s.IsSuccess()) return s; int low = 0, high = EdgeCount - 1;
            if (high >= 15)
            {
                var first = edges.Read(0).Handle; var last = edges.Read(high).Handle;
                if (h.Index < first.Index || h.Index > last.Index) return DeterministicStatus.NotFound;
                if (last.Index > first.Index)
                {
                    int hint = (int)((ulong)(h.Index - first.Index) * (uint)high / (last.Index - first.Index));
                    var entry = edges.Read(hint); int c = entry.Handle.CompareTo(h);
                    if (c == 0) { edge = entry.Edge; return DeterministicStatus.None; }
                    if (c < 0)
                    { low = hint + 1; if (low > high || edges.Read(low).Handle.Index > h.Index) return DeterministicStatus.NotFound; }
                    else
                    { high = hint - 1; if (high < low || edges.Read(high).Handle.Index < h.Index) return DeterministicStatus.NotFound; }
                }
            }
            while (low <= high) { int mid = low + (high - low) / 2; var entry = edges.Read(mid); int c = entry.Handle.CompareTo(h);
                if (c == 0) { edge = entry.Edge; return DeterministicStatus.None; } if (c < 0) low = mid + 1; else high = mid - 1; }
            return DeterministicStatus.NotFound;
        }
        public DeterministicStatus TryNodeAt(int ordinal, out GraphNodeHandle h, out TNode data)
        {
            h = default; data = default; var s = Check(); if (!s.IsSuccess()) return s;
            if ((uint)ordinal >= (uint)NodeCount) return DeterministicStatus.OutOfRange;
            var entry = nodes.Read(ordinal); h = entry.Handle; data = entry.Data; return DeterministicStatus.None;
        }
        public DeterministicStatus TryEdgeAt(int ordinal, out GraphEdgeHandle h, out GraphEdge<TEdge> edge)
        {
            h = default; edge = default; var s = Check(); if (!s.IsSuccess()) return s;
            if ((uint)ordinal >= (uint)EdgeCount) return DeterministicStatus.OutOfRange;
            var entry = edges.Read(ordinal); h = entry.Handle; edge = entry.Edge; return DeterministicStatus.None;
        }
        public DeterministicStatus TryFirstArc(GraphNodeHandle node, GraphTraversal traversal, out int cursor)
        {
            cursor = 0; if (traversal > GraphTraversal.Incoming) return DeterministicStatus.InvalidConfiguration;
            var s = TryNodeOrdinal(node, out int row); if (!s.IsSuccess()) return s;
            var offsets = traversal == GraphTraversal.Outgoing || Options.Direction == GraphDirection.Undirected ? outgoing : incoming;
            int start = offsets.Read(row), end = offsets.Read(row + 1); if (start < end) cursor = start + 1; return DeterministicStatus.None;
        }
        public DeterministicStatus TryReadArc(int cursor, out GraphArc arc, out int next)
        {
            arc = default; next = 0; var s = Check(); if (!s.IsSuccess()) return s;
            if (cursor <= 0 || cursor > EdgeCount * 2) return DeterministicStatus.OutOfRange;
            var entry = arcs.Read(cursor - 1); arc = new GraphArc(entry.Edge, entry.Neighbor, entry.Side); next = entry.Next; return DeterministicStatus.None;
        }
    }
    /// <summary>Preallocated CSR destination. The owner publishes counts only after successful construction.</summary>
    public static unsafe class FrozenGraphKernel<TNode, TEdge, TNodes, TEdges, TOffsets, TArcs>
        where TNode : unmanaged where TEdge : unmanaged
        where TNodes : struct, IDeterministicBuffer<GraphNodeItem<TNode>>
        where TEdges : struct, IDeterministicBuffer<GraphEdgeItem<TEdge>>
        where TOffsets : struct, IDeterministicBuffer<int> where TArcs : struct, IDeterministicBuffer<GraphIncidence>
    {
        public static DeterministicStatus TryBuild<TReader>(TReader source, in GraphOptions options,
            ref TNodes nodes, ref TEdges edges, ref TOffsets outgoing, ref TOffsets incoming, ref TArcs arcs,
            ref CanonicalScratch scratch, out int nodeCount, out int edgeCount)
            where TReader : struct, IDeterministicGraphReader<TNode, TEdge>
        {
            nodeCount = edgeCount = 0; var s = source.Check(); if (!s.IsSuccess()) return s;
            if (source.Options.SemanticFingerprint != options.SemanticFingerprint) return DeterministicStatus.ProfileMismatch;
            int nc = source.NodeCount, ec = source.EdgeCount;
            if (nc < 0 || ec < 0 || ec > (int.MaxValue - 1) / 2 || nc == int.MaxValue) return DeterministicStatus.InvalidConfiguration;
            s = nodes.CanPrepareWrite(nc, 0, nc); if (!s.IsSuccess()) return s; s = edges.CanPrepareWrite(ec, 0, ec); if (!s.IsSuccess()) return s;
            s = outgoing.CanPrepareWrite(nc + 1, 0, nc + 1); if (!s.IsSuccess()) return s; s = incoming.CanPrepareWrite(nc + 1, 0, nc + 1); if (!s.IsSuccess()) return s;
            s = arcs.CanPrepareWrite(ec * 2, 0, ec * 2); if (!s.IsSuccess()) return s;
            var work = scratch;
            s = work.TryReserve<int>(source.NodeExtent, out var map); if (!s.IsSuccess()) return s;
            s = work.TryReserve<int>(nc, out var outCounts); if (!s.IsSuccess()) return s;
            s = work.TryReserve<int>(nc, out var inCounts); if (!s.IsSuccess()) return s;
            for (int i = 0; i < source.NodeExtent; i++) map[i] = -1;
            for (int i = 0; i < nc; i++) outCounts[i] = inCounts[i] = 0;
            nodes.PrepareWrite(nc, 0, nc); edges.PrepareWrite(ec, 0, ec); outgoing.PrepareWrite(nc + 1, 0, nc + 1); incoming.PrepareWrite(nc + 1, 0, nc + 1); arcs.PrepareWrite(ec * 2, 0, ec * 2);
            int n = 0;
            for (int i = 0; i < source.NodeExtent; i++)
            {
                s = source.TryNodeAt(i, out var h, out var data); if (s == DeterministicStatus.NotFound) continue; if (!s.IsSuccess()) return s;
                if (n >= nc || n != 0 && nodes.Read(n - 1).Handle.CompareTo(h) >= 0) return DeterministicStatus.InvalidEncoding;
                map[i] = n; nodes.Write(n++, new GraphNodeItem<TNode>(h, data));
            }
            if (n != nc) return DeterministicStatus.InvalidEncoding;
            int e = 0;
            for (int i = 0; i < source.EdgeExtent; i++)
            {
                s = source.TryEdgeAt(i, out var h, out var edge); if (s == DeterministicStatus.NotFound) continue; if (!s.IsSuccess()) return s;
                if (e >= ec || e != 0 && edges.Read(e - 1).Handle.CompareTo(h) >= 0) return DeterministicStatus.InvalidEncoding;
                s = source.TryNodeOrdinal(edge.Source, out int a); if (!s.IsSuccess()) return s; s = source.TryNodeOrdinal(edge.Target, out int b); if (!s.IsSuccess()) return s;
                if ((uint)a >= (uint)source.NodeExtent || (uint)b >= (uint)source.NodeExtent || map[a] < 0 || map[b] < 0) return DeterministicStatus.InvalidEncoding;
                edges.Write(e++, new GraphEdgeItem<TEdge>(h, edge)); outCounts[map[a]]++;
                if (options.Direction == GraphDirection.Directed) inCounts[map[b]]++; else outCounts[map[b]]++;
            }
            if (e != ec) return DeterministicStatus.InvalidEncoding;
            int total = 0;
            for (int i = 0; i < nc; i++) { outgoing.Write(i, total); total += outCounts[i]; outCounts[i] = outgoing.Read(i); }
            outgoing.Write(nc, total);
            if (options.Direction == GraphDirection.Directed)
            {
                for (int i = 0; i < nc; i++) { incoming.Write(i, total); total += inCounts[i]; inCounts[i] = incoming.Read(i); }
                incoming.Write(nc, total);
            }
            else for (int i = 0; i <= nc; i++) incoming.Write(i, outgoing.Read(i));
            for (int i = 0; i < ec; i++)
            {
                var item = edges.Read(i); source.TryNodeOrdinal(item.Edge.Source, out int a); source.TryNodeOrdinal(item.Edge.Target, out int b);
                arcs.Write(outCounts[map[a]]++, new GraphIncidence { Edge = item.Handle, Neighbor = item.Edge.Target, Side = 0 });
                if (options.Direction == GraphDirection.Directed) arcs.Write(inCounts[map[b]]++, new GraphIncidence { Edge = item.Handle, Neighbor = item.Edge.Source, Side = 1 });
                else arcs.Write(outCounts[map[b]]++, new GraphIncidence { Edge = item.Handle, Neighbor = item.Edge.Source, Side = 1 });
            }
            for (int row = 0; row < nc; row++)
            {
                SetNext(ref arcs, outgoing.Read(row), outgoing.Read(row + 1));
                if (options.Direction == GraphDirection.Directed) SetNext(ref arcs, incoming.Read(row), incoming.Read(row + 1));
            }
            s = source.Check(); if (!s.IsSuccess()) return s;
            nodes.CommitWrite(); edges.CommitWrite(); outgoing.CommitWrite(); incoming.CommitWrite(); arcs.CommitWrite();
            nodeCount = nc; edgeCount = ec; scratch = work; return DeterministicStatus.None;
        }
        private static void SetNext(ref TArcs arcs, int first, int end)
        {
            for (int i = first; i < end; i++) { var entry = arcs.Read(i); entry.Next = i + 1 < end ? i + 2 : 0; arcs.Write(i, entry); }
        }
    }
}
