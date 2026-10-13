using System;

namespace LegendaryTools.Deterministic
{
    internal readonly struct ManagedGraphReadBuffer<T> : IDeterministicImportBuffer<T> where T : unmanaged
    {
        private readonly ManagedStorage<T> storage;
        private readonly ManagedBufferSnapshot<T> snapshot;
        private readonly bool captured;
        internal ManagedGraphReadBuffer(ManagedStorage<T> storage) { this.storage = storage; snapshot = default; captured = false; }
        internal ManagedGraphReadBuffer(ManagedBufferSnapshot<T> snapshot) { this.snapshot = snapshot; storage = default; captured = true; }
        public int Capacity => captured ? snapshot.Capacity : storage.Capacity;
        public ulong Revision => captured ? snapshot.Lease : storage.Revision;
        public bool IsCreated => captured ? snapshot.IsCreated : storage.IsCreated;
        public T Read(int index) => captured ? snapshot.Owner.ReadSnapshot(snapshot.Slot, index) : storage.Read(index);
        public void Write(int index, in T value) { }
        public DeterministicStatus CanPrepareWrite(int required, int start, int count) => DeterministicStatus.InvalidConfiguration;
        public DeterministicStatus PrepareWrite(int required, int start, int count) => DeterministicStatus.InvalidConfiguration;
        public void CommitWrite() { }
        public DeterministicStatus CanReplaceCapacity(int capacity) => DeterministicStatus.InvalidConfiguration;
        public DeterministicStatus PrepareReplace(int capacity) => DeterministicStatus.InvalidConfiguration;
    }
    public readonly struct ManagedGraphReadOnly<TNode, TEdge> : IDeterministicGraphReader<TNode, TEdge>
        where TNode : unmanaged where TEdge : unmanaged
    {
        private readonly GraphBufferReader<TNode, TEdge, ManagedGraphReadBuffer<SlotMapNode<GraphNodeRecord<TNode>>>,
            ManagedGraphReadBuffer<SlotMapNode<GraphEdgeRecord<TEdge>>>, ManagedGraphReadBuffer<GraphIncidence>> reader;
        internal ManagedGraphReadOnly(ManagedStorage<SlotMapNode<GraphNodeRecord<TNode>>> nodes,
            ManagedStorage<SlotMapNode<GraphEdgeRecord<TEdge>>> edges, ManagedStorage<GraphIncidence> arcs,
            SlotMapState ns, SlotMapState es, GraphOptions options)
        { reader = new GraphBufferReader<TNode, TEdge, ManagedGraphReadBuffer<SlotMapNode<GraphNodeRecord<TNode>>>, ManagedGraphReadBuffer<SlotMapNode<GraphEdgeRecord<TEdge>>>, ManagedGraphReadBuffer<GraphIncidence>>(
            new ManagedGraphReadBuffer<SlotMapNode<GraphNodeRecord<TNode>>>(nodes), new ManagedGraphReadBuffer<SlotMapNode<GraphEdgeRecord<TEdge>>>(edges), new ManagedGraphReadBuffer<GraphIncidence>(arcs), ns, es, options); }
        internal ManagedGraphReadOnly(in ManagedGraphSnapshot<TNode, TEdge> snapshot)
        { reader = new GraphBufferReader<TNode, TEdge, ManagedGraphReadBuffer<SlotMapNode<GraphNodeRecord<TNode>>>, ManagedGraphReadBuffer<SlotMapNode<GraphEdgeRecord<TEdge>>>, ManagedGraphReadBuffer<GraphIncidence>>(
            new ManagedGraphReadBuffer<SlotMapNode<GraphNodeRecord<TNode>>>(snapshot.Nodes), new ManagedGraphReadBuffer<SlotMapNode<GraphEdgeRecord<TEdge>>>(snapshot.Edges), new ManagedGraphReadBuffer<GraphIncidence>(snapshot.Arcs), snapshot.NodeState, snapshot.EdgeState, snapshot.Options); }
        public GraphOptions Options => reader.Options;
        public int NodeCount => reader.NodeCount;
        public int EdgeCount => reader.EdgeCount;
        public int NodeExtent => reader.NodeExtent;
        public int EdgeExtent => reader.EdgeExtent;
        public DeterministicStatus Check() => reader.Check();
        public DeterministicStatus TryGetNode(GraphNodeHandle h, out TNode data) => reader.TryGetNode(h, out data);
        public DeterministicStatus TryGetEdge(GraphEdgeHandle h, out GraphEdge<TEdge> edge) => reader.TryGetEdge(h, out edge);
        public DeterministicStatus TryNodeAt(int ordinal, out GraphNodeHandle h, out TNode data) => reader.TryNodeAt(ordinal, out h, out data);
        public DeterministicStatus TryEdgeAt(int ordinal, out GraphEdgeHandle h, out GraphEdge<TEdge> edge) => reader.TryEdgeAt(ordinal, out h, out edge);
        public DeterministicStatus TryNodeOrdinal(GraphNodeHandle h, out int ordinal) => reader.TryNodeOrdinal(h, out ordinal);
        public DeterministicStatus TryFirstArc(GraphNodeHandle node, GraphTraversal traversal, out int cursor) => reader.TryFirstArc(node, traversal, out cursor);
        public DeterministicStatus TryReadArc(int cursor, out GraphArc arc, out int next) => reader.TryReadArc(cursor, out arc, out next);
        public GraphNodeEnumerator<TNode, TEdge, ManagedGraphReadOnly<TNode, TEdge>> Nodes => new GraphNodeEnumerator<TNode, TEdge, ManagedGraphReadOnly<TNode, TEdge>>(this);
        public GraphEdgeEnumerator<TNode, TEdge, ManagedGraphReadOnly<TNode, TEdge>> Edges => new GraphEdgeEnumerator<TNode, TEdge, ManagedGraphReadOnly<TNode, TEdge>>(this);
        public GraphArcEnumerator<TNode, TEdge, ManagedGraphReadOnly<TNode, TEdge>> Outgoing(GraphNodeHandle node) => new GraphArcEnumerator<TNode, TEdge, ManagedGraphReadOnly<TNode, TEdge>>(this, node, GraphTraversal.Outgoing);
        public GraphArcEnumerator<TNode, TEdge, ManagedGraphReadOnly<TNode, TEdge>> Incoming(GraphNodeHandle node) => new GraphArcEnumerator<TNode, TEdge, ManagedGraphReadOnly<TNode, TEdge>>(this, node, GraphTraversal.Incoming);
    }
    /// <summary>Composite lease. Copies alias the same lease; dispose once after all readers finish.</summary>
    public readonly partial struct ManagedGraphSnapshot<TNode, TEdge> : IDisposable where TNode : unmanaged where TEdge : unmanaged
    {
        internal readonly ManagedBufferSnapshot<SlotMapNode<GraphNodeRecord<TNode>>> Nodes;
        internal readonly ManagedBufferSnapshot<SlotMapNode<GraphEdgeRecord<TEdge>>> Edges;
        internal readonly ManagedBufferSnapshot<GraphIncidence> Arcs;
        internal readonly SlotMapState NodeState, EdgeState;
        public readonly GraphOptions Options;
        internal ManagedGraphSnapshot(ManagedBufferSnapshot<SlotMapNode<GraphNodeRecord<TNode>>> nodes,
            ManagedBufferSnapshot<SlotMapNode<GraphEdgeRecord<TEdge>>> edges, ManagedBufferSnapshot<GraphIncidence> arcs,
            SlotMapState ns, SlotMapState es, GraphOptions options)
        { Nodes = nodes; Edges = edges; Arcs = arcs; NodeState = ns; EdgeState = es; Options = options; }
        public bool IsCreated => Nodes.IsCreated && Edges.IsCreated && Arcs.IsCreated;
        public int NodeCount => NodeState.Count;
        public int EdgeCount => EdgeState.Count;
        public ManagedGraphReadOnly<TNode, TEdge> AsReadOnly() => new ManagedGraphReadOnly<TNode, TEdge>(this);
        public DeterministicStatus TryGetNode(GraphNodeHandle h, out TNode data) => AsReadOnly().TryGetNode(h, out data);
        public DeterministicStatus TryGetEdge(GraphEdgeHandle h, out GraphEdge<TEdge> edge) => AsReadOnly().TryGetEdge(h, out edge);
        public void Dispose() { Nodes.Dispose(); Edges.Dispose(); Arcs.Dispose(); }
    }
    public sealed unsafe partial class DeterministicGraph<TNode, TEdge> : IDisposable where TNode : unmanaged where TEdge : unmanaged
    {
        internal ManagedStorage<SlotMapNode<GraphNodeRecord<TNode>>> NodesStorage;
        internal ManagedStorage<SlotMapNode<GraphEdgeRecord<TEdge>>> EdgesStorage;
        internal ManagedStorage<GraphIncidence> ArcsStorage;
        internal SlotMapState NodeState, EdgeState;
        private readonly byte[] workspace;
        public GraphOptions Options { get; }
        public DeterministicGraph(int nodeCapacity, int edgeCapacity) : this(new GraphOptions(nodeCapacity, edgeCapacity)) { }
        public DeterministicGraph(GraphOptions options)
        {
            if (!options.IsValid) DeterministicStatus.InvalidConfiguration.ThrowIfFailure(); Options = options;
            GraphScratch.TryMeasure(options.Nodes.MaxCapacity, options.Edges.MaxCapacity, out int bytes); workspace = new byte[bytes];
            NodesStorage = new ManagedStorage<SlotMapNode<GraphNodeRecord<TNode>>>(options.Nodes);
            EdgesStorage = new ManagedStorage<SlotMapNode<GraphEdgeRecord<TEdge>>>(options.Edges);
            ArcsStorage = new ManagedStorage<GraphIncidence>(options.AdjacencyOptions);
        }
        public bool IsCreated => NodesStorage.IsCreated && EdgesStorage.IsCreated && ArcsStorage.IsCreated;
        public int NodeCount => NodeState.Count;
        public int EdgeCount => EdgeState.Count;
        public int NodeCapacity => NodesStorage.Capacity;
        public int EdgeCapacity => EdgesStorage.Capacity;
        public ulong Revision => NodesStorage.Revision;
        public ManagedGraphReadOnly<TNode, TEdge> AsReadOnly() => new ManagedGraphReadOnly<TNode, TEdge>(NodesStorage, EdgesStorage, ArcsStorage, NodeState, EdgeState, Options);
        public GraphNodeEnumerator<TNode, TEdge, ManagedGraphReadOnly<TNode, TEdge>> Nodes => AsReadOnly().Nodes;
        public GraphEdgeEnumerator<TNode, TEdge, ManagedGraphReadOnly<TNode, TEdge>> Edges => AsReadOnly().Edges;
        public GraphArcEnumerator<TNode, TEdge, ManagedGraphReadOnly<TNode, TEdge>> Outgoing(GraphNodeHandle node) => AsReadOnly().Outgoing(node);
        public GraphArcEnumerator<TNode, TEdge, ManagedGraphReadOnly<TNode, TEdge>> Incoming(GraphNodeHandle node) => AsReadOnly().Incoming(node);
        public DeterministicStatus TryGetNode(GraphNodeHandle h, out TNode data) => AsReadOnly().TryGetNode(h, out data);
        public DeterministicStatus TryGetEdge(GraphEdgeHandle h, out GraphEdge<TEdge> edge) => AsReadOnly().TryGetEdge(h, out edge);
        public DeterministicStatus TryAddNode(in TNode data, out GraphNodeHandle h) => GraphKernel<TNode, TEdge, ManagedStorage<SlotMapNode<GraphNodeRecord<TNode>>>, ManagedStorage<SlotMapNode<GraphEdgeRecord<TEdge>>>, ManagedStorage<GraphIncidence>>.TryAddNode(ref NodesStorage, ref NodeState, data, out h);
        public GraphNodeHandle AddNode(TNode data) { TryAddNode(data, out var h).ThrowIfFailure(); return h; }
        public DeterministicStatus TrySetNode(GraphNodeHandle h, in TNode data) => GraphKernel<TNode, TEdge, ManagedStorage<SlotMapNode<GraphNodeRecord<TNode>>>, ManagedStorage<SlotMapNode<GraphEdgeRecord<TEdge>>>, ManagedStorage<GraphIncidence>>.TrySetNode(ref NodesStorage, NodeState, h, data);
        public DeterministicStatus TrySetEdge(GraphEdgeHandle h, in TEdge data) => GraphKernel<TNode, TEdge, ManagedStorage<SlotMapNode<GraphNodeRecord<TNode>>>, ManagedStorage<SlotMapNode<GraphEdgeRecord<TEdge>>>, ManagedStorage<GraphIncidence>>.TrySetEdge(ref NodesStorage, ref EdgesStorage, EdgeState, h, data);
        public DeterministicStatus TryAddEdge(GraphNodeHandle source, GraphNodeHandle target, in TEdge data, out GraphEdgeHandle h)
        {
            fixed (byte* p = workspace) { var scratch = new CanonicalScratch(p, workspace.Length);
                return GraphKernel<TNode, TEdge, ManagedStorage<SlotMapNode<GraphNodeRecord<TNode>>>, ManagedStorage<SlotMapNode<GraphEdgeRecord<TEdge>>>, ManagedStorage<GraphIncidence>>.TryAddEdge(ref NodesStorage, ref EdgesStorage, ref ArcsStorage, NodeState, ref EdgeState, Options, source, target, data, ref scratch, out h); }
        }
        public GraphEdgeHandle AddEdge(GraphNodeHandle source, GraphNodeHandle target, TEdge data) { TryAddEdge(source, target, data, out var h).ThrowIfFailure(); return h; }
        public DeterministicStatus TryRemoveEdge(GraphEdgeHandle h) => GraphKernel<TNode, TEdge, ManagedStorage<SlotMapNode<GraphNodeRecord<TNode>>>, ManagedStorage<SlotMapNode<GraphEdgeRecord<TEdge>>>, ManagedStorage<GraphIncidence>>.TryRemoveEdge(ref NodesStorage, ref EdgesStorage, ref ArcsStorage, ref EdgeState, Options, h);
        public DeterministicStatus TryRemoveNode(GraphNodeHandle h)
        {
            fixed (byte* p = workspace) { var scratch = new CanonicalScratch(p, workspace.Length);
                return GraphKernel<TNode, TEdge, ManagedStorage<SlotMapNode<GraphNodeRecord<TNode>>>, ManagedStorage<SlotMapNode<GraphEdgeRecord<TEdge>>>, ManagedStorage<GraphIncidence>>.TryRemoveNode(ref NodesStorage, ref EdgesStorage, ref ArcsStorage, ref NodeState, ref EdgeState, Options, h, ref scratch); }
        }
        public DeterministicStatus TryClear() => GraphKernel<TNode, TEdge, ManagedStorage<SlotMapNode<GraphNodeRecord<TNode>>>, ManagedStorage<SlotMapNode<GraphEdgeRecord<TEdge>>>, ManagedStorage<GraphIncidence>>.TryClear(ref NodesStorage, ref EdgesStorage, ref ArcsStorage, ref NodeState, ref EdgeState, Options);
        public DeterministicStatus TryCapture(out ManagedGraphSnapshot<TNode, TEdge> snapshot)
        {
            snapshot = default; var s = NodesStorage.TryCapture(out var n); if (!s.IsSuccess()) return s;
            s = EdgesStorage.TryCapture(out var e); if (!s.IsSuccess()) { n.Dispose(); return s; }
            s = ArcsStorage.TryCapture(out var a); if (!s.IsSuccess()) { n.Dispose(); e.Dispose(); return s; }
            snapshot = new ManagedGraphSnapshot<TNode, TEdge>(n, e, a, NodeState, EdgeState, Options); return DeterministicStatus.None;
        }
        public DeterministicStatus TryRestore(in ManagedGraphSnapshot<TNode, TEdge> snapshot)
        {
            if (!IsCreated) return DeterministicStatus.Disposed;
            if (!ReferenceEquals(NodesStorage.Owner, snapshot.Nodes.Owner) || !ReferenceEquals(EdgesStorage.Owner, snapshot.Edges.Owner) || !ReferenceEquals(ArcsStorage.Owner, snapshot.Arcs.Owner)) return DeterministicStatus.InvalidOwner;
            if (!snapshot.IsCreated) return DeterministicStatus.Disposed;
            NodesStorage.TryRestore(snapshot.Nodes); EdgesStorage.TryRestore(snapshot.Edges); ArcsStorage.TryRestore(snapshot.Arcs);
            NodeState = snapshot.NodeState; EdgeState = snapshot.EdgeState; return DeterministicStatus.None;
        }
        public void Dispose() { NodesStorage.Dispose(); EdgesStorage.Dispose(); ArcsStorage.Dispose(); NodeState = EdgeState = default; }
    }
}
