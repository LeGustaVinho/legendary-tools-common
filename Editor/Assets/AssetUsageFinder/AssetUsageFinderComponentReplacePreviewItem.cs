namespace LegendaryTools.Editor
{
    public sealed class AssetUsageFinderComponentReplacePreviewItem
    {
        public string FileAssetPath { get; }
        public string ObjectPath { get; }
        public string ComponentLabel { get; }
        public string FromTypeName { get; }
        public string ToTypeName { get; }
        public bool IsScene { get; }
        public bool WillDisableOldComponentInsteadOfRemove { get; }

        public AssetUsageFinderComponentReplacePreviewItem(
            string fileAssetPath,
            string objectPath,
            string componentLabel,
            string fromTypeName,
            string toTypeName,
            bool isScene,
            bool willDisableOldComponentInsteadOfRemove)
        {
            FileAssetPath = fileAssetPath;
            ObjectPath = objectPath;
            ComponentLabel = componentLabel;
            FromTypeName = fromTypeName;
            ToTypeName = toTypeName;
            IsScene = isScene;
            WillDisableOldComponentInsteadOfRemove = willDisableOldComponentInsteadOfRemove;
        }
    }
}