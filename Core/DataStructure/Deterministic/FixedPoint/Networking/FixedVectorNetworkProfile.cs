namespace LegendaryTools.FixedPoint
{
    /// <summary>A negotiated vector schema. Coordinates share settings and codec, with independent bounds/precision.</summary>
    public readonly struct FixedVectorNetworkProfile
    {
        public const byte Version = 1;
        public readonly byte Dimensions;
        public readonly FixedNetworkProfile X, Y, Z;
        public FixedVectorNetworkProfile(FixedNetworkProfile x, FixedNetworkProfile y)
        { Dimensions = 2; X = x; Y = y; Z = default; }
        public FixedVectorNetworkProfile(FixedNetworkProfile x, FixedNetworkProfile y, FixedNetworkProfile z)
        { Dimensions = 3; X = x; Y = y; Z = z; }
        public FixedVectorNetworkProfile(FixedNetworkProfile component, int dimensions)
        { Dimensions = dimensions == 2 || dimensions == 3 ? (byte)dimensions : (byte)0; X = Y = component; Z = dimensions == 3 ? component : default; }
        public int DescriptorBytes => 8 + Dimensions * FixedNetworkProfile.DescriptorBytes;
        public int PackedBits => IsValid && X.Codec == FixedCodec.BitPacked ? X.PackedBits + Y.PackedBits + (Dimensions == 3 ? Z.PackedBits : 0) : 0;
        internal FixedNetworkProfile Get(int index) => index == 0 ? X : index == 1 ? Y : Z;
        public bool IsValid => (Dimensions == 2 || Dimensions == 3) && ValidComponent(X) && ValidComponent(Y) &&
            SameConfiguration(Y) && (Dimensions == 2 || ValidComponent(Z) && SameConfiguration(Z));
        private static bool ValidComponent(FixedNetworkProfile p) => p.IsValid && p.Kind == FixedKind.Signed64;
        private bool SameConfiguration(FixedNetworkProfile p) => p.Settings.NumericEquals(X.Settings) && p.Codec == X.Codec;
        public bool IsCompatible(FixedVectorNetworkProfile other) => IsValid && other.IsValid && Dimensions == other.Dimensions &&
            X.IsCompatible(other.X) && Y.IsCompatible(other.Y) && (Dimensions == 2 || Z.IsCompatible(other.Z));
    }
}
