#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Handles node resizing from borders: cursors, hit testing and size application.
/// </summary>
public class ResizeController
{
    private readonly NodeAppearance _appearance = new();

    /// <summary>
    /// Adds cursor feedback for all nodes and performs the resize gesture.
    /// </summary>
    public void HandleResize(DagEditorContext ctx, Rect canvasRect, System.Func<IDagNode, Rect> getNodeRect)
    {
        Event e = Event.current;
        if (!canvasRect.Contains(e.mousePosition)) return;

        Vector2 mouseLogical = CoordinateSystem.ScreenToLogical(e.mousePosition, canvasRect, ctx.Scroll, ctx.Zoom);

        // Cursor feedback for all nodes
        foreach (IDagNode n in ctx.Graph.Nodes)
        {
            if (n == null) continue;
            Rect rect = getNodeRect(n);
            AddResizeCursors(rect, canvasRect, ctx);
        }

        // Begin resize
        if (e.type == EventType.MouseDown && e.button == 0 && !ctx.Resize.Active)
            for (int i = ctx.Graph.Nodes.Count - 1; i >= 0; i--)
            {
                IDagNode n = ctx.Graph.Nodes[i];
                if (n == null) continue;

                Rect rect = getNodeRect(n);
                if (!rect.Contains(mouseLogical)) continue;

                GetSidesUnderMouse(rect, canvasRect, ctx, out bool l, out bool r, out bool t, out bool b);
                if (!(l || r || t || b)) continue;

                if (!ctx.SelectedNodes.Contains(n))
                {
                    ctx.SelectedNodes.Clear();
                    ctx.SelectedNodes.Add(n);
                }

                ctx.SelectedEdges.Clear();

                ctx.Resize.Active = true;
                ctx.Resize.Node = n;
                ctx.Resize.StartRect = rect;
                ctx.Resize.StartMouse = mouseLogical;
                ctx.Resize.Left = l;
                ctx.Resize.Right = r;
                ctx.Resize.Top = t;
                ctx.Resize.Bottom = b;
                e.Use();
                return;
            }

        // Update resize
        if (e.type == EventType.MouseDrag && e.button == 0 && ctx.Resize.Active && ctx.Resize.Node != null)
        {
            Vector2 delta = mouseLogical - ctx.Resize.StartMouse;
            Rect r0 = ctx.Resize.StartRect;

            float newX = r0.x, newY = r0.y, newW = r0.width, newH = r0.height;

            if (ctx.Resize.Left)
            {
                newX = r0.x + delta.x;
                newW = r0.width - delta.x;
            }

            if (ctx.Resize.Right) newW = r0.width + delta.x;
            if (ctx.Resize.Top)
            {
                newY = r0.y + delta.y;
                newH = r0.height - delta.y;
            }

            if (ctx.Resize.Bottom) newH = r0.height + delta.y;

            newW = Mathf.Max(DagEditorLayout.MinNodeWidth, newW);
            newH = Mathf.Max(DagEditorLayout.MinNodeHeight, newH);

            if (ctx.Resize.Left) newX = r0.x + (r0.width - newW);
            if (ctx.Resize.Top) newY = r0.y + (r0.height - newH);

            ApplyResize(ctx, ctx.Resize.Node, newX, newY, newW, newH);
            e.Use();
            return;
        }

        // End resize
        if (e.type == EventType.MouseUp && ctx.Resize.Active)
        {
            ctx.Resize.Active = false;
            ctx.Resize.Node = null;
            ctx.Resize.Left = ctx.Resize.Right = ctx.Resize.Top = ctx.Resize.Bottom = false;
            e.Use();
        }
    }

    private static void AddResizeCursors(Rect logicalRect, Rect canvasRect, DagEditorContext ctx)
    {
        float band = DagEditorLayout.ResizeBandPx;
        Rect screenRect = CoordinateSystem.LogicalToScreenRect(logicalRect, canvasRect, ctx.Scroll, ctx.Zoom);

        Rect leftBand = new(screenRect.xMin - band * 0.5f, screenRect.yMin, band, screenRect.height);
        Rect rightBand = new(screenRect.xMax - band * 0.5f, screenRect.yMin, band, screenRect.height);
        Rect topBand = new(screenRect.xMin, screenRect.yMin - band * 0.5f, screenRect.width, band);
        Rect bottomBand = new(screenRect.xMin, screenRect.yMax - band * 0.5f, screenRect.width, band);

        EditorGUIUtility.AddCursorRect(leftBand, MouseCursor.ResizeHorizontal);
        EditorGUIUtility.AddCursorRect(rightBand, MouseCursor.ResizeHorizontal);
        EditorGUIUtility.AddCursorRect(topBand, MouseCursor.ResizeVertical);
        EditorGUIUtility.AddCursorRect(bottomBand, MouseCursor.ResizeVertical);
    }

    private static void GetSidesUnderMouse(Rect logicalRect, Rect canvasRect, DagEditorContext ctx,
        out bool left, out bool right, out bool top, out bool bottom)
    {
        left = right = top = bottom = false;

        Event e = Event.current;
        float band = DagEditorLayout.ResizeBandPx;
        Rect screenRect = CoordinateSystem.LogicalToScreenRect(logicalRect, canvasRect, ctx.Scroll, ctx.Zoom);

        Rect leftBand = new(screenRect.xMin - band * 0.5f, screenRect.yMin, band, screenRect.height);
        Rect rightBand = new(screenRect.xMax - band * 0.5f, screenRect.yMin, band, screenRect.height);
        Rect topBand = new(screenRect.xMin, screenRect.yMin - band * 0.5f, screenRect.width, band);
        Rect bottomBand = new(screenRect.xMin, screenRect.yMax - band * 0.5f, screenRect.width, band);

        if (leftBand.Contains(e.mousePosition)) left = true;
        if (rightBand.Contains(e.mousePosition)) right = true;
        if (topBand.Contains(e.mousePosition)) top = true;
        if (bottomBand.Contains(e.mousePosition)) bottom = true;
    }

    private static void ApplyResize(DagEditorContext ctx, IDagNode node, float x, float y, float w, float h)
    {
        if (node is Object uo)
            using (SerializedObject so = new(uo))
            {
                SerializedProperty pOverride = so.FindProperty("overrideSize");
                SerializedProperty pSize = so.FindProperty("customSize");

                if (pOverride != null) pOverride.boolValue = true;
                if (pSize != null) pSize.vector2Value = new Vector2(w, h);

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(uo);
            }

        ctx.Graph.MoveNode(node.Id, new Vector2(x, y));
        EditorUtility.SetDirty(ctx.Graph);
    }
}
#endif