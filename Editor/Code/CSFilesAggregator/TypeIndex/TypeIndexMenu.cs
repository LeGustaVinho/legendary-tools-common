// Assets/legendary-tools-common/Editor/Code/CSFilesAggregator/TypeIndex/TypeIndexMenu.cs
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.CSFilesAggregator.TypeIndex
{
    /// <summary>
    /// Menu integration for building and locating the type index.
    /// </summary>
    public static class TypeIndexMenu
    {
        [MenuItem("Tools/Legendary Tools/Type Index/Rebuild (Assets/Packages)")]
        private static void Rebuild()
        {
            TypeIndex index = TypeIndexService.RebuildAndSave();

            int count = index?.Data?.Entries?.Count ?? 0;
            string path = TypeIndexService.GetIndexFileAbsolutePath();

            UnityEngine.Debug.Log($"Type Index rebuilt. Entries: {count}. File: {path}");
            AssetDatabase.Refresh();
        }

        [MenuItem("Tools/Legendary Tools/Type Index/Open Index Folder")]
        private static void OpenIndexFolder()
        {
            string file = TypeIndexService.GetIndexFileAbsolutePath();
            string folder = Path.GetDirectoryName(file);

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                UnityEngine.Debug.LogWarning("Index folder does not exist yet. Rebuild the index first.");
                return;
            }

            // Unity will open the folder in the OS file explorer.
            EditorUtility.RevealInFinder(folder);
        }

        [MenuItem("Tools/Legendary Tools/Type Index/Settings")]
        private static void SelectSettings()
        {
            Selection.activeObject = TypeIndexSettings.instance;
            EditorGUIUtility.PingObject(TypeIndexSettings.instance);
        }
    }
}
