using System;
using System.Collections.Generic;

namespace LegendaryTools.Editor
{
    internal sealed class ReferenceTrackerGroupingService
    {
        public List<ReferenceTrackerGroupBucket> BuildGroups(
            IList<ReferenceTrackerUsageResult> results,
            ReferenceTrackerGroupMode groupMode)
        {
            Dictionary<string, ReferenceTrackerGroupBucket> buckets =
                new Dictionary<string, ReferenceTrackerGroupBucket>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < results.Count; i++)
            {
                ReferenceTrackerUsageResult result = results[i];
                string key = GetGroupKey(result, groupMode);

                ReferenceTrackerGroupBucket bucket;
                if (!buckets.TryGetValue(key, out bucket))
                {
                    bucket = new ReferenceTrackerGroupBucket
                    {
                        Key = key,
                    };

                    buckets.Add(key, bucket);
                }

                bucket.Items.Add(result);
            }

            List<ReferenceTrackerGroupBucket> orderedBuckets = new List<ReferenceTrackerGroupBucket>(buckets.Values);
            orderedBuckets.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));

            for (int i = 0; i < orderedBuckets.Count; i++)
            {
                orderedBuckets[i].Items.Sort(ReferenceTrackerSearchService.CompareResults);
            }

            return orderedBuckets;
        }

        private static string GetGroupKey(ReferenceTrackerUsageResult result, ReferenceTrackerGroupMode groupMode)
        {
            switch (groupMode)
            {
                case ReferenceTrackerGroupMode.None:
                    return "All Results";

                case ReferenceTrackerGroupMode.Asset:
                    return string.IsNullOrEmpty(result.AssetPath) ? "<unknown asset>" : result.AssetPath;

                case ReferenceTrackerGroupMode.AssetKind:
                    return string.IsNullOrEmpty(result.AssetKindLabel) ? "<unknown kind>" : result.AssetKindLabel;

                case ReferenceTrackerGroupMode.GameObject:
                    return string.IsNullOrEmpty(result.HostGameObjectPath)
                        ? result.AssetPath
                        : string.Format("{0} / {1}", result.AssetPath, result.HostGameObjectPath);

                case ReferenceTrackerGroupMode.Component:
                    return string.IsNullOrEmpty(result.HostGameObjectPath)
                        ? string.Format("{0} / {1}", result.AssetPath, result.HostComponentLabel)
                        : string.Format("{0} / {1} / {2}", result.AssetPath, result.HostGameObjectPath,
                            result.HostComponentLabel);

                case ReferenceTrackerGroupMode.Property:
                    return string.IsNullOrEmpty(result.PropertyPath) ? "<unknown property>" : result.PropertyPath;

                case ReferenceTrackerGroupMode.ReferenceType:
                    return result.ReferenceTypeLabel;

                default:
                    return result.AssetPath;
            }
        }
    }
}
