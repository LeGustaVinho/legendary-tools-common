using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using LegendaryTools.GraphV2;

namespace LegendaryTools.NodeEditor
{
    /// <summary>
    /// Concrete node stored as a sub-asset. Implements both editor-facing IEditorNode
    /// and algorithm/graph-facing INode. Connection data is resolved through the owning graph,
    /// keeping dependencies abstract.
    /// </summary>
    [Serializable]
    public class Node : ScriptableObject, IEditorNode, INode
    {
        [SerializeField] private string id;
        [SerializeField] private string title = "Node";
        [SerializeField] private Vector2 position;

        // Example serialized data visible in the editor UI
        [ShowInNode] [SerializeField] public string note;
        [ShowInNode] [SerializeField] public int someInt;
        [SerializeField] public float someFloat;
        [ShowInNode] [SerializeField] public UnityEngine.Object reference;

        // Optional appearance overrides for the editor
        [Header("Appearance (Optional)")] [SerializeField]
        private bool overrideSize = false;

        [SerializeField] private Vector2 customSize = new(200f, 110f);

        [SerializeField] private bool overrideStyles = false;
        [SerializeField] private GUISkin styleSkin = null;

        [SerializeField] private string normalStyleName = "flow node 0";
        [SerializeField] private string selectedStyleName = "flow node 0 on";

        // Graph ownership (abstraction only)
        [NonSerialized] private IGraph owner;

        // -------------------- IEditorNode --------------------

        /// <summary>Unique node identifier (GUID string).</summary>
        public string Id
        {
            get => id;
            set => id = value;
        }

        /// <summary>Gets the display title.</summary>
        public string Title
        {
            get => title;
            set => title = value;
        }

        /// <summary>Gets the logical canvas position.</summary>
        public Vector2 Position
        {
            get => position;
            set => position = value;
        }

        /// <summary>Gets a value indicating whether a custom size is in use.</summary>
        public bool HasCustomNodeSize => overrideSize;

        /// <summary>Gets the custom size.</summary>
        public Vector2 NodeSize => customSize;

        /// <summary>Gets a value indicating whether custom GUI styles are in use.</summary>
        public bool HasCustomNodeStyles => overrideStyles;

        /// <summary>Gets the unselected style name.</summary>
        public string NormalStyleName => normalStyleName;

        /// <summary>Gets the selected style name.</summary>
        public string SelectedStyleName => selectedStyleName;

        /// <summary>Gets the GUISkin where styles are resolved first.</summary>
        public GUISkin StyleSkin => styleSkin;

        // -------------------- INode --------------------

        string INode.Id
        {
            get => Id;
            set => Id = value;
        }

        /// <summary>
        /// Whether opposite connections should be merged (domain-specific behavior).
        /// Default is false; change if your algorithms require merging semantics.
        /// </summary>
        public bool ShouldMergeOppositeConnections => false;

        /// <summary>Neighbours computed via the owner graph.</summary>
        public INode[] Neighbours => owner?.Neighbours(this) ?? Array.Empty<INode>();

        /// <summary>Owning graph (abstraction only).</summary>
        public IGraph Owner => owner;

        /// <summary>All connections that touch this node, materialized from the owner graph.</summary>
        public List<INodeConnection> Connections
        {
            get
            {
                if (owner is not Graph g) return new List<INodeConnection>();
                // Materialize to a list to follow the contract.
                return g
                    .Edges
                    .OfType<INodeConnection>()
                    .Where(e => e.FromNode == this || e.ToNode == this)
                    .ToList();
            }
        }

        public INodeConnection[] OutboundConnections =>
            Connections.Where(c => c.FromNode == this).ToArray();

        public INodeConnection[] InboundConnections =>
            Connections.Where(c => c.ToNode == this).ToArray();

        public int Count => Connections.Count;

        public INodeConnection ConnectTo(INode to, NodeConnectionDirection newDirection)
        {
            if (owner is not Graph graph)
                throw new NotSupportedException("Owner graph does not support connection creation.");

            if (graph.TryAddEdge(this, to, newDirection, out INodeConnection connection, out string error))
                return connection;

            throw new InvalidOperationException(error ?? "Failed to create connection.");
        }

        public bool RemoveConnection(INodeConnection nodeConnection)
        {
            if (nodeConnection == null) return false;
            if (owner is not Graph g) return false;

            // Remove by endpoints to keep a single code path.
            string from = nodeConnection.FromNode?.Id;
            string to = nodeConnection.ToNode?.Id;
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) return false;

            int before = g.Edges.OfType<INodeConnection>().Count();
            g.RemoveEdge(from, to);
            int after = g.Edges.OfType<INodeConnection>().Count();
            return after < before;
        }

        public INodeConnection FindConnectionBetweenNodes(INode from, INode to)
        {
            return Connections.FirstOrDefault(c => c.FromNode == from && c.ToNode == to);
        }

        void INode.SetOwner(IGraph newOwner)
        {
            SetOwner(newOwner);
        }

        // -------------------- Internal setters used by the graph/editor --------------------

        /// <summary>Sets the owning graph (abstraction).</summary>
        internal void SetOwner(IGraph newOwner)
        {
            owner = newOwner;
        }
    }
}