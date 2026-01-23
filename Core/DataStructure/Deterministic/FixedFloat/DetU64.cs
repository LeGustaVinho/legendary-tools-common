using System;
using System.Globalization;
using System.Numerics;
using UnityEngine;

namespace DeterministicFixedPoint
{
    [Serializable]
    public struct DetU64 : IEquatable<DetU64>, IComparable<DetU64>
    {
        public const int Scale = 1000;

        [SerializeField] private ulong raw;

        public ulong Raw
        {
            get
            {
                DetConfig.Touch();
                return raw;
            }
        }

        // Avoid locking DetConfig during type initialization.
        public static readonly DetU64 Zero = new(0UL);
        public static readonly DetU64 One = new((ulong)Scale);
        public static readonly DetU64 MinValue = new(0UL);
        public static readonly DetU64 MaxValue = new(ulong.MaxValue);

        private DetU64(ulong rawValue)
        {
            raw = rawValue;
        }

        public static DetU64 FromRaw(ulong rawValue)
        {
            DetConfig.Touch();
            return new DetU64(rawValue);
        }

        public static DetU64 FromULong(ulong value)
        {
            DetConfig.Touch();
            BigInteger scaled = (BigInteger)value * Scale;
            return FromBigRawWithMode(scaled);
        }

        /// <summary>
        /// Authoring/UI only. Float is not allowed in simulation.
        /// Rounding: nearest; ties up.
        /// </summary>
        public static DetU64 FromFloat(float value)
        {
            DetConfig.Touch();
            decimal d = (decimal)value;
            if (d <= 0m)
                return Zero;

            decimal scaled = d * Scale;
            BigInteger rounded = RoundDecimalNearestTiesUpToBigInteger(scaled);
            return FromBigRawWithMode(rounded);
        }

        public ulong ToIntFloor()
        {
            DetConfig.Touch();
            return raw / (ulong)Scale;
        }

