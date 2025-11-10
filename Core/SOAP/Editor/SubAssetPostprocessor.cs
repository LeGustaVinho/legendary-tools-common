#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.SOAP.Editor
{
    /// <summary>
    /// AssetPostprocessor to ensure [SubAsset]-marked fields are created on asset import/move.
    /// This complements the PropertyDrawer for cases where the inspector isn't open.
    /// </summary>
    public class SubAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            // Only process assets we know changed (imported or moved).
            Process(importedAssets);
            Process(movedAssets);
        }

        private static void Process(string[] paths)
        {
            if (paths == null || paths.Length == 0) return;

            foreach (string path in paths)
            {
                if (string.IsNullOrEmpty(path)) continue;

                // Load the main asset only (fast path).
                Object main = AssetDatabase.LoadMainAssetAtPath(path);
                if (main is ScriptableObject so) SubAssetEditorUtility.EnsureSubAssets(so);
            }
        }
    }
}
#endif