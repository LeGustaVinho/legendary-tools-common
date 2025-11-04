#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Built-in toolbar provider that reproduces the previous default toolbar:
/// - Graph object field
/// - "New Asset" button
/// - "Add Node" button
/// - Connection-mode status label (dynamic)
/// </summary>
public sealed class DefaultToolbarProvider : IToolbarProvider
{
    public void Build(ToolbarContext context, IToolbarBuilder toolbar)
    {
        // Graph asset field (350px)
        toolbar.AddObjectField<DagGraph>(
            () => context.Graph,
            obj => context.Graph = obj as DagGraph,
            350f,
            "Graph Asset");

        toolbar.AddSeparator();

        // New Asset
        toolbar.AddButton("New Asset", () => context.CreateNewAsset?.Invoke());

        // Add Node
        toolbar.AddButton("Add Node", () => context.AddNodeAtViewportCenter?.Invoke());

        // Right-aligned dynamic info
        toolbar.AddFlexibleSpace();

        if (context.IsConnectionMode)
            toolbar.AddLabel(() => "Connection mode: click a destination node or press Esc to cancel");
    }
}

/// <summary>
/// Auto-registers default toolbar provider on load.
/// </summary>
[InitializeOnLoad]
internal static class DefaultToolbarProviderBootstrap
{
    static DefaultToolbarProviderBootstrap()
    {
        ToolbarService.Register(new DefaultToolbarProvider());
    }
}
#endif