using System;
using UnityEngine;

namespace LegendaryTools
{
    public readonly struct SpatialHashGridSettings : IEquatable<SpatialHashGridSettings>
    {
        public readonly Bounds WorldBounds;
        public readonly float BaseCellSize;
        public readonly int LevelCount;
        public readonly SpatialHashGridDimension Dimension;
        public readonly int MaxCellsPerAxis;
        public readonly int MaxCellsPerObject;

        public SpatialHashGridSettings(
            Bounds worldBounds,
            float baseCellSize,
            int levelCount,
            SpatialHashGridDimension dimension = SpatialHashGridDimension.XYZ3D,
            int maxCellsPerAxis = 4,
            int maxCellsPerObject = 64)
        {
            WorldBounds = worldBounds;
            BaseCellSize = Mathf.Max(0.0001f, baseCellSize);
            LevelCount = Mathf.Max(1, levelCount);
            Dimension = dimension;
            MaxCellsPerAxis = Mathf.Max(1, maxCellsPerAxis);
            MaxCellsPerObject = Mathf.Max(1, maxCellsPerObject);
        }

        public bool Equals(SpatialHashGridSettings other)
        {
            return WorldBounds.Equals(other.WorldBounds) &&
                   Mathf.Approximately(BaseCellSize, other.BaseCellSize) &&
                   LevelCount == other.LevelCount &&
                   Dimension == other.Dimension &&
                   MaxCellsPerAxis == other.MaxCellsPerAxis &&
                   MaxCellsPerObject == other.MaxCellsPerObject;
        }

        public override bool Equals(object obj)
        {
            return obj is SpatialHashGridSettings other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = WorldBounds.GetHashCode();
                hashCode = (hashCode * 397) ^ BaseCellSize.GetHashCode();
                hashCode = (hashCode * 397) ^ LevelCount;
                hashCode = (hashCode * 397) ^ (int)Dimension;
                hashCode = (hashCode * 397) ^ MaxCellsPerAxis;
                hashCode = (hashCode * 397) ^ MaxCellsPerObject;
                return hashCode;
            }
        }
    }
}
