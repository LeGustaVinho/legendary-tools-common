#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Handles all keyboard/mouse input for the DAG editor window.
/// This class must not use GUILayout/EditorGUILayout to avoid layout mismatches during Repaint.
/// </summary>
public sealed class DagInputController
{
    // Services (injected)
    private GridRenderer _grid; // kept for parity/future
    private NodeAppearance _appearance;
    private EdgeRenderer _edges;
    private PanController _panOnly;
    private MarqueeController _marquee;
    private ResizeController _resize;

    /// <summary>
    /// Initializes the controller with the rendering and interaction services.
    /// </summary>
    public void Initialize(
        GridRenderer grid,
        NodeAppearance appearance,
        EdgeRenderer edges,
        PanController panOnly,
        MarqueeController marquee,
        ResizeController resize)
    {
        _grid = grid;
        _appearance = appearance;
        _edges = edges;
        _panOnly = panOnly;
        _marquee = marquee;
        _resize = resize;
    }

    /// <summary>
    /// Per-frame input handling (outside of node window callbacks).
    /// </summary>
    /// <param name="ctx">Shared editor context.</param>
    /// <param name="canvasRect">Canvas viewport rectangle.</param>
    /// <param name="window">Owner window (for Repaint/ShowNotification).</param>
    public void HandleFrameInputs(DagEditorContext ctx, Rect canvasRect, EditorWindow window)
    {
        // Always keep zoom locked
        ctx.Zoom = 1f;

        Event e = Event.current;

        // Global cancel for connection mode
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            if (!string.IsNullOrEmpty(ctx.PendingFromNodeId))
            {
                ctx.PendingFromNodeId = null;
                window.Repaint();
                e.Use();
                return;
            }

        // Shortcuts
        HandleShortcuts(ctx, canvasRect, window);

        // Delete key
        SelectionUtil.HandleDeleteShortcut(ctx);

        // Hover checks (logical space)
        bool overNode = IsMouseOverAnyNode(ctx, canvasRect);
        bool overEdge = _edges.HitTestEdge(ctx,
            CoordinateSystem.ScreenToLogical(e.mousePosition, canvasRect, ctx.Scroll, 1f)) != null;

        // Input routing
        _panOnly.HandlePan(ctx, canvasRect, !(overNode || overEdge));
        HandleEdgeClickSelection(ctx, canvasRect);
        HandleMarquee(ctx, canvasRect, overNode, overEdge);
        _resize.HandleResize(ctx, canvasRect, n => _appearance.GetNodeRect(n, ctx.ShowInNodeCache));
        HandleGraphContextMenu(ctx, canvasRect, overNode, overEdge);

        // Deselect on empty click
        SelectionUtil.HandleEmptyLeftClick(ctx, canvasRect, () => IsMouseOverAnyNode(ctx, canvasRect));
    }

    /// <summary>
    /// Handles inputs that occur inside each node window callback, before drawing content.
    /// Must not use GUILayout/EditorGUILayout here.
    /// </summary>
    /// <param name="ctx">Shared context.</param>
    /// <param name="node">Node whose window is being processed.</param>
    /// <param name="repaint">Repaint callback.</param>
    /// <returns>True if event was consumed and drawing should early-out.</returns>
    public bool HandleNodeWindowInput(DagEditorContext ctx, IDagNode node, Action repaint)
    {
        Event e = Event.current;

        // Finalize connection by clicking a destination node
        if (!string.IsNullOrEmpty(ctx.PendingFromNodeId) && e.type == EventType.MouseDown && e.button == 0)
        {
            string fromId = ctx.PendingFromNodeId;
            if (fromId != node.Id)
            {
                Undo.RecordObject(ctx.Graph, "Add Edge");
                if (!ctx.Graph.TryAddEdge(fromId, node.Id, out string err))
                    // Notify the user about the error.
                    EditorWindow.focusedWindow?.ShowNotification(new GUIContent(err));
                else
                    EditorUtility.SetDirty(ctx.Graph);

                ctx.PendingFromNodeId = null;
                e.Use();
                return true; // prevent selection/drag on this click
            }
        }

        // Selection and node context menu (disabled during resize drag)
        if (!(ctx.Resize.Active && ReferenceEquals(ctx.Resize.Node, node)))
        {
            // LMB select — DO NOT consume the event, so DragWindow can start on MouseDown (matches original behavior)
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (!e.shift) ctx.SelectedEdges.Clear();

                if (e.shift)
                {
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

                // Important: Do not e.Use() here; allow DragWindow to see the MouseDown.
                repaint?.Invoke();
            }

            // RMB / Context menu — consume the event
            if (e.type == EventType.ContextClick || (e.type == EventType.MouseDown && e.button == 1))
            {
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

                // Inject connection-mode starter into Node menu
                ContextMenuService.ShowNodeMenu(
                    ctx.Graph,
                    ctx.SelectedNodes.ToList(),
                    (fromId) =>
                    {
                        ctx.PendingFromNodeId = fromId;
                        repaint?.Invoke();
                    });

                e.Use();
                return true;
            }
        }

        return false;
    }

    // -----------------------
    // Private helpers (input)
    // -----------------------

