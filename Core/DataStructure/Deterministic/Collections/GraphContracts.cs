using System;

namespace LegendaryTools.Deterministic
{
    public enum GraphDirection : byte { Directed, Undirected }
    public enum GraphCyclePolicy : byte { Allow, Acyclic }
    public enum GraphTraversal : byte { Outgoing, Incoming }
    public enum GraphVersionRetention : byte { Leases, UntilOwnerDisposed }
    public enum GraphDeltaMode : byte { Forward, Reversible }
    public enum GraphTransferKind : byte { Patch, Checkpoint }
    public readonly struct GraphPersistenceOptions
    {
        public readonly int InitialVersionCapacity, MaxVersionCapacity;
        public readonly AllocationPolicy Allocation;
        public readonly GraphVersionRetention Retention;
        public GraphPersistenceOptions(int initialVersionCapacity, int maxVersionCapacity = -1,
            AllocationPolicy allocation = AllocationPolicy.Fixed, GraphVersionRetention retention = GraphVersionRetention.Leases)
        { InitialVersionCapacity = initialVersionCapacity; MaxVersionCapacity = maxVersionCapacity < 0 ? initialVersionCapacity : maxVersionCapacity; Allocation = allocation; Retention = retention; }
        public bool IsValid => InitialVersionCapacity > 0 && MaxVersionCapacity >= InitialVersionCapacity && MaxVersionCapacity <= int.MaxValue - 3 &&
            Allocation <= AllocationPolicy.Grow && Retention <= GraphVersionRetention.UntilOwnerDisposed &&
            (Allocation != AllocationPolicy.Fixed || InitialVersionCapacity == MaxVersionCapacity);
    }
    public readonly struct GraphVersionId : System.IEquatable<GraphVersionId>
    {
        public readonly int Slot;
        public readonly ulong Lease;
        public GraphVersionId(int slot, ulong lease) { Slot = slot; Lease = lease; }
        public bool IsValid => Slot >= 0 && Lease != 0;
        public bool Equals(GraphVersionId other) => Slot == other.Slot && Lease == other.Lease;
        public override bool Equals(object obj) => obj is GraphVersionId other && Equals(other);
        public override int GetHashCode() => unchecked(Slot * 397 ^ (int)Lease ^ (int)(Lease >> 32));
        public static bool operator ==(GraphVersionId a, GraphVersionId b) => a.Equals(b);
        public static bool operator !=(GraphVersionId a, GraphVersionId b) => !a.Equals(b);
    }
    public enum GraphMutationKind : byte { AddNode, AddEdge, SetNode, SetEdge, RemoveNode, RemoveEdge, Clear }
    public struct GraphMutation<TNode, TEdge> where TNode : unmanaged where TEdge : unmanaged
    {
        public GraphMutationKind Kind;
        public GraphNodeHandle Node, Source, Target;
        public GraphEdgeHandle Edge;
        public TNode NodeData;
        public TEdge EdgeData;
    }
    public readonly struct GraphMutationResult
    {
        public readonly GraphNodeHandle Node;
        public readonly GraphEdgeHandle Edge;
        public GraphMutationResult(GraphNodeHandle node, GraphEdgeHandle edge) { Node = node; Edge = edge; }
    }

    /// <summary>Topology rules are immutable. Storage and retention settings are local resources.</summary>
    public readonly struct GraphOptions
    {
        public readonly BufferOptions Nodes, Edges;
        public readonly GraphDirection Direction;
        public readonly GraphCyclePolicy Cycles;
        public readonly bool AllowParallelEdges, AllowSelfEdges;
        public GraphOptions(int nodeCapacity, int edgeCapacity,
            GraphDirection direction = GraphDirection.Directed, GraphCyclePolicy cycles = GraphCyclePolicy.Allow,
            bool allowParallelEdges = false, bool allowSelfEdges = false)
            : this(new BufferOptions(nodeCapacity), new BufferOptions(edgeCapacity), direction, cycles, allowParallelEdges, allowSelfEdges) { }
        public GraphOptions(BufferOptions nodes, BufferOptions edges,
            GraphDirection direction = GraphDirection.Directed, GraphCyclePolicy cycles = GraphCyclePolicy.Allow,
            bool allowParallelEdges = false, bool allowSelfEdges = false)
        { Nodes = nodes; Edges = edges; Direction = direction; Cycles = cycles; AllowParallelEdges = allowParallelEdges; AllowSelfEdges = allowSelfEdges; }
        public bool IsValid => Nodes.IsValid && Edges.IsValid && Edges.MaxCapacity <= (int.MaxValue - 1) / 2 &&
            Direction <= GraphDirection.Undirected && Cycles <= GraphCyclePolicy.Acyclic &&
            Nodes.Snapshots == Edges.Snapshots && Nodes.SnapshotSlots == Edges.SnapshotSlots &&
            GraphScratch.TryMeasure(Nodes.MaxCapacity, Edges.MaxCapacity, out _).IsSuccess();
        internal BufferOptions AdjacencyOptions => new BufferOptions(Edges.InitialCapacity * 2, Edges.MaxCapacity * 2,
            Edges.Allocation, Edges.Snapshots, Edges.SnapshotSlots, Edges.PageBytes);
        public ulong SemanticFingerprint
        {
            get
            {
                ulong h = DeterministicHash.Mix(0x4c44475241504831UL ^ (byte)Direction);
                h = DeterministicHash.Mix(h ^ (byte)Cycles ^ (AllowParallelEdges ? 256UL : 0) ^ (AllowSelfEdges ? 512UL : 0));
                h = DeterministicHash.Mix(h ^ (uint)Nodes.InitialCapacity ^ ((ulong)(uint)Nodes.MaxCapacity << 32));
                h = DeterministicHash.Mix(h ^ (uint)Edges.InitialCapacity ^ ((ulong)(uint)Edges.MaxCapacity << 32));
                return DeterministicHash.Mix(h ^ (byte)Nodes.Allocation ^ ((ulong)(byte)Edges.Allocation << 8));
            }
        }
        internal GraphOptions WithSnapshots(SnapshotPolicy policy, int slots) => new GraphOptions(
            new BufferOptions(Nodes.InitialCapacity, Nodes.MaxCapacity, Nodes.Allocation, policy, slots, Nodes.PageBytes),
            new BufferOptions(Edges.InitialCapacity, Edges.MaxCapacity, Edges.Allocation, policy, slots, Edges.PageBytes),
            Direction, Cycles, AllowParallelEdges, AllowSelfEdges);
    }

