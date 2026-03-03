using System.Collections.Generic;

namespace LegendaryTools.Editor
{
    public sealed class AssetUsageFinderSerializedFieldValueReplaceResult
    {
        public List<AssetUsageFinderSerializedFieldValueReplacePreviewItem> Items { get; }
        public int ReplacedValueCount { get; }
        public int AffectedFileCount { get; }

        public AssetUsageFinderSerializedFieldValueReplaceResult(
            List<AssetUsageFinderSerializedFieldValueReplacePreviewItem> items,
            int replacedValueCount,
            int affectedFileCount)
        {
            Items = items ?? new List<AssetUsageFinderSerializedFieldValueReplacePreviewItem>();
            ReplacedValueCount = replacedValueCount;
            AffectedFileCount = affectedFileCount;
        }
    }
}