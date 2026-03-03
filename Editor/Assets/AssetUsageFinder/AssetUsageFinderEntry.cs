namespace LegendaryTools.Editor
{
    public sealed class AssetUsageFinderEntry
    {
        public string AssetPath { get; }
        public AssetUsageFinderUsageType UsageType { get; }

        public bool UsageListExpanded { get; set; }
        public bool PrefabListExpanded { get; set; }

        public string SourcePrefabPath { get; }

        public AssetUsageFinderEntry(string assetPath, AssetUsageFinderUsageType usageType,
            string sourcePrefabPath = null)
        {
            AssetPath = assetPath;
            UsageType = usageType;
            SourcePrefabPath = sourcePrefabPath;
        }
    }
}