using System;

namespace LegendaryTools.Editor
{
    public sealed class AssetUsageFinderComponentReplaceRequest
    {
        public Type FromType { get; }
        public Type ToType { get; }
        public bool CopySerializedValues { get; }
        public bool DisableOldComponentInsteadOfRemove { get; }
        public AssetUsageFinderSearchScope SearchScope { get; }

        public AssetUsageFinderComponentReplaceRequest(
            Type fromType,
            Type toType,
            bool copySerializedValues,
            bool disableOldComponentInsteadOfRemove,
            AssetUsageFinderSearchScope searchScope)
        {
            FromType = fromType;
            ToType = toType;
            CopySerializedValues = copySerializedValues;
            DisableOldComponentInsteadOfRemove = disableOldComponentInsteadOfRemove;
            SearchScope = searchScope;
        }
    }
}
