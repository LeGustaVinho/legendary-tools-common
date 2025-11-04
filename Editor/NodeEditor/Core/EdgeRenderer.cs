#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Renders straight edges (line segments) and performs edge hit-testing (no zoom).
/// Supports optional styling via <see cref="IStyledEdge"/>:
/// – colored line
/// – multiple direction arrows placed along the segment
/// – a center label exactly at the midpoint
///
/// The segment starts at the midpoint of the most appropriate side of the source node
/// and ends at the midpoint of the closest side of the target node.
/// </summary>
public class EdgeRenderer
{
    private readonly NodeAppearance _nodeAppearance = new();

    private enum Side
    {
        Left,
        Right,
        Top,
        Bottom
    }

    /// <summary>
    /// Draws all edges behind the node windows, including arrows and optional center labels.
    /// </summary>
    public void DrawEdges(DagEditorContext ctx, Rect canvasRect)
    {
        foreach (IDagEdge<IDagNode> e in ctx.Graph.Edges)
        {
            if (e == null || e.From == null || e.To == null) continue;
            bool sel = ctx.SelectedEdges.Contains(e);
            DrawEdgeInternal(e, sel, ctx);
        }

        // Rubber-band while creating a new connection
        if (ctx.PendingFromNodeId.HasValue)
        {
            IDagNode from = ctx.Graph.Nodes.FirstOrDefault(n => n != null && n.Id == ctx.PendingFromNodeId.Value);
            if (from != null)
            {
                Rect srcRect = _nodeAppearance.GetNodeRect(from, ctx.ShowInNodeCache);
                Vector2 start = srcRect.center;
                // Logical = screen because there's no zoom; convert to logical with zoom=1 for consistency
                Vector2 mouseInCanvas =
                    CoordinateSystem.ScreenToLogical(Event.current.mousePosition, canvasRect, ctx.Scroll, 1f);
                DrawLineWithWidth(start, mouseInCanvas, Color.white, 2f);
            }
        }
    }

    /// <summary>
    /// Returns the first edge near the logical mouse point, or null.
    /// Uses the same segment computation as the draw routine for consistent picking.
    /// </summary>
    public IDagEdge<IDagNode> HitTestEdge(DagEditorContext ctx, Vector2 mouseLogical)
    {
        float tolerance = 8f; // constant — no zoom scaling
        IEnumerable<IDagEdge<IDagNode>> order = ctx.SelectedEdges.Concat(ctx.Graph.Edges.Except(ctx.SelectedEdges));

        foreach (IDagEdge<IDagNode> e in order)
        {
            if (!TryGetSegment(e, ctx, out Vector2 a, out Vector2 b)) continue;
            if (DistancePointToSegment(mouseLogical, a, b) <= tolerance)
                return e;
        }

        return null;
    }

    /// <summary>
    /// Draws a single edge as a straight line with:
    /// - colored segment
    /// - multiple direction arrows along the segment
    /// - optional center label at midpoint
    /// </summary>
    private void DrawEdgeInternal(IDagEdge<IDagNode> e, bool selected, DagEditorContext ctx)
    {
        if (!TryGetSegment(e, ctx, out Vector2 a, out Vector2 b)) return;

        Color baseColor = (e as IStyledEdge)?.EdgeColor ?? Color.white;
        Color col = selected ? Color.cyan : baseColor;
        float thickness = selected ? 3.5f : 2f;

        DrawLineWithWidth(a, b, col, thickness);
        DrawDirectionArrowsSegment(a, b, col);

        string label = (e as IStyledEdge)?.CenterText;
        if (!string.IsNullOrEmpty(label))
        {
            Vector2 mid = (a + b) * 0.5f;
            DrawCenteredLabel(mid, label, selected ? Color.cyan : baseColor);
        }
    }

    /// <summary>
    /// Computes the straight segment endpoints for an edge using side-aware logic.
    /// </summary>
    private bool TryGetSegment(IDagEdge<IDagNode> e, DagEditorContext ctx, out Vector2 a, out Vector2 b)
    {
        a = b = default;
        if (e == null || e.From == null || e.To == null) return false;

        Rect fromRect = _nodeAppearance.GetNodeRect(e.From, ctx.ShowInNodeCache);
        Rect toRect = _nodeAppearance.GetNodeRect(e.To, ctx.ShowInNodeCache);

        Side sourceSide = ChooseSourceSide(fromRect, toRect);
        Side destSide = ChooseDestinationSide(fromRect, toRect, sourceSide);

        a = GetSideMid(fromRect, sourceSide);
        b = GetSideMid(toRect, destSide);
        return true;
    }

