#if UNITY_EDITOR_WIN
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace AiClipboardPipeline.Editor
{
    internal sealed class UnityAssetService
    {
        public void ImportAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return;

            if (!assetPath.StartsWith("Assets/", System.StringComparison.Ordinal) &&
                !string.Equals(assetPath, "Assets", System.StringComparison.Ordinal))
                return;

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        public void ImportManyIfExists(IReadOnlyList<string> assetPaths)
        {
            if (assetPaths == null || assetPaths.Count == 0)
                return;

            for (int i = 0; i < assetPaths.Count; i++)
            {
                string ap = assetPaths[i];
                if (string.IsNullOrEmpty(ap))
                    continue;

                if (!ap.StartsWith("Assets/", System.StringComparison.Ordinal))
                    continue;

                // Avoid importing non-existing files (patch can delete files).
                string abs;
                try
                {
                    abs = ProjectPaths.AssetPathToAbsolute(ap);
                }
                catch
                {
                    // Defensive: ignore invalid paths to avoid throwing during refresh/import.
                    continue;
                }

                if (File.Exists(abs))
                    ImportAsset(ap);
            }
        }

        public void Refresh()
        {
            AssetDatabase.Refresh();
        }
    }
}
#endif