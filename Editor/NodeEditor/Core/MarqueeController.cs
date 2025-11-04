#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Handles marquee (box) selection gesture and draws its overlay.
/// </summary>
public class MarqueeController
{
    /// <summary>
    /// Updates marquee state based on mouse input and toggles node selection.
    /// </summary>
    public void HandleMarquee(DagEditorContext ctx, Rect canvasRect, System.Func<IDagNode, Rect> getNodeRect)
    {
        Event e = Event.current;
        if (!canvasRect.Contains(e.mousePosition)) return;

        Vector2 mouseLogical = CoordinateSystem.ScreenToLogical(e.mousePosition, canvasRect, ctx.Scroll, ctx.Zoom);

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            // Start marquee only if not over a node/edge (caller guarantees)
            ctx.Marquee.Active = true;
            ctx.Marquee.Start = mouseLogical;
            ctx.Marquee.End = mouseLogical;

            if (!e.shift)
            {
                ctx.SelectedNodes.Clear();
                ctx.SelectedEdges.Clear();
            }

            e.Use();
        }
        else if (e.type == EventType.MouseDrag && e.button == 0 && ctx.Marquee.Active)
        {
            ctx.Marquee.End = mouseLogical;
            e.Use();
        }
        else if (e.type == EventType.MouseUp && e.button == 0 && ctx.Marquee.Active)
        {
            Rect rectLogical = CoordinateSystem.RectFromPoints(ctx.Marquee.Start, ctx.Marquee.End);

            foreach (IDagNode n in ctx.Graph.Nodes)
            {
                if (n == null) continue;
                if (rectLogical.Overlaps(getNodeRect(n)))
                {
                    if (e.shift)
                    {
                        if (!ctx.SelectedNodes.Add(n)) ctx.SelectedNodes.Remove(n);
                    }
                    else
                    {
                        ctx.SelectedNodes.Add(n);
                    }
                }
            }

            ctx.Marquee.Active = false;
            e.Use();
        }
    }

    /// <summary>
    /// Draws the marquee translucent rectangle overlay (screen space).
    /// </summary>
    public void DrawOverlay(DagEditorContext ctx, Rect canvasRect)
    {
        if (!ctx.Marquee.Active) return;

        Rect screenRect = CoordinateSystem.LogicalToScreenRect(
            CoordinateSystem.RectFromPoints(ctx.Marquee.Start, ctx.Marquee.End),
            canvasRect, ctx.Scroll, ctx.Zoom);

        Color oldColor = GUI.color;
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
}
#endif