using System;
using UnityEngine;

/// <summary>
/// Concrete DAG edge stored as a sub-asset of <see cref="DagGraph"/>.
/// Bridges the abstraction <see cref="IDagEdge{TNode}"/> to concrete <see cref="DagNode"/> and
/// optionally exposes styling via <see cref="IStyledEdge"/>.
/// </summary>
[Serializable]
public class DagEdge : ScriptableObject, IDagEdge<IDagNode>, IStyledEdge
{
    [Header("Endpoints")] [SerializeField] private DagNode from;
    [SerializeField] private DagNode to;

    [Header("Metadata & Styling")] [SerializeField]
    private string edgeName = "Edge";

    [SerializeField] private Color edgeColor = Color.white;

    [Tooltip("Optional text drawn at the visual midpoint of the curve. Leave empty to hide.")] [SerializeField]
    private string centerText = string.Empty;

    /// <summary>
    /// Gets the source endpoint as an abstraction.
    /// </summary>
    public IDagNode From => from;

    /// <summary>
    /// Gets the destination endpoint as an abstraction.
    /// </summary>
    public IDagNode To => to;

    /// <summary>
    /// Gets or sets the concrete source endpoint.
    /// Editor code uses this to wire sub-assets and keep serialization stable.
    /// </summary>
    public DagNode FromConcrete
    {
        get => from;
        set => from = value;
    }

    /// <summary>
    /// Gets or sets the concrete destination endpoint.
    /// Editor code uses this to wire sub-assets and keep serialization stable.
    /// </summary>
    public DagNode ToConcrete
    {
        get => to;
        set => to = value;
    }

    /// <summary>
    /// Gets a human-readable edge name.
    /// </summary>
    public string Name => edgeName;

    /// <summary>
    /// Gets the preferred color for rendering this edge.
    /// </summary>
    public Color EdgeColor => edgeColor;

    /// <summary>
    /// Gets an optional text rendered at the curve midpoint.
    /// </summary>
    public string CenterText => centerText;
}