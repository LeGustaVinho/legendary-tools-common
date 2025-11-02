using System;
using UnityEngine;

/// <summary>
/// Concrete DAG edge stored as a sub-asset of <see cref="DagGraph"/>.
/// Bridges the abstraction <see cref="IDagEdge{TNode}"/> to concrete <see cref="DagNode"/>.
/// </summary>
[Serializable]
public class DagEdge : ScriptableObject, IDagEdge<IDagNode>
{
    [SerializeField] private DagNode from;
    [SerializeField] private DagNode to;

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
}