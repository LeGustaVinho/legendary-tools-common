using System;
using System.Globalization;
using System.Numerics;
using UnityEngine;

namespace DeterministicFixedPoint
{
    [Serializable]
    public struct DetS64 : IEquatable<DetS64>, IComparable<DetS64>
    {
        public const int Scale = 1000;

        [SerializeField] private long raw;

        public long Raw
        {
            get
            {
                DetConfig.Touch();
                return raw;
            }
        }

        // Avoid locking DetConfig during type initialization.
        public static readonly DetS64 Zero = new(0L);
        public static readonly DetS64 One = new(Scale);
        public static readonly DetS64 MinValue = new(long.MinValue);
        public static readonly DetS64 MaxValue = new(long.MaxValue);

        private DetS64(long rawValue)
        {
            raw = rawValue;
        }

        public static DetS64 FromRaw(long rawValue)
        {
            DetConfig.Touch();
            return new DetS64(rawValue);
        }

        public static DetS64 FromLong(long value)
        {
            DetConfig.Touch();
            BigInteger scaled = (BigInteger)value * Scale;
            return FromBigRawWithMode(scaled);
        }

        /// <summary>
        /// Authoring/UI only. Float is not allowed in simulation.
        /// Rounding: nearest; ties away from zero.
        /// </summary>
        public static DetS64 FromFloat(float value)
        {
            DetConfig.Touch();
            decimal d = (decimal)value;
            decimal scaled = d * Scale;

            BigInteger rounded = RoundDecimalAwayFromZeroToBigInteger(scaled);
            return FromBigRawWithMode(rounded);
        }

        public long ToIntFloor()
        {
            DetConfig.Touch();
            long r = raw;
            long q = r / Scale; // trunc toward zero
            long rem = r % Scale;
            if (r < 0 && rem != 0)
                q -= 1;
            return q;
        }

        public long ToIntRound()
        {
            DetConfig.Touch();
            BigInteger q = DetMath.RoundDivNearestAwayFromZero((BigInteger)raw, (BigInteger)Scale);
            // This is rounding the integer raw/Scale, so it always fits in long.
            return DetMath.ClampToInt64(q);
        }

        /// <summary>
        /// Debug/UI only.
        /// </summary>
        public float ToFloat()
        {
            DetConfig.Touch();
            return raw / (float)Scale;
        }

        public override string ToString()
        {
            DetConfig.Touch();
            decimal v = raw / (decimal)Scale;
            return v.ToString("0.000", CultureInfo.InvariantCulture);
        }

        public bool Equals(DetS64 other)
        {
            DetConfig.Touch();
            return raw == other.raw;
        }

        public override bool Equals(object obj)
        {
            return obj is DetS64 other && Equals(other);
        }

        public override int GetHashCode()
        {
            DetConfig.Touch();
            return raw.GetHashCode();
        }

        public int CompareTo(DetS64 other)
        {
            DetConfig.Touch();
            return raw.CompareTo(other.raw);
        }

        // -------------------------
        // Operators: arithmetic
        // -------------------------

        public static DetS64 operator +(DetS64 a, DetS64 b)
        {
            DetConfig.Touch();
            if (DetConfig.Mode == DetOverflowMode.Wrap)
                return new DetS64(unchecked(a.raw + b.raw));

            BigInteger s = (BigInteger)a.raw + b.raw;
            return new DetS64(DetMath.ClampToInt64(s));
        }

        public static DetS64 operator -(DetS64 a, DetS64 b)
        {
            DetConfig.Touch();
            if (DetConfig.Mode == DetOverflowMode.Wrap)
                return new DetS64(unchecked(a.raw - b.raw));

            BigInteger s = (BigInteger)a.raw - b.raw;
            return new DetS64(DetMath.ClampToInt64(s));
        }

        public static DetS64 operator *(DetS64 a, DetS64 b)
        {
            DetConfig.Touch();

            BigInteger prod = (BigInteger)a.raw * (BigInteger)b.raw; // can exceed 64-bit
            BigInteger div = DetMath.RoundDivNearestAwayFromZero(prod, (BigInteger)Scale);

            if (DetConfig.Mode == DetOverflowMode.Wrap)
                return new DetS64(DetMath.WrapToInt64(div));

            return new DetS64(DetMath.ClampToInt64(div));
        }

