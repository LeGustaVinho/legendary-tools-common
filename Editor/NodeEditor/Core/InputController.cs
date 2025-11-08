#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.NodeEditor
{
    /// <summary>
    /// Centralizes keyboard/mouse handling for the editor canvas.
    /// This class must not use GUILayout/EditorGUILayout to avoid layout mismatches.
    /// </summary>
    public sealed class InputController
    {
        // Services (injected)
        private GridRenderer _grid;
        private NodeAppearance _appearance;
        private EdgeRenderer _edges;
        private PanController _panOnly;
        private MarqueeController _marquee;
        private ResizeController _resize;

        // Context-menu delegates (provided by the window when a Graph is bound)
        private Action<NodeMenuContext> _showNodeMenu;
        private Action<EdgeMenuContext> _showEdgeMenu;
        private Action<GraphMenuContext> _showGraphMenu;

        /// <summary>
        /// Initializes the controller with rendering/interaction services and context-menu delegates.
        /// </summary>
        public void Initialize(
            GridRenderer grid,
            NodeAppearance appearance,
            EdgeRenderer edges,
            PanController panOnly,
            MarqueeController marquee,
            ResizeController resize,
            Action<NodeMenuContext> showNodeMenu,
            Action<EdgeMenuContext> showEdgeMenu,
            Action<GraphMenuContext> showGraphMenu)
        {
            _grid = grid;
            _appearance = appearance;
            _edges = edges;
            _panOnly = panOnly;
            _marquee = marquee;
            _resize = resize;

            _showNodeMenu = showNodeMenu ?? (_ => { });
            _showEdgeMenu = showEdgeMenu ?? (_ => { });
            _showGraphMenu = showGraphMenu ?? (_ => { });
        }

        /// <summary>
        /// Per-frame input handling (called once per OnGUI before drawing the canvas).
        /// </summary>
        public void HandleFrameInputs(NodeEditorContext ctx, Rect canvasRect, EditorWindow window)
        {
            // Hard-lock zoom to 1f
            ctx.Zoom = 1f;

            Event e = Event.current;
            if (e == null) return;

            // Global cancel: Esc exits connection mode
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
                if (!string.IsNullOrEmpty(ctx.PendingFromNodeId))
                {
                    ctx.PendingFromNodeId = null;
                    window.Repaint();
                    e.Use();
                    return;
                }

            // Global shortcuts (copy/paste/duplicate)
            HandleShortcuts(ctx, canvasRect, window);

            // Global delete (nodes/edges)
            SelectionUtil.HandleDeleteShortcut(ctx);

            // Hover checks in logical space
            bool mouseInsideCanvas = canvasRect.Contains(e.mousePosition);
            bool overNode = mouseInsideCanvas && IsMouseOverAnyNode(ctx, canvasRect);
            bool overEdge = false;
            if (mouseInsideCanvas && !overNode)
            {
                Vector2 logical = CoordinateSystem.ScreenToLogical(e.mousePosition, canvasRect, ctx.Scroll, 1f);
                overEdge = _edges.HitTestEdge(ctx, logical) != null;
            }

            // Route inputs
            _panOnly.HandlePan(ctx, canvasRect, mouseInsideCanvas && !(overNode || overEdge));
            HandleEdgeClickSelection(ctx, canvasRect);
            HandleMarquee(ctx, canvasRect, overNode, overEdge);
            _resize.HandleResize(ctx, canvasRect, n => _appearance.GetNodeRect(n, ctx.ShowInNodeCache));
            HandleGraphContextMenu(ctx, canvasRect, overNode, overEdge);

            // Deselect all on empty LMB inside canvas
            SelectionUtil.HandleEmptyLeftClick(ctx, canvasRect, () => IsMouseOverAnyNode(ctx, canvasRect));
        }

        /// <summary>
        /// Handles inputs inside each node's window. Must not use GUILayout here.
        /// Returns true if the event was consumed and drawing should early-out.
        /// </summary>
        public bool HandleNodeWindowInput(NodeEditorContext ctx, IEditorNode node, Action repaint)
        {
            Event e = Event.current;
            if (e == null) return false;

            // Finalize connection by clicking a destination node (LMB)
            if (!string.IsNullOrEmpty(ctx.PendingFromNodeId) && e.type == EventType.MouseDown && e.button == 0)
            {
                string fromId = ctx.PendingFromNodeId;
                if (fromId != node.Id)
                {
                    Undo.RecordObject(ctx.Graph, "Add Edge");
                    if (!ctx.Graph.TryAddEdge(fromId, node.Id, out string err))
                        EditorWindow.focusedWindow?.ShowNotification(new GUIContent(err));
                    else
                        EditorUtility.SetDirty(ctx.Graph);

                    ctx.PendingFromNodeId = null;
                    e.Use(); // consume so we don't start drag/selection on this click
                    return true;
                }
                // If user clicked the same node, ignore and let selection logic proceed (connection cancels on Esc)
            }

            // Block selection/menus while this node is actively resizing
            if (ctx.Resize.Active && ReferenceEquals(ctx.Resize.Node, node))
                return false;

            // LMB: selection (do not Use here to allow DragWindow to start)
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (!e.shift) ctx.SelectedEdges.Clear();

                if (e.shift)
                {
                    // Toggle in selection set
                    if (!ctx.SelectedNodes.Add(node)) ctx.SelectedNodes.Remove(node);
                }
                else
                {
                    if (!ctx.SelectedNodes.Contains(node))
                    {
                        ctx.SelectedNodes.Clear();
                        ctx.SelectedNodes.Add(node);
                    }
                }

                repaint?.Invoke();
            }

            // RMB or ContextClick: open Node context menu
            if ((e.type == EventType.MouseDown && e.button == 1) || e.type == EventType.ContextClick)
            {
                // Ensure node is part of the selection (respect Shift to multi-select)
                if (!ctx.SelectedNodes.Contains(node) && !e.shift)
                {
                    ctx.SelectedNodes.Clear();
                    ctx.SelectedNodes.Add(node);
                }
                else
                {
                    ctx.SelectedNodes.Add(node);
                }

                ctx.SelectedEdges.Clear();

                // Build menu via provider; inject connection starter
                _showNodeMenu?.Invoke(
                    new NodeMenuContext(
                        ctx.Graph,
                        ctx.SelectedNodes.ToList(),
                        fromId =>
                        {
                            ctx.PendingFromNodeId = fromId;
                            repaint?.Invoke();
                        }));

                e.Use();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Handles Ctrl/Cmd+C, Ctrl/Cmd+V, Ctrl/Cmd+D.
        /// </summary>
        private void HandleShortcuts(NodeEditorContext ctx, Rect canvasRect, EditorWindow window)
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown) return;

            bool action = e.control || e.command;
            if (!action) return;

            if (e.keyCode == KeyCode.C)
            {
                if (ctx.SelectedNodes.Count > 0)
                {
                    GraphClipboardService.Copy(ctx.Graph, ctx.SelectedNodes.ToList());
                    window.ShowNotification(new GUIContent($"Copied {ctx.SelectedNodes.Count} node(s)"));
                    e.Use();
                }

                return;
            }

            if (e.keyCode == KeyCode.V)
            {
                // Paste at viewport center
                Vector2 vp = new(canvasRect.width, canvasRect.height);
                Vector2 origin = ctx.Scroll + vp * 0.5f;

                Undo.RecordObject(ctx.Graph, "Paste Nodes");
                List<Node> created = GraphClipboardService.TryPaste(ctx.Graph, origin);
                if (created != null && created.Count > 0)
                {
                    ctx.SelectedEdges.Clear();
                    ctx.SelectedNodes.Clear();
                    foreach (Node n in created)
                    {
                        ctx.SelectedNodes.Add(n);
                    }

                    EditorUtility.SetDirty(ctx.Graph);
                    window.ShowNotification(new GUIContent($"Pasted {created.Count} node(s)"));
                }

                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.D)
                if (ctx.SelectedNodes.Count > 0)
                {
                    Undo.RecordObject(ctx.Graph, "Duplicate Nodes");
                    List<Node> created = GraphClipboardService.Duplicate(ctx.Graph, ctx.SelectedNodes.ToList(),
                        new Vector2(24f, 24f));
                    if (created != null && created.Count > 0)
                    {
                        ctx.SelectedEdges.Clear();
                        ctx.SelectedNodes.Clear();
                        foreach (Node n in created)
                        {
                            ctx.SelectedNodes.Add(n);
                        }

                        EditorUtility.SetDirty(ctx.Graph);
                        window.ShowNotification(new GUIContent($"Duplicated {created.Count} node(s)"));
                    }

                    e.Use();
                }
        }

        /// <summary>
        /// Handles edge selection (LMB) and edge context menu (RMB).
        /// Blocks LMB selection when in connection mode to avoid confusion.
        /// </summary>
        private void HandleEdgeClickSelection(NodeEditorContext ctx, Rect canvasRect)
        {
            Event e = Event.current;
            if (e == null) return;
            if (!canvasRect.Contains(e.mousePosition)) return;

            // Do not treat LMB while connecting (click should finish connection on node only)
            if (!string.IsNullOrEmpty(ctx.PendingFromNodeId) && e.type == EventType.MouseDown && e.button == 0)
                return;

            // If mouse is over a node, edge hit-test is ignored (nodes take precedence)
            if (IsMouseOverAnyNode(ctx, canvasRect)) return;

            Vector2 mouseLogical = CoordinateSystem.ScreenToLogical(e.mousePosition, canvasRect, ctx.Scroll, 1f);
            IEditorNodeEdge<IEditorNode> hit = _edges.HitTestEdge(ctx, mouseLogical);

            // LMB selects edges
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (hit != null)
                {
                    if (!e.shift) ctx.SelectedNodes.Clear();

                    if (e.shift)
                    {
                        if (!ctx.SelectedEdges.Add(hit)) ctx.SelectedEdges.Remove(hit);
                    }
                    else
                    {
                        ctx.SelectedEdges.Clear();
                        ctx.SelectedEdges.Add(hit);
                    }

                    EditorWindow.focusedWindow?.Repaint();
                    e.Use();
                }

                return;
            }

            // RMB opens edge context menu
            if ((e.type == EventType.MouseDown && e.button == 1) || e.type == EventType.ContextClick)
                if (hit != null)
                {
                    if (!ctx.SelectedEdges.Contains(hit) && !e.shift)
                    {
                        ctx.SelectedEdges.Clear();
                        ctx.SelectedEdges.Add(hit);
                    }
                    else
                    {
                        ctx.SelectedEdges.Add(hit);
                    }

                    if (!e.shift) ctx.SelectedNodes.Clear();

                    _showEdgeMenu?.Invoke(new EdgeMenuContext(ctx.Graph, ctx.SelectedEdges.ToList()));
                    e.Use();
                }
        }

        /// <summary>
        /// Shows graph (canvas) context menu on RMB in empty canvas space.
        /// </summary>
        private void HandleGraphContextMenu(NodeEditorContext ctx, Rect canvasRect, bool overNode, bool overEdge)
        {
            Event e = Event.current;
            if (e == null) return;
            if (!canvasRect.Contains(e.mousePosition)) return;

            bool rightClick = (e.type == EventType.MouseDown && e.button == 1) || e.type == EventType.ContextClick;
            if (!rightClick) return;

            if (!(overNode || overEdge))
            {
                Vector2 logical = CoordinateSystem.ScreenToLogical(e.mousePosition, canvasRect, ctx.Scroll, 1f);
                _showGraphMenu?.Invoke(new GraphMenuContext(ctx.Graph, logical));
                e.Use();
            }
        }

        /// <summary>
        /// Handles marquee selection start/update. Blocks starting while in connection mode.
        /// </summary>
        private void HandleMarquee(NodeEditorContext ctx, Rect canvasRect, bool overNode, bool overEdge)
        {
            Event e = Event.current;
            if (e == null) return;
            if (!canvasRect.Contains(e.mousePosition)) return;

            // Block marquee on LMB when connecting and not over a node/edge
            if (!string.IsNullOrEmpty(ctx.PendingFromNodeId) &&
                e.type == EventType.MouseDown && e.button == 0 &&
                !(overNode || overEdge))
            {
                e.Use();
                return;
            }

            // Start/update marquee only when starting on empty canvas
            if (e.type == EventType.MouseDown && e.button == 0 && !(overNode || overEdge))
            {
                _marquee.HandleMarquee(ctx, canvasRect, n => _appearance.GetNodeRect(n, ctx.ShowInNodeCache));
                return;
            }

            if (ctx.Marquee.Active)
                _marquee.HandleMarquee(ctx, canvasRect, n => _appearance.GetNodeRect(n, ctx.ShowInNodeCache));
        }

        /// <summary>
        /// Tests whether the current mouse position (logical) is over any node.
        /// </summary>
        private bool IsMouseOverAnyNode(NodeEditorContext ctx, Rect canvasRect)
        {
            Vector2 mouseLogical =
                CoordinateSystem.ScreenToLogical(Event.current.mousePosition, canvasRect, ctx.Scroll, 1f);
            return ctx.Graph != null &&
                   ctx.Graph.Nodes.Any(n =>
                       n != null && _appearance.GetNodeRect(n, ctx.ShowInNodeCache).Contains(mouseLogical));
        }
    }
}
#endif