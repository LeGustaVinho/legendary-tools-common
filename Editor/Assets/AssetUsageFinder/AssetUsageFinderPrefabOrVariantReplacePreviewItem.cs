namespace LegendaryTools.Editor
{
    public sealed class AssetUsageFinderPrefabOrVariantReplacePreviewItem
    {
        public string FileAssetPath { get; }
        public string ObjectPath { get; }
        public string SourcePrefabPath { get; }
        public bool IsScene { get; }
        public bool IsVariantMatch { get; }

        public AssetUsageFinderPrefabOrVariantReplacePreviewItem(
            string fileAssetPath,
            string objectPath,
            string sourcePrefabPath,
            bool isScene,
            bool isVariantMatch)
        {
            FileAssetPath = fileAssetPath;
            ObjectPath = objectPath;
            SourcePrefabPath = sourcePrefabPath;
            IsScene = isScene;
            IsVariantMatch = isVariantMatch;
        }
    }
}