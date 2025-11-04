#if UNITY_EDITOR
using UnityEngine;

/// <summary>
/// Provides stateless coordinate conversions and small geometry helpers.
/// </summary>
public static class CoordinateSystem
{
    /// <summary>
    /// Converts a screen-space point into logical canvas coordinates.
    /// </summary>
    public static Vector2 ScreenToLogical(Vector2 screen, Rect canvasRect, Vector2 scroll, float zoom)
    {
        Vector2 local = screen - canvasRect.position;
        return local / Mathf.Max(zoom, 0.0001f) + scroll;
    }

    /// <summary>
    /// Converts a logical rect to a screen-space rect (used for hit testing UI bands).
    /// </summary>
    public static Rect LogicalToScreenRect(Rect logical, Rect canvasRect, Vector2 scroll, float zoom)
    {
        Vector2 topLeft = (logical.position - scroll) * zoom + canvasRect.position;
        Vector2 size = logical.size * zoom;
        return new Rect(topLeft, size);
    }

    /// <summary>
    /// Creates a rect from two diagonal points in logical space.
    /// </summary>
    public static Rect RectFromPoints(Vector2 a, Vector2 b)
    {
        Vector2 min = Vector2.Min(a, b);
        Vector2 max = Vector2.Max(a, b);
        return new Rect(min, max - min);
    }
}
#endif