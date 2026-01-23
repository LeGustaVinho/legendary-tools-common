using System;
using System.Globalization;
using UnityEngine;

namespace DeterministicFixedPoint
{
    [Serializable]
    public struct DetS32 : IEquatable<DetS32>, IComparable<DetS32>
    {
        public const int Scale = 1000;

        [SerializeField] private int raw;

        public int Raw
        {
            get
            {
                DetConfig.Touch();
                return raw;
            }
        }

        // IMPORTANT:
        // Do not call FromRaw here, otherwise static initialization would lock DetConfig too early.
        public static readonly DetS32 Zero = new(0);
        public static readonly DetS32 One = new(Scale);
        public static readonly DetS32 MinValue = new(int.MinValue);
        public static readonly DetS32 MaxValue = new(int.MaxValue);

        private DetS32(int rawValue)
        {
            raw = rawValue;
        }

        public static DetS32 FromRaw(int rawValue)
        {
            DetConfig.Touch();
            return new DetS32(rawValue);
        }

        public static DetS32 FromInt(int value)
        {
            DetConfig.Touch();
            long scaled = (long)value * Scale;
            return FromLongRawWithMode(scaled);
        }

        /// <summary>
        /// Authoring/UI only. Float is not allowed in simulation.
        /// Rounding: nearest; ties away from zero.
        /// </summary>
        public static DetS32 FromFloat(float value)
        {
            DetConfig.Touch();
            decimal d = (decimal)value;
            decimal scaled = d * Scale;

            long rounded = RoundDecimalAwayFromZeroToInt64(scaled);
            return FromLongRawWithMode(rounded);
        }

        public int ToIntFloor()
        {
            DetConfig.Touch();
            int r = raw;
            int q = r / Scale; // trunc toward zero
            int rem = r % Scale;

            if (r < 0 && rem != 0)
                q -= 1;

            return q;
        }

