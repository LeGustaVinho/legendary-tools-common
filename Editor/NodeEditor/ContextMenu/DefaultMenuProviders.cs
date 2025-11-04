#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

public class DefaultNodeMenuProvider : INodeContextMenuProvider
{
    public void Build(NodeMenuContext context, IMenuBuilder menu)
    {
        int count = context.Nodes.Count;

        // Connections
        menu.AddItemIf(
            path: "Connections/Create Connection",
            isVisible: () => count >= 1,
            onClick: () =>
            {
                var n = context.Nodes.FirstOrDefault() as DagNode;
                if (n == null) return;
                context.StartConnection?.Invoke(n.Id);
            },
            isEnabled: () => count >= 1
        );

        // --- Edit group: Copy/Duplicate ---
        menu.AddSeparator("");
        menu.AddItem("Edit/Copy", () => { GraphClipboardService.Copy(context.Graph, context.Nodes.ToList()); },
            count > 0);

        menu.AddItem(count > 1 ? "Edit/Duplicate Selection" : "Edit/Duplicate Node", () =>
        {
            Undo.RecordObject(context.Graph, "Duplicate Nodes");
            List<DagNode> created =
                GraphClipboardService.Duplicate(context.Graph, context.Nodes.ToList(), new Vector2(24f, 24f));
            if (created != null && created.Count > 0)
            {
                // selection refresh is handled by the window after menu closes (optional)
            }

            EditorUtility.SetDirty(context.Graph);
        }, count > 0);

        // Delete
        menu.AddSeparator("");
        menu.AddItem(count > 1 ? $"Delete/Delete Selected Nodes ({count})" : "Delete/Delete Node", () =>
        {
            Undo.RecordObject(context.Graph, "Delete Nodes");
            foreach (IDagNode sn in context.Nodes.ToList())
            {
                context.Graph.RemoveNode(sn.Id);
            }

            EditorUtility.SetDirty(context.Graph);
        }, count > 0);
    }
}

public sealed class DefaultEdgeMenuProvider : IEdgeContextMenuProvider
{
    public void Build(EdgeMenuContext context, IMenuBuilder menu)
    {
        int count = context.Edges.Count;
        menu.AddItem(count > 1 ? $"Delete/Delete Selected Edges ({count})" : "Delete/Delete Edge", () =>
        {
            Undo.RecordObject(context.Graph, "Delete Edges");
            foreach (IDagEdge<IDagNode> e in context.Edges.ToList())
            {
                context.Graph.RemoveEdge(e.From.Id, e.To.Id);
            }

            EditorUtility.SetDirty(context.Graph);
        }, count > 0);
    }
}

public sealed class DefaultGraphMenuProvider : IGraphContextMenuProvider
{
    public void Build(GraphMenuContext context, IMenuBuilder menu)
    {
        // Create Node
        menu.AddItem("Create/Add Node Here", () =>
        {
            Undo.RecordObject(context.Graph, "Add Node");
            context.Graph.CreateNode($"Node {Random.Range(0, 9999)}", context.LogicalClickPosition);
            EditorUtility.SetDirty(context.Graph);
        });

        // Paste at click
        menu.AddSeparator("");
        menu.AddItem("Edit/Paste Here", () =>
        {
            Undo.RecordObject(context.Graph, "Paste Nodes");
            List<DagNode> created = GraphClipboardService.TryPaste(context.Graph, context.LogicalClickPosition);
            if (created != null && created.Count > 0)
                EditorUtility.SetDirty(context.Graph);
        }, true);
    }
}

[InitializeOnLoad]
internal static class DefaultMenuProvidersBootstrap
{
    static DefaultMenuProvidersBootstrap()
    {
        ContextMenuRegistry.Register(new DefaultNodeMenuProvider());
        ContextMenuRegistry.Register(new DefaultEdgeMenuProvider());
        ContextMenuRegistry.Register(new DefaultGraphMenuProvider());
    }
}
#endif