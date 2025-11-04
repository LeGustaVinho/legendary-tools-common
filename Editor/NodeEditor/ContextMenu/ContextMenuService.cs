#if UNITY_EDITOR
using System;
using System.Collections.Generic;

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
    public static void ShowNodeMenu(DagGraph graph, IReadOnlyList<IDagNode> nodes, System.Action<string> startConnection)
    {
        var builder = new MenuBuilder();
        var ctx = new NodeMenuContext(graph, nodes, startConnection);

        foreach (var p in ContextMenuRegistry.NodeProviders)
            p.Build(ctx, builder);

        builder.Show();
    }

    /// <summary>Builds and shows the Edge context menu using all registered providers.</summary>
    public static void ShowEdgeMenu(DagGraph graph, IReadOnlyList<IDagEdge<IDagNode>> edges)
    {
        var builder = new MenuBuilder();
        var ctx = new EdgeMenuContext(graph, edges);

        foreach (var p in ContextMenuRegistry.EdgeProviders)
            p.Build(ctx, builder);

        builder.Show();
    }

    /// <summary>Builds and shows the Graph (canvas) context menu using all registered providers.</summary>
    public static void ShowGraphMenu(DagGraph graph, UnityEngine.Vector2 logicalClickPosition)
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
#endif