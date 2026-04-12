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
                case ReferenceTrackerGroupMode.GameObject:
                    return result.HostGameObjectPath;

                case ReferenceTrackerGroupMode.Component:
                    return string.Format("{0} / {1}", result.HostGameObjectPath, result.HostComponentLabel);

                case ReferenceTrackerGroupMode.ReferenceType:
                    return result.ReferenceTypeLabel;

                default:
                    return result.HostGameObjectPath;
            }
        }
    }
}
