#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Main editor window for DAG authoring. This orchestrates input handling, drawing, selection,
/// context menus and clipboard/duplication features. The canvas runs at 1:1 scale (no zoom).
/// </summary>
public class DagGraphEditorWindow : EditorWindow
{
    // Services (composition over inheritance)
    private readonly GridRenderer _grid = new();
    private readonly NodeAppearance _appearance = new();
    private readonly EdgeRenderer _edges = new();
    private readonly PanController _panOnly = new();
    private readonly MarqueeController _marquee = new();
    private readonly ResizeController _resize = new();
    private readonly InspectorPanel _inspector = new();

    // Shared context
    private readonly DagEditorContext _ctx = new();

    // Shared state bag for toolbar providers (optional use).
    private readonly System.Collections.Generic.Dictionary<string, object> _toolbarState = new();

    /// <summary>
    /// Opens the editor window.
    /// </summary>
    [MenuItem("Window/Graphs/DAG Editor")]
    public static void Open()
    {
        DagGraphEditorWindow win = GetWindow<DagGraphEditorWindow>("DAG Editor");
        win.minSize = new Vector2(900, 450);
    }

    /// <summary>
    /// Unity enable hook. Initializes stable defaults.
    /// </summary>
    private void OnEnable()
    {
        _ctx.Zoom = 1f; // hard-lock to 1:1 (no zoom)
    }

    /// <summary>
    /// Unity disable hook. Cleans temporary editors to avoid leaks.
    /// </summary>
    private void OnDisable()
    {
        if (_ctx.CachedNodeEditor != null) DestroyImmediate(_ctx.CachedNodeEditor);
        if (_ctx.CachedEdgeEditor != null) DestroyImmediate(_ctx.CachedEdgeEditor);
    }

