namespace LegendaryTools.Editor
{
    public sealed class AssetUsageFinderSerializedFieldValueReplacePreviewItem
    {
        public string FileAssetPath { get; }
        public string ObjectPath { get; }
        public string ObjectTypeName { get; }
        public string PropertyPath { get; }
        public string CurrentValue { get; }
        public string NewValue { get; }
        public bool IsScene { get; }

        public AssetUsageFinderSerializedFieldValueReplacePreviewItem(
            string fileAssetPath,
            string objectPath,
            string objectTypeName,
            string propertyPath,
            string currentValue,
            string newValue,
            bool isScene)
        {
            FileAssetPath = fileAssetPath;
            ObjectPath = objectPath;
            ObjectTypeName = objectTypeName;
            PropertyPath = propertyPath;
            CurrentValue = currentValue;
            NewValue = newValue;
            IsScene = isScene;
        }
    }
}