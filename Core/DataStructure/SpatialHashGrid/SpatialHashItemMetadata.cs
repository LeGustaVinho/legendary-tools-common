namespace LegendaryTools
{
    public readonly struct SpatialHashItemMetadata
    {
        public static readonly SpatialHashItemMetadata Default = new(true, 0, 0u);

        public readonly bool IsDynamic;
        public readonly int Layer;
        public readonly uint UserFlags;

        public SpatialHashItemMetadata(bool isDynamic, int layer, uint userFlags)
        {
            IsDynamic = isDynamic;
            Layer = layer;
            UserFlags = userFlags;
        }
    }
}
