namespace LegendaryTools.Deterministic.Samples
{
    /// <summary>Example rule shared by both backends: change payloads/topology and exercise FIFO node reuse.</summary>
    public static class GraphSimulationTransition<TGraph> where TGraph : IDeterministicGraphMutator<int, int>
    {
        public static int Fold(int value, int input, PlayerId player) => unchecked(value * 31 + input * (int)player.Value);
        public static DeterministicStatus TryApply(ref TGraph graph, GraphNodeHandle a, GraphNodeHandle b, int value)
        {
            var mutation = new GraphMutation<int, int> { Kind = GraphMutationKind.SetNode, Node = a, NodeData = value };
            var s = graph.TryApply(mutation, out _); if (!s.IsSuccess()) return s;
            s = graph.TryFirstEdge(out var edge); bool exists = s.IsSuccess(); if (!exists && s != DeterministicStatus.NotFound) return s;
            if ((value & 1) == 0)
                mutation = exists ? new GraphMutation<int, int> { Kind = GraphMutationKind.SetEdge, Edge = edge, EdgeData = value } :
                    new GraphMutation<int, int> { Kind = GraphMutationKind.AddEdge, Source = a, Target = b, EdgeData = value };
            else mutation = exists ? new GraphMutation<int, int> { Kind = GraphMutationKind.RemoveEdge, Edge = edge } : default;
            if ((value & 1) == 0 || exists) { s = graph.TryApply(mutation, out _); if (!s.IsSuccess()) return s; }
            if ((value & 4) != 0)
            {
                mutation = new GraphMutation<int, int> { Kind = GraphMutationKind.AddNode, NodeData = value };
                s = graph.TryApply(mutation, out var created); if (!s.IsSuccess()) return s;
                mutation = new GraphMutation<int, int> { Kind = GraphMutationKind.RemoveNode, Node = created.Node };
                s = graph.TryApply(mutation, out _);
            }
            return s == DeterministicStatus.NotFound ? DeterministicStatus.None : s;
        }
    }
}