    /// <summary>
    /// Handles Ctrl+C / Ctrl+V / Ctrl+D shortcuts.
    /// </summary>
    private void HandleShortcuts(DagEditorContext ctx, Rect canvasRect, EditorWindow window)
    {
        Event e = Event.current;
        if (e.type != EventType.KeyDown) return;

        bool action = e.control || e.command; // Ctrl on Windows/Linux, Cmd on macOS
        if (!action) return;

        if (e.keyCode == KeyCode.C)
        {
            // Copy selection
            if (ctx.SelectedNodes.Count > 0)
            {
                GraphClipboardService.Copy(ctx.Graph, ctx.SelectedNodes.ToList());
                window.ShowNotification(new GUIContent($"Copied {ctx.SelectedNodes.Count} node(s)"));
                e.Use();
            }
        }
        else if (e.keyCode == KeyCode.V)
        {
            // Paste at viewport center
            Vector2 vp = new(canvasRect.width, canvasRect.height);
            Vector2 origin = ctx.Scroll + vp * 0.5f;

            Undo.RecordObject(ctx.Graph, "Paste Nodes");
            List<DagNode> created = GraphClipboardService.TryPaste(ctx.Graph, origin);
            if (created != null && created.Count > 0)
            {
                ctx.SelectedEdges.Clear();
                ctx.SelectedNodes.Clear();
                foreach (DagNode n in created)
                {
                    ctx.SelectedNodes.Add(n);
                }

                EditorUtility.SetDirty(ctx.Graph);
                window.ShowNotification(new GUIContent($"Pasted {created.Count} node(s)"));
            }

            e.Use();
        }
        else if (e.keyCode == KeyCode.D)
        {
            // Duplicate with offset
            if (ctx.SelectedNodes.Count > 0)
            {
                Undo.RecordObject(ctx.Graph, "Duplicate Nodes");
                List<DagNode> created =
                    GraphClipboardService.Duplicate(ctx.Graph, ctx.SelectedNodes.ToList(), new Vector2(24f, 24f));
                if (created != null && created.Count > 0)
                {
                    ctx.SelectedEdges.Clear();
                    ctx.SelectedNodes.Clear();
                    foreach (DagNode n in created)
                    {
                        ctx.SelectedNodes.Add(n);
                    }

                    EditorUtility.SetDirty(ctx.Graph);
                    window.ShowNotification(new GUIContent($"Duplicated {created.Count} node(s)"));
                }

                e.Use();
            }
        }
    }

    /// <summary>
    /// Handles edge click selection and shows Edge context menu.
    /// Blocks LMB selection while in connection mode to avoid confusion.
    /// </summary>
    private void HandleEdgeClickSelection(DagEditorContext ctx, Rect canvasRect)
    {
        Event e = Event.current;
        if (!canvasRect.Contains(e.mousePosition)) return;

        if (!string.IsNullOrEmpty(ctx.PendingFromNodeId) && e.type == EventType.MouseDown && e.button == 0) return;

        Vector2 mouseLogical = CoordinateSystem.ScreenToLogical(e.mousePosition, canvasRect, ctx.Scroll, 1f);
        if (IsMouseOverAnyNode(ctx, canvasRect)) return;

        IDagEdge<IDagNode> hit = _edges.HitTestEdge(ctx, mouseLogical);

        if (e.type == EventType.MouseDown && e.button == 0)
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

                ContextMenuService.ShowEdgeMenu(ctx.Graph, ctx.SelectedEdges.ToList());
                e.Use();
            }
    }

    /// <summary>
    /// Shows Graph (canvas) context menu when RMB on empty canvas.
    /// </summary>
    private void HandleGraphContextMenu(DagEditorContext ctx, Rect canvasRect, bool overNode, bool overEdge)
    {
        Event e = Event.current;
        if (!canvasRect.Contains(e.mousePosition)) return;

        bool rmb = (e.type == EventType.MouseDown && e.button == 1) || e.type == EventType.ContextClick;
        if (!rmb) return;

        if (!(overNode || overEdge))
        {
            Vector2 logical = CoordinateSystem.ScreenToLogical(e.mousePosition, canvasRect, ctx.Scroll, 1f);
            ContextMenuService.ShowGraphMenu(ctx.Graph, logical);
            e.Use();
        }
    }

    /// <summary>
    /// Handles marquee selection start/update and prevents starting while connecting.
    /// </summary>
    private void HandleMarquee(DagEditorContext ctx, Rect canvasRect, bool overNode, bool overEdge)
    {
        Event e = Event.current;
        if (!canvasRect.Contains(e.mousePosition)) return;

        if (!string.IsNullOrEmpty(ctx.PendingFromNodeId) && e.type == EventType.MouseDown && e.button == 0 &&
            !(overNode || overEdge))
        {
            // Block marquee start while connecting
            e.Use();
            return;
        }

        if (e.type == EventType.MouseDown && e.button == 0 && !(overNode || overEdge))
        {
            _marquee.HandleMarquee(ctx, canvasRect, n => _appearance.GetNodeRect(n, ctx.ShowInNodeCache));
            return;
        }

        if (ctx.Marquee.Active)
            _marquee.HandleMarquee(ctx, canvasRect, n => _appearance.GetNodeRect(n, ctx.ShowInNodeCache));
    }

    /// <summary>
    /// Tests whether the mouse is over any node (logical space).
    /// </summary>
    private bool IsMouseOverAnyNode(DagEditorContext ctx, Rect canvasRect)
    {
        Vector2 mouseLogical =
            CoordinateSystem.ScreenToLogical(Event.current.mousePosition, canvasRect, ctx.Scroll, 1f);
        return ctx.Graph.Nodes.Any(n =>
            n != null && _appearance.GetNodeRect(n, ctx.ShowInNodeCache).Contains(mouseLogical));
    }
}
#endif