    /// <summary>Local to a graph/lineage. Rollback may revalidate a handle. Not a Unity Entity.</summary>
    public readonly struct GraphNodeHandle : IEquatable<GraphNodeHandle>, IComparable<GraphNodeHandle>
    {
        public readonly StableHandle Value;
        public GraphNodeHandle(StableHandle value) { Value = value; }
        public uint Index => Value.Index;
        public uint Generation => Value.Generation;
        public bool IsValid => Value.IsValid;
        public bool Equals(GraphNodeHandle other) => Value == other.Value;
        public int CompareTo(GraphNodeHandle other) => Value.CompareTo(other.Value);
        public override bool Equals(object obj) => obj is GraphNodeHandle other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(GraphNodeHandle a, GraphNodeHandle b) => a.Equals(b);
        public static bool operator !=(GraphNodeHandle a, GraphNodeHandle b) => !a.Equals(b);
    }
    public readonly struct GraphEdgeHandle : IEquatable<GraphEdgeHandle>, IComparable<GraphEdgeHandle>
    {
        public readonly StableHandle Value;
        public GraphEdgeHandle(StableHandle value) { Value = value; }
        public uint Index => Value.Index;
        public uint Generation => Value.Generation;
        public bool IsValid => Value.IsValid;
        public bool Equals(GraphEdgeHandle other) => Value == other.Value;
        public int CompareTo(GraphEdgeHandle other) => Value.CompareTo(other.Value);
        public override bool Equals(object obj) => obj is GraphEdgeHandle other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(GraphEdgeHandle a, GraphEdgeHandle b) => a.Equals(b);
        public static bool operator !=(GraphEdgeHandle a, GraphEdgeHandle b) => !a.Equals(b);
    }
    public readonly struct GraphEdge<T> where T : unmanaged
    {
        public readonly GraphNodeHandle Source, Target;
        public readonly T Data;
        public GraphEdge(GraphNodeHandle source, GraphNodeHandle target, in T data) { Source = source; Target = target; Data = data; }
    }
    public readonly struct GraphNodeItem<T> where T : unmanaged
    {
        public readonly GraphNodeHandle Handle;
        public readonly T Data;
        public GraphNodeItem(GraphNodeHandle handle, in T data) { Handle = handle; Data = data; }
    }
    public readonly struct GraphEdgeItem<T> where T : unmanaged
    {
        public readonly GraphEdgeHandle Handle;
        public readonly GraphEdge<T> Edge;
        public GraphEdgeItem(GraphEdgeHandle handle, in GraphEdge<T> edge) { Handle = handle; Edge = edge; }
    }
    public readonly struct GraphArc
    {
        public readonly GraphEdgeHandle Edge;
        public readonly GraphNodeHandle Neighbor;
        public readonly byte Side;
        public GraphArc(GraphEdgeHandle edge, GraphNodeHandle neighbor, byte side) { Edge = edge; Neighbor = neighbor; Side = side; }
    }
    // Cache fields never enter canonical bytes. Incidences are 1-based, two per edge slot.
    public struct GraphNodeRecord<T> where T : unmanaged
    {
        public T Data;
        public int OutHead, OutTail, InHead, InTail, OutDegree, InDegree;
    }
    public struct GraphEdgeRecord<T> where T : unmanaged
    {
        public T Data;
        public GraphNodeHandle Source, Target;
    }
    public struct GraphIncidence
    {
        public GraphEdgeHandle Edge;
        public GraphNodeHandle Neighbor;
        public int Previous, Next;
        public byte Side;
    }
    /// <summary>Ordinals and arc cursors are borrowed traversal positions, never persistent identities.</summary>
    public interface IDeterministicGraphReader<TNode, TEdge> where TNode : unmanaged where TEdge : unmanaged
    {
        GraphOptions Options { get; }
        int NodeCount { get; }
        int EdgeCount { get; }
        int NodeExtent { get; }
        int EdgeExtent { get; }
        DeterministicStatus Check();
        DeterministicStatus TryGetNode(GraphNodeHandle handle, out TNode data);
        DeterministicStatus TryGetEdge(GraphEdgeHandle handle, out GraphEdge<TEdge> edge);
        DeterministicStatus TryNodeAt(int ordinal, out GraphNodeHandle handle, out TNode data);
        DeterministicStatus TryEdgeAt(int ordinal, out GraphEdgeHandle handle, out GraphEdge<TEdge> edge);
        DeterministicStatus TryNodeOrdinal(GraphNodeHandle handle, out int ordinal);
        DeterministicStatus TryFirstArc(GraphNodeHandle node, GraphTraversal traversal, out int cursor);
        DeterministicStatus TryReadArc(int cursor, out GraphArc arc, out int next);
    }
    /// <summary>Constrained generic dispatch permits reusable transitions on Managed classes and Native structs.</summary>
    public interface IDeterministicGraphMutator<TNode, TEdge> where TNode : unmanaged where TEdge : unmanaged
    {
        DeterministicStatus TryGetNode(GraphNodeHandle node, out TNode data);
        DeterministicStatus TryGetEdge(GraphEdgeHandle edge, out GraphEdge<TEdge> data);
        DeterministicStatus TryFirstEdge(out GraphEdgeHandle edge);
        DeterministicStatus TryApply(in GraphMutation<TNode, TEdge> mutation, out GraphMutationResult result);
    }
    public static class GraphScratch
    {
        public static DeterministicStatus TryMeasureFrozen(int nodeExtent, int nodeCapacity, out int bytes)
        {
            bytes = 0;
            if (nodeExtent < 0 || nodeCapacity < 0 || nodeCapacity > nodeExtent) return DeterministicStatus.InvalidConfiguration;
            long size = (long)nodeExtent * sizeof(int) + (long)nodeCapacity * sizeof(int) * 2 + 64;
            if (size > int.MaxValue) return DeterministicStatus.CapacityExceeded;
            bytes = (int)size; return DeterministicStatus.None;
        }
        public static DeterministicStatus TryMeasure(int nodeExtent, int edgeExtent, out int bytes)
        {
            bytes = 0;
            if (nodeExtent < 0 || edgeExtent < 0) return DeterministicStatus.InvalidConfiguration;
            long size = (long)nodeExtent * 96 + (long)edgeExtent * 8 + 256;
            if (size > int.MaxValue) return DeterministicStatus.CapacityExceeded;
            bytes = (int)size; return DeterministicStatus.None;
        }
    }
    /// <summary>Caller-owned scratch/output memory. Allocation and disposal are outside kernels.</summary>
    public unsafe struct GraphWorkBuffer<T> : IDeterministicImportBuffer<T> where T : unmanaged
    {
        public readonly T* Pointer;
        private readonly int physicalCapacity;
        private readonly bool initialized;
        public int Capacity { get; private set; }
        public ulong Revision { get; private set; }
        public bool IsCreated => initialized && (Pointer != null || Capacity == 0);
        public GraphWorkBuffer(T* pointer, int capacity) { Pointer = pointer; physicalCapacity = capacity; Capacity = capacity; Revision = 0; initialized = capacity >= 0; }
        public T Read(int index) => Pointer[index];
        public void Write(int index, in T value) => Pointer[index] = value;
        public DeterministicStatus CanPrepareWrite(int required, int start, int count) => required < 0 || start < 0 || count < 0 ||
            (long)start + count > Capacity || required > Capacity ? DeterministicStatus.BufferTooSmall : DeterministicStatus.None;
        public DeterministicStatus PrepareWrite(int required, int start, int count) => CanPrepareWrite(required, start, count);
        public void CommitWrite() { Revision++; }
        public DeterministicStatus CanReplaceCapacity(int capacity) => capacity >= 0 && capacity <= physicalCapacity ? DeterministicStatus.None : DeterministicStatus.BufferTooSmall;
        public DeterministicStatus PrepareReplace(int capacity)
        {
            var s = CanReplaceCapacity(capacity); if (!s.IsSuccess()) return s;
            for (int i = 0; i < capacity; i++) Pointer[i] = default;
            Capacity = capacity; return DeterministicStatus.None;
        }
    }
}