    /// <summary>
    /// Main GUI loop. Handles input, draws canvas and UI, manages context menus and clipboard actions.
    /// </summary>
    private void OnGUI()
    {
        _ctx.Zoom = 1f; // enforce every frame
        DrawToolbar();

        if (!_ctx.Graph)
        {
            EditorGUILayout.HelpBox("Create or load a DagGraph asset to begin.", MessageType.Info);
            return;
        }

        // Layout: canvas + inspector + scrollbars
        Rect fullRect = new(0, DagEditorLayout.ToolbarHeight, position.width,
            position.height - DagEditorLayout.ToolbarHeight);
        Rect canvasRect = new(fullRect.x, fullRect.y,
            fullRect.width - DagEditorLayout.InspectorWidth - DagEditorLayout.ScrollbarThickness,
            fullRect.height - DagEditorLayout.ScrollbarThickness);
        Rect inspectorRect = new(canvasRect.xMax, fullRect.y, DagEditorLayout.InspectorWidth,
            canvasRect.height + DagEditorLayout.ScrollbarThickness);

        // Global cancel for connection mode
        Event e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            if (_ctx.PendingFromNodeId.HasValue)
            {
                _ctx.PendingFromNodeId = null;
                Repaint();
                e.Use();
            }

        // Shortcuts
        HandleShortcuts(canvasRect);

        // Delete key
        SelectionUtil.HandleDeleteShortcut(_ctx);

        // Hover checks
        bool overNode = IsMouseOverAnyNode(canvasRect);
        bool overEdge = _edges.HitTestEdge(_ctx,
            CoordinateSystem.ScreenToLogical(Event.current.mousePosition, canvasRect, _ctx.Scroll, 1f)) != null;

        // Input routing
        _panOnly.HandlePan(_ctx, canvasRect, !(overNode || overEdge));
        HandleEdgeClickSelection(canvasRect);
        HandleMarquee(canvasRect, overNode, overEdge);
        _resize.HandleResize(_ctx, canvasRect, n => _appearance.GetNodeRect(n, _ctx.ShowInNodeCache));
        HandleGraphContextMenu(canvasRect, overNode, overEdge);

        // Background grid
        _grid.Draw(canvasRect, _ctx.Scroll);

        // -----------------------------------------------------------------------------------------
        // Canvas groups (scroll only)
        // Outer group clips to the viewport (canvasRect). Inner group is the full virtual canvas
        // shifted by -Scroll, so windows are NOT clipped by the viewport size while we pan.
        // -----------------------------------------------------------------------------------------
        GUI.BeginGroup(canvasRect); // viewport clipping only
        Rect contentRect = new(-_ctx.Scroll.x, -_ctx.Scroll.y, _ctx.VirtualCanvasSize.x, _ctx.VirtualCanvasSize.y);
        GUI.BeginGroup(contentRect); // full logical canvas, translated by scroll

        // Edges behind nodes
        _edges.DrawEdges(_ctx, canvasRect);

        // Nodes
        BeginWindows(); // IMPORTANT: must live in the window class
        for (int i = 0; i < _ctx.Graph.Nodes.Count; i++)
        {
            IDagNode node = _ctx.Graph.Nodes[i];
            if (node == null) continue;

            GUIStyle style = _appearance.ResolveStyle(_ctx, node, _ctx.SelectedNodes.Contains(node));
            Vector2 beforePos = node.Position;
            Rect nodeRect = _appearance.GetNodeRect(node, _ctx.ShowInNodeCache);

            Rect windowRect = GUI.Window(i, nodeRect, id => DrawNodeWindowBody(id, node), node.Title, style);

            // Move single or multi-selection when window is dragged (no resize active)
            if (!_ctx.Resize.Active && windowRect.position != beforePos)
            {
                Vector2 delta = windowRect.position - beforePos;
                Undo.RecordObject(_ctx.Graph, _ctx.SelectedNodes.Contains(node) ? "Move Nodes" : "Move Node");

                if (_ctx.SelectedNodes.Contains(node))
                {
                    foreach (IDagNode sn in _ctx.SelectedNodes.ToList())
                    {
                        if (sn != null) _ctx.Graph.MoveNode(sn.Id, sn.Position + delta);
                    }
                }
                else
                {
                    _ctx.Graph.MoveNode(node.Id, windowRect.position);
                }

                EditorUtility.SetDirty(_ctx.Graph);
            }
        }

        EndWindows(); // IMPORTANT: must live in the window class

        GUI.EndGroup(); // contentRect
        GUI.EndGroup(); // canvasRect

        // Overlays + side panels
        DrawConnectionModeOverlay(canvasRect);
        _marquee.DrawOverlay(_ctx, canvasRect);
        DrawScrollbars(canvasRect);
        _inspector.Draw(_ctx, inspectorRect);

        // Deselect on empty click
        SelectionUtil.HandleEmptyLeftClick(_ctx, canvasRect, () => IsMouseOverAnyNode(canvasRect));
    }

    /// <summary>
    /// Renders the toolbar using the extensible ToolbarService. This is the only place that uses EditorStyles.toolbar scope.
    /// </summary>
    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            ToolbarContext ctx = new(
                _ctx.Graph,
                _ctx.SelectedNodes,
                _ctx.SelectedEdges,
                _ctx.VirtualCanvasSize,
                _ctx.PendingFromNodeId.HasValue,
                CreateNewAsset,
                AddNodeAtViewportCenter,
                Repaint,
                (msg) => ShowNotification(new GUIContent(msg)),
                _toolbarState
            );

