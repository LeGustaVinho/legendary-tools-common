using System.Collections.Generic;
using System;
using System.Linq;

namespace LegendaryTools.Editor
{
    public sealed class AssetUsageFinderState
    {
        // Serialized Field Finder state
        public bool SerializedFieldFinderIsBusy { get; set; }
        public string SerializedFieldFinderStatus { get; set; }
        public List<AssetUsageFinderSerializedFieldResult> SerializedFieldFinderResults { get; set; } = new();

        public UnityEngine.Object TargetAsset { get; set; }

        public bool IsBusy { get; set; }
        public string StatusMessage { get; set; }

        public AssetUsageFinderSearchScope SearchScope { get; set; } =
            AssetUsageFinderSearchScopeUtility.DefaultProjectScope;

        public List<AssetUsageFinderEntry> Entries { get; set; } = new();

        public bool PrefabOrVariantReplaceIsBusy { get; set; }
        public string PrefabOrVariantReplaceStatus { get; set; }

        public List<AssetUsageFinderPrefabOrVariantReplacePreviewItem> PrefabOrVariantReplacePreview { get; set; } =
            new();

        public bool SerializedFieldValueReplaceIsBusy { get; set; }
        public string SerializedFieldValueReplaceStatus { get; set; }

        public List<AssetUsageFinderSerializedFieldValueReplacePreviewItem> SerializedFieldValueReplacePreview
        {
            get;
            set;
        } = new();

        public bool ComponentReplaceIsBusy { get; set; }
        public string ComponentReplaceStatus { get; set; }
        public List<AssetUsageFinderComponentReplacePreviewItem> ComponentReplacePreview { get; set; } = new();

        public bool PassesFilter(AssetUsageFinderUsageType type)
        {
            return AssetUsageFinderSearchScopeUtility.MatchesUsageType(type, SearchScope);
        }

        public Dictionary<string, List<string>> BuildScenePrefabGroups()
        {
            return Entries
                .Where(e => e.UsageType == AssetUsageFinderUsageType.SceneWithPrefabInstance)
                .GroupBy(e => e.AssetPath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.SourcePrefabPath).Where(p => !string.IsNullOrEmpty(p))
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    StringComparer.OrdinalIgnoreCase);
        }
    }
}
