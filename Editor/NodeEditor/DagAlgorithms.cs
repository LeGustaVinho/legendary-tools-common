using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Default DAG algorithms implementation. Intentionally small:
/// only cycle detection is offered to keep the surface minimal and composable.
/// </summary>
/// <typeparam name="TNode">Node abstraction.</typeparam>
/// <typeparam name="TEdge">Edge abstraction.</typeparam>
public sealed class DagAlgorithms<TNode, TEdge> : IDagAlgorithms<TNode, TEdge>
{
    /// <inheritdoc/>
    public bool HasCycle(
        IReadOnlyDag<TNode, TEdge> dag,
        Func<TEdge, TNode> getFrom,
        Func<TEdge, TNode> getTo)
    {
        if (dag == null) throw new ArgumentNullException(nameof(dag));
        if (getFrom == null) throw new ArgumentNullException(nameof(getFrom));
        if (getTo == null) throw new ArgumentNullException(nameof(getTo));

        List<TNode> nodes = dag.Nodes.Where(n => n != null).ToList();
        List<TEdge> edges = dag.Edges.Where(e => e != null && getFrom(e) != null && getTo(e) != null).ToList();

        HashSet<TNode> visited = new();
        HashSet<TNode> stack = new();

        bool Dfs(TNode n)
        {
            if (stack.Contains(n)) return true; // Back edge
            if (visited.Contains(n)) return false;

            visited.Add(n);
            stack.Add(n);

            // Traverse outgoing edges
            foreach (TEdge e in edges)
            {
                if (EqualityComparer<TNode>.Default.Equals(getFrom(e), n) && Dfs(getTo(e)))
                    return true;
            }

            stack.Remove(n);
            return false;
        }

        foreach (TNode n in nodes)
        {
            if (Dfs(n)) return true;
        }

        return false;
    }
}