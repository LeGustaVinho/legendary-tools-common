#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace LegendaryTools.NodeEditor
{
    /// <summary>
    /// Abstraction for building hierarchical context menus without referencing Unity GUI directly.
    /// Providers call this API to declare items and groups; the window converts it to a concrete menu.
    /// </summary>
    public interface IMenuBuilder
    {
        /// <summary>
        /// Adds an actionable item at a hierarchical path (e.g., "Group/Subgroup/Action").
        /// </summary>
        void AddItem(string path, Action onClick, bool enabled = true, bool isChecked = false);

        /// <summary>
        /// Adds a separator after the specified group prefix (e.g., "Group/").
        /// </summary>
        void AddSeparator(string pathPrefix = "");

        /// <summary>
        /// Adds an item that is only created when the visibility predicate returns true.
        /// </summary>
        void AddItemIf(string path, Func<bool> isVisible, Action onClick, Func<bool> isEnabled = null,
            Func<bool> isChecked = null);

        /// <summary>
        /// Adds an item whose visibility/enabled/checked states are all computed dynamically.
        /// </summary>
        void AddItemDynamic(string path, Action onClick, Func<bool> isVisible, Func<bool> isEnabled,
            Func<bool> isChecked);
    }

    /// <summary>
    /// Context passed to Node context-menu providers. It contains the graph, the target nodes,
    /// and editor callbacks (e.g., starting a connection).
    /// </summary>
    public sealed class NodeMenuContext
    {
        /// <summary>The asset instance.</summary>
        public Graph Graph { get; }

        /// <summary>Selected nodes for this context menu invocation.</summary>
        public IReadOnlyList<IEditorNode> Nodes { get; }

        /// <summary>
        /// Callback injected by the editor window to start the "connection mode" from a given node id.
        /// Providers should call this instead of touching editor state directly.
        /// </summary>
        /// <summary>Callback to start the connection mode from a given node id (GUID string).</summary>
        public Action<string> StartConnection { get; }

        public NodeMenuContext(Graph graph, IReadOnlyList<IEditorNode> nodes, Action<string> startConnection)
        {
            Graph = graph;
            Nodes = nodes;
            StartConnection = startConnection ?? (_ => { });
        }
    }

    /// <summary>
    /// Context passed to Edge context-menu providers. It contains the graph and the target edges.
    /// </summary>
    public sealed class EdgeMenuContext
    {
        public Graph Graph { get; }
        public IReadOnlyList<IEditorNodeEdge<IEditorNode>> Edges { get; }

        public EdgeMenuContext(Graph graph, IReadOnlyList<IEditorNodeEdge<IEditorNode>> edges)
        {
            Graph = graph;
            Edges = edges;
        }
    }

    /// <summary>
    /// Context passed to Graph (canvas) context-menu providers. It contains the graph and the click position.
    /// </summary>
    public sealed class GraphMenuContext
    {
        public Graph Graph { get; }
        public Vector2 LogicalClickPosition { get; }

        public GraphMenuContext(Graph graph, Vector2 logicalClickPosition)
        {
            Graph = graph;
            LogicalClickPosition = logicalClickPosition;
        }
    }

    /// <summary>Provider for Node context menus.</summary>
    public interface INodeContextMenuProvider
    {
        /// <summary>Adds items for the given nodes.</summary>
        void Build(NodeMenuContext context, IMenuBuilder menu);
    }

    /// <summary>Provider for Edge context menus.</summary>
    public interface IEdgeContextMenuProvider
    {
        /// <summary>Adds items for the given edges.</summary>
        void Build(EdgeMenuContext context, IMenuBuilder menu);
    }

    /// <summary>Provider for Graph (canvas) context menus.</summary>
    public interface IGraphContextMenuProvider
    {
        /// <summary>Adds items for the given graph.</summary>
        void Build(GraphMenuContext context, IMenuBuilder menu);
    }
#endif
}