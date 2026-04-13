namespace LegendaryTools
{
    public readonly struct SpatialHashQueryFilter
    {
        public static readonly SpatialHashQueryFilter Default = new(~0, 0u, 0u);

        public readonly int LayerMask;
        public readonly uint RequiredFlags;
        public readonly uint ExcludedFlags;

        public SpatialHashQueryFilter(int layerMask, uint requiredFlags, uint excludedFlags)
        {
            LayerMask = layerMask;
            RequiredFlags = requiredFlags;
            ExcludedFlags = excludedFlags;
        }
    }
}
