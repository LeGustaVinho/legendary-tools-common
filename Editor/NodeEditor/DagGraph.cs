using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// ScriptableObject that stores a DAG as sub-assets (nodes and edges).
/// This is the composition root consumed both in the editor window and at runtime.
/// Public methods are virtual to enable extension (OCP), and the surface
/// operates on abstractions to respect LSP.
/// </summary>
[CreateAssetMenu(fileName = "NewDagGraph", menuName = "Graphs/DAG Graph")]
public class DagGraph : ScriptableObject,
    IReadOnlyDag<IDagNode, IDagEdge<IDagNode>>
{
    // Concrete serialized storage (Unity sub-assets)
    [SerializeField] private List<DagNode> nodes = new();
    [SerializeField] private List<DagEdge> edges = new();
    [SerializeField] private int nextId = 1;

    // Algorithms dependency (injected or defaulted on demand)
    private IDagAlgorithms<IDagNode, IDagEdge<IDagNode>> _algorithms;

    /// <summary>
    /// Replaces the algorithms implementation at runtime or in-editor.
    /// </summary>
    /// <param name="algorithms">Algorithms implementation.</param>
    public virtual void UseAlgorithms(IDagAlgorithms<IDagNode, IDagEdge<IDagNode>> algorithms)
    {
        _algorithms = algorithms ?? throw new ArgumentNullException(nameof(algorithms));
    }

    /// <summary>
    /// Gets the algorithms instance (lazy defaults).
    /// </summary>
    private IDagAlgorithms<IDagNode, IDagEdge<IDagNode>> Algorithms
        => _algorithms ??= new DagAlgorithms<IDagNode, IDagEdge<IDagNode>>();

    /// <inheritdoc/>
    public virtual IReadOnlyList<IDagNode> Nodes
        => nodes.Where(n => n != null).Cast<IDagNode>().ToList();

    /// <inheritdoc/>
    public virtual IReadOnlyList<IDagEdge<IDagNode>> Edges
        => edges.Where(e => e != null && e.From != null && e.To != null).Cast<IDagEdge<IDagNode>>().ToList();

    /// <summary>
    /// Gets child nodes (outgoing neighbors) by node Id.
    /// Projection uses the concrete store internally but returns abstractions.
    /// </summary>
    /// <param name="nodeId">Source node identifier.</param>
    /// <returns>Enumerable of child nodes.</returns>
    public virtual IEnumerable<IDagNode> GetChildren(int nodeId)
    {
        return SafeEdges().Where(e => e.From is DagNode fn && fn.Id == nodeId).Select(e => e.To);
    }

    /// <summary>
    /// Gets parent nodes (incoming neighbors) by node Id.
    /// </summary>
    /// <param name="nodeId">Destination node identifier.</param>
    /// <returns>Enumerable of parent nodes.</returns>
    public virtual IEnumerable<IDagNode> GetParents(int nodeId)
    {
        return SafeEdges().Where(e => e.To is DagNode tn && tn.Id == nodeId).Select(e => e.From);
    }

    /// <summary>
    /// Checks whether this graph contains cycles.
    /// Although the asset is intended to be acyclic, editor operations may try to create cycles;
    /// this method is used as a guard.
    /// </summary>
    /// <returns><c>true</c> if a cycle exists; otherwise <c>false</c>.</returns>
    public virtual bool HasCycle()
    {
        return Algorithms.HasCycle(this, e => e.From, e => e.To);
    }

#if UNITY_EDITOR
    /// <summary>
    /// Creates and registers a new <see cref="DagNode"/> sub-asset.
    /// </summary>
    /// <param name="title">Display title.</param>
    /// <param name="position">Logical canvas position.</param>
    /// <returns>The created node as an abstraction.</returns>
    public virtual IDagNode CreateNode(string title, Vector2 position)
    {
        CleanupNulls();

        DagNode node = CreateInstance<DagNode>();
        node.name = $"Node_{nextId}";
        node.SetId(nextId++);
        node.SetTitle(title);
        node.SetPosition(position);

        AssetDatabase.AddObjectToAsset(node, this);
        nodes.Add(node);

        MarkDirty(node, true, true);
        return node;
    }

    /// <summary>
    /// Removes the node with the given Id and all incident edges.
    /// </summary>
    /// <param name="nodeId">Node identifier.</param>
    public virtual void RemoveNode(int nodeId)
    {
        CleanupNulls();

        DagNode node = FindNode(nodeId);
        if (!node) return;

        List<DagEdge> incident =
            edges.Where(e => e != null && (e.FromConcrete == node || e.ToConcrete == node)).ToList();
        foreach (DagEdge e in incident)
        {
            edges.Remove(e);
            DestroyImmediate(e, true);
        }

        nodes.Remove(node);
        DestroyImmediate(node, true);

        MarkDirty(this, false, true);
        Reimport();
    }

    /// <summary>
    /// Renames a node by Id.
    /// </summary>
    /// <param name="nodeId">Node identifier.</param>
    /// <param name="newTitle">New title.</param>
    public virtual void RenameNode(int nodeId, string newTitle)
    {
        DagNode n = FindNode(nodeId);
        if (!n) return;

        n.SetTitle(newTitle);
        MarkDirty(n, false, true);
    }

    /// <summary>
    /// Moves a node to a new logical position.
    /// </summary>
    /// <param name="nodeId">Node identifier.</param>
    /// <param name="newPos">New position.</param>
    public virtual void MoveNode(int nodeId, Vector2 newPos)
    {
        DagNode n = FindNode(nodeId);
        if (!n) return;

        n.SetPosition(newPos);
        MarkDirty(n, false, false);
    }

    /// <summary>
    /// Attempts to create a new edge between two nodes; cancels if a cycle would be introduced.
    /// </summary>
    /// <param name="fromId">Source node identifier.</param>
    /// <param name="toId">Destination node identifier.</param>
    /// <param name="error">Error message if creation fails.</param>
    /// <returns><c>true</c> if edge is created; otherwise <c>false</c>.</returns>
    public virtual bool TryAddEdge(int fromId, int toId, out string error)
    {
        CleanupNulls();

        error = null;
        DagNode from = FindNode(fromId);
        DagNode to = FindNode(toId);

        if (!from || !to)
        {
            error = "Both endpoints must exist.";
            return false;
        }

        if (ReferenceEquals(from, to))
        {
            error = "Self-loops are not allowed.";
            return false;
        }

        if (edges.Any(e => e != null && e.FromConcrete == from && e.ToConcrete == to))
        {
            error = "Edge already exists.";
            return false;
        }

        DagEdge edge = CreateInstance<DagEdge>();
        edge.name = $"Edge_{from.Id}_to_{to.Id}";
        edge.FromConcrete = from;
        edge.ToConcrete = to;

        edges.Add(edge);
        AssetDatabase.AddObjectToAsset(edge, this);

        if (HasCycle())
        {
            edges.Remove(edge);
            DestroyImmediate(edge, true);
            error = "This connection would create a cycle.";
            return false;
        }

        MarkDirty(edge, true, true);
        return true;
    }

    /// <summary>
    /// Removes an edge given its endpoints.
    /// </summary>
    /// <param name="fromId">Source node identifier.</param>
    /// <param name="toId">Destination node identifier.</param>
    public virtual void RemoveEdge(int fromId, int toId)
    {
        DagNode from = FindNode(fromId);
        DagNode to = FindNode(toId);
        if (!from || !to) return;

        DagEdge edge = edges.FirstOrDefault(e => e != null && e.FromConcrete == from && e.ToConcrete == to);
        if (!edge) return;

        edges.Remove(edge);
        DestroyImmediate(edge, true);

        MarkDirty(this, false, true);
        Reimport();
    }

    /// <summary>
    /// Compacts storage by removing null references or invalid edges.
    /// Safe to call frequently (e.g., OnValidate).
    /// </summary>
    public virtual void CleanupNulls()
    {
        bool changed = false;

        if (nodes.RemoveAll(n => n == null) > 0) changed = true;
        if (edges.RemoveAll(e => e == null || e.From == null || e.To == null) > 0) changed = true;

        if (changed) MarkDirty(this, false, false);
    }

    /// <summary>
    /// Marks an object as dirty and optionally saves assets.
    /// </summary>
    private void MarkDirty(UnityEngine.Object obj, bool alsoGraph, bool save)
    {
        EditorUtility.SetDirty(obj);
        if (alsoGraph) EditorUtility.SetDirty(this);
        if (save) AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Reimports this asset to refresh the Project view.
    /// </summary>
    private void Reimport()
    {
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(this));
    }

    private void OnValidate()
    {
        CleanupNulls();
        int maxId = nodes.Where(n => n != null).Select(n => n.Id).DefaultIfEmpty(0).Max();
        if (nextId <= maxId) nextId = maxId + 1;
    }
#endif

    /// <summary>
    /// Enumerates safe edges (non-null endpoints) as abstractions.
    /// </summary>
    private IEnumerable<IDagEdge<IDagNode>> SafeEdges()
    {
        return edges.Where(e => e != null && e.From != null && e.To != null).Cast<IDagEdge<IDagNode>>();
    }

    /// <summary>
    /// Finds the concrete node by Id in O(n) without allocations.
    /// </summary>
    private DagNode FindNode(int id)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            DagNode n = nodes[i];
            if (n != null && n.Id == id) return n;
        }

        return null;
    }
}