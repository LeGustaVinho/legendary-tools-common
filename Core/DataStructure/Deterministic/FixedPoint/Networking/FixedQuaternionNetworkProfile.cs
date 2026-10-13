namespace LegendaryTools.FixedPoint
{
    public enum FixedQuaternionCodec : byte { Components, SmallestThree }

    /// <summary>Negotiated rotation encoding. Individual values carry no runtime configuration.</summary>
    public readonly struct FixedQuaternionNetworkProfile
    {
        public const ushort Version = 1;
        public const int MaxDescriptorBytes = 172;
        public readonly FixedSettings Settings;
        public readonly FixedQuaternionCodec Codec;
        public readonly FixedNetworkProfile X, Y, Z, W;
        public readonly byte TotalBits;
        public readonly bool AllowLossy;
        public FixedQuaternionNetworkProfile(FixedSettings settings, FixedCodec componentCodec = FixedCodec.FixedWidth,
            int wireDecimalPlaces = -1, bool allowLossy = false, ulong lowerRaw = 0, ulong upperRaw = 0)
            : this(new FixedNetworkProfile(settings, FixedKind.Signed64, componentCodec, wireDecimalPlaces, allowLossy, lowerRaw, upperRaw)) { }
        public FixedQuaternionNetworkProfile(FixedNetworkProfile component) : this(component, component, component, component) { }
        public FixedQuaternionNetworkProfile(FixedNetworkProfile x, FixedNetworkProfile y, FixedNetworkProfile z, FixedNetworkProfile w)
            : this(x, y, z, w, x.Settings) { }
        internal FixedQuaternionNetworkProfile(FixedNetworkProfile x, FixedNetworkProfile y, FixedNetworkProfile z, FixedNetworkProfile w, FixedSettings settings)
        { Settings = settings; Codec = FixedQuaternionCodec.Components; X = x; Y = y; Z = z; W = w; TotalBits = 0; AllowLossy = x.AllowLossy || y.AllowLossy || z.AllowLossy || w.AllowLossy; }
        private FixedQuaternionNetworkProfile(FixedSettings settings, int totalBits, bool allowLossy)
        {
            Settings = settings; Codec = FixedQuaternionCodec.SmallestThree;
            X = Y = Z = W = new FixedNetworkProfile(settings, FixedKind.Signed64, FixedCodec.FixedWidth);
            TotalBits = totalBits == 32 || totalBits == 48 || totalBits == 64 ? (byte)totalBits : (byte)0; AllowLossy = allowLossy;
        }
        public static FixedQuaternionNetworkProfile SmallestThree(FixedSettings settings, int totalBits = 48, bool allowLossy = false)
            => new FixedQuaternionNetworkProfile(settings, totalBits, allowLossy);
        internal FixedNetworkProfile Get(int index) => index == 0 ? X : index == 1 ? Y : index == 2 ? Z : W;
        public bool IsValid => Settings.IsValid && Valid(X) && Settings.NumericEquals(X.Settings) &&
            (Codec == FixedQuaternionCodec.SmallestThree ? AllowLossy && (TotalBits == 32 || TotalBits == 48 || TotalBits == 64) :
             Codec == FixedQuaternionCodec.Components && TotalBits == 0 && Valid(Y) && Valid(Z) && Valid(W) && Same(Y) && Same(Z) && Same(W));
        private static bool Valid(FixedNetworkProfile value) => value.IsValid && value.Kind == FixedKind.Signed64;
        private bool Same(FixedNetworkProfile value) => value.Codec == X.Codec && value.Settings.NumericEquals(Settings);
        public int ComponentBits => Codec == FixedQuaternionCodec.SmallestThree ? TotalBits == 32 ? 10 : TotalBits == 48 ? 15 : 20 : 0;
        public int DescriptorBytes => 12 + FixedNetworkProfile.DescriptorBytes * (Codec == FixedQuaternionCodec.Components ? 4 : 1);
        public int PackedBits => !IsValid ? 0 : Codec == FixedQuaternionCodec.SmallestThree ? TotalBits : X.Codec == FixedCodec.BitPacked ? X.PackedBits + Y.PackedBits + Z.PackedBits + W.PackedBits : 0;
        public int MaxPayloadBytes => !IsValid ? 0 : Codec == FixedQuaternionCodec.SmallestThree ? TotalBits / 8 : X.Codec == FixedCodec.VarInt ? 40 : X.Codec == FixedCodec.FixedWidth ? 32 : (PackedBits + 7) / 8;
        public bool IsCompatible(FixedQuaternionNetworkProfile other) => IsValid && other.IsValid && Settings.QuaternionNumericEquals(other.Settings) && Codec == other.Codec && TotalBits == other.TotalBits && AllowLossy == other.AllowLossy &&
            (Codec == FixedQuaternionCodec.SmallestThree || X.IsCompatible(other.X) && Y.IsCompatible(other.Y) && Z.IsCompatible(other.Z) && W.IsCompatible(other.W));
        /// <summary>Includes the rotation algorithm and encoding. Arithmetic/execution backends are excluded.</summary>
        public ulong SchemaFingerprint
        {
            get
            {
                ulong hash = Mix(14695981039346656037UL, Settings.QuaternionFingerprint);
                hash = Mix(hash, Version); hash = Mix(hash, (byte)Codec); hash = Mix(hash, TotalBits); hash = Mix(hash, AllowLossy ? 1UL : 0UL);
                if (Codec == FixedQuaternionCodec.Components)
                    for (int i = 0; i < 4; i++) { var c = Get(i); hash = Mix(hash, (byte)c.Codec); hash = Mix(hash, (uint)c.WireDecimalPlaces); hash = Mix(hash, c.AllowLossy ? 1UL : 0UL); hash = Mix(hash, c.LowerRaw); hash = Mix(hash, c.UpperRaw); }
                return hash;
            }
        }
        private static ulong Mix(ulong hash, ulong value)
        { for (int i = 0; i < 8; i++) { hash = unchecked((hash ^ (byte)value) * 1099511628211UL); value >>= 8; } return hash; }
    }
}
