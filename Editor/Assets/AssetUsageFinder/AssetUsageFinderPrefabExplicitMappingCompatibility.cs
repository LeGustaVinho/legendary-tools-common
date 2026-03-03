using System;
using System.Collections.Generic;
using System.Linq;

namespace LegendaryTools.Editor
{
    public static class AssetUsageFinderPrefabExplicitMappingCompatibility
    {
        public static List<AssetUsageFinderPrefabSubobjectDescriptor> GetCompatibleTargets(
            AssetUsageFinderPrefabSubobjectDescriptor fromDescriptor,
            IEnumerable<AssetUsageFinderPrefabSubobjectDescriptor> candidates)
        {
            if (candidates == null)
                return new List<AssetUsageFinderPrefabSubobjectDescriptor>();

            return candidates
                .Where(candidate => AreCompatible(fromDescriptor, candidate))
                .ToList();
        }

        public static bool AreCompatible(
            AssetUsageFinderPrefabSubobjectDescriptor fromDescriptor,
            AssetUsageFinderPrefabSubobjectDescriptor toDescriptor)
        {
            if (fromDescriptor == null || toDescriptor == null)
                return false;

            if (IsNodeDescriptor(fromDescriptor))
                return IsNodeDescriptor(toDescriptor);

            if (IsNodeDescriptor(toDescriptor))
                return false;

            return string.Equals(
                fromDescriptor.ComponentTypeName ?? string.Empty,
                toDescriptor.ComponentTypeName ?? string.Empty,
                StringComparison.Ordinal);
        }

        public static bool IsNodeDescriptor(AssetUsageFinderPrefabSubobjectDescriptor descriptor)
        {
            if (descriptor == null)
                return false;

            if (descriptor.Kind == AssetUsageFinderPrefabSubobjectKind.GameObject)
                return true;

            if (descriptor.Kind != AssetUsageFinderPrefabSubobjectKind.Component)
                return false;

            string componentTypeName = descriptor.ComponentTypeName ?? string.Empty;
            return string.Equals(componentTypeName, typeof(UnityEngine.Transform).AssemblyQualifiedName,
                       StringComparison.Ordinal) ||
                   string.Equals(componentTypeName, typeof(UnityEngine.Transform).FullName, StringComparison.Ordinal) ||
                   string.Equals(componentTypeName, nameof(UnityEngine.Transform), StringComparison.Ordinal);
        }
    }
}