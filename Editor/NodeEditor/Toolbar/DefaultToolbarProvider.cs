#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.NodeEditor
{
    /// <summary>
    /// Built-in toolbar provider that reproduces the previous default toolbar:
    /// - "New Asset" button
    /// - "Add Node" button
    /// - Connection-mode status label (dynamic)
    /// The Graph object field itself is always added by the host window.
    /// </summary>
    public sealed class DefaultToolbarProvider : IToolbarProvider
    {
        public void Build(ToolbarContext context, IToolbarBuilder toolbar)
        {
            // Separator after the Graph ObjectField that is always present.
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
}
#endif