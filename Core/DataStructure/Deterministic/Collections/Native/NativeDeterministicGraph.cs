using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace LegendaryTools.Deterministic
{
    internal readonly unsafe struct NativeGraphReadBuffer<T> : IDeterministicImportBuffer<T> where T : unmanaged
    {
        private readonly NativeStorage<T> storage;
        private readonly int slot;
        private readonly ulong lease;
        private readonly bool captured;
        internal NativeGraphReadBuffer(NativeStorage<T> storage) { this.storage = storage; slot = 0; lease = 0; captured = false; }
        internal NativeGraphReadBuffer(NativeStorage<T> storage, int slot, ulong lease) { this.storage = storage; this.slot = slot; this.lease = lease; captured = true; }
        public int Capacity => captured ? IsCreated ? storage.Data->SnapshotCapacities[slot] : 0 : storage.Capacity;
        public ulong Revision => captured ? lease : storage.Revision;
        internal bool HasOwner => storage.IsCreated;
        public bool IsCreated => captured ? storage.ValidSnapshot(slot, lease) : storage.IsCreated;
        public T Read(int index) => captured ? storage.ReadSnapshot(slot, index) : storage.Read(index);
        public void Write(int index, in T value) { }
        public DeterministicStatus CanPrepareWrite(int required, int start, int count) => DeterministicStatus.InvalidConfiguration;
        public DeterministicStatus PrepareWrite(int required, int start, int count) => DeterministicStatus.InvalidConfiguration;
        public void CommitWrite() { }
        public DeterministicStatus CanReplaceCapacity(int capacity) => DeterministicStatus.InvalidConfiguration;
        public DeterministicStatus PrepareReplace(int capacity) => DeterministicStatus.InvalidConfiguration;
    }
    [NativeContainer, NativeContainerIsReadOnly]
    public readonly unsafe partial struct NativeGraphReadOnly<TNode, TEdge> : IDeterministicGraphReader<TNode, TEdge>
        where TNode : unmanaged where TEdge : unmanaged
    {
        private readonly GraphBufferReader<TNode, TEdge, NativeGraphReadBuffer<SlotMapNode<GraphNodeRecord<TNode>>>,
            NativeGraphReadBuffer<SlotMapNode<GraphEdgeRecord<TEdge>>>, NativeGraphReadBuffer<GraphIncidence>> reader;
        private readonly bool hasSafety;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal readonly AtomicSafetyHandle m_Safety;
#endif
        internal NativeGraphReadOnly(NativeGraphReadBuffer<SlotMapNode<GraphNodeRecord<TNode>>> nodes,
            NativeGraphReadBuffer<SlotMapNode<GraphEdgeRecord<TEdge>>> edges, NativeGraphReadBuffer<GraphIncidence> arcs,
            SlotMapState ns, SlotMapState es, GraphOptions options
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            , AtomicSafetyHandle safety
#endif
            )
        {
            hasSafety = nodes.HasOwner;
            reader = new GraphBufferReader<TNode, TEdge, NativeGraphReadBuffer<SlotMapNode<GraphNodeRecord<TNode>>>, NativeGraphReadBuffer<SlotMapNode<GraphEdgeRecord<TEdge>>>, NativeGraphReadBuffer<GraphIncidence>>(nodes, edges, arcs, ns, es, options);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = safety;
#endif
        }
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void CheckRead()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (hasSafety) AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
        }
        public GraphOptions Options { get { CheckRead(); return reader.Options; } }
        public int NodeCount { get { CheckRead(); return reader.NodeCount; } }
        public int EdgeCount { get { CheckRead(); return reader.EdgeCount; } }
        public int NodeExtent { get { CheckRead(); return reader.NodeExtent; } }
        public int EdgeExtent { get { CheckRead(); return reader.EdgeExtent; } }
        public DeterministicStatus Check() { CheckRead(); return reader.Check(); }
        public DeterministicStatus TryGetNode(GraphNodeHandle h, out TNode data) { CheckRead(); return reader.TryGetNode(h, out data); }
        public DeterministicStatus TryGetEdge(GraphEdgeHandle h, out GraphEdge<TEdge> edge) { CheckRead(); return reader.TryGetEdge(h, out edge); }
        public DeterministicStatus TryNodeAt(int ordinal, out GraphNodeHandle h, out TNode data) { CheckRead(); return reader.TryNodeAt(ordinal, out h, out data); }
        public DeterministicStatus TryEdgeAt(int ordinal, out GraphEdgeHandle h, out GraphEdge<TEdge> edge) { CheckRead(); return reader.TryEdgeAt(ordinal, out h, out edge); }
        public DeterministicStatus TryNodeOrdinal(GraphNodeHandle h, out int ordinal) { CheckRead(); return reader.TryNodeOrdinal(h, out ordinal); }
        public DeterministicStatus TryFirstArc(GraphNodeHandle node, GraphTraversal traversal, out int cursor) { CheckRead(); return reader.TryFirstArc(node, traversal, out cursor); }
        public DeterministicStatus TryReadArc(int cursor, out GraphArc arc, out int next) { CheckRead(); return reader.TryReadArc(cursor, out arc, out next); }
        public GraphNodeEnumerator<TNode, TEdge, NativeGraphReadOnly<TNode, TEdge>> Nodes => new GraphNodeEnumerator<TNode, TEdge, NativeGraphReadOnly<TNode, TEdge>>(this);
        public GraphEdgeEnumerator<TNode, TEdge, NativeGraphReadOnly<TNode, TEdge>> Edges => new GraphEdgeEnumerator<TNode, TEdge, NativeGraphReadOnly<TNode, TEdge>>(this);
        public GraphArcEnumerator<TNode, TEdge, NativeGraphReadOnly<TNode, TEdge>> Outgoing(GraphNodeHandle node) => new GraphArcEnumerator<TNode, TEdge, NativeGraphReadOnly<TNode, TEdge>>(this, node, GraphTraversal.Outgoing);
        public GraphArcEnumerator<TNode, TEdge, NativeGraphReadOnly<TNode, TEdge>> Incoming(GraphNodeHandle node) => new GraphArcEnumerator<TNode, TEdge, NativeGraphReadOnly<TNode, TEdge>>(this, node, GraphTraversal.Incoming);
    }
    [NativeContainer, NativeContainerIsReadOnly]
    public unsafe partial struct NativeGraphSnapshot<TNode, TEdge> : IDisposable where TNode : unmanaged where TEdge : unmanaged
    {
        internal NativeStorage<SlotMapNode<GraphNodeRecord<TNode>>> Nodes;
        internal NativeStorage<SlotMapNode<GraphEdgeRecord<TEdge>>> Edges;
        internal NativeStorage<GraphIncidence> Arcs;
        internal int NodeSlot, EdgeSlot, ArcSlot;
        internal ulong NodeLease, EdgeLease, ArcLease;
        internal SlotMapState NodeState, EdgeState;
        public GraphOptions Options { get; internal set; }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif
        public bool IsCreated
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (Nodes.IsCreated) AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                return Nodes.ValidSnapshot(NodeSlot, NodeLease) && Edges.ValidSnapshot(EdgeSlot, EdgeLease) && Arcs.ValidSnapshot(ArcSlot, ArcLease);
            }
        }
        public int NodeCount => NodeState.Count;
        public int EdgeCount => EdgeState.Count;
        public NativeGraphReadOnly<TNode, TEdge> AsReadOnly() => new NativeGraphReadOnly<TNode, TEdge>(
            new NativeGraphReadBuffer<SlotMapNode<GraphNodeRecord<TNode>>>(Nodes, NodeSlot, NodeLease),
            new NativeGraphReadBuffer<SlotMapNode<GraphEdgeRecord<TEdge>>>(Edges, EdgeSlot, EdgeLease),
            new NativeGraphReadBuffer<GraphIncidence>(Arcs, ArcSlot, ArcLease), NodeState, EdgeState, Options
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            , m_Safety
#endif
            );
        public DeterministicStatus TryGetNode(GraphNodeHandle h, out TNode data) => AsReadOnly().TryGetNode(h, out data);
        public DeterministicStatus TryGetEdge(GraphEdgeHandle h, out GraphEdge<TEdge> edge) => AsReadOnly().TryGetEdge(h, out edge);
        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Nodes.IsCreated) AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            Nodes.ReleaseSnapshot(NodeSlot, NodeLease); Edges.ReleaseSnapshot(EdgeSlot, EdgeLease); Arcs.ReleaseSnapshot(ArcSlot, ArcLease);
            Nodes = default; Edges = default; Arcs = default;
        }
    }
    [NativeContainer]
    public unsafe partial struct NativeDeterministicGraph<TNode, TEdge> : IDisposable where TNode : unmanaged where TEdge : unmanaged
    {
        internal NativeStorage<SlotMapNode<GraphNodeRecord<TNode>>> NodesStorage;
        internal NativeStorage<SlotMapNode<GraphEdgeRecord<TEdge>>> EdgesStorage;
        internal NativeStorage<GraphIncidence> ArcsStorage;
        private NativeStorage<byte> workspace;
        private GraphOptions options;
        internal ref SlotMapState NodeState => ref NodesStorage.Data->SlotState;
        internal ref SlotMapState EdgeState => ref EdgesStorage.Data->SlotState;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif
        public NativeDeterministicGraph(int nodeCapacity, int edgeCapacity, Allocator allocator) : this(new GraphOptions(nodeCapacity, edgeCapacity), allocator) { }
        public NativeDeterministicGraph(GraphOptions options, Allocator allocator)
        {
            this = default; if (!options.IsValid) DeterministicStatus.InvalidConfiguration.ThrowIfFailure(); this.options = options;
            try
            {
                NodesStorage = NativeStorage<SlotMapNode<GraphNodeRecord<TNode>>>.Create(options.Nodes, allocator);
                EdgesStorage = NativeStorage<SlotMapNode<GraphEdgeRecord<TEdge>>>.Create(options.Edges, allocator);
                ArcsStorage = NativeStorage<GraphIncidence>.Create(options.AdjacencyOptions, allocator);
                GraphScratch.TryMeasure(options.Nodes.MaxCapacity, options.Edges.MaxCapacity, out int bytes);
                workspace = NativeStorage<byte>.Create(new BufferOptions(bytes, snapshotSlots: 0), allocator);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = AtomicSafetyHandle.Create(); AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);
#endif
            }
            catch { NodesStorage.Destroy(); EdgesStorage.Destroy(); ArcsStorage.Destroy(); workspace.Destroy(); throw; }
        }
        public bool IsCreated => NodesStorage.IsCreated && EdgesStorage.IsCreated && ArcsStorage.IsCreated;
        public GraphOptions Options { get { CheckRead(); return options; } }
        public int NodeCount { get { CheckRead(); return IsCreated ? NodeState.Count : 0; } }
        public int EdgeCount { get { CheckRead(); return IsCreated ? EdgeState.Count : 0; } }
        public int NodeCapacity { get { CheckRead(); return NodesStorage.Capacity; } }
        public int EdgeCapacity { get { CheckRead(); return EdgesStorage.Capacity; } }
        public ulong Revision { get { CheckRead(); return NodesStorage.Revision; } }
        public NativeGraphReadOnly<TNode, TEdge> AsReadOnly()
        {
            CheckRead(); return new NativeGraphReadOnly<TNode, TEdge>(new NativeGraphReadBuffer<SlotMapNode<GraphNodeRecord<TNode>>>(NodesStorage),
                new NativeGraphReadBuffer<SlotMapNode<GraphEdgeRecord<TEdge>>>(EdgesStorage), new NativeGraphReadBuffer<GraphIncidence>(ArcsStorage),
                IsCreated ? NodeState : default, IsCreated ? EdgeState : default, options
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                , m_Safety
#endif
                );
        }
        public GraphNodeEnumerator<TNode, TEdge, NativeGraphReadOnly<TNode, TEdge>> Nodes => AsReadOnly().Nodes;
        public GraphEdgeEnumerator<TNode, TEdge, NativeGraphReadOnly<TNode, TEdge>> Edges => AsReadOnly().Edges;
        public GraphArcEnumerator<TNode, TEdge, NativeGraphReadOnly<TNode, TEdge>> Outgoing(GraphNodeHandle node) => AsReadOnly().Outgoing(node);
        public GraphArcEnumerator<TNode, TEdge, NativeGraphReadOnly<TNode, TEdge>> Incoming(GraphNodeHandle node) => AsReadOnly().Incoming(node);
        public DeterministicStatus TryGetNode(GraphNodeHandle h, out TNode data) => AsReadOnly().TryGetNode(h, out data);
        public DeterministicStatus TryGetEdge(GraphEdgeHandle h, out GraphEdge<TEdge> edge) => AsReadOnly().TryGetEdge(h, out edge);
        public DeterministicStatus TryAddNode(in TNode data, out GraphNodeHandle h)
        {
            CheckWrite(); h = default; if (!IsCreated) return DeterministicStatus.Disposed;
            return GraphKernel<TNode, TEdge, NativeStorage<SlotMapNode<GraphNodeRecord<TNode>>>, NativeStorage<SlotMapNode<GraphEdgeRecord<TEdge>>>, NativeStorage<GraphIncidence>>.TryAddNode(ref NodesStorage, ref NodeState, data, out h);
        }
        public GraphNodeHandle AddNode(TNode data) { var s = TryAddNode(data, out var h); NativeChecks.Status(s); return h; }
        public DeterministicStatus TrySetNode(GraphNodeHandle h, in TNode data)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            return GraphKernel<TNode, TEdge, NativeStorage<SlotMapNode<GraphNodeRecord<TNode>>>, NativeStorage<SlotMapNode<GraphEdgeRecord<TEdge>>>, NativeStorage<GraphIncidence>>.TrySetNode(ref NodesStorage, NodeState, h, data);
        }
        public DeterministicStatus TrySetEdge(GraphEdgeHandle h, in TEdge data)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            return GraphKernel<TNode, TEdge, NativeStorage<SlotMapNode<GraphNodeRecord<TNode>>>, NativeStorage<SlotMapNode<GraphEdgeRecord<TEdge>>>, NativeStorage<GraphIncidence>>.TrySetEdge(ref NodesStorage, ref EdgesStorage, EdgeState, h, data);
        }
        public DeterministicStatus TryAddEdge(GraphNodeHandle source, GraphNodeHandle target, in TEdge data, out GraphEdgeHandle h)
        {
            CheckWrite(); h = default; if (!IsCreated) return DeterministicStatus.Disposed; var scratch = new CanonicalScratch(workspace.Data->Values, workspace.Capacity);
            return GraphKernel<TNode, TEdge, NativeStorage<SlotMapNode<GraphNodeRecord<TNode>>>, NativeStorage<SlotMapNode<GraphEdgeRecord<TEdge>>>, NativeStorage<GraphIncidence>>.TryAddEdge(ref NodesStorage, ref EdgesStorage, ref ArcsStorage, NodeState, ref EdgeState, options, source, target, data, ref scratch, out h);
        }
        public GraphEdgeHandle AddEdge(GraphNodeHandle source, GraphNodeHandle target, TEdge data) { var s = TryAddEdge(source, target, data, out var h); NativeChecks.Status(s); return h; }
        public DeterministicStatus TryRemoveEdge(GraphEdgeHandle h)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            return GraphKernel<TNode, TEdge, NativeStorage<SlotMapNode<GraphNodeRecord<TNode>>>, NativeStorage<SlotMapNode<GraphEdgeRecord<TEdge>>>, NativeStorage<GraphIncidence>>.TryRemoveEdge(ref NodesStorage, ref EdgesStorage, ref ArcsStorage, ref EdgeState, options, h);
        }
        public DeterministicStatus TryRemoveNode(GraphNodeHandle h)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed; var scratch = new CanonicalScratch(workspace.Data->Values, workspace.Capacity);
            return GraphKernel<TNode, TEdge, NativeStorage<SlotMapNode<GraphNodeRecord<TNode>>>, NativeStorage<SlotMapNode<GraphEdgeRecord<TEdge>>>, NativeStorage<GraphIncidence>>.TryRemoveNode(ref NodesStorage, ref EdgesStorage, ref ArcsStorage, ref NodeState, ref EdgeState, options, h, ref scratch);
        }
        public DeterministicStatus TryClear()
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            return GraphKernel<TNode, TEdge, NativeStorage<SlotMapNode<GraphNodeRecord<TNode>>>, NativeStorage<SlotMapNode<GraphEdgeRecord<TEdge>>>, NativeStorage<GraphIncidence>>.TryClear(ref NodesStorage, ref EdgesStorage, ref ArcsStorage, ref NodeState, ref EdgeState, Options);
        }
        public DeterministicStatus TryCapture(out NativeGraphSnapshot<TNode, TEdge> snapshot)
        {
            CheckWrite(); snapshot = default; var s = NodesStorage.TryCapture(out int n, out ulong nl); if (!s.IsSuccess()) return s;
            s = EdgesStorage.TryCapture(out int e, out ulong el); if (!s.IsSuccess()) { NodesStorage.ReleaseSnapshot(n, nl); return s; }
            s = ArcsStorage.TryCapture(out int a, out ulong al); if (!s.IsSuccess()) { NodesStorage.ReleaseSnapshot(n, nl); EdgesStorage.ReleaseSnapshot(e, el); return s; }
            snapshot = new NativeGraphSnapshot<TNode, TEdge> { Nodes = NodesStorage, Edges = EdgesStorage, Arcs = ArcsStorage,
                NodeSlot = n, EdgeSlot = e, ArcSlot = a, NodeLease = nl, EdgeLease = el, ArcLease = al, NodeState = NodeState, EdgeState = EdgeState, Options = options };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            snapshot.m_Safety = m_Safety;
#endif
            return DeterministicStatus.None;
        }
        public DeterministicStatus TryRestore(in NativeGraphSnapshot<TNode, TEdge> snapshot)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            if (snapshot.Nodes.Data != NodesStorage.Data || snapshot.Edges.Data != EdgesStorage.Data || snapshot.Arcs.Data != ArcsStorage.Data) return DeterministicStatus.InvalidOwner;
            if (!NodesStorage.ValidSnapshot(snapshot.NodeSlot, snapshot.NodeLease) || !EdgesStorage.ValidSnapshot(snapshot.EdgeSlot, snapshot.EdgeLease) ||
                !ArcsStorage.ValidSnapshot(snapshot.ArcSlot, snapshot.ArcLease)) return DeterministicStatus.Disposed;
            NodesStorage.TryRestore(snapshot.NodeSlot, snapshot.NodeLease); EdgesStorage.TryRestore(snapshot.EdgeSlot, snapshot.EdgeLease); ArcsStorage.TryRestore(snapshot.ArcSlot, snapshot.ArcLease);
            NodeState = snapshot.NodeState; EdgeState = snapshot.EdgeState; return DeterministicStatus.None;
        }
        public void Dispose()
        {
            if (!IsCreated) return; CheckWrite();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(m_Safety);
#endif
            NodesStorage.Destroy(); EdgesStorage.Destroy(); ArcsStorage.Destroy(); workspace.Destroy();
        }
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal void CheckRead()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (NodesStorage.IsCreated) AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
        }
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal void CheckWrite()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (NodesStorage.IsCreated) AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
        }
    }
}