            // Draw and sync back any changes to the graph reference made by providers
            ToolbarService.Draw(ctx);
            _ctx.Graph = ctx.Graph;
        }
    }

    /// <summary>
    /// Creates a new graph asset on disk and loads it in the window.
    /// </summary>
    private void CreateNewAsset()
    {
        string path =
            EditorUtility.SaveFilePanelInProject("Create DAG Graph", "NewDagGraph", "asset", "Choose a location");
        if (!string.IsNullOrEmpty(path))
        {
            DagGraph asset = CreateInstance<DagGraph>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            _ctx.Graph = asset;
            _ctx.Scroll = Vector2.zero;
            _ctx.Zoom = 1f;
            _ctx.SelectedNodes.Clear();
            _ctx.SelectedEdges.Clear();
            Repaint();
        }
    }

    /// <summary>
    /// Node window body. Handles selection, node context menu and finalizes connection mode.
    /// </summary>
    private void DrawNodeWindowBody(int windowId, IDagNode node)
    {
        Event e = Event.current;

        // Finalize connection by clicking a destination node
        if (_ctx.PendingFromNodeId.HasValue && e.type == EventType.MouseDown && e.button == 0)
        {
            int fromId = _ctx.PendingFromNodeId.Value;
            if (fromId != node.Id)
            {
                Undo.RecordObject(_ctx.Graph, "Add Edge");
                if (!_ctx.Graph.TryAddEdge(fromId, node.Id, out string err))
                    ShowNotification(new GUIContent(err));
                else
                    EditorUtility.SetDirty(_ctx.Graph);

                _ctx.PendingFromNodeId = null;
                e.Use();
                return; // prevent selection on this click
            }
        }

        // Selection and node context menu (disabled during resize drag)
        if (!(_ctx.Resize.Active && ReferenceEquals(_ctx.Resize.Node, node)))
        {
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (!e.shift) _ctx.SelectedEdges.Clear();

                if (e.shift)
                {
                    if (!_ctx.SelectedNodes.Add(node)) _ctx.SelectedNodes.Remove(node);
                }
                else
                {
                    if (!_ctx.SelectedNodes.Contains(node))
                    {
                        _ctx.SelectedNodes.Clear();
                        _ctx.SelectedNodes.Add(node);
                    }
                }

                Repaint();
            }

            if (e.type == EventType.ContextClick || (e.type == EventType.MouseDown && e.button == 1))
            {
                if (!_ctx.SelectedNodes.Contains(node) && !e.shift)
                {
                    _ctx.SelectedNodes.Clear();
                    _ctx.SelectedNodes.Add(node);
                }
                else
                {
                    _ctx.SelectedNodes.Add(node);
                }

                _ctx.SelectedEdges.Clear();

                // Inject connection-mode starter into Node menu
                ContextMenuService.ShowNodeMenu(
                    _ctx.Graph,
                    _ctx.SelectedNodes.ToList(),
                    (fromId) =>
                    {
                        _ctx.PendingFromNodeId = fromId;
                        Repaint();
                    });

                e.Use();
            }
        }

        using (new EditorGUILayout.VerticalScope())
        {
            // Inline [ShowInNode] fields
            _appearance.DrawInlineFields(node, _ctx.ShowInNodeCache);
        }

        if (!(_ctx.Resize.Active && ReferenceEquals(_ctx.Resize.Node, node)))
            GUI.DragWindow();
    }

    /// <summary>
    /// Handles edge click selection and shows Edge context menu.
    /// Blocks LMB selection while in connection mode to avoid confusion.
    /// </summary>
    private void HandleEdgeClickSelection(Rect canvasRect)
    {
        Event e = Event.current;
        if (!canvasRect.Contains(e.mousePosition)) return;

        if (_ctx.PendingFromNodeId.HasValue && e.type == EventType.MouseDown && e.button == 0)
            return; // ignore while connecting

        Vector2 mouseLogical = CoordinateSystem.ScreenToLogical(e.mousePosition, canvasRect, _ctx.Scroll, 1f);
        if (IsMouseOverAnyNode(canvasRect)) return;

        IDagEdge<IDagNode> hit = _edges.HitTestEdge(_ctx, mouseLogical);

        if (e.type == EventType.MouseDown && e.button == 0)
            if (hit != null)
            {
                if (!e.shift) _ctx.SelectedNodes.Clear();

                if (e.shift)
                {
                    if (!_ctx.SelectedEdges.Add(hit)) _ctx.SelectedEdges.Remove(hit);
                }
                else
                {
                    _ctx.SelectedEdges.Clear();
                    _ctx.SelectedEdges.Add(hit);
                }

                Repaint();
                e.Use();
            }

        if ((e.type == EventType.MouseDown && e.button == 1) || e.type == EventType.ContextClick)
            if (hit != null)
            {
                if (!_ctx.SelectedEdges.Contains(hit) && !e.shift)
                {
                    _ctx.SelectedEdges.Clear();
                    _ctx.SelectedEdges.Add(hit);
                }
                else
                {
                    _ctx.SelectedEdges.Add(hit);
                }

                if (!e.shift) _ctx.SelectedNodes.Clear();

                ContextMenuService.ShowEdgeMenu(_ctx.Graph, _ctx.SelectedEdges.ToList());
                e.Use();
            }
    }

    /// <summary>
    /// Shows Graph (canvas) context menu when RMB on empty canvas.
    /// </summary>
    private void HandleGraphContextMenu(Rect canvasRect, bool overNode, bool overEdge)
    {
        Event e = Event.current;
        if (!canvasRect.Contains(e.mousePosition)) return;

        bool rmb = (e.type == EventType.MouseDown && e.button == 1) || e.type == EventType.ContextClick;
        if (!rmb) return;

        if (!(overNode || overEdge))
        {
            Vector2 logical = CoordinateSystem.ScreenToLogical(e.mousePosition, canvasRect, _ctx.Scroll, 1f);
            ContextMenuService.ShowGraphMenu(_ctx.Graph, logical);
            e.Use();
        }
    }

    /// <summary>
    /// Handles marquee selection start/update and prevents starting while connecting.
    /// </summary>
    private void HandleMarquee(Rect canvasRect, bool overNode, bool overEdge)
    {
        Event e = Event.current;
        if (!canvasRect.Contains(e.mousePosition)) return;

        if (_ctx.PendingFromNodeId.HasValue && e.type == EventType.MouseDown && e.button == 0 &&
            !(overNode || overEdge))
        {
            // Block marquee start while connecting
            e.Use();
            return;
        }

        if (e.type == EventType.MouseDown && e.button == 0 && !(overNode || overEdge))
        {
            _marquee.HandleMarquee(_ctx, canvasRect, n => _appearance.GetNodeRect(n, _ctx.ShowInNodeCache));
            return;
        }

        if (_ctx.Marquee.Active)
            _marquee.HandleMarquee(_ctx, canvasRect, n => _appearance.GetNodeRect(n, _ctx.ShowInNodeCache));
    }

    /// <summary>
    /// Tests whether the mouse is over any node (logical space).
    /// </summary>
    private bool IsMouseOverAnyNode(Rect canvasRect)
    {
        Vector2 mouseLogical =
            CoordinateSystem.ScreenToLogical(Event.current.mousePosition, canvasRect, _ctx.Scroll, 1f);
        return _ctx.Graph.Nodes.Any(n =>
            n != null && _appearance.GetNodeRect(n, _ctx.ShowInNodeCache).Contains(mouseLogical));
    }

    /// <summary>
    /// Draws a subtle overlay banner when the editor is in connection mode.
    /// </summary>
    private void DrawConnectionModeOverlay(Rect canvasRect)
    {
        if (!_ctx.PendingFromNodeId.HasValue) return;

        Rect banner = new(canvasRect.x + 8, canvasRect.y + 8, canvasRect.width - 16, 26);
        Color prev = GUI.color;
        GUI.color = new Color(0.15f, 0.55f, 1f, 0.10f);
        EditorGUI.DrawRect(banner, GUI.color);
        GUI.color = prev;

        GUIContent content = new("Connection mode: click a destination node (Esc to cancel)");
        GUIStyle style = EditorStyles.boldLabel;
        Vector2 size = style.CalcSize(content);
        Rect label = new(banner.x + 8, banner.y + (banner.height - size.y) * 0.5f, size.x, size.y);
        GUI.Label(label, content, style);
    }

    /// <summary>
    /// Renders horizontal and vertical scrollbars and clamps the scroll within the virtual canvas.
    /// </summary>
    private void DrawScrollbars(Rect canvasRect)
    {
        Vector2 viewport = canvasRect.size;

        float maxX = Mathf.Max(_ctx.VirtualCanvasSize.x - viewport.x, 0f);
        float maxY = Mathf.Max(_ctx.VirtualCanvasSize.y - viewport.y, 0f);

        _ctx.Scroll.x = Mathf.Clamp(_ctx.Scroll.x, 0f, maxX);
        _ctx.Scroll.y = Mathf.Clamp(_ctx.Scroll.y, 0f, maxY);

        Rect hRect = new(canvasRect.x, canvasRect.yMax, canvasRect.width, DagEditorLayout.ScrollbarThickness);
        float newX = GUI.HorizontalScrollbar(hRect, _ctx.Scroll.x, viewport.x, 0f, _ctx.VirtualCanvasSize.x);
        if (!Mathf.Approximately(newX, _ctx.Scroll.x))
            _ctx.Scroll.x = Mathf.Clamp(newX, 0f, maxX);

        Rect vRect = new(canvasRect.xMax, canvasRect.y, DagEditorLayout.ScrollbarThickness, canvasRect.height);
        float newY = GUI.VerticalScrollbar(vRect, _ctx.Scroll.y, viewport.y, 0f, _ctx.VirtualCanvasSize.y);
        if (!Mathf.Approximately(newY, _ctx.Scroll.y))
            _ctx.Scroll.y = Mathf.Clamp(newY, 0f, maxY);
    }

    /// <summary>
    /// Handles Ctrl+C (copy), Ctrl+V (paste at viewport center) and Ctrl+D (duplicate with offset).
    /// </summary>
    private void HandleShortcuts(Rect canvasRect)
    {
        Event e = Event.current;
        if (e.type != EventType.KeyDown) return;

        bool action = e.control || e.command; // Ctrl on Windows/Linux, Cmd on macOS
        if (!action) return;

        if (e.keyCode == KeyCode.C)
        {
            // Copy selection
            if (_ctx.SelectedNodes.Count > 0)
            {
                GraphClipboardService.Copy(_ctx.Graph, _ctx.SelectedNodes.ToList());
                ShowNotification(new GUIContent($"Copied {_ctx.SelectedNodes.Count} node(s)"));
                e.Use();
            }
        }
        else if (e.keyCode == KeyCode.V)
        {
            // Paste at viewport center
            Vector2 vp = new(canvasRect.width, canvasRect.height);
            Vector2 origin = _ctx.Scroll + vp * 0.5f;

            Undo.RecordObject(_ctx.Graph, "Paste Nodes");
            List<DagNode> created = GraphClipboardService.TryPaste(_ctx.Graph, origin);
            if (created != null && created.Count > 0)
            {
                _ctx.SelectedEdges.Clear();
                _ctx.SelectedNodes.Clear();
                foreach (DagNode n in created)
                {
                    _ctx.SelectedNodes.Add(n);
                }

                EditorUtility.SetDirty(_ctx.Graph);
                ShowNotification(new GUIContent($"Pasted {created.Count} node(s)"));
            }

            e.Use();
        }
        else if (e.keyCode == KeyCode.D)
        {
            // Duplicate with offset
            if (_ctx.SelectedNodes.Count > 0)
            {
                Undo.RecordObject(_ctx.Graph, "Duplicate Nodes");
                List<DagNode> created =
                    GraphClipboardService.Duplicate(_ctx.Graph, _ctx.SelectedNodes.ToList(), new Vector2(24f, 24f));
                if (created != null && created.Count > 0)
                {
                    _ctx.SelectedEdges.Clear();
                    _ctx.SelectedNodes.Clear();
                    foreach (DagNode n in created)
                    {
                        _ctx.SelectedNodes.Add(n);
                    }

                    EditorUtility.SetDirty(_ctx.Graph);
                    ShowNotification(new GUIContent($"Duplicated {created.Count} node(s)"));
                }

                e.Use();
            }
        }
    }

    /// <summary>
    /// Adds a node at the logical center of the current viewport.
    /// </summary>
    private void AddNodeAtViewportCenter()
    {
        if (_ctx.Graph == null) return;

        Undo.RecordObject(_ctx.Graph, "Add Node");

        float vpWidth = position.width - DagEditorLayout.InspectorWidth - DagEditorLayout.ScrollbarThickness;
        float vpHeight = position.height - DagEditorLayout.ToolbarHeight - DagEditorLayout.ScrollbarThickness;
        Vector2 center = _ctx.Scroll + new Vector2(vpWidth, vpHeight) * 0.5f;

        DagNode n = _ctx.Graph.CreateNode("Node " + Random.Range(0, 9999), center);
        _ctx.SelectedEdges.Clear();
        _ctx.SelectedNodes.Clear();
        _ctx.SelectedNodes.Add(n);
        EditorUtility.SetDirty(_ctx.Graph);
        Repaint();
    }
}
#endif