        public ulong ToIntRound()
        {
            DetConfig.Touch();
            BigInteger q = DetMath.RoundDivNearestUp((BigInteger)raw, (BigInteger)Scale);
            return DetMath.ClampToUInt64(q);
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

        public bool Equals(DetU64 other)
        {
            DetConfig.Touch();
            return raw == other.raw;
        }

        public override bool Equals(object obj)
        {
            return obj is DetU64 other && Equals(other);
        }

        public override int GetHashCode()
        {
            DetConfig.Touch();
            return raw.GetHashCode();
        }

        public int CompareTo(DetU64 other)
        {
            DetConfig.Touch();
            return raw.CompareTo(other.raw);
        }

        // -------------------------
        // Operators: arithmetic
        // -------------------------

        public static DetU64 operator +(DetU64 a, DetU64 b)
        {
            DetConfig.Touch();
            if (DetConfig.Mode == DetOverflowMode.Wrap)
                return new DetU64(unchecked(a.raw + b.raw));

            BigInteger s = (BigInteger)a.raw + b.raw;
            return new DetU64(DetMath.ClampToUInt64(s));
        }

        public static DetU64 operator -(DetU64 a, DetU64 b)
        {
            DetConfig.Touch();
            if (DetConfig.Mode == DetOverflowMode.Wrap)
                return new DetU64(unchecked(a.raw - b.raw));

            // Saturate: clamp at 0.
            if (a.raw <= b.raw) return Zero;
            return new DetU64(a.raw - b.raw);
        }

        public static DetU64 operator *(DetU64 a, DetU64 b)
        {
            DetConfig.Touch();

            BigInteger prod = (BigInteger)a.raw * (BigInteger)b.raw; // can exceed 64-bit
            BigInteger div = DetMath.RoundDivNearestUp(prod, (BigInteger)Scale);

            if (DetConfig.Mode == DetOverflowMode.Wrap)
                return new DetU64(DetMath.WrapToUInt64(div));

            return new DetU64(DetMath.ClampToUInt64(div));
        }

        public static DetU64 operator /(DetU64 a, DetU64 b)
        {
            DetConfig.Touch();
            if (b.raw == 0UL)
                throw new DivideByZeroException();

            BigInteger num = (BigInteger)a.raw * Scale;
            BigInteger div = DetMath.RoundDivNearestUp(num, (BigInteger)b.raw);

            if (DetConfig.Mode == DetOverflowMode.Wrap)
                return new DetU64(DetMath.WrapToUInt64(div));

            return new DetU64(DetMath.ClampToUInt64(div));
        }

        public static DetU64 operator %(DetU64 a, DetU64 b)
        {
            DetConfig.Touch();
            if (b.raw == 0UL)
                throw new DivideByZeroException();

            return new DetU64(a.raw % b.raw);
        }

        // -------------------------
        // Operators: comparisons
        // -------------------------

        public static bool operator ==(DetU64 a, DetU64 b)
        {
            DetConfig.Touch();
            return a.raw == b.raw;
        }

        public static bool operator !=(DetU64 a, DetU64 b)
        {
            DetConfig.Touch();
            return a.raw != b.raw;
        }

        public static bool operator <(DetU64 a, DetU64 b)
        {
            DetConfig.Touch();
            return a.raw < b.raw;
        }

        public static bool operator <=(DetU64 a, DetU64 b)
        {
            DetConfig.Touch();
            return a.raw <= b.raw;
        }

        public static bool operator >(DetU64 a, DetU64 b)
        {
            DetConfig.Touch();
            return a.raw > b.raw;
        }

        public static bool operator >=(DetU64 a, DetU64 b)
        {
            DetConfig.Touch();
            return a.raw >= b.raw;
        }

        // -------------------------
        // Operators: bitwise
        // -------------------------

        public static DetU64 operator &(DetU64 a, DetU64 b)
        {
            DetConfig.Touch();
            return new DetU64(a.raw & b.raw);
        }

        public static DetU64 operator |(DetU64 a, DetU64 b)
        {
            DetConfig.Touch();
            return new DetU64(a.raw | b.raw);
        }

        public static DetU64 operator ^(DetU64 a, DetU64 b)
        {
            DetConfig.Touch();
            return new DetU64(a.raw ^ b.raw);
        }

        public static DetU64 operator ~(DetU64 a)
        {
            DetConfig.Touch();
            return new DetU64(~a.raw);
        }

        // -------------------------
        // Operators: shifts
        // -------------------------

        public static DetU64 operator <<(DetU64 a, int shift)
        {
            DetConfig.Touch();
            int s = shift & 63;

            if (DetConfig.Mode == DetOverflowMode.Wrap)
                return new DetU64(unchecked(a.raw << s));

            BigInteger v = (BigInteger)a.raw << s;
            return new DetU64(DetMath.ClampToUInt64(v));
        }

        public static DetU64 operator >> (DetU64 a, int shift)
        {
            DetConfig.Touch();
            int s = shift & 63;
            return new DetU64(a.raw >> s); // logical shift
        }

        // -------------------------
        // Internal helpers
        // -------------------------

        private static DetU64 FromBigRawWithMode(BigInteger rawValue)
        {
            DetConfig.Touch();
            if (DetConfig.Mode == DetOverflowMode.Wrap)
                return new DetU64(DetMath.WrapToUInt64(rawValue));
            return new DetU64(DetMath.ClampToUInt64(rawValue));
        }

        private static BigInteger RoundDecimalNearestTiesUpToBigInteger(decimal value)
        {
            if (value <= 0m) return BigInteger.Zero;

            decimal trunc = decimal.Truncate(value);
            decimal frac = value - trunc;

            BigInteger q = BigInteger.Parse(trunc.ToString("0", CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture);

            if (frac == 0m)
                return q;

            if (frac > 0.5m)
                return q + BigInteger.One;

            if (frac == 0.5m)
                return q + BigInteger.One; // ties up

            return q;
        }
    }
}