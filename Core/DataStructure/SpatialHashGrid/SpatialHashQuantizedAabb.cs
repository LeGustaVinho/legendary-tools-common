namespace LegendaryTools
{
    internal readonly struct SpatialHashQuantizedAabb : System.IEquatable<SpatialHashQuantizedAabb>
    {
        public readonly int MinX;
        public readonly int MinY;
        public readonly int MinZ;
        public readonly int MaxX;
        public readonly int MaxY;
        public readonly int MaxZ;

        public SpatialHashQuantizedAabb(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
        {
            MinX = minX;
            MinY = minY;
            MinZ = minZ;
            MaxX = maxX;
            MaxY = maxY;
            MaxZ = maxZ;
        }

        public bool Intersects(SpatialHashQuantizedAabb other, SpatialHashGridDimension dimension)
        {
            return Overlaps(MinX, MaxX, other.MinX, other.MaxX) &&
                   (!UsesAxis(dimension, 1) || Overlaps(MinY, MaxY, other.MinY, other.MaxY)) &&
                   (!UsesAxis(dimension, 2) || Overlaps(MinZ, MaxZ, other.MinZ, other.MaxZ));
        }

        public bool Equals(SpatialHashQuantizedAabb other)
        {
            return MinX == other.MinX &&
                   MinY == other.MinY &&
                   MinZ == other.MinZ &&
                   MaxX == other.MaxX &&
                   MaxY == other.MaxY &&
                   MaxZ == other.MaxZ;
        }

        public override bool Equals(object obj)
        {
            return obj is SpatialHashQuantizedAabb other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = MinX;
                hashCode = (hashCode * 397) ^ MinY;
                hashCode = (hashCode * 397) ^ MinZ;
                hashCode = (hashCode * 397) ^ MaxX;
                hashCode = (hashCode * 397) ^ MaxY;
                hashCode = (hashCode * 397) ^ MaxZ;
                return hashCode;
            }
        }

        public int GetLargestAxisCellCount(int level, SpatialHashGridDimension dimension)
        {
            int count = GetAxisCellCount(MinX, MaxX, level);
            if (UsesAxis(dimension, 1))
            {
                count = System.Math.Max(count, GetAxisCellCount(MinY, MaxY, level));
            }

            if (UsesAxis(dimension, 2))
            {
                count = System.Math.Max(count, GetAxisCellCount(MinZ, MaxZ, level));
            }

            return count;
        }

        public int GetOccupiedCellCount(int level, SpatialHashGridDimension dimension)
        {
            int x = GetAxisCellCount(MinX, MaxX, level);
            int y = UsesAxis(dimension, 1) ? GetAxisCellCount(MinY, MaxY, level) : 1;
            int z = UsesAxis(dimension, 2) ? GetAxisCellCount(MinZ, MaxZ, level) : 1;
            return x * y * z;
        }

        public void GetCellRange(
            int level,
            SpatialHashGridDimension dimension,
            out int minX,
            out int maxX,
            out int minY,
            out int maxY,
            out int minZ,
            out int maxZ)
        {
            minX = ShiftDown(MinX, level);
            maxX = ShiftDown(MaxX, level);
            minY = UsesAxis(dimension, 1) ? ShiftDown(MinY, level) : 0;
            maxY = UsesAxis(dimension, 1) ? ShiftDown(MaxY, level) : 0;
            minZ = UsesAxis(dimension, 2) ? ShiftDown(MinZ, level) : 0;
            maxZ = UsesAxis(dimension, 2) ? ShiftDown(MaxZ, level) : 0;
        }

        private static int GetAxisCellCount(int minInclusive, int maxInclusive, int level)
        {
            return (ShiftDown(maxInclusive, level) - ShiftDown(minInclusive, level)) + 1;
        }

        private static int ShiftDown(int value, int level)
        {
            return value >> level;
        }

        private static bool Overlaps(int minA, int maxA, int minB, int maxB)
        {
            return minA <= maxB && maxA >= minB;
        }

        private static bool UsesAxis(SpatialHashGridDimension dimension, int axis)
        {
            return dimension switch
            {
                SpatialHashGridDimension.XY2D => axis != 2,
                SpatialHashGridDimension.XZ2D => axis != 1,
                _ => true
            };
        }
    }
}
