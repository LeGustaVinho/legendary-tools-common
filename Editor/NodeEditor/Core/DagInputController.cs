#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Centralizes and handles all input responsibilities for the DAG editor:
/// keyboard shortcuts, mouse routing, selection, context menus, connection mode,
/// pan, marquee and resize interactions.
/// </summary>
public sealed class DagInputController
{
    // Services
    private readonly GridRenderer _grid;
    private readonly NodeAppearance _appearance;
    private readonly EdgeRenderer _edges;
    private readonly PanController _panOnly;
    private readonly MarqueeController _marquee;
    private readonly ResizeController _resize;

    // Window callbacks
    private readonly Action _repaint;
    private readonly Action<string> _notify;

    /// <summary>
    /// Initializes a new instance of the input controller with the required services and callbacks.
    /// </summary>
    public DagInputController(
        GridRenderer grid,
        NodeAppearance appearance,
        EdgeRenderer edges,
        PanController panOnly,
        MarqueeController marquee,
        ResizeController resize,
        Action repaint,
        Action<string> notify)
    {
        _grid = grid;
        _appearance = appearance;
        _edges = edges;
        _panOnly = panOnly;
        _marquee = marquee;
        _resize = resize;
        _repaint = repaint;
        _notify = notify;
    }

    /// <summary>
    /// Entry-point to handle all per-frame input for the editor.
    /// Must be called from OnGUI after layout rects are computed.
    /// </summary>
    public void Process(DagEditorContext ctx, Rect canvasRect)
    {
        // Global cancel for connection mode (Esc)
        HandleGlobalEscape(ctx);

        // Shortcuts: copy/paste/duplicate
        HandleShortcuts(ctx, canvasRect);

        // Delete shortcut
        SelectionUtil.HandleDeleteShortcut(ctx);

        // Hover checks (logical space)
        bool overNode = IsMouseOverAnyNode(ctx, canvasRect);
        bool overEdge = _edges.HitTestEdge(ctx,
            CoordinateSystem.ScreenToLogical(Event.current.mousePosition, canvasRect, ctx.Scroll, 1f)) != null;

        // Route inputs
        _panOnly.HandlePan(ctx, canvasRect, !(overNode || overEdge));

        HandleEdgeClickSelection(ctx, canvasRect, overNode);
        HandleMarquee(ctx, canvasRect, overNode, overEdge);

        // Resize (mouse) — provide node rect resolver
        _resize.HandleResize(ctx, canvasRect, n => _appearance.GetNodeRect(n, ctx.ShowInNodeCache));

        HandleGraphContextMenu(ctx, canvasRect, overNode, overEdge);

        // Deselect on empty LMB click
        SelectionUtil.HandleEmptyLeftClick(ctx, canvasRect, () => IsMouseOverAnyNode(ctx, canvasRect));
    }

    /// <summary>
    /// Node window body used by GUI.Window. Contains only node-specific input concerns
    /// (selection toggling, node context menu and finalizing connection).
    /// </summary>
    public void DrawNodeWindowBody(int windowId, IDagNode node, DagEditorContext ctx)
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
                    _notify?.Invoke(err);
                else
                    EditorUtility.SetDirty(ctx.Graph);

                ctx.PendingFromNodeId = null;
                e.Use();
                return; // prevent selection on this click
            }
        }

        // Selection and node context menu (disabled during resize drag)
        if (!(ctx.Resize.Active && ReferenceEquals(ctx.Resize.Node, node)))
        {
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (!e.shift) ctx.SelectedEdges.Clear();

                if (e.shift)
                {
                    if (!ctx.SelectedNodes.Add(node))
                        ctx.SelectedNodes.Remove(node);
                }
                else
                {
                    if (!ctx.SelectedNodes.Contains(node))
                    {
                        ctx.SelectedNodes.Clear();
                        ctx.SelectedNodes.Add(node);
                    }
                }

                _repaint?.Invoke();
            }

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
                        _repaint?.Invoke();
                    });

                e.Use();
            }
        }

        using (new EditorGUILayout.VerticalScope())
        {
            // Inline [ShowInNode] fields
            _appearance.DrawInlineFields(node, ctx.ShowInNodeCache);
        }

        if (!(ctx.Resize.Active && ReferenceEquals(ctx.Resize.Node, node)))
            GUI.DragWindow();
    }

    // -----------------------------
    // Internal helpers (input only)
    // -----------------------------

    private static void HandleGlobalEscape(DagEditorContext ctx)
    {
        Event e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            if (!string.IsNullOrEmpty(ctx.PendingFromNodeId))
            {
                ctx.PendingFromNodeId = null;
                e.Use();
            }
    }

    private void HandleShortcuts(DagEditorContext ctx, Rect canvasRect)
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
                _notify?.Invoke($"Copied {ctx.SelectedNodes.Count} node(s)");
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
                _notify?.Invoke($"Pasted {created.Count} node(s)");
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
                    _notify?.Invoke($"Duplicated {created.Count} node(s)");
                }

                e.Use();
            }
        }
    }

    private void HandleEdgeClickSelection(DagEditorContext ctx, Rect canvasRect, bool overNode)
    {
        Event e = Event.current;
        if (!canvasRect.Contains(e.mousePosition)) return;

        // Block LMB selection while in connection mode to avoid confusion
        if (!string.IsNullOrEmpty(ctx.PendingFromNodeId) && e.type == EventType.MouseDown && e.button == 0) return;

        Vector2 mouseLogical = CoordinateSystem.ScreenToLogical(e.mousePosition, canvasRect, ctx.Scroll, 1f);
        if (overNode) return;

        IDagEdge<IDagNode> hit = _edges.HitTestEdge(ctx, mouseLogical);

        if (e.type == EventType.MouseDown && e.button == 0)
            if (hit != null)
            {
                if (!e.shift) ctx.SelectedNodes.Clear();

                if (e.shift)
                {
                    if (!ctx.SelectedEdges.Add(hit))
                        ctx.SelectedEdges.Remove(hit);
                }
                else
                {
                    ctx.SelectedEdges.Clear();
                    ctx.SelectedEdges.Add(hit);
                }

                _repaint?.Invoke();
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

    private void HandleMarquee(DagEditorContext ctx, Rect canvasRect, bool overNode, bool overEdge)
    {
        Event e = Event.current;
        if (!canvasRect.Contains(e.mousePosition)) return;

        // Block marquee start while connecting (LMB on empty space)
        if (!string.IsNullOrEmpty(ctx.PendingFromNodeId) && e.type == EventType.MouseDown && e.button == 0 &&
            !(overNode || overEdge))
        {
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

    private bool IsMouseOverAnyNode(DagEditorContext ctx, Rect canvasRect)
    {
        Vector2 mouseLogical =
            CoordinateSystem.ScreenToLogical(Event.current.mousePosition, canvasRect, ctx.Scroll, 1f);
        return ctx.Graph.Nodes.Any(n =>
            n != null && _appearance.GetNodeRect(n, ctx.ShowInNodeCache).Contains(mouseLogical));
    }
}
#endif