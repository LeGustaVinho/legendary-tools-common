namespace LegendaryTools.FixedPoint
{
    internal enum FixedClass : byte { Finite, PositiveInfinity, NegativeInfinity, NaN }

    internal readonly struct FixedDecoded
    {
        internal readonly ulong Magnitude;
        internal readonly bool Negative;
        internal readonly FixedClass Class;
        internal FixedDecoded(ulong magnitude, bool negative = false, FixedClass @class = FixedClass.Finite)
        { Magnitude = magnitude; Negative = negative; Class = @class; }
        internal bool IsFinite => Class == FixedClass.Finite;
        internal bool IsZero => IsFinite && Magnitude == 0;
        internal bool IsNaN => Class == FixedClass.NaN;
        internal FixedDecoded Negated => new FixedDecoded(Magnitude, !Negative,
            Class == FixedClass.PositiveInfinity ? FixedClass.NegativeInfinity :
            Class == FixedClass.NegativeInfinity ? FixedClass.PositiveInfinity : Class);
    }

    internal readonly struct FixedFormat
    {
        internal readonly FixedKind Kind;
        internal readonly bool Unsigned;
        internal readonly int Bits;
        internal readonly ulong Mask;
        internal readonly ulong Scale;
        internal readonly int DecimalPlaces;
        internal readonly ulong NaN;
        internal readonly ulong PositiveInfinity;
        internal readonly ulong NegativeInfinity;
        internal readonly ulong NegativeZero;
        internal readonly ulong MaxPositive;
        internal readonly ulong MaxNegative;
        internal readonly ulong FiniteCount;

        internal FixedFormat(FixedKind kind, FixedRuntimeState state)
        {
            Kind = kind;
            Unsigned = kind == FixedKind.Unsigned32 || kind == FixedKind.Unsigned64;
            Bits = kind == FixedKind.Signed32 || kind == FixedKind.Unsigned32 ? 32 : 64;
            Mask = Bits == 32 ? uint.MaxValue : ulong.MaxValue;
            Scale = Bits == 32 ? state.Scale32 : state.Scale64;
            DecimalPlaces = Bits == 32 ? state.Settings.DecimalPlaces32 : state.Settings.DecimalPlaces64;
            FiniteCount = Mask - 3;
            if (Unsigned)
            {
                NaN = Mask; NegativeInfinity = Mask - 1; PositiveInfinity = Mask - 2; NegativeZero = Mask - 3;
                MaxPositive = Mask - 4; MaxNegative = 0;
            }
            else
            {
                ulong sign = 1UL << (Bits - 1);
                NaN = sign; NegativeInfinity = sign + 1; PositiveInfinity = sign - 1; NegativeZero = sign - 2;
                MaxPositive = sign - 3; MaxNegative = sign - 2;
            }
        }

        internal ulong MinValue => Unsigned ? 0 : unchecked(0UL - MaxNegative) & Mask;
        internal FixedDecoded Decode(ulong bits)
        {
            bits &= Mask;
            if (bits == NaN) return new FixedDecoded(0, false, FixedClass.NaN);
            if (bits == PositiveInfinity) return new FixedDecoded(0, false, FixedClass.PositiveInfinity);
            if (bits == NegativeInfinity) return new FixedDecoded(0, true, FixedClass.NegativeInfinity);
            if (bits == NegativeZero) return new FixedDecoded(0, true);
            bool negative = !Unsigned && (bits & (1UL << (Bits - 1))) != 0;
            return new FixedDecoded(negative ? unchecked(0UL - bits) & Mask : bits, negative);
        }
        internal ulong Finite(ulong magnitude, bool negative) => magnitude == 0 ? (negative ? NegativeZero : 0) :
            negative ? unchecked(0UL - magnitude) & Mask : magnitude;
        internal ulong Infinity(bool negative) => negative ? NegativeInfinity : PositiveInfinity;
    }
}
