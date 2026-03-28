using UnityEditor;

namespace LegendaryTools.Editor
{
    internal static class PrefabThumbnailCacheMenu
    {
        private const string StartMenuPath = "Tools/LegendaryTools/AssetIcons/Start Prefab Thumbnail Generation";
        private const string StopMenuPath = "Tools/LegendaryTools/AssetIcons/Stop Prefab Thumbnail Generation";
        private const string ClearMenuPath = "Tools/LegendaryTools/AssetIcons/Clear Generated Prefab Thumbnails";

        [MenuItem(StartMenuPath)]
        private static void StartGeneratedPrefabThumbnails()
        {
            PrefabThumbnailOrchestrator.SetEnabled(true);
        }

        [MenuItem(StartMenuPath, true)]
        private static bool ValidateStartGeneratedPrefabThumbnails()
        {
            return !PrefabThumbnailOrchestrator.IsEnabled();
        }

        [MenuItem(StopMenuPath)]
        private static void StopGeneratedPrefabThumbnails()
        {
            PrefabThumbnailOrchestrator.SetEnabled(false);
        }

        [MenuItem(StopMenuPath, true)]
        private static bool ValidateStopGeneratedPrefabThumbnails()
        {
            return PrefabThumbnailOrchestrator.IsEnabled();
        }

        [MenuItem(ClearMenuPath)]
        private static void ClearGeneratedPrefabThumbnails()
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Clear Generated Prefab Thumbnails",
                "Delete all generated prefab thumbnails and stored hashes from Library?",
                "Delete",
                "Cancel");

            if (!confirmed) return;

            PrefabThumbnailOrchestrator.ClearPendingWork();
            PrefabThumbnailCache.ClearAll();
            EditorApplication.RepaintProjectWindow();
        }
    }
}