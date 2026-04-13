using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools
{
    public sealed class SpatialHashGridSystem<T> : IDisposable where T : class, ISpatialHashGridItem
    {
        private readonly Dictionary<int, T> itemsById = new();
        private readonly List<T> allItems = new();
        private readonly List<T> dynamicItems = new();
        private HierarchicalSparseSpatialHashGrid<T> grid;
        private SpatialHashGridSettings settings;

        public SpatialHashGridSystem(
            SpatialHashGridSettings settings,
            SpatialHashDynamicUpdateMode dynamicUpdateMode = SpatialHashDynamicUpdateMode.FullDynamicRebuild)
        {
            this.settings = settings;
            DynamicUpdateMode = dynamicUpdateMode;
            grid = CreateGrid(settings);
        }

        public int Count => itemsById.Count;
        public SpatialHashGridSettings Settings => settings;
        public SpatialHashDynamicUpdateMode DynamicUpdateMode { get; set; }
        public HierarchicalSparseSpatialHashGrid<T> Grid => grid;

        public bool Register(T item, out T conflictingItem)
        {
            conflictingItem = null;
            if (!IsValid(item))
            {
                return false;
            }

            if (itemsById.TryGetValue(item.StableId, out T existing) && !ReferenceEquals(existing, item))
            {
                conflictingItem = existing;
                return false;
            }

            if (itemsById.ContainsKey(item.StableId))
            {
                return Refresh(item);
            }

            if (!TryUpsert(item, out _))
            {
                return false;
            }

            itemsById.Add(item.StableId, item);
            InsertSorted(allItems, item);
            SyncDynamicList(item);
            return true;
        }

        public bool Unregister(T item)
        {
            if (item == null || item.StableId == 0)
            {
                return false;
            }

            if (!itemsById.Remove(item.StableId))
            {
                return false;
            }

            RemoveSorted(allItems, item);
            RemoveSorted(dynamicItems, item);
            return grid.Remove(item.StableId);
        }

        public bool Refresh(T item)
        {
            return Refresh(item, out _);
        }

        public bool Refresh(T item, out bool changed)
        {
            if (!IsValid(item))
            {
                changed = false;
                return false;
            }

            if (!itemsById.TryGetValue(item.StableId, out T trackedItem) || !ReferenceEquals(trackedItem, item))
            {
                changed = false;
                return false;
            }

            SyncDynamicList(item);
            return TryUpsert(item, out changed);
        }

        private bool TryUpsert(T item, out bool changed)
        {
            if (!item.TryGetWorldBounds(settings.Dimension, out Bounds bounds))
            {
                changed = grid.Remove(item.StableId);
                return false;
            }

            SpatialHashItemMetadata metadata = new(item.IsDynamic, item.Layer, item.UserFlags);
            return grid.Upsert(item.StableId, bounds, item, metadata, out changed);
        }

        public void RefreshDynamic()
        {
            if (DynamicUpdateMode == SpatialHashDynamicUpdateMode.FullDynamicRebuild)
            {
                grid.ClearDynamic();
                for (int i = dynamicItems.Count - 1; i >= 0; i--)
                {
                    T item = dynamicItems[i];
                    if (item != null)
                    {
                        Refresh(item, out _);
                    }
                }

                grid.RebuildDynamicIndex();
                return;
            }

            bool changed = false;
            for (int i = dynamicItems.Count - 1; i >= 0; i--)
            {
                T item = dynamicItems[i];
                if (item != null)
                {
                    Refresh(item, out bool itemChanged);
                    changed |= itemChanged;
                }
            }

            if (changed)
            {
                grid.RebuildDynamicIndex();
            }
        }

        public void Rebuild(SpatialHashGridSettings newSettings)
        {
            settings = newSettings;
            grid.Dispose();
            grid = CreateGrid(settings);

            for (int i = 0; i < allItems.Count; i++)
            {
                T item = allItems[i];
                if (item != null)
                {
                    Refresh(item);
                }
            }

            grid.RebuildStaticIndex();
            grid.RebuildDynamicIndex();
        }

        public int Query(Bounds bounds, List<T> results, SpatialHashQueryFilter filter = default)
        {
            return grid.Query(bounds, results, filter);
        }

        public int QueryBroadphase(Bounds bounds, List<T> results, SpatialHashQueryFilter filter = default)
        {
            return grid.QueryBroadphase(bounds, results, filter);
        }

        public int QueryIds(Bounds bounds, List<int> results, SpatialHashQueryFilter filter = default)
        {
            return grid.QueryIds(bounds, results, filter);
        }

        public int QueryIdsBroadphase(Bounds bounds, List<int> results, SpatialHashQueryFilter filter = default)
        {
            return grid.QueryIdsBroadphase(bounds, results, filter);
        }

        public void Dispose()
        {
            grid?.Dispose();
            grid = null;
            itemsById.Clear();
            allItems.Clear();
            dynamicItems.Clear();
        }

        private static HierarchicalSparseSpatialHashGrid<T> CreateGrid(SpatialHashGridSettings settings)
        {
            return new HierarchicalSparseSpatialHashGrid<T>(
                settings.WorldBounds,
                settings.BaseCellSize,
                settings.LevelCount,
                settings.Dimension,
                settings.MaxCellsPerAxis,
                settings.MaxCellsPerObject);
        }

        private void SyncDynamicList(T item)
        {
            if (item.IsDynamic)
            {
                if (!Contains(dynamicItems, item))
                {
                    InsertSorted(dynamicItems, item);
                }

                return;
            }

            RemoveSorted(dynamicItems, item);
        }

        private static bool IsValid(T item)
        {
            return item != null && item.StableId != 0;
        }

        private static bool Contains(List<T> values, T item)
        {
            return values.BinarySearch(item, SpatialHashGridItemComparer.Instance) >= 0;
        }

        private static void InsertSorted(List<T> values, T item)
        {
            int index = values.BinarySearch(item, SpatialHashGridItemComparer.Instance);
            if (index >= 0)
            {
                return;
            }

            values.Insert(~index, item);
        }

        private static void RemoveSorted(List<T> values, T item)
        {
            int index = values.BinarySearch(item, SpatialHashGridItemComparer.Instance);
            if (index >= 0)
            {
                values.RemoveAt(index);
            }
        }

        private sealed class SpatialHashGridItemComparer : IComparer<T>
        {
            public static readonly SpatialHashGridItemComparer Instance = new();

            public int Compare(T x, T y)
            {
                if (ReferenceEquals(x, y))
                {
                    return 0;
                }

                if (x == null)
                {
                    return -1;
                }

                if (y == null)
                {
                    return 1;
                }

                return x.StableId.CompareTo(y.StableId);
            }
        }
    }
}