        public int ToIntRound()
        {
            DetConfig.Touch();
            long rounded = DetMath.RoundDivNearestAwayFromZero(raw, Scale);
            return DetMath.ClampToInt(rounded);
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

        public bool Equals(DetS32 other)
        {
            DetConfig.Touch();
            return raw == other.raw;
        }

        public override bool Equals(object obj)
        {
            return obj is DetS32 other && Equals(other);
        }

        public override int GetHashCode()
        {
            DetConfig.Touch();
            return raw;
        }

        public int CompareTo(DetS32 other)
        {
            DetConfig.Touch();
            return raw.CompareTo(other.raw);
        }

        // -------------------------
        // Operators: arithmetic
        // -------------------------

        public static DetS32 operator +(DetS32 a, DetS32 b)
        {
            DetConfig.Touch();
            if (DetConfig.Mode == DetOverflowMode.Wrap)
            {
                int r = unchecked(a.raw + b.raw);
                return new DetS32(r);
            }
            else
            {
                long s = (long)a.raw + b.raw;
                return new DetS32(DetMath.ClampToInt(s));
            }
        }

        public static DetS32 operator -(DetS32 a, DetS32 b)
        {
            DetConfig.Touch();
            if (DetConfig.Mode == DetOverflowMode.Wrap)
            {
                int r = unchecked(a.raw - b.raw);
                return new DetS32(r);
            }
            else
            {
                long s = (long)a.raw - b.raw;
                return new DetS32(DetMath.ClampToInt(s));
            }
        }

        public static DetS32 operator *(DetS32 a, DetS32 b)
        {
            DetConfig.Touch();
            long prod = (long)a.raw * (long)b.raw;
            long div = DetMath.RoundDivNearestAwayFromZero(prod, Scale);

            if (DetConfig.Mode == DetOverflowMode.Wrap)
                return new DetS32(DetMath.WrapToInt(div));

            return new DetS32(DetMath.ClampToInt(div));
        }

        public static DetS32 operator /(DetS32 a, DetS32 b)
        {
            DetConfig.Touch();
            if (b.raw == 0)
                throw new DivideByZeroException();

            long num = (long)a.raw * Scale;
            long div = DetMath.RoundDivNearestAwayFromZero(num, b.raw);

            if (DetConfig.Mode == DetOverflowMode.Wrap)
                return new DetS32(DetMath.WrapToInt(div));

            return new DetS32(DetMath.ClampToInt(div));
        }

        public static DetS32 operator %(DetS32 a, DetS32 b)
        {
            DetConfig.Touch();
            if (b.raw == 0)
                throw new DivideByZeroException();

            long r = (long)a.raw % (long)b.raw;

            if (DetConfig.Mode == DetOverflowMode.Wrap)
                return new DetS32(DetMath.WrapToInt(r));

            return new DetS32(DetMath.ClampToInt(r));
        }

        public static DetS32 operator -(DetS32 v)
        {
            DetConfig.Touch();
            if (DetConfig.Mode == DetOverflowMode.Wrap)
                return new DetS32(unchecked(-v.raw));

            // Saturate
            // -int.MinValue cannot be represented as int, so saturate to MaxValue.
            if (v.raw == int.MinValue) return MaxValue;
            return new DetS32(-v.raw);
        }

        // -------------------------
        // Operators: comparisons
        // -------------------------

        public static bool operator ==(DetS32 a, DetS32 b)
        {
            DetConfig.Touch();
            return a.raw == b.raw;
        }

        public static bool operator !=(DetS32 a, DetS32 b)
        {
            DetConfig.Touch();
            return a.raw != b.raw;
        }

        public static bool operator <(DetS32 a, DetS32 b)
        {
            DetConfig.Touch();
            return a.raw < b.raw;
        }

        public static bool operator <=(DetS32 a, DetS32 b)
        {
            DetConfig.Touch();
            return a.raw <= b.raw;
        }

        public static bool operator >(DetS32 a, DetS32 b)
        {
            DetConfig.Touch();
            return a.raw > b.raw;
        }

        public static bool operator >=(DetS32 a, DetS32 b)
        {
            DetConfig.Touch();
            return a.raw >= b.raw;
        }

        // -------------------------
        // Operators: bitwise
        // -------------------------

        public static DetS32 operator &(DetS32 a, DetS32 b)
        {
            DetConfig.Touch();
            return new DetS32(a.raw & b.raw);
        }

        public static DetS32 operator |(DetS32 a, DetS32 b)
        {
            DetConfig.Touch();
            return new DetS32(a.raw | b.raw);
        }

        public static DetS32 operator ^(DetS32 a, DetS32 b)
        {
            DetConfig.Touch();
            return new DetS32(a.raw ^ b.raw);
        }

        public static DetS32 operator ~(DetS32 a)
        {
            DetConfig.Touch();
            return new DetS32(~a.raw);
        }

        // -------------------------
        // Operators: shifts
        // -------------------------

        public static DetS32 operator <<(DetS32 a, int shift)
        {
            DetConfig.Touch();
            int s = shift & 31;

            if (DetConfig.Mode == DetOverflowMode.Wrap)
            {
                int r = unchecked(a.raw << s);
                return new DetS32(r);
            }
            else
            {
                long v = (long)a.raw << s;
                return new DetS32(DetMath.ClampToInt(v));
            }
        }

        public static DetS32 operator >> (DetS32 a, int shift)
        {
            DetConfig.Touch();
            int s = shift & 31;
            return new DetS32(a.raw >> s); // arithmetic shift
        }

        // -------------------------
        // Internal helpers
        // -------------------------

        private static DetS32 FromLongRawWithMode(long rawValue)
        {
            DetConfig.Touch();

            if (DetConfig.Mode == DetOverflowMode.Wrap)
                return new DetS32(DetMath.WrapToInt(rawValue));

            return new DetS32(DetMath.ClampToInt(rawValue));
        }

        private static long RoundDecimalAwayFromZeroToInt64(decimal value)
        {
            decimal trunc = decimal.Truncate(value);
            decimal frac = value - trunc;

            if (frac == 0m)
                return (long)trunc;

            decimal absFrac = Math.Abs(frac); // FIXED (was decimal.Abs)

            if (absFrac > 0.5m)
                return (long)(trunc + Math.Sign(frac)); // FIXED (was decimal.Sign)

            if (absFrac == 0.5m)
                return (long)(trunc + Math.Sign(value)); // FIXED (was decimal.Sign) ties away from zero

            return (long)trunc;
        }
    }
}