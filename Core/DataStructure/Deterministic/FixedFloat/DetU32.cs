using System;
using System.Globalization;
using UnityEngine;

namespace DeterministicFixedPoint
{
    [Serializable]
    public struct DetU32 : IEquatable<DetU32>, IComparable<DetU32>
    {
        public const int Scale = 1000;

        [SerializeField] private uint raw;

        public uint Raw
        {
            get
            {
                DetConfig.Touch();
                return raw;
            }
        }

        // IMPORTANT:
        // Do not call FromRaw here, otherwise static initialization would lock DetConfig too early.
        public static readonly DetU32 Zero = new(0u);
        public static readonly DetU32 One = new((uint)Scale);
        public static readonly DetU32 MinValue = new(0u);
        public static readonly DetU32 MaxValue = new(uint.MaxValue);

        private DetU32(uint rawValue)
        {
            raw = rawValue;
        }

        public static DetU32 FromRaw(uint rawValue)
        {
            DetConfig.Touch();
            return new DetU32(rawValue);
        }

        public static DetU32 FromUInt(uint value)
        {
            DetConfig.Touch();
            ulong scaled = (ulong)value * (ulong)Scale;
            return FromULongRawWithMode(scaled);
        }

        /// <summary>
        /// Authoring/UI only. Float is not allowed in simulation.
        /// Rounding: nearest; ties up.
        /// </summary>
        public static DetU32 FromFloat(float value)
        {
            DetConfig.Touch();
            decimal d = (decimal)value;
            if (d <= 0m)
                return Zero;

            decimal scaled = d * Scale;
            ulong rounded = RoundDecimalNearestTiesUpToUInt64(scaled);
            return FromULongRawWithMode(rounded);
        }

        public uint ToUIntFloor()
        {
            DetConfig.Touch();
            return raw / (uint)Scale;
        }

        public uint ToUIntRound()
        {
            DetConfig.Touch();
            ulong rounded = DetMath.RoundDivNearestUp(raw, (uint)Scale);
            return DetMath.ClampToUInt(rounded);
        }

        // Aliases to match the required API naming.
        public uint ToIntFloor()
        {
            return ToUIntFloor();
        }

