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

                // Avoid importing non-existing files (patch can delete files).
                string abs = ProjectPaths.AssetPathToAbsolute(ap);
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