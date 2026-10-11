using System.Runtime.CompilerServices;
#if !FIXEDPOINT_STANDALONE
using Unity.Burst.Intrinsics;
#endif

namespace LegendaryTools.FixedPoint
{
    /// <summary>Unsigned wide arithmetic with no System.Int128/BigInteger dependency or heap allocation.</summary>
    internal readonly struct FixedUInt128
    {
        internal readonly ulong Lo;
        internal readonly ulong Hi;
        internal FixedUInt128(ulong lo, ulong hi = 0) { Lo = lo; Hi = hi; }
        internal bool IsZero => (Lo | Hi) == 0;
        internal int BitLength => Hi != 0 ? 64 + Bits(Hi) : Bits(Lo);

        internal static int Bits(ulong value)
        {
            int bits = 0;
            if (value >= 1UL << 32) { value >>= 32; bits += 32; }
            if (value >= 1UL << 16) { value >>= 16; bits += 16; }
            if (value >= 1UL << 8) { value >>= 8; bits += 8; }
            if (value >= 1UL << 4) { value >>= 4; bits += 4; }
            if (value >= 1UL << 2) { value >>= 2; bits += 2; }
            if (value >= 1UL << 1) { value >>= 1; bits += 1; }
            return value != 0 ? bits + 1 : bits;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static FixedUInt128 Multiply(ulong a, ulong b, FixedArithmeticMode mode)
        {
#if !FIXEDPOINT_STANDALONE
            if (mode != FixedArithmeticMode.Portable)
            {
                ulong lo = Common.umul128(a, b, out ulong hi);
                return new FixedUInt128(lo, hi);
            }
#endif
            ulong a0 = (uint)a, a1 = a >> 32, b0 = (uint)b, b1 = b >> 32;
            ulong t = a0 * b0;
            ulong w0 = (uint)t;
            t = a1 * b0 + (t >> 32);
            ulong w1 = (uint)t, w2 = t >> 32;
            t = a0 * b1 + w1;
            return new FixedUInt128((t << 32) | w0, a1 * b1 + w2 + (t >> 32));
        }

        internal static FixedUInt128 Add(FixedUInt128 a, FixedUInt128 b, out bool overflow)
        {
            ulong lo = unchecked(a.Lo + b.Lo);
            ulong carry = lo < a.Lo ? 1UL : 0UL;
            ulong hi = unchecked(a.Hi + b.Hi);
            bool first = hi < a.Hi;
            ulong high = unchecked(hi + carry);
            overflow = first || high < hi;
            return new FixedUInt128(lo, high);
        }

        internal static FixedUInt128 Add(FixedUInt128 a, ulong b) => Add(a, new FixedUInt128(b), out _);
        internal static FixedUInt128 Subtract(FixedUInt128 a, FixedUInt128 b) =>
            new FixedUInt128(unchecked(a.Lo - b.Lo), unchecked(a.Hi - b.Hi - (a.Lo < b.Lo ? 1UL : 0UL)));
        internal static int Compare(FixedUInt128 a, FixedUInt128 b) => a.Hi < b.Hi ? -1 : a.Hi > b.Hi ? 1 : a.Lo < b.Lo ? -1 : a.Lo > b.Lo ? 1 : 0;
        internal bool GetBit(int bit) => bit < 64 ? ((Lo >> bit) & 1) != 0 : ((Hi >> (bit - 64)) & 1) != 0;

        internal FixedUInt128 ShiftLeft(int bits)
        {
            if (bits <= 0) return this;
            if (bits >= 128) return default;
            if (bits >= 64) return new FixedUInt128(0, Lo << (bits - 64));
            return new FixedUInt128(Lo << bits, (Hi << bits) | (Lo >> (64 - bits)));
        }
        internal FixedUInt128 ShiftRight(int bits)
        {
            if (bits <= 0) return this;
            if (bits >= 128) return default;
            if (bits >= 64) return new FixedUInt128(Hi >> (bits - 64));
            return new FixedUInt128((Lo >> bits) | (Hi << (64 - bits)), Hi >> bits);
        }

        internal static FixedUInt128 Divide(FixedUInt128 numerator, ulong denominator, out ulong remainder)
        {
            if (numerator.Hi == 0)
            {
                remainder = numerator.Lo % denominator;
                return new FixedUInt128(numerator.Lo / denominator);
            }
            // The remainder is < denominator. Its extra shift bit is handled explicitly for divisors > 2^63.
            ulong rem = 0, lo = 0, hi = 0;
            for (int bit = numerator.BitLength - 1; bit >= 0; bit--)
            {
                bool carry = (rem >> 63) != 0;
                rem = (rem << 1) | (numerator.GetBit(bit) ? 1UL : 0UL);
                if (carry || rem >= denominator)
                {
                    rem = unchecked(rem - denominator);
                    if (bit >= 64) hi |= 1UL << (bit - 64); else lo |= 1UL << bit;
                }
            }
            remainder = rem;
            return new FixedUInt128(lo, hi);
        }

        internal static FixedUInt128 RoundedDivide(FixedUInt128 numerator, ulong denominator, bool negative,
            FixedRounding rounding, out bool inexact)
        {
            FixedUInt128 q = Divide(numerator, denominator, out ulong rem);
            inexact = rem != 0;
            if (ShouldIncrement(q.Lo, rem, denominator, negative, rounding)) q = Add(q, 1);
            return q;
        }

        internal static bool ShouldIncrement(ulong quotient, ulong remainder, ulong divisor, bool negative, FixedRounding rounding)
        {
            if (remainder == 0) return false;
            if (rounding == FixedRounding.TowardPositiveInfinity) return !negative;
            if (rounding == FixedRounding.TowardNegativeInfinity) return negative;
            if (rounding == FixedRounding.TowardZero) return false;
            ulong complement = divisor - remainder;
            return remainder > complement || (remainder == complement &&
                (rounding == FixedRounding.AwayFromZero || (quotient & 1) != 0));
        }

        internal static ulong AddModulo(ulong a, ulong b, ulong modulus) => a >= modulus - b ? a - (modulus - b) : a + b;
        internal static ulong PowerOfTwoModulo(uint exponent, ulong modulus, FixedArithmeticMode mode)
        {
            ulong result = 1, factor = 2;
            while (exponent != 0)
            {
                if ((exponent & 1) != 0) { Divide(Multiply(result, factor, mode), modulus, out result); }
                exponent >>= 1;
                if (exponent != 0) { Divide(Multiply(factor, factor, mode), modulus, out factor); }
            }
            return result;
        }
    }
}
