#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom editor window for authoring and inspecting a DAG asset.
/// The window renders a scrollable, zoomable canvas with nodes and curved edges,
/// supports multi-selection (Shift), marquee selection, right-mouse panning,
/// per-node custom size and styles, inline fields via <see cref="ShowInNodeAttribute"/>,
/// edge/node context menus, and edge selection. The inspector panel on the right
/// renders Unity's inspector for the currently selected node or edge.
/// </summary>
public class DagGraphEditorWindow : EditorWindow
{
    // --- Backing asset ---
    private DagGraph graph;

    // --- Canvas state (logical scroll and zoom) ---
    private Vector2 canvasScroll;
    private float zoom = 1f;
    private static readonly Vector2 VirtualCanvasSize = new Vector2(8000f, 8000f);

    // --- Default node layout when no custom size is provided ---
    private const float DefaultNodeWidth = 200f;
    private const float DefaultNodeBaseHeight = 80f;
    private const float InlineFieldHeight = 20f;

    // --- Docked UI layout ---
    private const float ScrollbarThickness = 16f;
    private const float InspectorWidth = 320f;

    // --- Edge creation (rubber-band) ---
    private int? pendingFromNode = null;

    // --- Selection sets (abstractions to preserve LSP) ---
    private readonly HashSet<IDagNode> selectedNodes = new HashSet<IDagNode>();
    private readonly HashSet<IDagEdge<IDagNode>> selectedEdges = new HashSet<IDagEdge<IDagNode>>();

    // --- Pan / zoom / marquee state ---
    private bool rmbPanningActive = false;
    private bool marqueeActive = false;
    private Vector2 marqueeStartLogical, marqueeEndLogical;

    // --- Default GUI styles (fallbacks when node doesn't provide custom ones) ---
    private GUIStyle defaultNodeStyle;
    private GUIStyle defaultNodeSelectedStyle;

    // --- Inspector editors cache to avoid allocations ---
    private Editor nodeInspectorEditor;
    private Editor edgeInspectorEditor;

    // --- Reflection cache for [ShowInNode] ---
    private readonly Dictionary<System.Type, FieldInfo[]> showCache = new Dictionary<System.Type, FieldInfo[]>();

    // --- Resize state ---
    private bool resizingActive = false;
    private IDagNode resizingNode = null;
    private bool resizeLeft, resizeRight, resizeTop, resizeBottom;
    private Rect resizeStartRectLogical;
    private Vector2 resizeStartMouseLogical;
    private const float ResizeBandPx = 6f;
    private const float MinNodeWidth = 100f;
    private const float MinNodeHeight = 60f;

    /// <summary>
    /// Opens the window from the Unity menu.
    /// </summary>
    [MenuItem("Window/Graphs/DAG Editor")]
    public static void Open()
    {
        var win = GetWindow<DagGraphEditorWindow>("DAG Editor");
        win.minSize = new Vector2(900, 450);
    }

    private void OnEnable()
    {
        EnsureDefaultStyles();
    }

    private void OnDisable()
    {
        if (nodeInspectorEditor != null) DestroyImmediate(nodeInspectorEditor);
        if (edgeInspectorEditor != null) DestroyImmediate(edgeInspectorEditor);
    }

    private void OnGUI()
    {
        EnsureDefaultStyles();

        DrawToolbar();

        if (!graph)
        {
            EditorGUILayout.HelpBox("Create or load a DagGraph asset to begin.", MessageType.Info);
            return;
        }

        // Compute docked layout rectangles
        var fullRect = new Rect(0, 20, position.width, position.height - 20);
        var canvasRect = new Rect(fullRect.x, fullRect.y, fullRect.width - InspectorWidth - ScrollbarThickness, fullRect.height - ScrollbarThickness);
        var inspectorRect = new Rect(canvasRect.xMax, fullRect.y, InspectorWidth, canvasRect.height + ScrollbarThickness);

        // Input handlers (clamped to the canvas to avoid stealing inspector focus)
        HandleShortcuts();
        HandleMouseZoom(canvasRect);
        HandleRightMousePan(canvasRect);
        HandleMarquee(canvasRect);
        HandleEdgeClickSelection(canvasRect);
        HandleNodeResizeInput(canvasRect);

        // Draw main regions
        DrawCanvas(canvasRect);
        DrawMarqueeOverlay(canvasRect);
        DrawScrollbars(canvasRect);
        DrawInspector(inspectorRect);

        // Ensure clicks outside the canvas never clear selection accidentally
        ConsumeEmptyClicksOutsideCanvas(canvasRect);
    }

    // -------------------- Styles & size helpers --------------------

    /// <summary>
    /// Initializes default fallback styles used when nodes do not provide custom styles.
    /// </summary>
    private void EnsureDefaultStyles()
    {
        if (defaultNodeStyle == null || defaultNodeSelectedStyle == null)
        {
            try
            {
                defaultNodeStyle = new GUIStyle("flow node 0");
                defaultNodeSelectedStyle = new GUIStyle("flow node 0 on");
            }
            catch
            {
                defaultNodeStyle = new GUIStyle(EditorStyles.helpBox);
                defaultNodeSelectedStyle = new GUIStyle(EditorStyles.helpBox);
            }
        }
    }

