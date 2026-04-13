using UnityEngine;

namespace LegendaryTools
{
    public readonly struct SpatialHashGridEntryInfo
    {
        public readonly int StableId;
        public readonly int Level;
        public readonly int OccupiedCellCount;
        public readonly bool IsDynamic;
        public readonly bool IsOverflow;
        public readonly Bounds Bounds;

        public SpatialHashGridEntryInfo(
            int stableId,
            int level,
            int occupiedCellCount,
            bool isDynamic,
            bool isOverflow,
            Bounds bounds)
        {
            StableId = stableId;
            Level = level;
            OccupiedCellCount = occupiedCellCount;
            IsDynamic = isDynamic;
            IsOverflow = isOverflow;
            Bounds = bounds;
        }
    }
}
