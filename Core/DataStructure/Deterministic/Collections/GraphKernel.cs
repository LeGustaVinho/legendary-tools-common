namespace LegendaryTools.Deterministic
{
    /// <summary>Specialized buffer reader used by mutation checks and canonical staging.</summary>
    public readonly struct GraphBufferReader<TNode, TEdge, TNodes, TEdges, TArcs> : IDeterministicGraphReader<TNode, TEdge>
        where TNode : unmanaged where TEdge : unmanaged
        where TNodes : struct, IDeterministicBuffer<SlotMapNode<GraphNodeRecord<TNode>>>
        where TEdges : struct, IDeterministicBuffer<SlotMapNode<GraphEdgeRecord<TEdge>>>
        where TArcs : struct, IDeterministicBuffer<GraphIncidence>
    {
        private readonly TNodes nodes;
        private readonly TEdges edges;
        private readonly TArcs arcs;
        private readonly SlotMapState nodeState, edgeState;
        private readonly ulong revision;
        public GraphOptions Options { get; }
        public int NodeCount => nodeState.Count;
        public int EdgeCount => edgeState.Count;
        public int NodeExtent => nodeState.Used;
        public int EdgeExtent => edgeState.Used;
        public GraphBufferReader(TNodes nodes, TEdges edges, TArcs arcs, SlotMapState nodeState, SlotMapState edgeState, GraphOptions options)
        { this.nodes = nodes; this.edges = edges; this.arcs = arcs; this.nodeState = nodeState; this.edgeState = edgeState; Options = options; revision = nodes.Revision; }
        public DeterministicStatus Check() => !nodes.IsCreated || !edges.IsCreated || !arcs.IsCreated ? DeterministicStatus.Disposed :
            nodes.Revision != revision ? DeterministicStatus.Invalidated : DeterministicStatus.None;
        private DeterministicStatus Node(GraphNodeHandle handle, out GraphNodeRecord<TNode> value)
        {
            value = default; var s = Check(); if (!s.IsSuccess()) return s;
            if (!handle.IsValid || handle.Index >= (uint)nodeState.Used) return DeterministicStatus.NotFound;
            var slot = nodes.Read((int)handle.Index);
            if (slot.Active == 0 || slot.Generation != handle.Generation) return DeterministicStatus.NotFound;
            value = slot.Value; return DeterministicStatus.None;
        }
        public DeterministicStatus TryGetNode(GraphNodeHandle handle, out TNode data)
        { var s = Node(handle, out var n); data = n.Data; return s; }
        public DeterministicStatus TryGetEdge(GraphEdgeHandle handle, out GraphEdge<TEdge> edge)
        {
            edge = default; var s = Check(); if (!s.IsSuccess()) return s;
            if (!handle.IsValid || handle.Index >= (uint)edgeState.Used) return DeterministicStatus.NotFound;
            var slot = edges.Read((int)handle.Index);
            if (slot.Active == 0 || slot.Generation != handle.Generation) return DeterministicStatus.NotFound;
            edge = new GraphEdge<TEdge>(slot.Value.Source, slot.Value.Target, slot.Value.Data); return DeterministicStatus.None;
        }
        public DeterministicStatus TryNodeAt(int ordinal, out GraphNodeHandle handle, out TNode data)
        {
            handle = default; data = default; var s = Check(); if (!s.IsSuccess()) return s;
            if ((uint)ordinal >= (uint)NodeExtent) return DeterministicStatus.OutOfRange;
            var slot = nodes.Read(ordinal); if (slot.Active == 0) return DeterministicStatus.NotFound;
            handle = new GraphNodeHandle(new StableHandle((uint)ordinal, slot.Generation)); data = slot.Value.Data; return DeterministicStatus.None;
        }
        public DeterministicStatus TryEdgeAt(int ordinal, out GraphEdgeHandle handle, out GraphEdge<TEdge> edge)
        {
            handle = default; edge = default; var s = Check(); if (!s.IsSuccess()) return s;
            if ((uint)ordinal >= (uint)EdgeExtent) return DeterministicStatus.OutOfRange;
            var slot = edges.Read(ordinal); if (slot.Active == 0) return DeterministicStatus.NotFound;
            handle = new GraphEdgeHandle(new StableHandle((uint)ordinal, slot.Generation));
            edge = new GraphEdge<TEdge>(slot.Value.Source, slot.Value.Target, slot.Value.Data); return DeterministicStatus.None;
        }
        public DeterministicStatus TryNodeOrdinal(GraphNodeHandle handle, out int ordinal)
        { ordinal = -1; var s = Node(handle, out _); if (s.IsSuccess()) ordinal = (int)handle.Index; return s; }
        public DeterministicStatus TryFirstArc(GraphNodeHandle node, GraphTraversal traversal, out int cursor)
        {
            cursor = 0; if (traversal > GraphTraversal.Incoming) return DeterministicStatus.InvalidConfiguration;
            var s = Node(node, out var n); if (s.IsSuccess()) cursor = traversal == GraphTraversal.Outgoing || Options.Direction == GraphDirection.Undirected ? n.OutHead : n.InHead;
            return s;
        }
        public DeterministicStatus TryReadArc(int cursor, out GraphArc arc, out int next)
        {
            arc = default; next = 0; var s = Check(); if (!s.IsSuccess()) return s;
            if (cursor <= 0 || (long)cursor > (long)edgeState.Used * 2) return DeterministicStatus.OutOfRange;
            var incidence = arcs.Read(cursor - 1); if (!incidence.Edge.IsValid) return DeterministicStatus.NotFound;
            arc = new GraphArc(incidence.Edge, incidence.Neighbor, incidence.Side); next = incidence.Next; return DeterministicStatus.None;
        }
    }

    /// <summary>All participating writes are reserved before publishing a topology change.</summary>
    public static unsafe class GraphKernel<TNode, TEdge, TNodes, TEdges, TArcs>
        where TNode : unmanaged where TEdge : unmanaged
        where TNodes : struct, IDeterministicBuffer<SlotMapNode<GraphNodeRecord<TNode>>>
        where TEdges : struct, IDeterministicBuffer<SlotMapNode<GraphEdgeRecord<TEdge>>>
        where TArcs : struct, IDeterministicBuffer<GraphIncidence>
    {
        private static DeterministicStatus Reserve<T, TBuffer>(ref TBuffer b, int required, int start, int count, int phase)
            where T : unmanaged where TBuffer : struct, IDeterministicBuffer<T>
            => phase == 0 ? b.CanPrepareWrite(required, start, count) : b.PrepareWrite(required, start, count);
        private static DeterministicStatus NodeReserve(ref TNodes b, int index, int phase)
            => Reserve<SlotMapNode<GraphNodeRecord<TNode>>, TNodes>(ref b, b.Capacity, index, 1, phase);
        private static DeterministicStatus ArcReserve(ref TArcs b, int required, int cursor, int phase)
            => cursor == 0 ? DeterministicStatus.None : Reserve<GraphIncidence, TArcs>(ref b, required, cursor - 1, 1, phase);
        private static bool ValidNode(ref TNodes nodes, in SlotMapState state, GraphNodeHandle h)
            => h.IsValid && h.Index < (uint)state.Used && nodes.Read((int)h.Index).Active == 1 && nodes.Read((int)h.Index).Generation == h.Generation;
        private static bool ValidEdge(ref TEdges edges, in SlotMapState state, GraphEdgeHandle h)
            => h.IsValid && h.Index < (uint)state.Used && edges.Read((int)h.Index).Active == 1 && edges.Read((int)h.Index).Generation == h.Generation;
        public static DeterministicStatus TryAddNode(ref TNodes nodes, ref SlotMapState state, in TNode data, out GraphNodeHandle handle)
        {
            var record = new GraphNodeRecord<TNode> { Data = data };
            var s = SlotMapKernel<GraphNodeRecord<TNode>, TNodes>.TryAdd(ref nodes, ref state, record, out var h);
            handle = new GraphNodeHandle(h); return s;
        }
        public static DeterministicStatus TrySetNode(ref TNodes nodes, in SlotMapState state, GraphNodeHandle handle, in TNode data)
        {
            var s = SlotMapKernel<GraphNodeRecord<TNode>, TNodes>.TryGet(ref nodes, state, handle.Value, out var record); if (!s.IsSuccess()) return s;
            record.Data = data; return SlotMapKernel<GraphNodeRecord<TNode>, TNodes>.TrySet(ref nodes, state, handle.Value, record);
        }
        public static DeterministicStatus TrySetEdge(ref TNodes nodes, ref TEdges edges, in SlotMapState state, GraphEdgeHandle handle, in TEdge data)
        {
            var s = SlotMapKernel<GraphEdgeRecord<TEdge>, TEdges>.TryGet(ref edges, state, handle.Value, out var record); if (!s.IsSuccess()) return s;
            record.Data = data; s = SlotMapKernel<GraphEdgeRecord<TEdge>, TEdges>.TrySet(ref edges, state, handle.Value, record);
            if (s.IsSuccess()) nodes.CommitWrite(); return s;
        }
        private static void Position(ref TArcs arcs, int head, GraphEdgeHandle edge, byte side, out int previous, out int next)
        {
            previous = 0; next = head;
            while (next != 0)
            {
                var a = arcs.Read(next - 1); int c = a.Edge.CompareTo(edge);
                if (c > 0 || c == 0 && a.Side >= side) break;
                previous = next; next = a.Next;
            }
        }
        private static void Position(ref TNodes nodes, ref TArcs arcs, GraphNodeHandle owner,
            GraphEdgeHandle edge, byte side, bool outgoing, out int previous, out int next)
        {
            var value = nodes.Read((int)owner.Index).Value; int tail = outgoing ? value.OutTail : value.InTail;
            if (tail != 0)
            {
                var last = arcs.Read(tail - 1); int order = last.Edge.CompareTo(edge);
                if (order < 0 || order == 0 && last.Side < side) { previous = tail; next = 0; return; }
            }
            Position(ref arcs, outgoing ? value.OutHead : value.InHead, edge, side, out previous, out next);
        }
        private static int Head(ref TNodes nodes, GraphNodeHandle node, bool outgoing)
        { var n = nodes.Read((int)node.Index).Value; return outgoing ? n.OutHead : n.InHead; }
        private static void Link(ref TNodes nodes, ref TArcs arcs, GraphNodeHandle owner, GraphNodeHandle neighbor,
            GraphEdgeHandle edge, byte side, bool outgoing, int cursor)
        {
            Position(ref nodes, ref arcs, owner, edge, side, outgoing, out int previous, out int next);
            var a = new GraphIncidence { Edge = edge, Neighbor = neighbor, Side = side, Previous = previous, Next = next };
            arcs.Write(cursor - 1, a);
            if (previous != 0) { var p = arcs.Read(previous - 1); p.Next = cursor; arcs.Write(previous - 1, p); }
            if (next != 0) { var n = arcs.Read(next - 1); n.Previous = cursor; arcs.Write(next - 1, n); }
            var slot = nodes.Read((int)owner.Index); var value = slot.Value;
            if (outgoing) { if (previous == 0) value.OutHead = cursor; if (next == 0) value.OutTail = cursor; value.OutDegree++; }
            else { if (previous == 0) value.InHead = cursor; if (next == 0) value.InTail = cursor; value.InDegree++; }
            slot.Value = value; nodes.Write((int)owner.Index, slot);
        }
        private static DeterministicStatus PrepareLink(ref TNodes nodes, ref TArcs arcs, GraphNodeHandle owner,
            GraphEdgeHandle edge, byte side, bool outgoing, int required, int phase)
        {
            var s = NodeReserve(ref nodes, (int)owner.Index, phase); if (!s.IsSuccess()) return s;
            Position(ref nodes, ref arcs, owner, edge, side, outgoing, out int previous, out int next);
            s = ArcReserve(ref arcs, required, previous, phase); if (!s.IsSuccess()) return s;
            return ArcReserve(ref arcs, required, next, phase);
        }
        public static DeterministicStatus TryAddEdge(ref TNodes nodes, ref TEdges edges, ref TArcs arcs,
            in SlotMapState nodeState, ref SlotMapState edgeState, in GraphOptions options,
            GraphNodeHandle source, GraphNodeHandle target, in TEdge data, ref CanonicalScratch scratch, out GraphEdgeHandle handle)
        {
            handle = default;
            if (!nodes.IsCreated || !edges.IsCreated || !arcs.IsCreated) return DeterministicStatus.Disposed;
            if (!options.IsValid) return DeterministicStatus.InvalidConfiguration;
            if (!ValidNode(ref nodes, nodeState, source) || !ValidNode(ref nodes, nodeState, target)) return DeterministicStatus.NotFound;
            if (source == target && !options.AllowSelfEdges) return DeterministicStatus.InvalidConfiguration;
            if (options.Direction == GraphDirection.Undirected && source.CompareTo(target) > 0) { var swap = source; source = target; target = swap; }
            if (!options.AllowParallelEdges)
            {
                var from = nodes.Read((int)source.Index).Value;
                var to = source == target ? from : nodes.Read((int)target.Index).Value;
                bool directed = options.Direction == GraphDirection.Directed;
                bool useTarget = (directed ? to.InDegree : to.OutDegree) < from.OutDegree;
                int cursor = useTarget ? (directed ? to.InHead : to.OutHead) : from.OutHead;
                var neighbor = useTarget ? source : target;
                while (cursor != 0)
                {
                    var arc = arcs.Read(cursor - 1);
                    if (arc.Neighbor == neighbor) return DeterministicStatus.DuplicateKey;
                    cursor = arc.Next;
                }
            }
            if (options.Cycles == GraphCyclePolicy.Acyclic)
            {
                var reader = new GraphBufferReader<TNode, TEdge, TNodes, TEdges, TArcs>(nodes, edges, arcs, nodeState, edgeState, options);
                var work = scratch;
                var s = GraphAlgorithms<TNode, TEdge>.TryReachable(reader, target, source, ref work, out bool reachable);
                if (!s.IsSuccess()) return s; if (reachable) return DeterministicStatus.CycleDetected;
            }
            bool reuse = edgeState.FreeHead != 0;
            if (!reuse && edgeState.Used >= (int.MaxValue - 1) / 2) return DeterministicStatus.CapacityExceeded;
            int index = reuse ? edgeState.FreeHead - 1 : edgeState.Used;
            var predicted = new GraphEdgeHandle(new StableHandle((uint)index, reuse ? edges.Read(index).Generation : 1));
            var status = options.Edges.TryCapacity(edges.Capacity, reuse ? edges.Capacity : edgeState.Used + 1, out int capacity);
            if (!status.IsSuccess()) return status;
            int requiredArcs = capacity * 2;
            for (int phase = 0; phase < 2; phase++)
            {
                status = Reserve<SlotMapNode<GraphEdgeRecord<TEdge>>, TEdges>(ref edges, capacity, index, 1, phase); if (!status.IsSuccess()) return status;
                status = Reserve<GraphIncidence, TArcs>(ref arcs, requiredArcs, index * 2, 2, phase); if (!status.IsSuccess()) return status;
                status = PrepareLink(ref nodes, ref arcs, source, predicted, 0, true, requiredArcs, phase); if (!status.IsSuccess()) return status;
                status = PrepareLink(ref nodes, ref arcs, target, predicted, 1, options.Direction == GraphDirection.Undirected, requiredArcs, phase); if (!status.IsSuccess()) return status;
            }
            var record = new GraphEdgeRecord<TEdge> { Source = source, Target = target, Data = data };
            SlotMapKernel<GraphEdgeRecord<TEdge>, TEdges>.AddReserved(ref edges, ref edgeState, record, out var stable);
            handle = new GraphEdgeHandle(stable);
            Link(ref nodes, ref arcs, source, target, handle, 0, true, index * 2 + 1);
            Link(ref nodes, ref arcs, target, source, handle, 1, options.Direction == GraphDirection.Undirected, index * 2 + 2);
            nodes.CommitWrite(); edges.CommitWrite(); arcs.CommitWrite(); return DeterministicStatus.None;
        }
        private static DeterministicStatus PrepareUnlink(ref TNodes nodes, ref TArcs arcs, GraphNodeHandle owner, int cursor, int phase)
        {
            var s = NodeReserve(ref nodes, (int)owner.Index, phase); if (!s.IsSuccess()) return s;
            var a = arcs.Read(cursor - 1);
            s = ArcReserve(ref arcs, arcs.Capacity, cursor, phase); if (!s.IsSuccess()) return s;
            s = ArcReserve(ref arcs, arcs.Capacity, a.Previous, phase); if (!s.IsSuccess()) return s;
            return ArcReserve(ref arcs, arcs.Capacity, a.Next, phase);
        }
        private static void Unlink(ref TNodes nodes, ref TArcs arcs, GraphNodeHandle owner, int cursor, bool outgoing)
        {
            var a = arcs.Read(cursor - 1);
            if (a.Previous != 0) { var p = arcs.Read(a.Previous - 1); p.Next = a.Next; arcs.Write(a.Previous - 1, p); }
            if (a.Next != 0) { var n = arcs.Read(a.Next - 1); n.Previous = a.Previous; arcs.Write(a.Next - 1, n); }
            var slot = nodes.Read((int)owner.Index); var v = slot.Value;
            if (outgoing) { if (a.Previous == 0) v.OutHead = a.Next; if (a.Next == 0) v.OutTail = a.Previous; v.OutDegree--; }
            else { if (a.Previous == 0) v.InHead = a.Next; if (a.Next == 0) v.InTail = a.Previous; v.InDegree--; }
            slot.Value = v; nodes.Write((int)owner.Index, slot); arcs.Write(cursor - 1, default);
        }
        private static DeterministicStatus PrepareRemove(ref TNodes nodes, ref TEdges edges, ref TArcs arcs,
            in SlotMapState edgeState, int index, int phase)
        {
            var edge = edges.Read(index);
            var s = Reserve<SlotMapNode<GraphEdgeRecord<TEdge>>, TEdges>(ref edges, edges.Capacity, index, 1, phase); if (!s.IsSuccess()) return s;
            if (edge.Generation != uint.MaxValue && edgeState.FreeTail != 0)
            { s = Reserve<SlotMapNode<GraphEdgeRecord<TEdge>>, TEdges>(ref edges, edges.Capacity, edgeState.FreeTail - 1, 1, phase); if (!s.IsSuccess()) return s; }
            s = PrepareUnlink(ref nodes, ref arcs, edge.Value.Source, index * 2 + 1, phase); if (!s.IsSuccess()) return s;
            return PrepareUnlink(ref nodes, ref arcs, edge.Value.Target, index * 2 + 2, phase);
        }
        private static void RemoveReserved(ref TNodes nodes, ref TEdges edges, ref TArcs arcs,
            ref SlotMapState edgeState, in GraphOptions options, int index)
        {
            var edge = edges.Read(index);
            Unlink(ref nodes, ref arcs, edge.Value.Source, index * 2 + 1, true);
            Unlink(ref nodes, ref arcs, edge.Value.Target, index * 2 + 2, options.Direction == GraphDirection.Undirected);
            SlotMapKernel<GraphEdgeRecord<TEdge>, TEdges>.RemoveReserved(ref edges, ref edgeState, index, edge);
        }
        public static DeterministicStatus TryRemoveEdge(ref TNodes nodes, ref TEdges edges, ref TArcs arcs,
            ref SlotMapState edgeState, in GraphOptions options, GraphEdgeHandle handle)
        {
            if (!nodes.IsCreated || !edges.IsCreated || !arcs.IsCreated) return DeterministicStatus.Disposed;
            if (!ValidEdge(ref edges, edgeState, handle)) return DeterministicStatus.NotFound;
            for (int phase = 0; phase < 2; phase++)
            { var s = PrepareRemove(ref nodes, ref edges, ref arcs, edgeState, (int)handle.Index, phase); if (!s.IsSuccess()) return s; }
            RemoveReserved(ref nodes, ref edges, ref arcs, ref edgeState, options, (int)handle.Index);
            nodes.CommitWrite(); edges.CommitWrite(); arcs.CommitWrite(); return DeterministicStatus.None;
        }
        public static DeterministicStatus TryRemoveNode(ref TNodes nodes, ref TEdges edges, ref TArcs arcs,
            ref SlotMapState nodeState, ref SlotMapState edgeState, in GraphOptions options, GraphNodeHandle handle, ref CanonicalScratch scratch)
        {
            if (!nodes.IsCreated || !edges.IsCreated || !arcs.IsCreated) return DeterministicStatus.Disposed;
            if (!ValidNode(ref nodes, nodeState, handle)) return DeterministicStatus.NotFound;
            int index = (int)handle.Index; var node = nodes.Read(index); var work = scratch;
            var status = work.TryReserve<int>(node.Value.OutDegree + node.Value.InDegree, out int* incident); if (!status.IsSuccess()) return status;
            int a = node.Value.OutHead, b = node.Value.InHead, count = 0, last = -1;
            while (a != 0 || b != 0)
            {
                bool takeA = b == 0 || a != 0 && arcs.Read(a - 1).Edge.CompareTo(arcs.Read(b - 1).Edge) <= 0;
                var entry = arcs.Read((takeA ? a : b) - 1); int e = (int)entry.Edge.Index;
                if (e != last) { incident[count++] = e; last = e; }
                if (takeA) a = entry.Next; else b = entry.Next;
            }
            int firstNode = index, lastNode = index;
            bool contiguous = count >= 64 && count == edgeState.Count && incident[count - 1] - incident[0] + 1 == count;
            if (contiguous)
            {
                for (int i = 0; i < count; i++)
                {
                    var e = edges.Read(incident[i]).Value; int source = (int)e.Source.Index, target = (int)e.Target.Index;
                    firstNode = System.Math.Min(firstNode, System.Math.Min(source, target)); lastNode = System.Math.Max(lastNode, System.Math.Max(source, target));
                }
                contiguous = (long)lastNode - firstNode + 1 <= (long)count * 2 + 1;
                for (int i = firstNode; contiguous && i <= lastNode; i++)
                {
                    var n = nodes.Read(i);
                    // Every reserved slot must participate. Isolated/inactive gaps keep the sparse path.
                    if (n.Active == 0 || i != index && n.Value.OutDegree == 0 && n.Value.InDegree == 0) contiguous = false;
                }
            }
            for (int phase = 0; phase < 2; phase++)
            {
                if (contiguous)
                {
                    // All live edges are incident and contiguous: no unlink neighbor can be outside this arc range.
                    status = Reserve<SlotMapNode<GraphNodeRecord<TNode>>, TNodes>(ref nodes, nodes.Capacity, firstNode, lastNode - firstNode + 1, phase); if (!status.IsSuccess()) return status;
                    status = Reserve<SlotMapNode<GraphEdgeRecord<TEdge>>, TEdges>(ref edges, edges.Capacity, incident[0], count, phase); if (!status.IsSuccess()) return status;
                    status = Reserve<GraphIncidence, TArcs>(ref arcs, arcs.Capacity, incident[0] * 2, count * 2, phase); if (!status.IsSuccess()) return status;
                    if (edgeState.FreeTail != 0)
                    { status = Reserve<SlotMapNode<GraphEdgeRecord<TEdge>>, TEdges>(ref edges, edges.Capacity, edgeState.FreeTail - 1, 1, phase); if (!status.IsSuccess()) return status; }
                }
                else
                {
                    status = NodeReserve(ref nodes, index, phase); if (!status.IsSuccess()) return status;
                    for (int i = 0; i < count; i++)
                    { status = PrepareRemove(ref nodes, ref edges, ref arcs, edgeState, incident[i], phase); if (!status.IsSuccess()) return status; }
                }
                if (node.Generation != uint.MaxValue && nodeState.FreeTail != 0)
                { status = NodeReserve(ref nodes, nodeState.FreeTail - 1, phase); if (!status.IsSuccess()) return status; }
            }
            for (int i = 0; i < count; i++) RemoveReserved(ref nodes, ref edges, ref arcs, ref edgeState, options, incident[i]);
            SlotMapKernel<GraphNodeRecord<TNode>, TNodes>.RemoveReserved(ref nodes, ref nodeState, index, nodes.Read(index));
            nodes.CommitWrite(); edges.CommitWrite(); arcs.CommitWrite(); return DeterministicStatus.None;
        }
        public static DeterministicStatus TryClear(ref TNodes nodes, ref TEdges edges, ref TArcs arcs,
            ref SlotMapState nodeState, ref SlotMapState edgeState)
            => TryClear(ref nodes, ref edges, ref arcs, ref nodeState, ref edgeState, default);
        public static DeterministicStatus TryClear(ref TNodes nodes, ref TEdges edges, ref TArcs arcs,
            ref SlotMapState nodeState, ref SlotMapState edgeState, in GraphOptions options)
        {
            if (!nodes.IsCreated || !edges.IsCreated || !arcs.IsCreated) return DeterministicStatus.Disposed;
            if (nodeState.Count == 0) return DeterministicStatus.None;
            if ((nodeState.Used >= 4096 || edgeState.Used >= 4096) && nodeState.Count <= 256 && edgeState.Count <= 256 &&
                (nodeState.Count <= nodeState.Used / 16 || edgeState.Count <= edgeState.Used / 16))
                return ClearSparse(ref nodes, ref edges, ref arcs, ref nodeState, ref edgeState, options);
            for (int phase = 0; phase < 2; phase++)
            {
                var s = Reserve<SlotMapNode<GraphNodeRecord<TNode>>, TNodes>(ref nodes, nodes.Capacity, 0, nodeState.Used, phase); if (!s.IsSuccess()) return s;
                s = Reserve<SlotMapNode<GraphEdgeRecord<TEdge>>, TEdges>(ref edges, edges.Capacity, 0, edgeState.Used, phase); if (!s.IsSuccess()) return s;
                s = Reserve<GraphIncidence, TArcs>(ref arcs, arcs.Capacity, 0, edgeState.Used * 2, phase); if (!s.IsSuccess()) return s;
            }
            for (int i = 0; i < nodeState.Used; i++) if (nodes.Read(i).Active != 0)
                SlotMapKernel<GraphNodeRecord<TNode>, TNodes>.RemoveReserved(ref nodes, ref nodeState, i, nodes.Read(i));
            for (int i = 0; i < edgeState.Used; i++) if (edges.Read(i).Active != 0)
                SlotMapKernel<GraphEdgeRecord<TEdge>, TEdges>.RemoveReserved(ref edges, ref edgeState, i, edges.Read(i));
            for (int i = 0; i < edgeState.Used * 2; i++) arcs.Write(i, default);
            nodes.CommitWrite(); edges.CommitWrite(); arcs.CommitWrite(); return DeterministicStatus.None;
        }
        private static DeterministicStatus ClearSparse(ref TNodes nodes, ref TEdges edges, ref TArcs arcs,
            ref SlotMapState ns, ref SlotMapState es, in GraphOptions options)
        {
            // Bounded stack memory; sorted indices preserve the dense clear's FIFO removal order.
            int* nodeIndices = stackalloc int[256]; int* edgeIndices = stackalloc int[256]; int nc = 0, ec = 0;
            for (int i = 0; i < ns.Used && nc < ns.Count; i++) if (nodes.Read(i).Active != 0) nodeIndices[nc++] = i;
            for (int i = 0; i < es.Used && ec < es.Count; i++) if (edges.Read(i).Active != 0) edgeIndices[ec++] = i;
            if (nc != ns.Count || ec != es.Count) return DeterministicStatus.ProtocolConflict;
            bool paged = options.IsValid && options.Nodes.Snapshots == SnapshotPolicy.CopyOnWrite && options.Nodes.SnapshotSlots != 0;
            int np = paged ? System.Math.Max(1, System.Math.Min(options.Nodes.MaxCapacity, options.Nodes.PageBytes / sizeof(SlotMapNode<GraphNodeRecord<TNode>>))) : 1;
            paged = options.IsValid && options.Edges.Snapshots == SnapshotPolicy.CopyOnWrite && options.Edges.SnapshotSlots != 0;
            int ep = paged ? System.Math.Max(1, System.Math.Min(options.Edges.MaxCapacity, options.Edges.PageBytes / sizeof(SlotMapNode<GraphEdgeRecord<TEdge>>))) : 1;
            int ap = paged ? System.Math.Max(1, System.Math.Min(options.Edges.MaxCapacity * 2, options.Edges.PageBytes / sizeof(GraphIncidence))) : 1;
            for (int phase = 0; phase < 2; phase++)
            {
                DeterministicStatus s;
                s = ReserveIndices<SlotMapNode<GraphNodeRecord<TNode>>, TNodes>(ref nodes, nodeIndices, nc, 1, np, phase); if (!s.IsSuccess()) return s;
                if (ns.FreeTail != 0) { s = NodeReserve(ref nodes, ns.FreeTail - 1, phase); if (!s.IsSuccess()) return s; }
                s = ReserveIndices<SlotMapNode<GraphEdgeRecord<TEdge>>, TEdges>(ref edges, edgeIndices, ec, 1, ep, phase); if (!s.IsSuccess()) return s;
                s = ReserveIndices<GraphIncidence, TArcs>(ref arcs, edgeIndices, ec, 2, ap, phase); if (!s.IsSuccess()) return s;
                if (es.FreeTail != 0) { s = Reserve<SlotMapNode<GraphEdgeRecord<TEdge>>, TEdges>(ref edges, edges.Capacity, es.FreeTail - 1, 1, phase); if (!s.IsSuccess()) return s; }
            }
            for (int i = 0; i < nc; i++) SlotMapKernel<GraphNodeRecord<TNode>, TNodes>.RemoveReserved(ref nodes, ref ns, nodeIndices[i], nodes.Read(nodeIndices[i]));
            for (int i = 0; i < ec; i++)
            {
                int index = edgeIndices[i]; SlotMapKernel<GraphEdgeRecord<TEdge>, TEdges>.RemoveReserved(ref edges, ref es, index, edges.Read(index));
                arcs.Write(index * 2, default); arcs.Write(index * 2 + 1, default);
            }
            nodes.CommitWrite(); edges.CommitWrite(); arcs.CommitWrite(); return DeterministicStatus.None;
        }
        private static DeterministicStatus ReserveIndices<T, TBuffer>(ref TBuffer buffer, int* indices, int count, int stride, int perPage, int phase)
            where T : unmanaged where TBuffer : struct, IDeterministicBuffer<T>
        {
            for (int i = 0; i < count;)
            {
                int start = indices[i] * stride, end = start + stride, lastPage = (end - 1) / perPage; i++;
                // Merge only consecutive touched pages. No clean COW page is detached to bridge a gap.
                while (i < count && indices[i] * stride / perPage <= lastPage + 1)
                { end = indices[i] * stride + stride; lastPage = (end - 1) / perPage; i++; }
                var s = Reserve<T, TBuffer>(ref buffer, buffer.Capacity, start, end - start, phase); if (!s.IsSuccess()) return s;
            }
            return DeterministicStatus.None;
        }
        /// <summary>Called after staging validation and reservation. Rebuilds sorted caches in O(V+E).</summary>
        public static void Rebuild(ref TNodes nodes, ref TEdges edges, ref TArcs arcs, in SlotMapState nodeState, in SlotMapState edgeState, in GraphOptions options)
        {
            for (int i = 0; i < nodeState.Used; i++)
            { var slot = nodes.Read(i); var data = slot.Value.Data; slot.Value = new GraphNodeRecord<TNode> { Data = data }; nodes.Write(i, slot); }
            for (int i = 0; i < edgeState.Used * 2; i++) arcs.Write(i, default);
            for (int i = 0; i < edgeState.Used; i++)
            {
                var slot = edges.Read(i); if (slot.Active == 0) continue;
                var handle = new GraphEdgeHandle(new StableHandle((uint)i, slot.Generation));
                Append(ref nodes, ref arcs, slot.Value.Source, slot.Value.Target, handle, 0, true, i * 2 + 1);
                Append(ref nodes, ref arcs, slot.Value.Target, slot.Value.Source, handle, 1, options.Direction == GraphDirection.Undirected, i * 2 + 2);
            }
        }
        private static void Append(ref TNodes nodes, ref TArcs arcs, GraphNodeHandle owner, GraphNodeHandle neighbor,
            GraphEdgeHandle edge, byte side, bool outgoing, int cursor)
        {
            var slot = nodes.Read((int)owner.Index); var n = slot.Value; int tail = outgoing ? n.OutTail : n.InTail;
            arcs.Write(cursor - 1, new GraphIncidence { Edge = edge, Neighbor = neighbor, Side = side, Previous = tail });
            if (tail != 0) { var p = arcs.Read(tail - 1); p.Next = cursor; arcs.Write(tail - 1, p); }
            if (outgoing) { if (tail == 0) n.OutHead = cursor; n.OutTail = cursor; n.OutDegree++; }
            else { if (tail == 0) n.InHead = cursor; n.InTail = cursor; n.InDegree++; }
            slot.Value = n; nodes.Write((int)owner.Index, slot);
        }
    }
}