    /// <summary>
    /// Resolves the GUIStyle for a node: uses custom style names if present,
    /// otherwise falls back to defaults.
    /// </summary>
    private GUIStyle ResolveNodeStyle(IDagNode node, bool selected)
    {
        if (node == null || !node.HasCustomNodeStyles)
            return selected ? defaultNodeSelectedStyle : defaultNodeStyle;

        var skin = node.StyleSkin ? node.StyleSkin : GUI.skin;
        var styleName = selected ? node.SelectedStyleName : node.NormalStyleName;

        if (!string.IsNullOrEmpty(styleName) && skin != null && skin.FindStyle(styleName) != null)
            return new GUIStyle(skin.GetStyle(styleName));

        if (!string.IsNullOrEmpty(styleName) && GUI.skin != null && GUI.skin.FindStyle(styleName) != null)
            return new GUIStyle(GUI.skin.GetStyle(styleName));

        return selected ? defaultNodeSelectedStyle : defaultNodeStyle;
    }

    /// <summary>
    /// Computes the node rectangle in logical coordinates. If a custom size is provided,
    /// it takes precedence; otherwise the height is expanded to fit inline fields.
    /// </summary>
    private Rect NodeRect(IDagNode n)
    {
        if (n != null && n.HasCustomNodeSize)
        {
            var size = n.NodeSize;
            return new Rect(n.Position, new Vector2(Mathf.Max(10f, size.x), Mathf.Max(10f, size.y)));
        }

        var count = GetShowFieldsFor(n).Length;
        float dynamicAdd = count > 0 ? (count * (InlineFieldHeight + 4f)) + 6f : 0f;
        float height = DefaultNodeBaseHeight + dynamicAdd;
        return new Rect(n.Position, new Vector2(DefaultNodeWidth, height));
    }

    /// <summary>
    /// Caches and returns serialized fields decorated with <see cref="ShowInNodeAttribute"/>.
    /// </summary>
    private FieldInfo[] GetShowFieldsFor(IDagNode node)
    {
        if (node is null) return System.Array.Empty<FieldInfo>();
        var t = node.GetType();
        if (showCache.TryGetValue(t, out var cached)) return cached;

        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var fields = t.GetFields(flags);
        var list = new List<FieldInfo>();

        foreach (var f in fields)
        {
            if (f.IsStatic) continue;

            bool isSerializable =
                (f.IsPublic && f.GetCustomAttribute<System.NonSerializedAttribute>() == null)
                || f.GetCustomAttribute<SerializeField>() != null;

            if (!isSerializable) continue;
            if (f.GetCustomAttribute<ShowInNodeAttribute>() != null)
                list.Add(f);
        }

        var arr = list.ToArray();
        showCache[t] = arr;
        return arr;
    }

    // -------------------- Toolbar --------------------

    /// <summary>
    /// Draws the top toolbar: asset slot, creation helper, add-node shortcut and zoom slider.
    /// </summary>
    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            graph = (DagGraph)EditorGUILayout.ObjectField(graph, typeof(DagGraph), false, GUILayout.Width(350));

            if (GUILayout.Button("New Asset", EditorStyles.toolbarButton, GUILayout.Width(90)))
                CreateNewAsset();

