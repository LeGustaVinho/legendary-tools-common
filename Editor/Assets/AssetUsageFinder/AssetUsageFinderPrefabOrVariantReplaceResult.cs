using System.Collections.Generic;

namespace LegendaryTools.Editor
{
    public sealed class AssetUsageFinderPrefabOrVariantReplaceResult
    {
        public List<AssetUsageFinderPrefabOrVariantReplacePreviewItem> Items { get; }
        public int ReplacedInstanceCount { get; }
        public int AffectedFileCount { get; }

        public AssetUsageFinderPrefabOrVariantReplaceResult(
            List<AssetUsageFinderPrefabOrVariantReplacePreviewItem> items,
            int replacedInstanceCount,
            int affectedFileCount)
        {
            Items = items ?? new List<AssetUsageFinderPrefabOrVariantReplacePreviewItem>();
            ReplacedInstanceCount = replacedInstanceCount;
            AffectedFileCount = affectedFileCount;
        }
    }
}