namespace LegendaryTools.Deterministic
{
    public sealed unsafe partial class DeterministicGraph<TNode, TEdge> : IDeterministicGraphMutator<TNode, TEdge> where TNode : unmanaged where TEdge : unmanaged
    {
        public int SnapshotCapacity => NodesStorage.Owner == null ? 0 : NodesStorage.Owner.SnapshotSlots;
        public DeterministicStatus TryFirstEdge(out GraphEdgeHandle edge)
        {
            edge = default; var iterator = Edges; var status = iterator.TryMoveNext(out bool moved);
            if (!status.IsSuccess()) return status; if (!moved) return DeterministicStatus.NotFound;
            edge = iterator.Current.Handle; return DeterministicStatus.None;
        }
        internal DeterministicStatus TryReserveSnapshotCapacity(int slots)
        {
            if (!IsCreated) return DeterministicStatus.Disposed;
            var s = NodesStorage.Owner.CanReserveSnapshotSlots(slots); if (!s.IsSuccess()) return s;
            s = EdgesStorage.Owner.CanReserveSnapshotSlots(slots); if (!s.IsSuccess()) return s;
            s = ArcsStorage.Owner.CanReserveSnapshotSlots(slots); if (!s.IsSuccess()) return s;
            s = NodesStorage.Owner.TryReserveSnapshotSlots(slots); if (!s.IsSuccess()) return s;
            s = EdgesStorage.Owner.TryReserveSnapshotSlots(slots); if (!s.IsSuccess()) return s;
            return ArcsStorage.Owner.TryReserveSnapshotSlots(slots);
        }
        public DeterministicStatus TryApply(in GraphMutation<TNode, TEdge> mutation, out GraphMutationResult result)
        {
            fixed (byte* p = workspace)
            { var scratch = new CanonicalScratch(p, workspace.Length);
                return GraphMutationKernel<TNode, TEdge, ManagedStorage<SlotMapNode<GraphNodeRecord<TNode>>>, ManagedStorage<SlotMapNode<GraphEdgeRecord<TEdge>>>, ManagedStorage<GraphIncidence>>.TryApply(ref NodesStorage, ref EdgesStorage, ref ArcsStorage, ref NodeState, ref EdgeState, Options, mutation, ref scratch, out result); }
        }
    }
}