            if (GUILayout.Button("Add Node", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                Undo.RecordObject(graph, "Add Node");
                var vp = GetViewportSize();
                var center = canvasScroll + vp * 0.5f;
                var n = graph.CreateNode("Node " + Random.Range(0, 9999), center);
                selectedEdges.Clear();
                selectedNodes.Clear();
                selectedNodes.Add(n);
                EditorUtility.SetDirty(graph);
            }

            GUILayout.FlexibleSpace();

            GUILayout.Label($"Zoom: {zoom:0.00}", EditorStyles.miniLabel);
            float newZoom = GUILayout.HorizontalSlider(zoom, 0.5f, 2f, GUILayout.Width(120));
            if (!Mathf.Approximately(newZoom, zoom))
                ZoomAroundPoint(newZoom, position.size * 0.5f);
        }
    }

    /// <summary>
    /// Creates a new graph asset at a user-selected location and resets editor state.
    /// </summary>
    private void CreateNewAsset()
    {
        var path = EditorUtility.SaveFilePanelInProject("Create DAG Graph", "NewDagGraph", "asset", "Choose a location");
        if (!string.IsNullOrEmpty(path))
        {
            var asset = ScriptableObject.CreateInstance<DagGraph>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            graph = asset;
            canvasScroll = Vector2.zero;
            zoom = 1f;
            selectedNodes.Clear();
            selectedEdges.Clear();
        }
    }

    // -------------------- Canvas & drawing --------------------

    /// <summary>
    /// Draws the grid, edges, and node windows, strictly clipped to the canvas rectangle.
    /// Also implements dragging of windows (when not resizing).
    /// </summary>
    private void DrawCanvas(Rect rect)
    {
        // Grid strictly clipped to the canvas
        DrawGrid(rect, 20 * zoom, 0.2f);
        DrawGrid(rect, 100 * zoom, 0.4f);

        // Zoomed drawing group
        var backup = GUI.matrix;
        GUI.BeginGroup(rect);
        GUIUtility.ScaleAroundPivot(Vector2.one * zoom, Vector2.zero);

        var viewport = GetViewportSize(rect.size);
        var groupRect = new Rect(-canvasScroll, viewport);
        GUI.BeginGroup(groupRect);

        // Edges layer (behind windows)
        foreach (var e in graph.Edges)
        {
            if (e == null || e.From == null || e.To == null) continue;
            bool sel = selectedEdges.Contains(e);
            DrawEdge(e, sel);
        }

        // Rubber-band while connecting edges
        if (pendingFromNode.HasValue)
        {
            var fromNode = graph.Nodes.FirstOrDefault(n => n != null && n.Id == pendingFromNode.Value);
            if (fromNode != null)
            {
                var srcRect = NodeRect(fromNode);
                var start = srcRect.center;
                var mouseInCanvas = (Event.current.mousePosition) / zoom + canvasScroll;
                Handles.DrawBezier(start, mouseInCanvas, start + Vector2.right * 50, mouseInCanvas + Vector2.left * 50, Color.white, null, 2f);
                Repaint();
            }
        }

        // Node windows
        BeginWindows();
        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            var n = graph.Nodes[i];
            if (n == null) continue;

            var style = ResolveNodeStyle(n, selectedNodes.Contains(n));

            var beforePos = n.Position;
            var nodeRect = NodeRect(n);
            var windowRect = GUI.Window(i, nodeRect, id => DrawNodeWindow(id, n), n.Title, style);

            // Window movement is disabled while a resize operation is active on this node
            if (!resizingActive && windowRect.position != beforePos)
            {
                var delta = windowRect.position - beforePos;
                Undo.RecordObject(graph, selectedNodes.Contains(n) ? "Move Nodes" : "Move Node");

                if (selectedNodes.Contains(n))
                {
                    foreach (var sn in selectedNodes.ToList())
                        if (sn != null) graph.MoveNode(sn.Id, sn.Position + delta);
                }
                else
                {
                    graph.MoveNode(n.Id, windowRect.position);
                }

                EditorUtility.SetDirty(graph);
            }
        }
        EndWindows();

        GUI.EndGroup();
        GUI.EndGroup();
        GUI.matrix = backup;

        // Empty-click deselect only inside canvas, never over the inspector
        if (Event.current.type == EventType.MouseDown &&
            Event.current.button == 0 &&
            rect.Contains(Event.current.mousePosition) &&
            !IsMouseOverAnyNode(rect))
        {
            if (!Event.current.shift)
            {
                selectedNodes.Clear();
                selectedEdges.Clear();
            }
            pendingFromNode = null;
            Repaint();
            Event.current.Use();
        }
    }

    /// <summary>
    /// Draws a clipped grid inside the canvas rectangle, accounting for scroll and zoom.
    /// </summary>
    private void DrawGrid(Rect rect, float spacing, float alpha)
    {
        GUI.BeginGroup(rect);
        Handles.BeginGUI();
        var prev = Handles.color;
        Handles.color = new Color(1f, 1f, 1f, alpha * 0.2f);

        Vector2 offset = new Vector2(
            Mathf.Repeat(canvasScroll.x * zoom, spacing),
            Mathf.Repeat(canvasScroll.y * zoom, spacing)
        );

        int cols = Mathf.CeilToInt(rect.width / spacing);
        int rows = Mathf.CeilToInt(rect.height / spacing);

        for (int i = -1; i <= cols + 1; i++)
        {
            float x = i * spacing + offset.x;
            if (x < 0f || x > rect.width) continue;
            Handles.DrawLine(new Vector3(x, 0f, 0f), new Vector3(x, rect.height, 0f));
        }
        for (int j = -1; j <= rows + 1; j++)
        {
            float y = j * spacing + offset.y;
            if (y < 0f || y > rect.height) continue;
            Handles.DrawLine(new Vector3(0f, y, 0f), new Vector3(rect.width, y, 0f));
        }

        Handles.color = prev;
        Handles.EndGUI();
        GUI.EndGroup();
    }

    /// <summary>
    /// Draws a single edge using a cubic Bezier; selected edges are highlighted.
    /// </summary>
    private void DrawEdge(IDagEdge<IDagNode> e, bool selected)
    {
        var fromRect = NodeRect(e.From);
        var toRect = NodeRect(e.To);

        var start = fromRect.center + Vector2.right * (fromRect.width * 0.25f);
        var end = toRect.center + Vector2.left * (toRect.width * 0.25f);

        var col = selected ? Color.cyan : Color.white;
        float thickness = selected ? 3.5f : 2f;

        Handles.DrawBezier(start, end, start + Vector2.right * 50, end + Vector2.left * 50, col, null, thickness);
    }

    /// <summary>
    /// Renders a node's content and handles selection and context menu inside a window.
    /// Inline fields decorated with <see cref="ShowInNodeAttribute"/> are drawn here.
    /// </summary>
    private void DrawNodeWindow(int windowId, IDagNode node)
    {
        var e = Event.current;

        if (!(resizingActive && ReferenceEquals(resizingNode, node)))
        {
            // Selection toggle (Shift) or single-select
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (!e.shift) selectedEdges.Clear();

                if (e.shift)
                {
                    if (!selectedNodes.Add(node)) selectedNodes.Remove(node);
                }
                else
                {
                    if (!selectedNodes.Contains(node))
                    {
                        selectedNodes.Clear();
                        selectedNodes.Add(node);
                    }
                }
                Repaint();
            }

            // Node context menu (RMB)
            if (e.type == EventType.ContextClick || (e.type == EventType.MouseDown && e.button == 1))
            {
                if (!selectedNodes.Contains(node) && !e.shift)
                {
                    selectedNodes.Clear();
                    selectedNodes.Add(node);
                }
                else
                {
                    selectedNodes.Add(node);
                }
                selectedEdges.Clear();
                ShowNodeContextMenu(node);
                e.Use();
            }
        }

        // Inline editor for [ShowInNode] properties
        using (new EditorGUILayout.VerticalScope())
        {
            DrawShowInNodeFieldsInline(node);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Out"))
                    pendingFromNode = node.Id;

                if (GUILayout.Button("In") && pendingFromNode.HasValue)
                {
                    if (pendingFromNode.Value == node.Id)
                    {
                        ShowNotification(new GUIContent("Cannot connect to itself."));
                    }
                    else
                    {
                        Undo.RecordObject(graph, "Add Edge");
                        if (!graph.TryAddEdge(pendingFromNode.Value, node.Id, out var err))
                            ShowNotification(new GUIContent(err));
                        else
                            EditorUtility.SetDirty(graph);
                    }
                    pendingFromNode = null;
                }
            }
        }

        if (!(resizingActive && ReferenceEquals(resizingNode, node)))
            GUI.DragWindow();
    }

    /// <summary>
    /// Draws serialized properties marked with <see cref="ShowInNodeAttribute"/> inline.
    /// </summary>
    private void DrawShowInNodeFieldsInline(IDagNode node)
    {
        if (!(node is UnityEngine.Object uo)) return;

        using (var so = new SerializedObject(uo))
        {
            so.UpdateIfRequiredOrScript();

            foreach (var fi in GetShowFieldsFor(node))
            {
                var sp = so.FindProperty(fi.Name);
                if (sp == null) continue;
                EditorGUILayout.PropertyField(sp, true);
            }

            if (so.ApplyModifiedProperties())
                EditorUtility.SetDirty(uo);
        }
    }

    // -------------------- Edge selection & context --------------------

    /// <summary>
    /// Handles hit-testing and selection of edges on the canvas (outside node windows).
    /// Also opens the edge context menu on right click.
    /// </summary>
    private void HandleEdgeClickSelection(Rect canvasRect)
    {
        var e = Event.current;
        if (!canvasRect.Contains(e.mousePosition)) return;

        Vector2 mouseLogical = ScreenToLogicalGlobal(e.mousePosition, canvasRect);
        if (IsMouseOverAnyNode(canvasRect)) return;

        var hit = FindEdgeUnderMouse(mouseLogical);

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (hit != null)
            {
                if (!e.shift) selectedNodes.Clear();

                if (e.shift)
                {
                    if (!selectedEdges.Add(hit)) selectedEdges.Remove(hit);
                }
                else
                {
                    selectedEdges.Clear();
                    selectedEdges.Add(hit);
                }

                Repaint();
                e.Use();
            }
        }

        if ((e.type == EventType.MouseDown && e.button == 1) || e.type == EventType.ContextClick)
        {
            if (hit != null)
            {
                if (!selectedEdges.Contains(hit) && !e.shift)
                {
                    selectedEdges.Clear();
                    selectedEdges.Add(hit);
                }
                else
                {
                    selectedEdges.Add(hit);
                }
                if (!e.shift) selectedNodes.Clear();

                ShowEdgeContextMenu();
                e.Use();
            }
        }
    }

    /// <summary>
    /// Finds the edge under the mouse by sampling the Bezier and taking the minimum distance.
    /// </summary>
    private IDagEdge<IDagNode> FindEdgeUnderMouse(Vector2 mouseLogical)
    {
        float tolerance = 10f / Mathf.Max(zoom, 0.001f);
        IEnumerable<IDagEdge<IDagNode>> order = selectedEdges.Concat(graph.Edges.Except(selectedEdges));

        foreach (var e in order)
        {
            if (e == null || e.From == null || e.To == null) continue;

            var fromRect = NodeRect(e.From);
            var toRect = NodeRect(e.To);

            var p0 = fromRect.center + Vector2.right * (fromRect.width * 0.25f);
            var p3 = toRect.center + Vector2.left * (toRect.width * 0.25f);
            var p1 = p0 + Vector2.right * 50f;
            var p2 = p3 + Vector2.left * 50f;

            if (DistanceToBezier(mouseLogical, p0, p1, p2, p3, 24) <= tolerance)
                return e;
        }

        return null;
    }

    /// <summary>
    /// Samples a cubic Bezier and computes minimal distance to a point using segment distances.
    /// </summary>
    private static float DistanceToBezier(Vector2 p, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, int steps)
    {
        float min = float.MaxValue;
        Vector2 prev = p0;
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector2 cur = BezierPoint(p0, p1, p2, p3, t);
            float d = DistancePointToSegment(p, prev, cur);
            if (d < min) min = d;
            prev = cur;
        }
        return min;
    }

    /// <summary>
    /// Computes a cubic Bezier point at parameter <paramref name="t"/>.
    /// </summary>
    private static Vector2 BezierPoint(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float u = 1f - t;
        return (u * u * u) * p0 + 3f * (u * u) * t * p1 + 3f * u * (t * t) * p2 + (t * t * t) * p3;
    }

    /// <summary>
    /// Computes the distance from a point to a line segment.
    /// </summary>
    private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.00001f);
        t = Mathf.Clamp01(t);
        Vector2 proj = a + ab * t;
        return Vector2.Distance(p, proj);
    }

    /// <summary>
    /// Shows the node context menu with single and multi-delete commands.
    /// </summary>
    private void ShowNodeContextMenu(IDagNode node)
    {
        var menu = new GenericMenu();

        menu.AddItem(new GUIContent("Delete Node"), false, () =>
        {
            Undo.RecordObject(graph, "Delete Node");
            graph.RemoveNode(node.Id);
            selectedNodes.Remove(node);
            EditorUtility.SetDirty(graph);
            Repaint();
        });

        string multiLabel = selectedNodes.Count > 1
            ? $"Delete Selected Nodes ({selectedNodes.Count})"
            : "Delete Selected Nodes (1)";

        menu.AddItem(new GUIContent(multiLabel), false, () =>
        {
            if (selectedNodes.Count == 0) selectedNodes.Add(node);

            Undo.RecordObject(graph, "Delete Nodes");
            foreach (var sn in selectedNodes.ToList())
                graph.RemoveNode(sn.Id);

            selectedNodes.Clear();
            EditorUtility.SetDirty(graph);
            Repaint();
        });

        menu.ShowAsContext();
    }

    /// <summary>
    /// Shows the edge context menu with single and multi-delete commands.
    /// </summary>
    private void ShowEdgeContextMenu()
    {
        var menu = new GenericMenu();

        menu.AddItem(new GUIContent("Delete Edge"), false, () =>
        {
            if (selectedEdges.Count == 0) return;
            Undo.RecordObject(graph, "Delete Edge");
            foreach (var e in selectedEdges.ToList())
                graph.RemoveEdge(e.From.Id, e.To.Id);

            selectedEdges.Clear();
            EditorUtility.SetDirty(graph);
            Repaint();
        });

        string multiLabel = selectedEdges.Count > 1
            ? $"Delete Selected Edges ({selectedEdges.Count})"
            : "Delete Selected Edges (1)";

        menu.AddItem(new GUIContent(multiLabel), false, () =>
        {
            if (selectedEdges.Count == 0) return;

            Undo.RecordObject(graph, "Delete Edges");
            foreach (var e in selectedEdges.ToList())
                graph.RemoveEdge(e.From.Id, e.To.Id);

            selectedEdges.Clear();
            EditorUtility.SetDirty(graph);
            Repaint();
        });

        menu.ShowAsContext();
    }

    // -------------------- Inspector --------------------

    /// <summary>
    /// Draws the right-hand inspector panel. It renders either:
    /// no selection, a multi-selection summary, or Unity's default inspector for the
    /// single selected node/edge.
    /// </summary>
    private void DrawInspector(Rect rect)
    {
        GUILayout.BeginArea(rect, EditorStyles.helpBox);
        GUILayout.Label("Inspector", EditorStyles.boldLabel);

        // Mixed selection
        if (selectedNodes.Count > 0 && selectedEdges.Count > 0)
        {
            EditorGUILayout.HelpBox($"{selectedNodes.Count} node(s) and {selectedEdges.Count} edge(s) selected.", MessageType.Info);
            GUILayout.EndArea();
            return;
        }

        // Nodes only
        if (selectedEdges.Count == 0)
        {
            if (selectedNodes.Count == 0)
            {
                EditorGUILayout.HelpBox("No selection.", MessageType.Info);
            }
            else if (selectedNodes.Count > 1)
            {
                EditorGUILayout.HelpBox($"{selectedNodes.Count} nodes selected.", MessageType.None);
            }
            else
            {
                var node = selectedNodes.FirstOrDefault();
                if (node is UnityEngine.Object uo)
                {
                    if (nodeInspectorEditor == null || nodeInspectorEditor.target != uo)
                    {
                        if (nodeInspectorEditor != null) DestroyImmediate(nodeInspectorEditor);
                        nodeInspectorEditor = Editor.CreateEditor(uo);
                    }

                    using (new EditorGUILayout.VerticalScope(EditorStyles.inspectorDefaultMargins))
                    {
                        nodeInspectorEditor.OnInspectorGUI();
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
        if (selectedNodes.Count == 0)
        {
            if (selectedEdges.Count > 1)
            {
                EditorGUILayout.HelpBox($"{selectedEdges.Count} edges selected.", MessageType.None);
            }
            else
            {
                var edge = selectedEdges.FirstOrDefault();
                if (edge is UnityEngine.Object eo)
                {
                    if (edgeInspectorEditor == null || edgeInspectorEditor.target != eo)
                    {
                        if (edgeInspectorEditor != null) DestroyImmediate(edgeInspectorEditor);
                        edgeInspectorEditor = Editor.CreateEditor(eo);
                    }

                    using (new EditorGUILayout.VerticalScope(EditorStyles.inspectorDefaultMargins))
                    {
                        edgeInspectorEditor.OnInspectorGUI();
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

    // -------------------- Marquee / scrollbars / pan / zoom --------------------

    /// <summary>
    /// Draws a translucent marquee rectangle during box-selection.
    /// </summary>
    private void DrawMarqueeOverlay(Rect canvasRect)
    {
        if (!marqueeActive) return;

        var rectLogical = MakeRectFromPoints(marqueeStartLogical, marqueeEndLogical);
        Rect screenRect = LogicalToScreenRect(rectLogical, canvasRect);

        var oldColor = GUI.color;
        GUI.color = new Color(0.3f, 0.6f, 1f, 0.15f);
        EditorGUI.DrawRect(screenRect, GUI.color);
        GUI.color = new Color(0.3f, 0.6f, 1f, 0.9f);
        Handles.BeginGUI();
        Handles.DrawAAPolyLine(2f,
            new Vector3(screenRect.xMin, screenRect.yMin),
            new Vector3(screenRect.xMax, screenRect.yMin),
            new Vector3(screenRect.xMax, screenRect.yMax),
            new Vector3(screenRect.xMin, screenRect.yMax),
            new Vector3(screenRect.xMin, screenRect.yMin));
        Handles.EndGUI();
        GUI.color = oldColor;
    }

    /// <summary>
    /// Draws and updates horizontal and vertical scrollbars based on the viewport versus a virtual canvas.
    /// </summary>
    private void DrawScrollbars(Rect canvasRect)
    {
        var viewport = GetViewportSize(canvasRect.size);

        float maxX = Mathf.Max(VirtualCanvasSize.x - viewport.x, 0f);
        float maxY = Mathf.Max(VirtualCanvasSize.y - viewport.y, 0f);

        canvasScroll.x = Mathf.Clamp(canvasScroll.x, 0f, maxX);
        canvasScroll.y = Mathf.Clamp(canvasScroll.y, 0f, maxY);

        var hRect = new Rect(canvasRect.x, canvasRect.yMax, canvasRect.width, ScrollbarThickness);
        float newX = GUI.HorizontalScrollbar(hRect, canvasScroll.x, viewport.x, 0f, VirtualCanvasSize.x);
        if (!Mathf.Approximately(newX, canvasScroll.x))
        {
            canvasScroll.x = Mathf.Clamp(newX, 0f, maxX);
            Repaint();
        }

        var vRect = new Rect(canvasRect.xMax, canvasRect.y, ScrollbarThickness, canvasRect.height);
        float newY = GUI.VerticalScrollbar(vRect, canvasScroll.y, viewport.y, 0f, VirtualCanvasSize.y);
        if (!Mathf.Approximately(newY, canvasScroll.y))
        {
            canvasScroll.y = Mathf.Clamp(newY, 0f, maxY);
            Repaint();
        }
    }

    /// <summary>
    /// Enables right-mouse panning when the cursor is not over nodes or edges.
    /// </summary>
    private void HandleRightMousePan(Rect canvasRect)
    {
        var e = Event.current;
        if (!canvasRect.Contains(e.mousePosition)) return;

        if (e.type == EventType.MouseDown && e.button == 1)
        {
            bool overNode = IsMouseOverAnyNode(canvasRect);
            bool overEdge = FindEdgeUnderMouse(ScreenToLogicalGlobal(e.mousePosition, canvasRect)) != null;

            rmbPanningActive = !(overNode || overEdge);
            if (rmbPanningActive) e.Use();
        }
        else if (e.type == EventType.MouseDrag && e.button == 1 && rmbPanningActive)
        {
            canvasScroll -= e.delta / zoom;
            e.Use();
            Repaint();
        }
        else if ((e.type == EventType.MouseUp && e.button == 1) || e.rawType == EventType.MouseUp)
        {
            rmbPanningActive = false;
        }
    }

    /// <summary>
    /// Zooms the canvas around the mouse position using the mouse wheel.
    /// </summary>
    private void HandleMouseZoom(Rect canvasRect)
    {
        var e = Event.current;
        if (!canvasRect.Contains(e.mousePosition)) return;

        if (e.type == EventType.ScrollWheel)
        {
            float delta = -e.delta.y;
            float factor = 1f + (delta * 0.1f);
            float target = Mathf.Clamp(zoom * factor, 0.5f, 2f);
            ZoomAroundPoint(target, e.mousePosition);
            e.Use();
        }
    }

    /// <summary>
    /// Handles marquee (box) selection for nodes only.
    /// </summary>
    private void HandleMarquee(Rect canvasRect)
    {
        var e = Event.current;
        if (!canvasRect.Contains(e.mousePosition)) return;

        Vector2 mouseLogical = ScreenToLogicalGlobal(e.mousePosition, canvasRect);

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (!IsMouseOverAnyNode(canvasRect) && FindEdgeUnderMouse(mouseLogical) == null)
            {
                marqueeActive = true;
                marqueeStartLogical = mouseLogical;
                marqueeEndLogical = mouseLogical;

                if (!e.shift)
                {
                    selectedNodes.Clear();
                    selectedEdges.Clear();
                }
                e.Use();
            }
        }
        else if (e.type == EventType.MouseDrag && e.button == 0 && marqueeActive)
        {
            marqueeEndLogical = mouseLogical;
            Repaint();
            e.Use();
        }
        else if (e.type == EventType.MouseUp && e.button == 0 && marqueeActive)
        {
            var rectLogical = MakeRectFromPoints(marqueeStartLogical, marqueeEndLogical);

            foreach (var n in graph.Nodes)
            {
                if (n == null) continue;
                if (rectLogical.Overlaps(NodeRect(n)))
                {
                    if (e.shift)
                    {
                        if (!selectedNodes.Add(n)) selectedNodes.Remove(n);
                    }
                    else
                    {
                        selectedNodes.Add(n);
                    }
                }
            }

            marqueeActive = false;
            Repaint();
            e.Use();
        }
    }

    /// <summary>
    /// Checks if the mouse is over any node in logical coordinates.
    /// </summary>
    private bool IsMouseOverAnyNode(Rect canvasRect)
    {
        Vector2 local = Event.current.mousePosition - canvasRect.position;
        Vector2 mouseLogical = (local / zoom) + canvasScroll;

        return graph.Nodes.Any(n => n != null && NodeRect(n).Contains(mouseLogical));
    }

    /// <summary>
    /// Handles Delete key for removing selected nodes and edges via the graph API.
    /// </summary>
    private void HandleShortcuts()
    {
        var e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Delete)
        {
            bool acted = false;

            if (selectedNodes.Count > 0)
            {
                Undo.RecordObject(graph, "Delete Nodes");
                foreach (var n in selectedNodes.ToList())
                    graph.RemoveNode(n.Id);

                selectedNodes.Clear();
                acted = true;
            }

            if (selectedEdges.Count > 0)
            {
                Undo.RecordObject(graph, "Delete Edges");
                foreach (var edge in selectedEdges.ToList())
                    graph.RemoveEdge(edge.From.Id, edge.To.Id);

                selectedEdges.Clear();
                acted = true;
            }

            if (acted)
            {
                EditorUtility.SetDirty(graph);
                Repaint();
                e.Use();
            }
        }
    }

    // -------------------- Resize handling --------------------

    /// <summary>
    /// Handles node resizing from the four borders, including cursor feedback,
    /// size clamping, and automatic activation of size override on the node.
    /// </summary>
    private void HandleNodeResizeInput(Rect canvasRect)
    {
        var e = Event.current;

        if (!canvasRect.Contains(e.mousePosition))
            return;

        Vector2 mouseLogical = ScreenToLogicalGlobal(e.mousePosition, canvasRect);

        // Cursor feedback across all nodes
        UpdateResizeCursorsForAllNodes(canvasRect);

        // Begin resize on LMB down inside a resize band
        if (e.type == EventType.MouseDown && e.button == 0 && !resizingActive)
        {
            for (int i = graph.Nodes.Count - 1; i >= 0; i--)
            {
                var n = graph.Nodes[i];
                if (n == null) continue;

                var rect = NodeRect(n);
                if (!rect.Contains(mouseLogical)) continue;

                GetResizeSidesUnderMouse(n, rect, canvasRect, out bool l, out bool r, out bool t, out bool b);
                if (!(l || r || t || b)) continue;

                if (!selectedNodes.Contains(n))
                {
                    selectedNodes.Clear();
                    selectedNodes.Add(n);
                }
                selectedEdges.Clear();

                resizingActive = true;
                resizingNode = n;
                resizeLeft = l; resizeRight = r; resizeTop = t; resizeBottom = b;
                resizeStartRectLogical = rect;
                resizeStartMouseLogical = mouseLogical;
                e.Use();
                Repaint();
                return;
            }
        }

        // Update resize while dragging
        if (e.type == EventType.MouseDrag && e.button == 0 && resizingActive && resizingNode != null)
        {
            Vector2 delta = mouseLogical - resizeStartMouseLogical;
            var r0 = resizeStartRectLogical;
            float newX = r0.x, newY = r0.y, newW = r0.width, newH = r0.height;

            if (resizeLeft)   { newX = r0.x + delta.x; newW = r0.width - delta.x; }
            if (resizeRight)  { newW = r0.width + delta.x; }
            if (resizeTop)    { newY = r0.y + delta.y; newH = r0.height - delta.y; }
            if (resizeBottom) { newH = r0.height + delta.y; }

            newW = Mathf.Max(MinNodeWidth, newW);
            newH = Mathf.Max(MinNodeHeight, newH);

            if (resizeLeft) newX = r0.x + (r0.width - newW);
            if (resizeTop)  newY = r0.y + (r0.height - newH);

            ApplyResizeToNode(resizingNode, newX, newY, newW, newH);

            e.Use();
            Repaint();
            return;
        }

        // End resize on mouse up
        if (e.type == EventType.MouseUp && resizingActive)
        {
            resizingActive = false;
            resizingNode = null;
            resizeLeft = resizeRight = resizeTop = resizeBottom = false;
            e.Use();
            Repaint();
        }
    }

    /// <summary>
    /// Adds resize cursors for all nodes on their borders in screen space.
    /// </summary>
    private void UpdateResizeCursorsForAllNodes(Rect canvasRect)
    {
        foreach (var n in graph.Nodes)
        {
            if (n == null) continue;
            var rect = NodeRect(n);
            AddResizeCursorsForNode(n, rect, canvasRect);
        }
    }

    /// <summary>
    /// Adds cursor rectangles for the four resize bands of a specific node.
    /// </summary>
    private void AddResizeCursorsForNode(IDagNode n, Rect logicalRect, Rect canvasRect)
    {
        float band = ResizeBandPx;
        var screenRect = LogicalToScreenRect(logicalRect, canvasRect);

        var leftBand   = new Rect(screenRect.xMin - band * 0.5f, screenRect.yMin, band, screenRect.height);
        var rightBand  = new Rect(screenRect.xMax - band * 0.5f, screenRect.yMin, band, screenRect.height);
        var topBand    = new Rect(screenRect.xMin, screenRect.yMin - band * 0.5f, screenRect.width, band);
        var bottomBand = new Rect(screenRect.xMin, screenRect.yMax - band * 0.5f, screenRect.width, band);

        EditorGUIUtility.AddCursorRect(leftBand, MouseCursor.ResizeHorizontal);
        EditorGUIUtility.AddCursorRect(rightBand, MouseCursor.ResizeHorizontal);
        EditorGUIUtility.AddCursorRect(topBand, MouseCursor.ResizeVertical);
        EditorGUIUtility.AddCursorRect(bottomBand, MouseCursor.ResizeVertical);
    }

    /// <summary>
    /// Determines which sides (if any) the mouse is hovering for resizing.
    /// </summary>
    private void GetResizeSidesUnderMouse(IDagNode n, Rect logicalRect, Rect canvasRect,
        out bool left, out bool right, out bool top, out bool bottom)
    {
        left = right = top = bottom = false;

        var e = Event.current;
        var screenRect = LogicalToScreenRect(logicalRect, canvasRect);
        float band = ResizeBandPx;

        var leftBand   = new Rect(screenRect.xMin - band * 0.5f, screenRect.yMin, band, screenRect.height);
        var rightBand  = new Rect(screenRect.xMax - band * 0.5f, screenRect.yMin, band, screenRect.height);
        var topBand    = new Rect(screenRect.xMin, screenRect.yMin - band * 0.5f, screenRect.width, band);
        var bottomBand = new Rect(screenRect.xMin, screenRect.yMax - band * 0.5f, screenRect.width, band);

        if (leftBand.Contains(e.mousePosition))   left = true;
        if (rightBand.Contains(e.mousePosition))  right = true;
        if (topBand.Contains(e.mousePosition))    top = true;
        if (bottomBand.Contains(e.mousePosition)) bottom = true;
    }

    /// <summary>
    /// Applies the resized geometry to the node: enables size override and writes custom size
    /// via <see cref="SerializedObject"/>, then updates the node position using the graph API.
    /// </summary>
    private void ApplyResizeToNode(IDagNode node, float newX, float newY, float newW, float newH)
    {
        if (node is UnityEngine.Object uo)
        {
            using (var so = new SerializedObject(uo))
            {
                var pOverride = so.FindProperty("overrideSize");
                var pSize = so.FindProperty("customSize");

                if (pOverride != null) pOverride.boolValue = true;
                if (pSize != null) pSize.vector2Value = new Vector2(newW, newH);

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(uo);
            }
        }

        graph.MoveNode(node.Id, new Vector2(newX, newY));
        EditorUtility.SetDirty(graph);
    }

    // -------------------- Guards & math helpers --------------------

    /// <summary>
    /// Prevents empty-click selection clearing outside the canvas (e.g., over the inspector).
    /// </summary>
    private void ConsumeEmptyClicksOutsideCanvas(Rect canvasRect)
    {
        var e = Event.current;
        if (e.type != EventType.MouseDown) return;

        if (!canvasRect.Contains(e.mousePosition))
        {
            // Intentionally no Use(); inspector must work normally.
        }
    }

    /// <summary>
    /// Converts a logical rectangle into a screen-space rectangle for hit testing.
    /// </summary>
    private Rect LogicalToScreenRect(Rect logical, Rect canvasRect)
    {
        Vector2 topLeft = (logical.position - canvasScroll) * zoom + canvasRect.position;
        Vector2 size = logical.size * zoom;
        return new Rect(topLeft, size);
    }

    /// <summary>
    /// Creates a rectangle from two diagonal points.
    /// </summary>
    private Rect MakeRectFromPoints(Vector2 a, Vector2 b)
    {
        var min = Vector2.Min(a, b);
        var max = Vector2.Max(a, b);
        return new Rect(min, max - min);
    }

    /// <summary>
    /// Converts a screen-space mouse position into logical canvas coordinates.
    /// </summary>
    private Vector2 ScreenToLogicalGlobal(Vector2 mousePos, Rect canvasRect)
    {
        Vector2 local = mousePos - canvasRect.position;
        return (local / zoom) + canvasScroll;
    }

    /// <summary>
    /// Changes zoom while keeping a screen point stable (zoom around cursor).
    /// Also clamps scroll to a virtual canvas rectangle.
    /// </summary>
    private void ZoomAroundPoint(float newZoom, Vector2 screenPoint)
    {
        if (Mathf.Approximately(newZoom, zoom)) return;

        var canvasRect = new Rect(0, 20, position.width - InspectorWidth - ScrollbarThickness, position.height - 20 - ScrollbarThickness);

        if (!canvasRect.Contains(screenPoint))
            screenPoint = new Vector2(canvasRect.x + canvasRect.width * 0.5f, canvasRect.y + canvasRect.height * 0.5f);

        Vector2 local = screenPoint - canvasRect.position;
        Vector2 worldBefore = (local / zoom) + canvasScroll;

        zoom = newZoom;

        Vector2 worldAfter = (local / zoom) + canvasScroll;
        Vector2 deltaWorld = worldBefore - worldAfter;
        canvasScroll += deltaWorld;

        var viewport = GetViewportSize(canvasRect.size);
        float maxX = Mathf.Max(VirtualCanvasSize.x - viewport.x, 0f);
        float maxY = Mathf.Max(VirtualCanvasSize.y - viewport.y, 0f);
        canvasScroll.x = Mathf.Clamp(canvasScroll.x, 0f, maxX);
        canvasScroll.y = Mathf.Clamp(canvasScroll.y, 0f, maxY);

        Repaint();
    }

    /// <summary>
    /// Gets the logical viewport size for a given canvas pixel size and zoom.
    /// </summary>
    private Vector2 GetViewportSize(Vector2 pixelSize) => pixelSize / Mathf.Max(zoom, 0.0001f);

    /// <summary>
    /// Gets the logical viewport size for the current window geometry.
    /// </summary>
    private Vector2 GetViewportSize()
    {
        var pixelSize = new Vector2(position.width - InspectorWidth - ScrollbarThickness, position.height - 20 - ScrollbarThickness);
        return GetViewportSize(pixelSize);
    }
}
#endif