namespace LegendaryTools.FixedPoint
{
    /// <summary>Negotiated storage schema. First/Second mean Min/Max or Center/Extents according to Representation.</summary>
    public readonly struct FixedBoundsNetworkProfile
    {
        public const byte Version = 1;
        public readonly FixedBoundsRepresentation Representation;
        public readonly FixedVectorNetworkProfile First, Second;
        public int Dimensions => First.Dimensions;
        public int DescriptorBytes => 8 + First.DescriptorBytes + Second.DescriptorBytes;
        public int PackedBits => IsValid && First.X.Codec == FixedCodec.BitPacked ? First.PackedBits + Second.PackedBits : 0;
        public FixedBoundsNetworkProfile(FixedBoundsRepresentation representation, FixedVectorNetworkProfile first, FixedVectorNetworkProfile second)
        { Representation = representation; First = first; Second = second; }
        public FixedBoundsNetworkProfile(FixedVectorNetworkProfile first, FixedVectorNetworkProfile second)
            : this(FixedBoundsRuntime.Settings.Representation, first, second) { }
        public FixedBoundsNetworkProfile(FixedVectorNetworkProfile fields) : this(fields, fields) { }
        public bool IsValid => Representation <= FixedBoundsRepresentation.CenterExtents && First.IsValid && Second.IsValid &&
            First.Dimensions == Second.Dimensions && First.X.Settings.NumericEquals(Second.X.Settings) && First.X.Codec == Second.X.Codec;
        public bool IsCompatible(FixedBoundsNetworkProfile other) => IsValid && other.IsValid && Representation == other.Representation &&
            First.IsCompatible(other.First) && Second.IsCompatible(other.Second);
    }
}
