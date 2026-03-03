using System;
using System.Collections.Generic;

namespace LegendaryTools.Editor
{
    public enum AssetUsageFinderPrefabSubobjectKind
    {
        GameObject = 0,
        Component = 1
    }

    [Serializable]
    public sealed class AssetUsageFinderPrefabSubobjectDescriptor
    {
        public string RelativePath = string.Empty;
        public AssetUsageFinderPrefabSubobjectKind Kind = AssetUsageFinderPrefabSubobjectKind.GameObject;
        public string ComponentTypeName = string.Empty;
        public int ComponentIndex;

        public AssetUsageFinderPrefabSubobjectDescriptor Clone()
        {
            return new AssetUsageFinderPrefabSubobjectDescriptor
            {
                RelativePath = RelativePath ?? string.Empty,
                Kind = Kind,
                ComponentTypeName = ComponentTypeName ?? string.Empty,
                ComponentIndex = ComponentIndex
            };
        }

        public bool Matches(AssetUsageFinderPrefabSubobjectDescriptor other)
        {
            if (other == null)
                return false;

            return string.Equals(RelativePath ?? string.Empty, other.RelativePath ?? string.Empty,
                       StringComparison.Ordinal) &&
                   Kind == other.Kind &&
                   string.Equals(ComponentTypeName ?? string.Empty, other.ComponentTypeName ?? string.Empty,
                       StringComparison.Ordinal) &&
                   ComponentIndex == other.ComponentIndex;
        }
    }

    [Serializable]
    public sealed class AssetUsageFinderPrefabExplicitRemapEntry
    {
        public AssetUsageFinderPrefabSubobjectDescriptor From = new();
        public AssetUsageFinderPrefabSubobjectDescriptor To = new();

        public AssetUsageFinderPrefabExplicitRemapEntry Clone()
        {
            return new AssetUsageFinderPrefabExplicitRemapEntry
            {
                From = From?.Clone() ?? new AssetUsageFinderPrefabSubobjectDescriptor(),
                To = To?.Clone() ?? new AssetUsageFinderPrefabSubobjectDescriptor()
            };
        }
    }

    [Serializable]
    public sealed class AssetUsageFinderPrefabExplicitMappingProfile
    {
        public string FromPrefabPath = string.Empty;
        public string ToPrefabPath = string.Empty;
        public List<AssetUsageFinderPrefabExplicitRemapEntry> Entries = new();

        public AssetUsageFinderPrefabExplicitMappingProfile Clone()
        {
            AssetUsageFinderPrefabExplicitMappingProfile clone = new()
            {
                FromPrefabPath = FromPrefabPath ?? string.Empty,
                ToPrefabPath = ToPrefabPath ?? string.Empty
            };

            if (Entries == null)
                return clone;

            for (int i = 0; i < Entries.Count; i++)
            {
                AssetUsageFinderPrefabExplicitRemapEntry entry = Entries[i];
                if (entry != null)
                    clone.Entries.Add(entry.Clone());
            }

            return clone;
        }
    }
}