        public uint ToIntRound()
        {
            return ToUIntRound();
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

        public bool Equals(DetU32 other)
        {
            DetConfig.Touch();
            return raw == other.raw;
        }

        public override bool Equals(object obj)
        {
            return obj is DetU32 other && Equals(other);
        }

        public override int GetHashCode()
        {
            DetConfig.Touch();
            return raw.GetHashCode();
        }

        public int CompareTo(DetU32 other)
        {
            DetConfig.Touch();
            return raw.CompareTo(other.raw);
        }

        // -------------------------
        // Operators: arithmetic
        // -------------------------

        public static DetU32 operator +(DetU32 a, DetU32 b)
        {
            DetConfig.Touch();
            if (DetConfig.Mode == DetOverflowMode.Wrap)
            {
                uint r = unchecked(a.raw + b.raw);
                return new DetU32(r);
            }
            else
            {
                ulong s = (ulong)a.raw + b.raw;
                return new DetU32(DetMath.ClampToUInt(s));
            }
        }

        public static DetU32 operator -(DetU32 a, DetU32 b)
        {
            DetConfig.Touch();
            if (DetConfig.Mode == DetOverflowMode.Wrap)
            {
                uint r = unchecked(a.raw - b.raw);
                return new DetU32(r);
            }
            else
            {
                if (a.raw <= b.raw) return Zero;
                return new DetU32(a.raw - b.raw);
            }
        }

        public static DetU32 operator *(DetU32 a, DetU32 b)
        {
            DetConfig.Touch();
            ulong prod = (ulong)a.raw * (ulong)b.raw;
            ulong div = DetMath.RoundDivNearestUp(prod, (uint)Scale);

            if (DetConfig.Mode == DetOverflowMode.Wrap)
                return new DetU32(DetMath.WrapToUInt(div));

            return new DetU32(DetMath.ClampToUInt(div));
        }

        public static DetU32 operator /(DetU32 a, DetU32 b)
        {
            DetConfig.Touch();
            if (b.raw == 0u)
                throw new DivideByZeroException();

            ulong num = (ulong)a.raw * (uint)Scale;
            ulong div = DetMath.RoundDivNearestUp(num, b.raw);

            if (DetConfig.Mode == DetOverflowMode.Wrap)
                return new DetU32(DetMath.WrapToUInt(div));

            return new DetU32(DetMath.ClampToUInt(div));
        }

        public static DetU32 operator %(DetU32 a, DetU32 b)
        {
            DetConfig.Touch();
            if (b.raw == 0u)
                throw new DivideByZeroException();

            uint r = a.raw % b.raw;
            return new DetU32(r);
        }

        // -------------------------
        // Operators: comparisons
        // -------------------------

        public static bool operator ==(DetU32 a, DetU32 b)
        {
            DetConfig.Touch();
            return a.raw == b.raw;
        }

        public static bool operator !=(DetU32 a, DetU32 b)
        {
            DetConfig.Touch();
            return a.raw != b.raw;
        }

        public static bool operator <(DetU32 a, DetU32 b)
        {
            DetConfig.Touch();
            return a.raw < b.raw;
        }

        public static bool operator <=(DetU32 a, DetU32 b)
        {
            DetConfig.Touch();
            return a.raw <= b.raw;
        }

        public static bool operator >(DetU32 a, DetU32 b)
        {
            DetConfig.Touch();
            return a.raw > b.raw;
        }

        public static bool operator >=(DetU32 a, DetU32 b)
        {
            DetConfig.Touch();
            return a.raw >= b.raw;
        }

        // -------------------------
        // Operators: bitwise
        // -------------------------

        public static DetU32 operator &(DetU32 a, DetU32 b)
        {
            DetConfig.Touch();
            return new DetU32(a.raw & b.raw);
        }

        public static DetU32 operator |(DetU32 a, DetU32 b)
        {
            DetConfig.Touch();
            return new DetU32(a.raw | b.raw);
        }

        public static DetU32 operator ^(DetU32 a, DetU32 b)
        {
            DetConfig.Touch();
            return new DetU32(a.raw ^ b.raw);
        }

        public static DetU32 operator ~(DetU32 a)
        {
            DetConfig.Touch();
            return new DetU32(~a.raw);
        }

        // -------------------------
        // Operators: shifts
        // -------------------------

        public static DetU32 operator <<(DetU32 a, int shift)
        {
            DetConfig.Touch();
            int s = shift & 31;

            if (DetConfig.Mode == DetOverflowMode.Wrap)
            {
                uint r = unchecked(a.raw << s);
                return new DetU32(r);
            }
            else
            {
                ulong v = (ulong)a.raw << s;
                return new DetU32(DetMath.ClampToUInt(v));
            }
        }

        public static DetU32 operator >> (DetU32 a, int shift)
        {
            DetConfig.Touch();
            int s = shift & 31;
            return new DetU32(a.raw >> s); // logical shift
        }

        // -------------------------
        // Internal helpers
        // -------------------------

        private static DetU32 FromULongRawWithMode(ulong rawValue)
        {
            DetConfig.Touch();

            if (DetConfig.Mode == DetOverflowMode.Wrap)
                return new DetU32(DetMath.WrapToUInt(rawValue));

            return new DetU32(DetMath.ClampToUInt(rawValue));
        }

        private static ulong RoundDecimalNearestTiesUpToUInt64(decimal value)
        {
            if (value <= 0m) return 0UL;

            decimal trunc = decimal.Truncate(value);
            decimal frac = value - trunc;

            if (frac == 0m)
                return (ulong)trunc;

            if (frac > 0.5m)
                return (ulong)(trunc + 1m);

            if (frac == 0.5m)
                return (ulong)(trunc + 1m); // ties up

            return (ulong)trunc;
        }
    }
}