    private static void DrawLineWithWidth(Vector2 a, Vector2 b, Color color, float width)
    {
        Color prev = Handles.color;
        Handles.color = color;
#if UNITY_2020_1_OR_NEWER
        Handles.DrawAAPolyLine(width, new Vector3[] { a, b });
#else
        Handles.DrawLine(a, b);
        Handles.DrawLine(a + Vector2.one * 0.5f, b + Vector2.one * 0.5f);
#endif
        Handles.color = prev;
    }

    /// <summary>
    /// Draws multiple arrowheads along the straight segment to indicate direction (from a to b).
    /// </summary>
    private void DrawDirectionArrowsSegment(Vector2 a, Vector2 b, Color color)
    {
        Vector2 dir = b - a;
        float len = dir.magnitude;
        if (len < 1e-3f) return;

        dir /= len;

        int arrows = Mathf.Clamp(Mathf.FloorToInt(len / 120f), 1, 8); // ~1 arrow every 120 px
        if (len < 80f) arrows = 1;

        float size = 12f; // fixed — independent of zoom

        float tStart = 0.15f;
        float tEnd = 0.85f;

        if (arrows == 1)
        {
            Vector2 pos = Vector2.Lerp(a, b, 0.5f);
            DrawTriangleArrow(pos, dir, size, color);
            return;
        }

        for (int i = 0; i < arrows; i++)
        {
            float t = Mathf.Lerp(tStart, tEnd, (i + 0.5f) / arrows);
            Vector2 pos = Vector2.Lerp(a, b, t);
            DrawTriangleArrow(pos, dir, size, color);
        }
    }

    private static void DrawTriangleArrow(Vector2 pos, Vector2 dir, float size, Color color)
    {
        if (dir.sqrMagnitude < 1e-6f) return;

        Vector2 fwd = dir.normalized;
        Vector2 right = new(-fwd.y, fwd.x);

        Vector2 tip = pos + fwd * (size * 0.8f);
        Vector2 baseCenter = pos - fwd * (size * 0.6f);
        Vector2 pA = baseCenter + right * (size * 0.5f);
        Vector2 pB = baseCenter - right * (size * 0.5f);

        Color prev = Handles.color;
        Handles.color = color;
        Handles.DrawAAConvexPolygon(tip, pA, pB);
        Handles.color = prev;
    }

    private static void DrawCenteredLabel(Vector2 center, string text, Color fg)
    {
        GUIContent content = new(text);
        GUIStyle style = EditorStyles.miniBoldLabel;
        Vector2 size = style.CalcSize(content);
        Rect rect = new(center.x - size.x / 2f, center.y - size.y / 2f, size.x, size.y);

        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.35f);
        EditorGUI.DrawRect(new Rect(rect.x - 4, rect.y - 2, rect.width + 8, rect.height + 4), GUI.color);
        GUI.color = prev;

        Color prevColor = GUI.contentColor;
        GUI.contentColor = fg;
        GUI.Label(rect, content, style);
        GUI.contentColor = prevColor;
    }

    private static Side ChooseSourceSide(Rect from, Rect to)
    {
        Vector2 d = to.center - from.center;
        if (Mathf.Abs(d.x) >= Mathf.Abs(d.y))
            return d.x >= 0 ? Side.Right : Side.Left;
        else
            return d.y >= 0 ? Side.Bottom : Side.Top;
    }

    private static Side ChooseDestinationSide(Rect from, Rect to, Side chosenSource)
    {
        switch (chosenSource)
        {
            case Side.Right: return Side.Left;
            case Side.Left: return Side.Right;
            case Side.Bottom: return Side.Top;
            case Side.Top: return Side.Bottom;
        }

        return NearestSide(to, from.center);
    }

    private static Side NearestSide(Rect r, Vector2 reference)
    {
        Side[] sides = new[] { Side.Left, Side.Right, Side.Top, Side.Bottom };
        float best = float.MaxValue;
        Side bestSide = Side.Left;

        foreach (Side s in sides)
        {
            float d = Vector2.SqrMagnitude(GetSideMid(r, s) - reference);
            if (d < best)
            {
                best = d;
                bestSide = s;
            }
        }

        return bestSide;
    }

    private static Vector2 GetSideMid(Rect r, Side s)
    {
        switch (s)
        {
            case Side.Left: return new Vector2(r.xMin, (r.yMin + r.yMax) * 0.5f);
            case Side.Right: return new Vector2(r.xMax, (r.yMin + r.yMax) * 0.5f);
            case Side.Top: return new Vector2((r.xMin + r.xMax) * 0.5f, r.yMin);
            case Side.Bottom: return new Vector2((r.xMin + r.xMax) * 0.5f, r.yMax);
            default: return r.center;
        }
    }

    private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.00001f);
        t = Mathf.Clamp01(t);
        Vector2 proj = a + ab * t;
        return Vector2.Distance(p, proj);
    }
}
#endif