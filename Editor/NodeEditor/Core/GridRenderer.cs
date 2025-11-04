#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Renders a dual-layer background grid clipped to the canvas viewport and synchronized with pan.
/// </summary>
public class GridRenderer
{
    /// <summary>
    /// Draws two grid layers (fine and coarse) inside <paramref name="canvasRect"/>,
    /// aligned to logical space and shifted by <paramref name="scroll"/>.
    /// </summary>
    /// <param name="canvasRect">Canvas viewport rectangle in screen coordinates.</param>
    /// <param name="scroll">Current logical scroll (world → viewport offset).</param>
    public void Draw(Rect canvasRect, Vector2 scroll)
    {
        DrawGridLayer(canvasRect, scroll, 20f, 0.25f); // fine
        DrawGridLayer(canvasRect, scroll, 100f, 0.5f); // coarse
    }

    /// <summary>
    /// Draws a single grid layer with the given spacing and opacity.
    /// </summary>
    private static void DrawGridLayer(Rect rect, Vector2 scroll, float spacing, float alpha)
    {
        // Clip drawing to the canvas viewport
        GUI.BeginGroup(rect);
        Handles.BeginGUI();

        Color prevColor = Handles.color;
        Handles.color = new Color(1f, 1f, 1f, alpha * 0.35f);

        // Correct pan offset: as scroll increases (+X/+Y), the on-screen grid moves −X/−Y.
        float ox = Mathf.Repeat(-scroll.x, spacing);
        float oy = Mathf.Repeat(-scroll.y, spacing);

        int cols = Mathf.CeilToInt(rect.width / spacing) + 2;
        int rows = Mathf.CeilToInt(rect.height / spacing) + 2;

        // Vertical lines
        for (int i = -1; i <= cols; i++)
        {
            float x = ox + i * spacing;
            if (x < 0f || x > rect.width) continue;
            Handles.DrawLine(new Vector3(x, 0f, 0f), new Vector3(x, rect.height, 0f));
        }

        // Horizontal lines
        for (int j = -1; j <= rows; j++)
        {
            float y = oy + j * spacing;
            if (y < 0f || y > rect.height) continue;
            Handles.DrawLine(new Vector3(0f, y, 0f), new Vector3(rect.width, y, 0f));
        }

        Handles.color = prevColor;
        Handles.EndGUI();
        GUI.EndGroup();
    }
}
#endif