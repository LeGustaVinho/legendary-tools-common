using System;
using UnityEngine;
using LegendaryTools.GraphV2;

namespace LegendaryTools.NodeEditor
{
    /// <summary>
    /// Edge sub-asset bridging graph abstractions: implements INodeConnection for
    /// algorithms/graph, and IEditorEdge for editor rendering metadata.
    /// </summary>
    [Serializable]
    public class Edge : ScriptableObject,
        INodeConnection,
        IEditorNodeEdge<IEditorNode>,
        IStyledEdge
    {
        [Header("Endpoints")] [SerializeField] private Node from;
        [SerializeField] private Node to;

        [Header("Direction")] [SerializeField]
        private NodeConnectionDirection direction = NodeConnectionDirection.Unidirectional;

        [Header("Metadata & Styling")] [SerializeField]
        private string edgeName = "Edge";

        [SerializeField] private Color edgeColor = Color.white;

        [Tooltip("Optional text drawn at the visual midpoint of the curve. Leave empty to hide.")] [SerializeField]
        private string centerText = string.Empty;

        [SerializeField] private string id;

        // -------------------- INodeConnection --------------------

        /// <summary>Unique edge identifier (GUID string).</summary>
        public string Id
        {
            get => string.IsNullOrEmpty(id) ? id = Guid.NewGuid().ToString("N") : id;
            set => id = value;
        }

        /// <summary>Gets or sets the source endpoint as INode.</summary>
        public INode FromNode
        {
            get => from;
            set => from = value as Node;
        }

        /// <summary>Gets or sets the destination endpoint as INode.</summary>
        public INode ToNode
        {
            get => to;
            set => to = value as Node;
        }

        /// <summary>Gets or sets the direction of this connection.</summary>
        public NodeConnectionDirection Direction
        {
            get => direction;
            set => direction = value;
        }

        /// <summary>Disconnects this connection (clears endpoints).</summary>
        public void Disconnect()
        {
            from = null;
            to = null;
        }

        /// <summary>Gets the outward node given a starting node.</summary>
        public INode GetOut(INode fromNode)
        {
            if (fromNode == null) return null;
            if (Direction == NodeConnectionDirection.Unidirectional)
                return ReferenceEquals(fromNode, FromNode) ? ToNode : null;

            // Bidirectional: return the opposite endpoint
            if (ReferenceEquals(fromNode, FromNode)) return ToNode;
            if (ReferenceEquals(fromNode, ToNode)) return FromNode;
            return null;
        }

        /// <summary>Gets the inward node given an ending node.</summary>
        public INode GetIn(INode toNode)
        {
            if (toNode == null) return null;
            if (Direction == NodeConnectionDirection.Unidirectional)
                return ReferenceEquals(toNode, ToNode) ? FromNode : null;

            // Bidirectional: return the opposite endpoint
            if (ReferenceEquals(toNode, ToNode)) return FromNode;
            if (ReferenceEquals(toNode, FromNode)) return ToNode;
            return null;
        }

        // -------------------- IEditorEdge<IEditorNode> --------------------

        /// <summary>Gets the source endpoint as an abstraction.</summary>
        public IEditorNode From => from;

        /// <summary>Gets the destination endpoint as an abstraction.</summary>
        public IEditorNode To => to;

        // -------------------- IStyledEdge (editor metadata) --------------------

        /// <summary>Gets a human-readable edge name.</summary>
        public string Name => edgeName;

        /// <summary>Gets the preferred color for rendering this edge.</summary>
        public Color EdgeColor => edgeColor;

        /// <summary>Gets an optional text rendered at the curve midpoint.</summary>
        public string CenterText => centerText;

        // -------------------- Helpers --------------------

        /// <summary>Sets concrete endpoints while keeping public API abstract.</summary>
        public void SetEndpoints(Node fromNode, Node toNode)
        {
            from = fromNode;
            to = toNode;
        }
    }
}