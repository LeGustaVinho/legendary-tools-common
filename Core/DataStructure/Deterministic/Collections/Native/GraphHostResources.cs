using Unity.Burst;

namespace LegendaryTools.Deterministic
{
    public unsafe partial struct NativeDeterministicGraph<TNode, TEdge> : IDeterministicGraphMutator<TNode, TEdge> where TNode : unmanaged where TEdge : unmanaged
    {
        public int SnapshotCapacity { get { CheckRead(); return IsCreated ? NodesStorage.Data->Options.SnapshotSlots : 0; } }
        public DeterministicStatus TryFirstEdge(out GraphEdgeHandle edge)
        {
            edge = default; var iterator = Edges; var status = iterator.TryMoveNext(out bool moved);
            if (!status.IsSuccess()) return status; if (!moved) return DeterministicStatus.NotFound;
            edge = iterator.Current.Handle; return DeterministicStatus.None;
        }
        internal DeterministicStatus TryReserveSnapshotCapacity(int slots)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            var status = DeterministicStatus.BackendUnavailable; ReserveSnapshotsHost(ref this, slots, ref status); return status;
        }
        [BurstDiscard]
        private static void ReserveSnapshotsHost(ref NativeDeterministicGraph<TNode, TEdge> graph, int slots, ref DeterministicStatus status)
        {
            status = graph.NodesStorage.CanReserveSnapshotSlots(slots); if (!status.IsSuccess()) return;
            status = graph.EdgesStorage.CanReserveSnapshotSlots(slots); if (!status.IsSuccess()) return;
            status = graph.ArcsStorage.CanReserveSnapshotSlots(slots); if (!status.IsSuccess()) return;
            status = graph.NodesStorage.TryReserveSnapshotSlots(slots); if (!status.IsSuccess()) return;
            status = graph.EdgesStorage.TryReserveSnapshotSlots(slots); if (!status.IsSuccess()) return;
            status = graph.ArcsStorage.TryReserveSnapshotSlots(slots);
        }
        public DeterministicStatus TryApply(in GraphMutation<TNode, TEdge> mutation, out GraphMutationResult result)
        {
            CheckWrite(); result = default; if (!IsCreated) return DeterministicStatus.Disposed;
            var scratch = new CanonicalScratch(workspace.Data->Values, workspace.Capacity);
            return GraphMutationKernel<TNode, TEdge, NativeStorage<SlotMapNode<GraphNodeRecord<TNode>>>, NativeStorage<SlotMapNode<GraphEdgeRecord<TEdge>>>, NativeStorage<GraphIncidence>>.TryApply(ref NodesStorage, ref EdgesStorage, ref ArcsStorage, ref NodeState, ref EdgeState, options, mutation, ref scratch, out result);
        }
    }
}
