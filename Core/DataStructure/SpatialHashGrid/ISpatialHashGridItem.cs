using UnityEngine;

namespace LegendaryTools
{
    public interface ISpatialHashGridItem
    {
        int StableId { get; }
        bool IsDynamic { get; }
        int Layer { get; }
        uint UserFlags { get; }

        bool TryGetWorldBounds(SpatialHashGridDimension dimension, out Bounds bounds);
    }
}
