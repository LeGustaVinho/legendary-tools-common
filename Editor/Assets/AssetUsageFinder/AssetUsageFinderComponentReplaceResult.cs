using System.Collections.Generic;

namespace LegendaryTools.Editor
{
    public sealed class AssetUsageFinderComponentReplaceResult
    {
        public List<AssetUsageFinderComponentReplacePreviewItem> Items { get; }
        public int ReplacedComponentCount { get; }
        public int AffectedFileCount { get; }

        public AssetUsageFinderComponentReplaceResult(
            List<AssetUsageFinderComponentReplacePreviewItem> items,
            int replacedComponentCount,
            int affectedFileCount)
        {
            Items = items ?? new List<AssetUsageFinderComponentReplacePreviewItem>();
            ReplacedComponentCount = replacedComponentCount;
            AffectedFileCount = affectedFileCount;
        }
    }
}