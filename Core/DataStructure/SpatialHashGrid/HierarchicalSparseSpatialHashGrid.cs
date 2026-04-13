using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools
{
    /// <summary>
    /// Hierarchical sparse spatial hash grid with quantized deterministic indexing.
    /// </summary>
    public sealed class HierarchicalSparseSpatialHashGrid<T> : IDisposable
    {
        private const int QueryMarkCleanupInterval = 4096;

        private sealed class Entry
        {
            public T Item;
            public Bounds WorldBounds;
            public SpatialHashQuantizedAabb QuantizedBounds;
            public int StableId;
            public int Level;
            public int OccupiedCellCount;
            public int Layer;
            public uint UserFlags;
            public bool IsDynamic;
            public bool IsOverflow;
        }

        private readonly Dictionary<int, Entry> entries;
        private readonly List<int> staticStableIds;
        private readonly List<int> dynamicStableIds;
        private readonly List<int> queryCandidatesScratch;
        private readonly List<int> queryResultsScratch;
        private readonly Dictionary<int, int> queryMarks;
        private readonly Bounds worldBounds;
        private readonly Vector3 worldMin;
        private readonly SpatialHashGridDimension dimension;
        private readonly float baseCellSize;
        private readonly float inverseBaseCellSize;
        private readonly int levelCount;
        private readonly int maxCellsPerAxis;
        private readonly int maxCellsPerObject;
        private readonly NativeSpatialHashGridIndex staticIndex;
        private readonly NativeSpatialHashGridIndex dynamicIndex;
        private bool staticDirty;
        private bool dynamicDirty;
        private int queryToken;

        public HierarchicalSparseSpatialHashGrid(
            Bounds worldBounds,
            float baseCellSize,
            int levelCount,
            SpatialHashGridDimension dimension = SpatialHashGridDimension.XYZ3D,
            int maxCellsPerAxis = 4,
            int maxCellsPerObject = 64)
        {
            if (baseCellSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(baseCellSize), "Base cell size must be greater than zero.");
            }

            if (levelCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(levelCount), "Level count must be greater than zero.");
            }

            if (maxCellsPerAxis <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCellsPerAxis), "Max cells per axis must be greater than zero.");
            }

            if (maxCellsPerObject <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCellsPerObject), "Max cells per object must be greater than zero.");
            }

            this.worldBounds = worldBounds;
            worldMin = worldBounds.min;
            this.baseCellSize = baseCellSize;
            inverseBaseCellSize = 1f / baseCellSize;
            this.levelCount = levelCount;
            this.dimension = dimension;
            this.maxCellsPerAxis = maxCellsPerAxis;
            this.maxCellsPerObject = maxCellsPerObject;

            entries = new Dictionary<int, Entry>();
            staticStableIds = new List<int>();
            dynamicStableIds = new List<int>();
            queryCandidatesScratch = new List<int>(128);
            queryResultsScratch = new List<int>(128);
            queryMarks = new Dictionary<int, int>();
            staticIndex = new NativeSpatialHashGridIndex(dimension);
            dynamicIndex = new NativeSpatialHashGridIndex(dimension);
            staticDirty = true;
            dynamicDirty = true;
            queryToken = 1;
        }

        public int Count => entries.Count;
        public Bounds WorldBounds => worldBounds;

        public bool Upsert(int stableId, Bounds bounds, T item, SpatialHashItemMetadata metadata)
        {
            return Upsert(stableId, bounds, item, metadata, out _);
        }

        public bool Upsert(int stableId, Bounds bounds, T item, SpatialHashItemMetadata metadata, out bool changed)
        {
            if (stableId == 0)
            {
                throw new ArgumentException("Stable id cannot be zero.", nameof(stableId));
            }

            if (!TryQuantizeBounds(bounds, out Bounds clippedBounds, out SpatialHashQuantizedAabb quantizedBounds))
            {
                changed = Remove(stableId);
                return false;
            }

            BuildPlacement(quantizedBounds, out int level, out int occupiedCellCount, out bool isOverflow);

            bool existed = entries.TryGetValue(stableId, out Entry entry);
            changed = !existed;

            if (!existed)
            {
                entry = new Entry
                {
                    StableId = stableId
                };
                entries.Add(stableId, entry);
            }

            bool movedBetweenIndices = existed && entry.IsDynamic != metadata.IsDynamic;
            changed |= movedBetweenIndices ||
                       !entry.QuantizedBounds.Equals(quantizedBounds) ||
                       entry.Level != level ||
                       entry.OccupiedCellCount != occupiedCellCount ||
                       entry.IsOverflow != isOverflow;

            if (movedBetweenIndices)
            {
                RemoveSorted(entry.IsDynamic ? dynamicStableIds : staticStableIds, stableId);
                MarkDirty(entry.IsDynamic);
            }

            List<int> ownerList = metadata.IsDynamic ? dynamicStableIds : staticStableIds;
            if (!ContainsStableId(ownerList, stableId))
            {
                InsertSortedUnique(ownerList, stableId);
                changed = true;
            }

            entry.Item = item;
            entry.WorldBounds = clippedBounds;
            entry.QuantizedBounds = quantizedBounds;
            entry.Level = level;
            entry.OccupiedCellCount = occupiedCellCount;
            entry.IsOverflow = isOverflow;
            entry.IsDynamic = metadata.IsDynamic;
            entry.Layer = metadata.Layer;
            entry.UserFlags = metadata.UserFlags;

            if (changed)
            {
                MarkDirty(metadata.IsDynamic);
            }

            return true;
        }

        public bool Remove(int stableId)
        {
            if (!entries.TryGetValue(stableId, out Entry entry))
            {
                return false;
            }

            entries.Remove(stableId);
            queryMarks.Remove(stableId);
            RemoveSorted(entry.IsDynamic ? dynamicStableIds : staticStableIds, stableId);
            MarkDirty(entry.IsDynamic);
            return true;
        }

        public void Clear()
        {
            entries.Clear();
            staticStableIds.Clear();
            dynamicStableIds.Clear();
            queryCandidatesScratch.Clear();
            queryResultsScratch.Clear();
            queryMarks.Clear();
            staticIndex.Clear();
            dynamicIndex.Clear();
            staticDirty = false;
            dynamicDirty = false;
        }

        public void ClearDynamic()
        {
            for (int i = 0; i < dynamicStableIds.Count; i++)
            {
                entries.Remove(dynamicStableIds[i]);
                queryMarks.Remove(dynamicStableIds[i]);
            }

            dynamicStableIds.Clear();
            dynamicIndex.Clear();
            dynamicDirty = false;
        }

        public void RebuildStaticIndex()
        {
            staticIndex.Rebuild(BuildItems(staticStableIds));
            staticDirty = false;
        }

        public void RebuildDynamicIndex()
        {
            dynamicIndex.Rebuild(BuildItems(dynamicStableIds));
            dynamicDirty = false;
        }

        public int Query(Bounds bounds, List<T> results, SpatialHashQueryFilter filter = default)
        {
            return Query(bounds, results, filter, exactIntersections: true);
        }

        public int QueryBroadphase(Bounds bounds, List<T> results, SpatialHashQueryFilter filter = default)
        {
            return Query(bounds, results, filter, exactIntersections: false);
        }

        public int Query(Bounds bounds, List<T> results, SpatialHashQueryFilter filter, bool exactIntersections)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            int startCount = results.Count;
            CollectCandidateIds(bounds, NormalizeFilter(filter), exactIntersections);

            for (int i = 0; i < queryResultsScratch.Count; i++)
            {
                results.Add(entries[queryResultsScratch[i]].Item);
            }

            return results.Count - startCount;
        }

        public int QueryIds(Bounds bounds, List<int> results, SpatialHashQueryFilter filter = default)
        {
            return QueryIds(bounds, results, filter, exactIntersections: true);
        }

        public int QueryIdsBroadphase(Bounds bounds, List<int> results, SpatialHashQueryFilter filter = default)
        {
            return QueryIds(bounds, results, filter, exactIntersections: false);
        }

        public int QueryIds(Bounds bounds, List<int> results, SpatialHashQueryFilter filter, bool exactIntersections)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            int startCount = results.Count;
            CollectCandidateIds(bounds, NormalizeFilter(filter), exactIntersections);
            results.AddRange(queryResultsScratch);
            return results.Count - startCount;
        }

        public bool TryGetEntryInfo(int stableId, out SpatialHashGridEntryInfo info)
        {
            if (!entries.TryGetValue(stableId, out Entry entry))
            {
                info = default;
                return false;
            }

            info = new SpatialHashGridEntryInfo(
                entry.StableId,
                entry.Level,
                entry.OccupiedCellCount,
                entry.IsDynamic,
                entry.IsOverflow,
                entry.WorldBounds);

            return true;
        }

        public bool Contains(int stableId)
        {
            return entries.ContainsKey(stableId);
        }

        public void Dispose()
        {
            staticIndex.Dispose();
            dynamicIndex.Dispose();
        }

        private void CollectCandidateIds(Bounds bounds, SpatialHashQueryFilter filter, bool exactIntersections)
        {
            queryCandidatesScratch.Clear();
            queryResultsScratch.Clear();

            if (!TryQuantizeBounds(bounds, out Bounds clippedBounds, out SpatialHashQuantizedAabb quantizedQueryBounds))
            {
                return;
            }

            RebuildDirtyIndices();
            AdvanceQueryToken();

            staticIndex.CollectCandidates(quantizedQueryBounds, levelCount, queryCandidatesScratch, queryMarks, queryToken);
            dynamicIndex.CollectCandidates(quantizedQueryBounds, levelCount, queryCandidatesScratch, queryMarks, queryToken);
            queryCandidatesScratch.Sort();

            for (int i = 0; i < queryCandidatesScratch.Count; i++)
            {
                Entry entry = entries[queryCandidatesScratch[i]];
                if (!PassesFilter(entry, filter))
                {
                    continue;
                }

                if (!entry.QuantizedBounds.Intersects(quantizedQueryBounds, dimension))
                {
                    continue;
                }

                if (exactIntersections && !Intersects(entry.WorldBounds, clippedBounds))
                {
                    continue;
                }

                queryResultsScratch.Add(entry.StableId);
            }
        }

        private void RebuildDirtyIndices()
        {
            if (staticDirty)
            {
                RebuildStaticIndex();
            }

            if (dynamicDirty)
            {
                RebuildDynamicIndex();
            }
        }

        private SpatialHashIndexBuildItem[] BuildItems(List<int> orderedStableIds)
        {
            SpatialHashIndexBuildItem[] buildItems = new SpatialHashIndexBuildItem[orderedStableIds.Count];
            for (int i = 0; i < orderedStableIds.Count; i++)
            {
                Entry entry = entries[orderedStableIds[i]];
                buildItems[i] = new SpatialHashIndexBuildItem(
                    entry.StableId,
                    entry.Level,
                    entry.OccupiedCellCount,
                    entry.IsOverflow,
                    entry.QuantizedBounds);
            }

            return buildItems;
        }

        private void BuildPlacement(SpatialHashQuantizedAabb quantizedBounds, out int level, out int occupiedCellCount, out bool isOverflow)
        {
            level = 0;
            while (level < levelCount - 1 && quantizedBounds.GetLargestAxisCellCount(level, dimension) > maxCellsPerAxis)
            {
                level++;
            }

            while (true)
            {
                occupiedCellCount = quantizedBounds.GetOccupiedCellCount(level, dimension);
                if (occupiedCellCount <= maxCellsPerObject)
                {
                    isOverflow = false;
                    return;
                }

                if (level >= levelCount - 1)
                {
                    isOverflow = true;
                    return;
                }

                level++;
            }
        }

        private bool TryQuantizeBounds(Bounds bounds, out Bounds clippedBounds, out SpatialHashQuantizedAabb quantizedBounds)
        {
            if (!TryClipBoundsToWorld(bounds, out clippedBounds))
            {
                quantizedBounds = default;
                return false;
            }

            Vector3 min = clippedBounds.min;
            Vector3 max = clippedBounds.max;

            QuantizeAxis(min.x, max.x, worldMin.x, UsesAxis(0), out int minX, out int maxX);
            QuantizeAxis(min.y, max.y, worldMin.y, UsesAxis(1), out int minY, out int maxY);
            QuantizeAxis(min.z, max.z, worldMin.z, UsesAxis(2), out int minZ, out int maxZ);

            quantizedBounds = new SpatialHashQuantizedAabb(
                minX,
                minY,
                minZ,
                maxX,
                maxY,
                maxZ);

            return true;
        }

        private void QuantizeAxis(float axisMin, float axisMax, float axisOrigin, bool relevant, out int minCell, out int maxCell)
        {
            if (!relevant)
            {
                minCell = 0;
                maxCell = 0;
                return;
            }

            minCell = Mathf.FloorToInt((axisMin - axisOrigin) * inverseBaseCellSize);
            maxCell = Mathf.CeilToInt((axisMax - axisOrigin) * inverseBaseCellSize) - 1;
            if (maxCell < minCell)
            {
                maxCell = minCell;
            }
        }

        private bool TryClipBoundsToWorld(Bounds bounds, out Bounds clippedBounds)
        {
            Vector3 inputMin = bounds.min;
            Vector3 inputMax = bounds.max;
            Vector3 resultMin = Vector3.zero;
            Vector3 resultMax = Vector3.zero;

            if (!TryClipAxis(inputMin.x, inputMax.x, worldBounds.min.x, worldBounds.max.x, UsesAxis(0), out resultMin.x, out resultMax.x) ||
                !TryClipAxis(inputMin.y, inputMax.y, worldBounds.min.y, worldBounds.max.y, UsesAxis(1), out resultMin.y, out resultMax.y) ||
                !TryClipAxis(inputMin.z, inputMax.z, worldBounds.min.z, worldBounds.max.z, UsesAxis(2), out resultMin.z, out resultMax.z))
            {
                clippedBounds = default;
                return false;
            }

            clippedBounds = new Bounds();
            clippedBounds.SetMinMax(resultMin, resultMax);
            return true;
        }

        private static bool TryClipAxis(float min, float max, float worldMin, float worldMax, bool relevant, out float clippedMin, out float clippedMax)
        {
            if (!relevant)
            {
                clippedMin = 0f;
                clippedMax = 0f;
                return true;
            }

            clippedMin = Mathf.Max(min, worldMin);
            clippedMax = Mathf.Min(max, worldMax);
            return clippedMin <= clippedMax;
        }

        private bool UsesAxis(int axis)
        {
            return dimension switch
            {
                SpatialHashGridDimension.XY2D => axis != 2,
                SpatialHashGridDimension.XZ2D => axis != 1,
                _ => true
            };
        }

        private bool Intersects(Bounds left, Bounds right)
        {
            Vector3 leftMin = left.min;
            Vector3 leftMax = left.max;
            Vector3 rightMin = right.min;
            Vector3 rightMax = right.max;

            bool intersectsX = leftMin.x <= rightMax.x && leftMax.x >= rightMin.x;
            bool intersectsY = !UsesAxis(1) || (leftMin.y <= rightMax.y && leftMax.y >= rightMin.y);
            bool intersectsZ = !UsesAxis(2) || (leftMin.z <= rightMax.z && leftMax.z >= rightMin.z);
            return intersectsX && intersectsY && intersectsZ;
        }

        private static bool PassesFilter(Entry entry, SpatialHashQueryFilter filter)
        {
            if ((filter.LayerMask & (1 << entry.Layer)) == 0)
            {
                return false;
            }

            if ((entry.UserFlags & filter.RequiredFlags) != filter.RequiredFlags)
            {
                return false;
            }

            if ((entry.UserFlags & filter.ExcludedFlags) != 0u)
            {
                return false;
            }

            return true;
        }

        private static SpatialHashQueryFilter NormalizeFilter(SpatialHashQueryFilter filter)
        {
            return filter.LayerMask == 0 && filter.RequiredFlags == 0u && filter.ExcludedFlags == 0u
                ? SpatialHashQueryFilter.Default
                : filter;
        }

        private void MarkDirty(bool isDynamic)
        {
            if (isDynamic)
            {
                dynamicDirty = true;
            }
            else
            {
                staticDirty = true;
            }
        }

        private void AdvanceQueryToken()
        {
            if (queryToken == int.MaxValue)
            {
                queryMarks.Clear();
                queryToken = 1;
                return;
            }

            queryToken++;
            if ((queryToken % QueryMarkCleanupInterval) == 0 && queryMarks.Count > Math.Max(1024, entries.Count * 2))
            {
                queryMarks.Clear();
                queryToken = 1;
            }
        }

        private static bool ContainsStableId(List<int> values, int stableId)
        {
            return values.BinarySearch(stableId) >= 0;
        }

        private static void InsertSortedUnique(List<int> values, int value)
        {
            int index = values.BinarySearch(value);
            if (index >= 0)
            {
                return;
            }

            values.Insert(~index, value);
        }

        private static void RemoveSorted(List<int> values, int value)
        {
            int index = values.BinarySearch(value);
            if (index >= 0)
            {
                values.RemoveAt(index);
            }
        }
    }
}
