using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Graph asset holding nodes and edges. Exposes virtual APIs to enable extension (OCP).
/// This version validates that the graph stays a Directed Acyclic Graph (DAG) by
/// preventing edge insertions that would create cycles.
/// </summary>
public class DagGraph : ScriptableObject, IReadOnlyDag<IDagNode, IDagEdge<IDagNode>>
{
    [SerializeField] private List<DagNode> nodes = new();
    [SerializeField] private List<DagEdge> edges = new();
    [SerializeField] private int nextNodeId = 1;

    /// <summary>Read-only nodes view.</summary>
    public virtual IReadOnlyList<IDagNode> Nodes => nodes;

    /// <summary>Read-only edges view.</summary>
    public virtual IReadOnlyList<IDagEdge<IDagNode>> Edges => edges;

    /// <summary>Creates a new node with a default type and title at a position.</summary>
    public virtual DagNode CreateNode(string title, Vector2 position)
    {
        DagNode n = CreateInstance<DagNode>();
        InitNewNode(n, title, position);
        return n;
    }

    /// <summary>Moves a node to a new position.</summary>
    public virtual void MoveNode(int nodeId, Vector2 newPosition)
    {
        DagNode n = nodes.Find(x => x != null && x.Id == nodeId);
        if (n != null) n.SetPosition(newPosition);
    }

    /// <summary>
    /// Tries to add an edge; refuses duplicates, self-loops and any edge that would create a cycle.
    /// Returns false with an error message on failure.
    /// </summary>
    public virtual bool TryAddEdge(int fromId, int toId, out string error)
    {
        error = null;

        if (fromId == toId)
        {
            error = "Cannot connect a node to itself.";
            return false;
        }

        DagNode from = nodes.Find(x => x != null && x.Id == fromId);
        DagNode to = nodes.Find(x => x != null && x.Id == toId);
        if (from == null || to == null)
        {
            error = "Endpoints not found.";
            return false;
        }

        // Prevent duplicates
        if (edges.Exists(e => e != null && e.FromConcrete == from && e.ToConcrete == to))
        {
            error = "Edge already exists.";
            return false;
        }

        // Prevent cycles: if 'to' can reach 'from', adding (from -> to) creates a cycle.
        if (WouldIntroduceCycle(from, to))
        {
            error = "Adding this edge would create a cycle.";
            return false;
        }

        AddEdgeInternal(from, to);
        return true;
    }

    /// <summary>Removes a node and any incident edges.</summary>
    public virtual void RemoveNode(int nodeId)
    {
        DagNode n = nodes.Find(x => x != null && x.Id == nodeId);
        if (n == null) return;

        // remove edges first
        edges.RemoveAll(e => e == null || e.FromConcrete == n || e.ToConcrete == n);

#if UNITY_EDITOR
        if (n != null) UnityEditor.AssetDatabase.RemoveObjectFromAsset(n);
#endif
        nodes.Remove(n);
    }

    /// <summary>Removes an edge by endpoints.</summary>
    public virtual void RemoveEdge(int fromId, int toId)
    {
        DagNode from = nodes.Find(x => x != null && x.Id == fromId);
        DagNode to = nodes.Find(x => x != null && x.Id == toId);
        edges.RemoveAll(e => e == null || (e.FromConcrete == from && e.ToConcrete == to));
    }

    // -------------------- Clone/Copy helpers --------------------

    /// <summary>
    /// Creates a deep clone of <paramref name="template"/> (same concrete type), assigns a new ID,
    /// sets position and adds as sub-asset of this graph.
    /// </summary>
    public virtual DagNode CreateNodeClone(DagNode template, Vector2 position)
    {
        if (template == null) return null;

        Type t = template.GetType();
        DagNode newNode = CreateInstance(t) as DagNode;

        // Copy all serializable fields
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(template), newNode);

        // Force unique identity and position/title consistency
        InitNewNode(newNode, template.Title, position);
        return newNode;
    }

    /// <summary>
    /// Utility used by editor paste/duplicate to rebuild edges among newly created nodes.
    /// This call respects DAG constraints.
    /// </summary>
    public virtual void AddEdgeBetween(DagNode from, DagNode to)
    {
        if (from == null || to == null) return;

        // Respect validation rules (duplicates, self, cycles)
        if (!TryAddEdge(from.Id, to.Id, out _))
            return;
    }

    // -------------------- Internals --------------------

    /// <summary>
    /// Adds an edge object to the graph (no validation). Call only after validation.
    /// </summary>
    private void AddEdgeInternal(DagNode from, DagNode to)
    {
        DagEdge e = CreateInstance<DagEdge>();
        e.name = $"Edge_{from.Id}_{to.Id}";
        e.FromConcrete = from;
        e.ToConcrete = to;

#if UNITY_EDITOR
        UnityEditor.AssetDatabase.AddObjectToAsset(e, this);
#endif
        edges.Add(e);
    }

    /// <summary>
    /// Initializes a newly created node, assigns stable ID and registers as sub-asset.
    /// </summary>
    private void InitNewNode(DagNode n, string title, Vector2 position)
    {
        n.SetId(nextNodeId++);
        n.SetTitle(title);
        n.SetPosition(position);

#if UNITY_EDITOR
        UnityEditor.AssetDatabase.AddObjectToAsset(n, this);
#endif
        nodes.Add(n);
    }

    /// <summary>
    /// Checks whether adding (from → to) would create a cycle by testing reachability:
    /// if 'to' can reach 'from' following outgoing edges, then the new edge closes a cycle.
    /// </summary>
    private bool WouldIntroduceCycle(DagNode from, DagNode to)
    {
        if (from == null || to == null) return false;

        // Build adjacency list (outgoing edges)
        Dictionary<DagNode, List<DagNode>> adj = new(nodes.Count);
        foreach (DagNode n in nodes)
        {
            if (n == null) continue;
            adj[n] = new List<DagNode>();
        }

        foreach (DagEdge e in edges)
        {
            if (e == null || e.FromConcrete == null || e.ToConcrete == null) continue;
            // skip the prospective new edge; we only check existing reachability
            if (!adj.TryGetValue(e.FromConcrete, out List<DagNode> list))
            {
                list = new List<DagNode>();
                adj[e.FromConcrete] = list;
            }

            list.Add(e.ToConcrete);
        }

        // DFS/BFS from 'to' to see if we can reach 'from'
        Stack<DagNode> stack = new();
        HashSet<DagNode> visited = new();
        stack.Push(to);

        while (stack.Count > 0)
        {
            DagNode cur = stack.Pop();
            if (cur == from) return true; // found a path: adding (from→to) would create a cycle

            if (!visited.Add(cur)) continue;

            if (adj.TryGetValue(cur, out List<DagNode> next))
                for (int i = 0; i < next.Count; i++)
                {
                    DagNode n = next[i];
                    if (n != null && !visited.Contains(n))
                        stack.Push(n);
                }
        }

        return false;
    }
}