#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.NodeEditor
{
    /// <summary>
    /// Main editor window for visual graph editor authoring. Draw-only; delegates all input to InputController.
    /// </summary>
    public class VisualGraphEditorWindow : EditorWindow
    {
        // Services (composition)
        private readonly GridRenderer _grid = new();
        private readonly NodeAppearance _appearance = new();
        private readonly EdgeRenderer _edges = new();
        private readonly PanController _panOnly = new();
        private readonly MarqueeController _marquee = new();
        private readonly ResizeController _resize = new();
        private readonly InspectorPanel _inspector = new();

        // NEW: Input controller
        private readonly InputController _input = new();

        // Shared context
        private readonly NodeEditorContext _ctx = new();

        // Shared state bag for toolbar providers (optional use).
        private readonly Dictionary<string, object> _toolbarState = new();

        /// <summary>
        /// Opens the editor window.
        /// </summary>
        [MenuItem("Tools/LegendaryTool/Graphs/NodeEditor")]
        public static void Open()
        {
            VisualGraphEditorWindow win = GetWindow<VisualGraphEditorWindow>("Node Editor");
            win.minSize = new Vector2(900, 450);
        }

        /// <summary>
        /// Unity enable hook. Initializes stable defaults.
        /// </summary>
        private void OnEnable()
        {
            _ctx.Zoom = 1f; // hard-lock to 1:1 (no zoom)

            // Initialize input controller with services
            _input.Initialize(_grid, _appearance, _edges, _panOnly, _marquee, _resize);
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
                EditorGUILayout.HelpBox("Create or load a Graph asset to begin.", MessageType.Info);
                return;
            }

            // Layout: canvas + inspector + scrollbars
            Rect fullRect = new(0, NodeEditorLayout.ToolbarHeight, position.width,
                position.height - NodeEditorLayout.ToolbarHeight);

            Rect canvasRect = new(fullRect.x, fullRect.y,
                fullRect.width - NodeEditorLayout.InspectorWidth - NodeEditorLayout.ScrollbarThickness,
                fullRect.height - NodeEditorLayout.ScrollbarThickness);

            // Keep inspector to the right of vertical scrollbar (prevents overlap)
            Rect inspectorRect = new(
                canvasRect.xMax + NodeEditorLayout.ScrollbarThickness,
                fullRect.y,
                NodeEditorLayout.InspectorWidth,
                fullRect.height);

            // --- INPUT (no GUILayout here) ---
            _input.HandleFrameInputs(_ctx, canvasRect, this);

            // --- DRAW ---

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

            // Nodes (draw + node-local input through controller)
            BeginWindows(); // IMPORTANT: must live in the window class
            for (int i = 0; i < _ctx.Graph.Nodes.Count; i++)
            {
                IEditorNode node = _ctx.Graph.Nodes[i];
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
                        foreach (IEditorNode sn in _ctx.SelectedNodes.ToList())
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
                    !string.IsNullOrEmpty(_ctx.PendingFromNodeId),
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
                EditorUtility.SaveFilePanelInProject("Create Graph", "New Graph", "asset", "Choose a location");
            if (!string.IsNullOrEmpty(path))
            {
                Graph asset = CreateInstance<Graph>();
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
        /// Node window body. Draws content and delegates input to InputController.
        /// </summary>
        private void DrawNodeWindowBody(int windowId, IEditorNode node)
        {
            // Input first (no GUILayout inside)
            bool consumed = _input.HandleNodeWindowInput(_ctx, node, Repaint);
            if (consumed)
                return;

            // Draw inline contents
            using (new EditorGUILayout.VerticalScope())
            {
                // Inline [ShowInNode] fields
                _appearance.DrawInlineFields(node, _ctx.ShowInNodeCache);
            }

            // Allow dragging (provided no resize on this node is active)
            if (!(_ctx.Resize.Active && ReferenceEquals(_ctx.Resize.Node, node)))
                GUI.DragWindow();
        }

        private void DrawConnectionModeOverlay(Rect canvasRect)
        {
            if (string.IsNullOrEmpty(_ctx.PendingFromNodeId)) return;

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

            Rect hRect = new(canvasRect.x, canvasRect.yMax, canvasRect.width, NodeEditorLayout.ScrollbarThickness);
            float newX = GUI.HorizontalScrollbar(hRect, _ctx.Scroll.x, viewport.x, 0f, _ctx.VirtualCanvasSize.x);
            if (!Mathf.Approximately(newX, _ctx.Scroll.x))
                _ctx.Scroll.x = Mathf.Clamp(newX, 0f, maxX);

            Rect vRect = new(canvasRect.xMax, canvasRect.y, NodeEditorLayout.ScrollbarThickness, canvasRect.height);
            float newY = GUI.VerticalScrollbar(vRect, _ctx.Scroll.y, viewport.y, 0f, _ctx.VirtualCanvasSize.y);
            if (!Mathf.Approximately(newY, _ctx.Scroll.y))
                _ctx.Scroll.y = Mathf.Clamp(newY, 0f, maxY);
        }

        /// <summary>
        /// Adds a node at the logical center of the current viewport.
        /// </summary>
        private void AddNodeAtViewportCenter()
        {
            if (_ctx.Graph == null) return;

            Undo.RecordObject(_ctx.Graph, "Add Node");

            float vpWidth = position.width - NodeEditorLayout.InspectorWidth - NodeEditorLayout.ScrollbarThickness;
            float vpHeight = position.height - NodeEditorLayout.ToolbarHeight - NodeEditorLayout.ScrollbarThickness;
            Vector2 center = _ctx.Scroll + new Vector2(vpWidth, vpHeight) * 0.5f;

            Node n = _ctx.Graph.CreateNode("Node " + Random.Range(0, 9999), center);
            _ctx.SelectedEdges.Clear();
            _ctx.SelectedNodes.Clear();
            _ctx.SelectedNodes.Add(n);
            EditorUtility.SetDirty(_ctx.Graph);
            Repaint();
        }
    }
}
#endif