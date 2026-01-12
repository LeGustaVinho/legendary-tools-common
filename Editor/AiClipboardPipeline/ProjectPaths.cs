#if UNITY_EDITOR_WIN
using System.IO;
using UnityEngine;

namespace AiClipboardPipeline.Editor
{
    internal static class ProjectPaths
    {
        public static string GetProjectRoot()
        {
            string assetsAbs = Application.dataPath.Replace("\\", "/");
            DirectoryInfo parent = Directory.GetParent(assetsAbs);
            return parent?.FullName?.Replace("\\", "/") ?? assetsAbs;
        }

        public static string AssetPathToAbsolute(string assetPath)
        {
            string assetsAbs = Application.dataPath.Replace("\\", "/");
            string rel = assetPath.Substring("Assets/".Length);
            return Path.Combine(assetsAbs, rel).Replace("\\", "/");
        }

        public static string GetTempPatchDirectory(string projectRoot)
        {
            return Path.Combine(projectRoot, "Library", "AICodePasteTemp");
        }
    }
}
#endif