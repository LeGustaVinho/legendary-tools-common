#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace LegendaryTools.NodeEditor
{
    /// <summary>
    /// Facade that composes registered providers and shows the resulting context menu.
    /// The editor window injects callbacks (e.g., StartConnection) to keep providers decoupled from GUI.
    /// </summary>
    public static class ContextMenuService
    {
        /// <summary>
        /// Builds and shows the Node context menu using all registered providers.
        /// </summary>
        /// <param name="graph">Graph asset.</param>
        /// <param name="nodes">Selected nodes.</param>
        /// <param name="startConnection">Callback to start connection mode from a given node id.</param>
        public static void ShowNodeMenu(Graph graph, IReadOnlyList<IEditorNode> nodes,
            Action<string> startConnection)
        {
            MenuBuilder builder = new();
            NodeMenuContext ctx = new(graph, nodes, startConnection);

            foreach (INodeContextMenuProvider p in ContextMenuRegistry.NodeProviders)
            {
                p.Build(ctx, builder);
            }

            builder.Show();
        }

        /// <summary>Builds and shows the Edge context menu using all registered providers.</summary>
        public static void ShowEdgeMenu(Graph graph, IReadOnlyList<IEditorNodeEdge<IEditorNode>> edges)
        {
            MenuBuilder builder = new();
            EdgeMenuContext ctx = new(graph, edges);

            foreach (IEdgeContextMenuProvider p in ContextMenuRegistry.EdgeProviders)
            {
                p.Build(ctx, builder);
            }

            builder.Show();
        }

        /// <summary>Builds and shows the Graph (canvas) context menu using all registered providers.</summary>
        public static void ShowGraphMenu(Graph graph, UnityEngine.Vector2 logicalClickPosition)
        {
            MenuBuilder builder = new();
            GraphMenuContext ctx = new(graph, logicalClickPosition);

            foreach (IGraphContextMenuProvider p in ContextMenuRegistry.GraphProviders)
            {
                p.Build(ctx, builder);
            }

            builder.Show();
        }
    }
}
#endif