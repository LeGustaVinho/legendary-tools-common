#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Resolves GUI styles by name at draw time (inside OnGUI), avoiding any GUI access during static init
/// or assembly reload. It never caches GUIStyle instances across frames.
/// </summary>
public static class GuiStyleService
{
    // Names we will try when no explicit style is provided by the node
    private const string DefaultNormalName = "flow node 0";
    private const string DefaultSelectedName = "flow node 0 on";

    /// <summary>
    /// Resolves a GUIStyle by style name and an optional preferred GUISkin.
    /// This method must be called during the OnGUI cycle.
    /// </summary>
    /// <param name="preferredSkin">Skin to search first (e.g., a custom skin from the node/graph). Can be null.</param>
    /// <param name="styleName">Explicit style name to resolve. If null/empty, uses a default name.</param>
    /// <param name="selectedFallback">If true and <paramref name="styleName"/> is empty, uses a "selected" default name.</param>
    /// <returns>A resolved GUIStyle. Guaranteed non-null, with a safe fallback.</returns>
    public static GUIStyle Resolve(GUISkin preferredSkin, string styleName, bool selectedFallback = false)
    {
        // Choose the name to try (explicit or default)
        string nameToTry = string.IsNullOrEmpty(styleName)
            ? selectedFallback ? DefaultSelectedName : DefaultNormalName
            : styleName;

        // 1) Preferred skin
        if (preferredSkin != null)
        {
            GUIStyle s = SafeFindStyle(preferredSkin, nameToTry);
            if (s != null) return s;
        }

        // 2) Current GUI.skin (only valid inside OnGUI)
        if (GUI.skin != null)
        {
            GUIStyle s = SafeFindStyle(GUI.skin, nameToTry);
            if (s != null) return s;
        }

        // 3) Built-in Inspector skin (often has "flow node" styles in Editor)
        GUISkin builtin = EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector);
        if (builtin != null)
        {
            GUIStyle s = SafeFindStyle(builtin, nameToTry);
            if (s != null) return s;
        }

        // 4) Generic, safe fallback (avoid EditorStyles here to not depend on GUI init timing)
        if (GUI.skin != null && GUI.skin.box != null)
            return GUI.skin.box;

        // Last resort: a plain style
        return new GUIStyle("box");
    }

    /// <summary>
    /// Wraps GUISkin.FindStyle with null checks.
    /// </summary>
    private static GUIStyle SafeFindStyle(GUISkin skin, string style)
    {
        if (skin == null || string.IsNullOrEmpty(style)) return null;
        try
        {
            return skin.FindStyle(style);
        }
        catch
        {
            return null;
        }
    }
}
#endif