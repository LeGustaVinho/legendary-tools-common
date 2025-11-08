#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.NodeEditor
{
    /// <summary>
    /// Draws the right-hand inspector panel for selected node or edge.
    /// </summary>
    public class InspectorPanel
    {
        /// <summary>
        /// Renders the inspector area and manages editor caching to reduce allocations.
        /// </summary>
        public void Draw(NodeEditorContext ctx, Rect inspectorRect)
        {
            GUILayout.BeginArea(inspectorRect, EditorStyles.helpBox);
            GUILayout.Label("Inspector", EditorStyles.boldLabel);

            // Mixed selection
            if (ctx.SelectedNodes.Count > 0 && ctx.SelectedEdges.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{ctx.SelectedNodes.Count} node(s) and {ctx.SelectedEdges.Count} edge(s) selected.",
                    MessageType.Info);
                GUILayout.EndArea();
                return;
            }

            // Nodes only
            if (ctx.SelectedEdges.Count == 0)
            {
                if (ctx.SelectedNodes.Count == 0)
                {
                    EditorGUILayout.HelpBox("No selection.", MessageType.Info);
                }
                else if (ctx.SelectedNodes.Count > 1)
                {
                    EditorGUILayout.HelpBox($"{ctx.SelectedNodes.Count} nodes selected.", MessageType.None);
                }
                else
                {
                    IEditorNode node = ctx.SelectedNodes.FirstOrDefault();
                    if (node is Object uo)
                    {
                        if (ctx.CachedNodeEditor == null || ctx.CachedNodeEditor.target != uo)
                        {
                            if (ctx.CachedNodeEditor != null) Object.DestroyImmediate(ctx.CachedNodeEditor);
                            ctx.CachedNodeEditor = UnityEditor.Editor.CreateEditor(uo);
                        }

                        using (new EditorGUILayout.VerticalScope(EditorStyles.inspectorDefaultMargins))
                        {
                            ctx.CachedNodeEditor.OnInspectorGUI();
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("Selected node is not a UnityEngine.Object.", MessageType.Warning);
                    }
                }

                GUILayout.EndArea();
                return;
            }

            // Edges only
            if (ctx.SelectedNodes.Count == 0)
            {
                if (ctx.SelectedEdges.Count > 1)
                {
                    EditorGUILayout.HelpBox($"{ctx.SelectedEdges.Count} edges selected.", MessageType.None);
                }
                else
                {
                    IEditorNodeEdge<IEditorNode> edge = ctx.SelectedEdges.FirstOrDefault();
                    if (edge is Object eo)
                    {
                        if (ctx.CachedEdgeEditor == null || ctx.CachedEdgeEditor.target != eo)
                        {
                            if (ctx.CachedEdgeEditor != null) Object.DestroyImmediate(ctx.CachedEdgeEditor);
                            ctx.CachedEdgeEditor = UnityEditor.Editor.CreateEditor(eo);
                        }

                        using (new EditorGUILayout.VerticalScope(EditorStyles.inspectorDefaultMargins))
                        {
                            ctx.CachedEdgeEditor.OnInspectorGUI();
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("Selected edge is not a UnityEngine.Object.", MessageType.Warning);
                    }
                }

                GUILayout.EndArea();
                return;
            }

            GUILayout.EndArea();
        }
    }
}
#endif