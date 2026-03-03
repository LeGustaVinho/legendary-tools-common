using UnityEngine;

namespace LegendaryTools.Editor
{
    public sealed class AssetUsageFinderPrefabOrVariantReplaceRequest
    {
        public GameObject FromPrefab { get; }
        public GameObject ToPrefab { get; }
        public bool IncludeVariants { get; }
        public bool KeepOverrides { get; }
        public bool CopyCommonRootComponentValues { get; }
        public AssetUsageFinderSearchScope SearchScope { get; }

        public AssetUsageFinderPrefabOrVariantReplaceRequest(
            GameObject fromPrefab,
            GameObject toPrefab,
            bool includeVariants,
            bool keepOverrides,
            bool copyCommonRootComponentValues,
            AssetUsageFinderSearchScope searchScope)
        {
            FromPrefab = fromPrefab;
            ToPrefab = toPrefab;
            IncludeVariants = includeVariants;
            KeepOverrides = keepOverrides;
            CopyCommonRootComponentValues = copyCommonRootComponentValues;
            SearchScope = searchScope;
        }
    }
}
