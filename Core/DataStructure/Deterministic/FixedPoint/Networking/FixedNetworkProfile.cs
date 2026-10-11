namespace LegendaryTools.FixedPoint
{
    public enum FixedCodec : byte { VarInt, FixedWidth, BitPacked }

    /// <summary>A negotiated field schema. Bounds use raw units at WireDecimalPlaces.</summary>
    public readonly struct FixedNetworkProfile
    {
        public const byte Version = 1;
        public const int DescriptorBytes = 40;
        public readonly FixedSettings Settings;
        public readonly FixedKind Kind;
        public readonly FixedCodec Codec;
        public readonly int WireDecimalPlaces;
        public readonly bool AllowLossy;
        public readonly ulong LowerRaw;
        public readonly ulong UpperRaw;

        public FixedNetworkProfile(FixedSettings settings, FixedKind kind, FixedCodec codec = FixedCodec.VarInt,
            int wireDecimalPlaces = -1, bool allowLossy = false, ulong lowerRaw = 0, ulong upperRaw = 0)
        {
            Settings = settings; Kind = kind; Codec = codec; AllowLossy = allowLossy;
            int nativePrecision = kind < FixedKind.Signed64 ? settings.DecimalPlaces32 : settings.DecimalPlaces64;
            WireDecimalPlaces = wireDecimalPlaces < 0 ? nativePrecision : wireDecimalPlaces;
            LowerRaw = lowerRaw; UpperRaw = upperRaw;
        }

        internal FixedRuntimeState WireState => new FixedRuntimeState(new FixedSettings(
            Kind < FixedKind.Signed64 ? WireDecimalPlaces : Settings.DecimalPlaces32,
            Kind >= FixedKind.Signed64 ? WireDecimalPlaces : Settings.DecimalPlaces64,
            Settings.Rounding, Settings.Overflow, Settings.ZeroDivision, Settings.UnsignedNegative, Settings.MathProfile, Settings.ArithmeticMode));

        public bool IsValid
        {
            get
            {
                int precision = Kind < FixedKind.Signed64 ? Settings.DecimalPlaces32 : Settings.DecimalPlaces64;
                if (!Settings.IsValid || Kind > FixedKind.Unsigned64 || Codec > FixedCodec.BitPacked ||
                    WireDecimalPlaces < 0 || WireDecimalPlaces > precision || WireDecimalPlaces < precision && !AllowLossy) return false;
                if (Codec != FixedCodec.BitPacked) return LowerRaw == 0 && UpperRaw == 0;
                var f = new FixedFormat(Kind, WireState);
                return LowerRaw <= f.Mask && UpperRaw <= f.Mask && f.Decode(LowerRaw).IsFinite &&
                    f.Decode(UpperRaw).IsFinite && LowerRaw != f.NegativeZero && UpperRaw != f.NegativeZero &&
                    FixedKernel.Compare(LowerRaw, UpperRaw, Kind, WireState) <= 0;
            }
        }

        public int PackedBits
        {
            get
            {
                if (!IsValid || Codec != FixedCodec.BitPacked) return 0;
                var f = new FixedFormat(Kind, WireState);
                return FixedBigUInt.From(unchecked((UpperRaw - LowerRaw) & f.Mask) + 4).BitLength;
            }
        }

        public bool IsCompatible(FixedNetworkProfile other) => IsValid && other.IsValid && Settings.NumericEquals(other.Settings) &&
            Kind == other.Kind && Codec == other.Codec && WireDecimalPlaces == other.WireDecimalPlaces &&
            AllowLossy == other.AllowLossy && LowerRaw == other.LowerRaw && UpperRaw == other.UpperRaw;
    }

    /// <summary>Caller-owned buffer and bit cursor. Bit-packed fields can share a byte.</summary>
    public unsafe struct FixedBufferWriter
    {
        internal readonly byte* Buffer;
        public readonly int CapacityBytes;
        public long BitPosition;
        public int BytesWritten => (int)((BitPosition + 7) / 8);
        public FixedBufferWriter(byte* buffer, int capacityBytes, long bitPosition = 0)
        { Buffer = buffer; CapacityBytes = capacityBytes; BitPosition = bitPosition; }
        internal bool Has(long bits) => Buffer != null && CapacityBytes >= 0 && BitPosition >= 0 && bits >= 0 && BitPosition <= (long)CapacityBytes * 8 - bits;
        internal void WriteBits(ulong value, int count)
        {
            for (int i = 0; i < count; i++)
            {
                long bit = BitPosition + i; int index = (int)(bit >> 3); byte mask = (byte)(1 << (int)(bit & 7));
                Buffer[index] = (byte)((Buffer[index] & ~mask) | (((value >> i) & 1) != 0 ? mask : 0));
            }
            BitPosition += count;
        }
    }

    public unsafe struct FixedBufferReader
    {
        internal readonly byte* Buffer;
        public readonly int LengthBytes;
        public long BitPosition;
        public FixedBufferReader(byte* buffer, int lengthBytes, long bitPosition = 0)
        { Buffer = buffer; LengthBytes = lengthBytes; BitPosition = bitPosition; }
        internal bool Has(long bits) => Buffer != null && LengthBytes >= 0 && BitPosition >= 0 && bits >= 0 && BitPosition <= (long)LengthBytes * 8 - bits;
        internal ulong ReadBits(int count)
        {
            ulong value = 0;
            for (int i = 0; i < count; i++)
            { long bit = BitPosition + i; if ((Buffer[(int)(bit >> 3)] & (1 << (int)(bit & 7))) != 0) value |= 1UL << i; }
            BitPosition += count; return value;
        }
    }
}
