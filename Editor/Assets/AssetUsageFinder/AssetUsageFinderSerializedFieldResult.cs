namespace LegendaryTools.Editor
{
    public sealed class AssetUsageFinderSerializedFieldResult
    {
        public string FileAssetPath { get; }
        public string ObjectPath { get; }
        public string ObjectTypeName { get; }
        public string PropertyPath { get; }
        public string CurrentValue { get; }

        public AssetUsageFinderSerializedFieldResult(
            string fileAssetPath,
            string objectPath,
            string objectTypeName,
            string propertyPath,
            string currentValue)
        {
            FileAssetPath = fileAssetPath;
            ObjectPath = objectPath;
            ObjectTypeName = objectTypeName;
            PropertyPath = propertyPath;
            CurrentValue = currentValue;
        }
    }
}