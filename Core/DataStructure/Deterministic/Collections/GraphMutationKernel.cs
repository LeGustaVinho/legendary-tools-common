namespace LegendaryTools.Deterministic
{
    public static unsafe class GraphMutationKernel<TNode, TEdge, TNodes, TEdges, TArcs>
        where TNode : unmanaged where TEdge : unmanaged
        where TNodes : struct, IDeterministicBuffer<SlotMapNode<GraphNodeRecord<TNode>>>
        where TEdges : struct, IDeterministicBuffer<SlotMapNode<GraphEdgeRecord<TEdge>>>
        where TArcs : struct, IDeterministicBuffer<GraphIncidence>
    {
        public static DeterministicStatus TryApply(ref TNodes nodes, ref TEdges edges, ref TArcs arcs,
            ref SlotMapState ns, ref SlotMapState es, in GraphOptions options, in GraphMutation<TNode, TEdge> mutation,
            ref CanonicalScratch scratch, out GraphMutationResult result)
        {
            result = default; DeterministicStatus s;
            switch (mutation.Kind)
            {
                case GraphMutationKind.AddNode:
                    s = GraphKernel<TNode, TEdge, TNodes, TEdges, TArcs>.TryAddNode(ref nodes, ref ns, mutation.NodeData, out var node);
                    if (s.IsSuccess()) result = new GraphMutationResult(node, default); return s;
                case GraphMutationKind.AddEdge:
                    s = GraphKernel<TNode, TEdge, TNodes, TEdges, TArcs>.TryAddEdge(ref nodes, ref edges, ref arcs, ns, ref es, options, mutation.Source, mutation.Target, mutation.EdgeData, ref scratch, out var edge);
                    if (s.IsSuccess()) result = new GraphMutationResult(default, edge); return s;
                case GraphMutationKind.SetNode: return GraphKernel<TNode, TEdge, TNodes, TEdges, TArcs>.TrySetNode(ref nodes, ns, mutation.Node, mutation.NodeData);
                case GraphMutationKind.SetEdge: return GraphKernel<TNode, TEdge, TNodes, TEdges, TArcs>.TrySetEdge(ref nodes, ref edges, es, mutation.Edge, mutation.EdgeData);
                case GraphMutationKind.RemoveNode: return GraphKernel<TNode, TEdge, TNodes, TEdges, TArcs>.TryRemoveNode(ref nodes, ref edges, ref arcs, ref ns, ref es, options, mutation.Node, ref scratch);
                case GraphMutationKind.RemoveEdge: return GraphKernel<TNode, TEdge, TNodes, TEdges, TArcs>.TryRemoveEdge(ref nodes, ref edges, ref arcs, ref es, options, mutation.Edge);
                case GraphMutationKind.Clear: return GraphKernel<TNode, TEdge, TNodes, TEdges, TArcs>.TryClear(ref nodes, ref edges, ref arcs, ref ns, ref es, options);
                default: return DeterministicStatus.InvalidConfiguration;
            }
        }
    }
}
