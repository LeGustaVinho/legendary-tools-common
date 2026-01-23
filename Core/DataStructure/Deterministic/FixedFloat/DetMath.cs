using System;
using System.Numerics;

namespace DeterministicFixedPoint
{
    public static class DetMath
    {
        // -------------------------
        // 32-bit + generic helpers
        // -------------------------

        /// <summary>
        /// Round to nearest; ties away from zero. Integer-only.
        /// </summary>
        public static long RoundDivNearestAwayFromZero(long numerator, long denominator)
        {
            if (denominator == 0)
                throw new DivideByZeroException();

            if (denominator < 0)
            {
                numerator = -numerator;
                denominator = -denominator;
            }

            long q = numerator / denominator;
            long r = numerator % denominator;

            if (r == 0)
                return q;

            ulong absR = (ulong)(r < 0 ? -r : r);
            ulong den = (ulong)denominator;

            if (absR * 2UL >= den)
                q += numerator > 0 ? 1L : -1L;

            return q;
        }

        /// <summary>
        /// Round to nearest; ties up. Integer-only.
        /// </summary>
        public static ulong RoundDivNearestUp(ulong numerator, ulong denominator)
        {
            if (denominator == 0)
                throw new DivideByZeroException();

            ulong q = numerator / denominator;
            ulong r = numerator % denominator;

            if (r == 0)
                return q;

            if (r * 2UL >= denominator)
                q += 1UL;

            return q;
        }

        public static int ClampToInt(long v)
        {
            if (v > int.MaxValue) return int.MaxValue;
            if (v < int.MinValue) return int.MinValue;
            return (int)v;
        }

        public static uint ClampToUInt(ulong v)
        {
            if (v > uint.MaxValue) return uint.MaxValue;
            return (uint)v;
        }

        public static int WrapToInt(long v)
        {
            return unchecked((int)v);
        }

        public static uint WrapToUInt(ulong v)
        {
            return unchecked((uint)v);
        }

        // -------------------------
        // BigInteger helpers for 64-bit safety
        // -------------------------

        private static readonly BigInteger MaskU64 = (BigInteger.One << 64) - BigInteger.One;

        public static BigInteger RoundDivNearestAwayFromZero(BigInteger numerator, BigInteger denominator)
        {
            if (denominator.IsZero)
                throw new DivideByZeroException();

            // Normalize so denominator > 0.
            if (denominator.Sign < 0)
            {
                numerator = BigInteger.Negate(numerator);
                denominator = BigInteger.Negate(denominator);
            }

            BigInteger q = BigInteger.DivRem(numerator, denominator, out BigInteger r);
            if (r.IsZero)
                return q;

            BigInteger absR = BigInteger.Abs(r);
            BigInteger twoAbsR = absR << 1;

            // ties away from zero: if 2*|r| >= |d| => increment magnitude
            if (twoAbsR >= denominator) q += numerator.Sign >= 0 ? BigInteger.One : BigInteger.Negate(BigInteger.One);

            return q;
        }

        public static BigInteger RoundDivNearestUp(BigInteger numerator, BigInteger denominator)
        {
            if (denominator.IsZero)
                throw new DivideByZeroException();

            if (numerator.Sign < 0 || denominator.Sign < 0)
                throw new ArgumentException("RoundDivNearestUp expects non-negative inputs.");

            BigInteger q = BigInteger.DivRem(numerator, denominator, out BigInteger r);
            if (r.IsZero)
                return q;

            // ties up: if 2*r >= d => q++
            if (r << 1 >= denominator)
                q += BigInteger.One;

            return q;
        }

        public static long ClampToInt64(BigInteger v)
        {
            if (v > long.MaxValue) return long.MaxValue;
            if (v < long.MinValue) return long.MinValue;
            return (long)v;
        }

        public static ulong ClampToUInt64(BigInteger v)
        {
            if (v.Sign <= 0) return 0UL;
            if (v > ulong.MaxValue) return ulong.MaxValue;
            return (ulong)v;
        }

        public static long WrapToInt64(BigInteger v)
        {
            // Low 64 bits of two's complement.
            BigInteger low = v & MaskU64;
            ulong u = (ulong)low;
            return unchecked((long)u);
        }

        public static ulong WrapToUInt64(BigInteger v)
        {
            BigInteger low = v & MaskU64;
            return (ulong)low;
        }
    }
}