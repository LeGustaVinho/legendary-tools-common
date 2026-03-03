using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    public static class AssetUsageFinderPrefabExplicitMappingPresenter
    {
        public static bool CanEdit(Object fromPrefab, Object toPrefab)
        {
            return AssetUsageFinderPrefabExplicitMappingStore.IsValidPrefabAsset(fromPrefab) &&
                   AssetUsageFinderPrefabExplicitMappingStore.IsValidPrefabAsset(toPrefab);
        }

        public static string BuildSummary(Object fromPrefab, Object toPrefab)
        {
            if (!CanEdit(fromPrefab, toPrefab))
                return "Select both prefabs to configure explicit subobject mappings.";

            int mappingCount = AssetUsageFinderPrefabExplicitMappingStore.GetMappingCount(fromPrefab, toPrefab);
            return $"{mappingCount} explicit subobject mapping(s) configured for this prefab pair.";
        }

        public static void OpenEditor(Object fromPrefab, Object toPrefab)
        {
            AssetUsageFinderPrefabExplicitMappingWindow.ShowWindow(fromPrefab, toPrefab);
        }
    }
}