        public static DetS64 operator /(DetS64 a, DetS64 b)
        {
            DetConfig.Touch();
            if (b.raw == 0L)
                throw new DivideByZeroException();

            BigInteger num = (BigInteger)a.raw * Scale;
            BigInteger div = DetMath.RoundDivNearestAwayFromZero(num, (BigInteger)b.raw);

            if (DetConfig.Mode == DetOverflowMode.Wrap)
                return new DetS64(DetMath.WrapToInt64(div));

            return new DetS64(DetMath.ClampToInt64(div));
        }

        public static DetS64 operator %(DetS64 a, DetS64 b)
        {
            DetConfig.Touch();
            if (b.raw == 0L)
                throw new DivideByZeroException();

            if (DetConfig.Mode == DetOverflowMode.Wrap)
                return new DetS64(unchecked(a.raw % b.raw));

            // Saturate: remainder always fits in range anyway.
            return new DetS64(a.raw % b.raw);
        }

        public static DetS64 operator -(DetS64 v)
        {
            DetConfig.Touch();
            if (DetConfig.Mode == DetOverflowMode.Wrap)
                return new DetS64(unchecked(-v.raw));

            // Saturate: -long.MinValue is not representable.
            if (v.raw == long.MinValue) return MaxValue;
            return new DetS64(-v.raw);
        }

        // -------------------------
        // Operators: comparisons
        // -------------------------

        public static bool operator ==(DetS64 a, DetS64 b)
        {
            DetConfig.Touch();
            return a.raw == b.raw;
        }

        public static bool operator !=(DetS64 a, DetS64 b)
        {
            DetConfig.Touch();
            return a.raw != b.raw;
        }

        public static bool operator <(DetS64 a, DetS64 b)
        {
            DetConfig.Touch();
            return a.raw < b.raw;
        }

        public static bool operator <=(DetS64 a, DetS64 b)
        {
            DetConfig.Touch();
            return a.raw <= b.raw;
        }

        public static bool operator >(DetS64 a, DetS64 b)
        {
            DetConfig.Touch();
            return a.raw > b.raw;
        }

        public static bool operator >=(DetS64 a, DetS64 b)
        {
            DetConfig.Touch();
            return a.raw >= b.raw;
        }

        // -------------------------
        // Operators: bitwise
        // -------------------------

        public static DetS64 operator &(DetS64 a, DetS64 b)
        {
            DetConfig.Touch();
            return new DetS64(a.raw & b.raw);
        }

        public static DetS64 operator |(DetS64 a, DetS64 b)
        {
            DetConfig.Touch();
            return new DetS64(a.raw | b.raw);
        }

        public static DetS64 operator ^(DetS64 a, DetS64 b)
        {
            DetConfig.Touch();
            return new DetS64(a.raw ^ b.raw);
        }

        public static DetS64 operator ~(DetS64 a)
        {
            DetConfig.Touch();
            return new DetS64(~a.raw);
        }

        // -------------------------
        // Operators: shifts
        // -------------------------

        public static DetS64 operator <<(DetS64 a, int shift)
        {
            DetConfig.Touch();
            int s = shift & 63;

            if (DetConfig.Mode == DetOverflowMode.Wrap)
                return new DetS64(unchecked(a.raw << s));

            BigInteger v = (BigInteger)a.raw << s;
            return new DetS64(DetMath.ClampToInt64(v));
        }

        public static DetS64 operator >> (DetS64 a, int shift)
        {
            DetConfig.Touch();
            int s = shift & 63;
            return new DetS64(a.raw >> s); // arithmetic shift
        }

        // -------------------------
        // Internal helpers
        // -------------------------

        private static DetS64 FromBigRawWithMode(BigInteger rawValue)
        {
            DetConfig.Touch();
            if (DetConfig.Mode == DetOverflowMode.Wrap)
                return new DetS64(DetMath.WrapToInt64(rawValue));
            return new DetS64(DetMath.ClampToInt64(rawValue));
        }

        private static BigInteger RoundDecimalAwayFromZeroToBigInteger(decimal value)
        {
            // value is scaled (value*1000), so we want integral quantization with ties away from zero.
            decimal trunc = decimal.Truncate(value);
            decimal frac = value - trunc;

            BigInteger q = BigInteger.Parse(trunc.ToString("0", CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture);

            if (frac == 0m)
                return q;

            decimal absFrac = Math.Abs(frac);

            if (absFrac > 0.5m)
                return q + (Math.Sign(frac) > 0 ? BigInteger.One : BigInteger.Negate(BigInteger.One));

            if (absFrac == 0.5m)
                return q + (Math.Sign(value) > 0
                    ? BigInteger.One
                    : BigInteger.Negate(BigInteger.One)); // ties away from zero

            return q;
        }
    }
}