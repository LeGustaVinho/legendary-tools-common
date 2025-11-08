#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.NodeEditor
{
    /// <summary>
    /// Small helpers for selection and keyboard shortcuts.
    /// </summary>
    public static class SelectionUtil
    {
        /// <summary>
        /// Clears selection on empty click inside the canvas and not over a node.
        /// </summary>
        public static void HandleEmptyLeftClick(NodeEditorContext ctx, Rect canvasRect, System.Func<bool> isOverAnyNode)
        {
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && canvasRect.Contains(e.mousePosition) &&
                !isOverAnyNode())
            {
                if (!e.shift)
                {
                    ctx.SelectedNodes.Clear();
                    ctx.SelectedEdges.Clear();
                }

                ctx.PendingFromNodeId = null;
                e.Use();
            }
        }

        /// <summary>
        /// Handles the Delete key to remove selected nodes/edges.
        /// </summary>
        public static void HandleDeleteShortcut(NodeEditorContext ctx)
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Delete)
            {
                bool acted = false;

                if (ctx.SelectedNodes.Count > 0)
                {
                    Undo.RecordObject(ctx.Graph, "Delete Nodes");
                    foreach (IEditorNode n in ctx.SelectedNodes.ToList())
                    {
                        ctx.Graph.RemoveNode(n.Id);
                    }

                    ctx.SelectedNodes.Clear();
                    acted = true;
                }

                if (ctx.SelectedEdges.Count > 0)
                {
                    Undo.RecordObject(ctx.Graph, "Delete Edges");
                    foreach (IEditorNodeEdge<IEditorNode> edge in ctx.SelectedEdges.ToList())
                    {
                        ctx.Graph.RemoveEdge(edge.From.Id, edge.To.Id);
                    }

                    ctx.SelectedEdges.Clear();
                    acted = true;
                }

                if (acted)
                {
                    EditorUtility.SetDirty(ctx.Graph);
                    e.Use();
                }
            }
        }
    }
}
#endif