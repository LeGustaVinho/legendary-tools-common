using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Read-only view of a directed acyclic graph (DAG).
/// Provides access to immutable collections so algorithm code and editors
/// can read the graph without mutating it.
/// </summary>
/// <typeparam name="TNode">Node abstraction.</typeparam>
/// <typeparam name="TEdge">Edge abstraction.</typeparam>
public interface IReadOnlyDag<TNode, TEdge>
{
    /// <summary>
    /// Gets the list of nodes. Implementations should not expose live mutable collections.
    /// </summary>
    IReadOnlyList<TNode> Nodes { get; }

    /// <summary>
    /// Gets the list of edges. Implementations should not expose live mutable collections.
    /// </summary>
    IReadOnlyList<TEdge> Edges { get; }
}

/// <summary>
/// Minimal node contract used by algorithms and the editor.
/// Also exposes optional appearance settings consumed by the editor window.
/// </summary>
public interface IDagNode
{
    /// <summary>Gets the unique identifier of this node within its graph.</summary>
    string Id { get; }

    /// <summary>Gets the display title.</summary>
    string Title { get; }

    /// <summary>Gets the position in canvas (logical) coordinates.</summary>
    Vector2 Position { get; }

    /// <summary>Gets a value indicating whether a custom node size is provided.</summary>
    bool HasCustomNodeSize { get; }

    /// <summary>Gets the custom node size.</summary>
    Vector2 NodeSize { get; }

    /// <summary>Gets a value indicating whether custom GUI styles are provided for this node.</summary>
    bool HasCustomNodeStyles { get; }

    /// <summary>Gets the name of the normal (unselected) GUIStyle.</summary>
    string NormalStyleName { get; }

    /// <summary>Gets the name of the selected GUIStyle.</summary>
    string SelectedStyleName { get; }

    /// <summary>
    /// Gets the <see cref="GUISkin"/> where styles will be resolved first.
    /// When <c>null</c>, the active <see cref="GUI.skin"/> is used as a fallback.
    /// </summary>
    GUISkin StyleSkin { get; }
}

/// <summary>
/// Minimal edge contract used by algorithms and the editor.
/// </summary>
/// <typeparam name="TNode">Node abstraction for endpoints.</typeparam>
public interface IDagEdge<TNode>
{
    /// <summary>Gets the source node.</summary>
    TNode From { get; }

    /// <summary>Gets the destination node.</summary>
    TNode To { get; }
}

/// <summary>
/// Optional styling/metadata contract for edges.
/// Implementations may provide name, color and a center text label for rendering,
/// while preserving compatibility with code that only expects <see cref="IDagEdge{TNode}"/>.
/// </summary>
public interface IStyledEdge
{
    /// <summary>Gets a human-readable edge name.</summary>
    string Name { get; }

    /// <summary>Gets the preferred color for rendering this edge.</summary>
    Color EdgeColor { get; }

    /// <summary>
    /// Gets an optional text rendered at the curve midpoint.
    /// When empty or null, nothing is drawn.
    /// </summary>
    string CenterText { get; }
}

/// <summary>
/// Algorithm surface for DAGs. Kept minimal by design.
/// </summary>
/// <typeparam name="TNode">Node abstraction.</typeparam>
/// <typeparam name="TEdge">Edge abstraction.</typeparam>
public interface IDagAlgorithms<TNode, TEdge>
{
    /// <summary>
    /// Detects if a cycle exists using depth-first search over the provided
    /// read-only graph and edge projection delegates.
    /// </summary>
    /// <param name="dag">Read-only graph.</param>
    /// <param name="getFrom">Projection to read edge <c>From</c>.</param>
    /// <param name="getTo">Projection to read edge <c>To</c>.</param>
    /// <returns><c>true</c> if a cycle exists; otherwise <c>false</c>.</returns>
    bool HasCycle(IReadOnlyDag<TNode, TEdge> dag,
        System.Func<TEdge, TNode> getFrom,
        System.Func<TEdge, TNode> getTo);
}