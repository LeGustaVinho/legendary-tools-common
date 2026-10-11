using System.Runtime.InteropServices;

namespace LegendaryTools.FixedPoint
{
    internal static class FixedIeee
    {
        [StructLayout(LayoutKind.Explicit)]
        private struct SingleBits { [FieldOffset(0)] internal float Value; [FieldOffset(0)] internal uint Bits; }
        [StructLayout(LayoutKind.Explicit)]
        private struct DoubleBits { [FieldOffset(0)] internal double Value; [FieldOffset(0)] internal ulong Bits; }

        internal static uint Bits(float value) => new SingleBits { Value = value }.Bits;
        internal static ulong Bits(double value) => new DoubleBits { Value = value }.Bits;
        internal static float Single(uint bits) => new SingleBits { Bits = bits }.Value;
        internal static double Double(ulong bits) => new DoubleBits { Bits = bits }.Value;

        internal static ulong From(float value, FixedKind kind, FixedRuntimeState state, out FixedStatus status)
        {
            uint bits = Bits(value);
            int exponent = (int)((bits >> 23) & 255);
            ulong mantissa = bits & 0x7fffff;
            bool negative = (bits >> 31) != 0;
            return FromParts(mantissa | (exponent == 0 ? 0UL : 1UL << 23),
                exponent == 0 ? -149 : exponent - 150, negative,
                exponent == 255, mantissa != 0, kind, state, out status);
        }

        internal static ulong From(double value, FixedKind kind, FixedRuntimeState state, out FixedStatus status)
        {
            ulong bits = Bits(value);
            int exponent = (int)((bits >> 52) & 2047);
            ulong mantissa = bits & 0xfffffffffffffUL;
            return FromParts(mantissa | (exponent == 0 ? 0UL : 1UL << 52),
                exponent == 0 ? -1074 : exponent - 1075, (bits >> 63) != 0,
                exponent == 2047, mantissa != 0, kind, state, out status);
        }

        private static ulong FromParts(ulong mantissa, int shift, bool negative, bool special, bool nan,
            FixedKind kind, FixedRuntimeState state, out FixedStatus status)
        {
            FixedFormat f = new FixedFormat(kind, state);
            status = state.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            if (!state.IsInitialized) return f.NaN;
            if (special) return nan ? f.NaN : f.Infinity(negative);
            if (mantissa == 0) return f.Finite(0, negative);
            FixedUInt128 scaled = FixedUInt128.Multiply(mantissa, f.Scale, state.Settings.ArithmeticMode);
            if (shift >= 0)
            {
                if (scaled.BitLength + shift <= 128)
                    return FixedKernel.Pack(scaled.ShiftLeft(shift), negative, f, state.Settings, ref status);
                FixedUInt128.Divide(scaled, f.FiniteCount, out ulong rem);
                ulong factor = FixedUInt128.PowerOfTwoModulo((uint)shift, f.FiniteCount, state.Settings.ArithmeticMode);
                FixedUInt128.Divide(FixedUInt128.Multiply(rem, factor, state.Settings.ArithmeticMode), f.FiniteCount, out rem);
                return FixedKernel.Oversized(rem, negative, f, state.Settings, ref status);
            }
            int right = -shift;
            FixedUInt128 q = scaled.ShiftRight(right);
            FixedUInt128 remBits = right >= 128 ? scaled : FixedUInt128.Subtract(scaled, q.ShiftLeft(right));
            if (!remBits.IsZero)
            {
                status |= FixedStatus.Inexact;
                bool increment = state.Settings.Rounding == FixedRounding.TowardPositiveInfinity && !negative ||
                    state.Settings.Rounding == FixedRounding.TowardNegativeInfinity && negative;
                if ((state.Settings.Rounding == FixedRounding.ToEven || state.Settings.Rounding == FixedRounding.AwayFromZero) && right <= 128)
                {
                    FixedUInt128 half = new FixedUInt128(1).ShiftLeft(right - 1);
                    int cmp = FixedUInt128.Compare(remBits, half);
                    increment = cmp > 0 || cmp == 0 && (state.Settings.Rounding == FixedRounding.AwayFromZero || (q.Lo & 1) != 0);
                }
                if (increment) q = FixedUInt128.Add(q, 1);
            }
            return FixedKernel.Pack(q, negative, f, state.Settings, ref status);
        }

        internal static float ToSingle(ulong raw, FixedKind kind, FixedRuntimeState state) => Single((uint)ToBits(raw, kind, state, 24, 127, 23));
        internal static double ToDouble(ulong raw, FixedKind kind, FixedRuntimeState state) => Double(ToBits(raw, kind, state, 53, 1023, 52));

        private static ulong ToBits(ulong raw, FixedKind kind, FixedRuntimeState state, int precision, int bias, int fractionBits)
        {
            FixedFormat f = new FixedFormat(kind, state);
            FixedDecoded value = f.Decode(raw);
            ulong sign = value.Negative ? (fractionBits == 23 ? 1UL << 31 : 1UL << 63) : 0;
            ulong infinity = fractionBits == 23 ? 0x7f800000UL : 0x7ff0000000000000UL;
            if (!state.IsInitialized || value.IsNaN) return infinity | (1UL << (fractionBits - 1));
            if (!value.IsFinite) return sign | infinity;
            if (value.IsZero) return sign;
            int exponent = FixedUInt128.Bits(value.Magnitude) - FixedUInt128.Bits(f.Scale);
            FixedUInt128 numerator = new FixedUInt128(value.Magnitude), denominator = new FixedUInt128(f.Scale);
            int cmp = exponent >= 0 ? FixedUInt128.Compare(numerator, denominator.ShiftLeft(exponent)) :
                FixedUInt128.Compare(numerator.ShiftLeft(-exponent), denominator);
            if (cmp < 0) exponent--;
            int shift = precision - 1 - exponent;
            FixedUInt128 q;
            if (shift >= 0)
                q = FixedUInt128.RoundedDivide(numerator.ShiftLeft(shift), f.Scale, false, FixedRounding.ToEven, out _);
            else
                q = FixedUInt128.RoundedDivide(numerator, f.Scale << -shift, false, FixedRounding.ToEven, out _);
            ulong significand = q.Lo;
            if (significand == (1UL << precision)) { significand >>= 1; exponent++; }
            return sign | ((ulong)(exponent + bias) << fractionBits) | (significand - (1UL << (precision - 1)));
        }
    }
}
