using System;
using System.Numerics;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    /// <summary>Independent rational oracle: no scalar/vector/quaternion runtime arithmetic in the expected result.</summary>
    public sealed class FixedQuaternionIntegerOracleTests
    {
        private static readonly BigInteger PositiveLimit = (BigInteger.One << 63) - 3;
        private static readonly BigInteger NegativeLimit = (BigInteger.One << 63) - 2;
        private static readonly BigInteger FiniteCount = (BigInteger.One << 64) - 4;
        private const ulong NegativeZero = 0x7ffffffffffffffeUL;

        private static ulong Rational(BigInteger n, BigInteger d, FixedSettings settings, ref FixedStatus status)
        {
            bool negative = n.Sign < 0;
            BigInteger q = BigInteger.DivRem(BigInteger.Abs(n), d, out BigInteger r);
            if (!r.IsZero)
            {
                status |= FixedStatus.Inexact;
                int half = (r * 2).CompareTo(d);
                bool up = settings.Rounding == FixedRounding.TowardPositiveInfinity && !negative ||
                          settings.Rounding == FixedRounding.TowardNegativeInfinity && negative ||
                          (settings.Rounding == FixedRounding.ToEven || settings.Rounding == FixedRounding.AwayFromZero) &&
                          (half > 0 || half == 0 && (settings.Rounding == FixedRounding.AwayFromZero || !q.IsEven));
                if (up) q++;
            }
            if (q > (negative ? NegativeLimit : PositiveLimit))
            {
                status |= FixedStatus.Overflow;
                switch (settings.Overflow)
                {
                    case FixedOverflow.NaN: return 0x8000000000000000UL;
                    case FixedOverflow.Infinity: return negative ? 0x8000000000000001UL : 0x7fffffffffffffffUL;
                    case FixedOverflow.Saturate: q = negative ? NegativeLimit : PositiveLimit; break;
                    case FixedOverflow.WrapFinite:
                        BigInteger wrapped = ((negative ? -q : q) + NegativeLimit) % FiniteCount;
                        if (wrapped.Sign < 0) wrapped += FiniteCount;
                        wrapped -= NegativeLimit;
                        return unchecked((ulong)(long)wrapped);
                }
            }
            return q.IsZero && negative ? NegativeZero : unchecked((ulong)(long)(negative ? -q : q));
        }

        private static long[] Input(int sample, long scale)
        {
            switch (sample)
            {
                case 0: return new[] { 1L, -1L, 2L, -3L };
                case 1: return new[] { long.MaxValue - 2, long.MinValue + 2, 1L, -1L };
                case 2: return new[] { long.MaxValue - 3, 19L, -37L, long.MinValue + 3 };
                case 3: return new[] { scale, -scale, scale, -scale };
                default:
                    // Stable input generation also on runtimes with a different System.Random implementation.
                    ulong rng = unchecked(0x9e3779b97f4a7c15UL * (ulong)(sample + 1));
                    var values = new long[4];
                    for (int i = 0; i < 4; i++)
                    {
                        rng ^= rng << 13; rng ^= rng >> 7; rng ^= rng << 17;
                        values[i] = (long)(rng % (ulong)(scale * 4 + 1)) - scale * 2;
                        if (values[i] == 0) values[i] = i + 1;
                    }
                    return values;
            }
        }
        private static FixedQuaternionBits Bits(long[] v) => new FixedQuaternionBits(unchecked((ulong)v[0]), unchecked((ulong)v[1]), unchecked((ulong)v[2]), unchecked((ulong)v[3]));
        private static void Equal(BigInteger numerator, ulong expected, ulong actual, string context)
        {
            // Exact cancellation has a rounding-dependent zero sign. The rational oracle compares its numerical zero.
            if (numerator.IsZero) Assert.IsTrue(actual == 0 || actual == NegativeZero, context);
            else Assert.AreEqual(expected, actual, context);
        }

        [TestCase(FixedOverflow.Infinity)] [TestCase(FixedOverflow.Saturate)]
        [TestCase(FixedOverflow.NaN)] [TestCase(FixedOverflow.WrapFinite)]
        public void InverseMatchesExactRationalsIncludingTinyInputsAndFiniteExtremes(FixedOverflow overflow)
        {
            for (int precision = 0; precision <= 18; precision++)
            foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
            foreach (FixedArithmeticMode arithmetic in Enum.GetValues(typeof(FixedArithmeticMode)))
            for (int sample = 0; sample < 12; sample++)
            {
                var settings = new FixedSettings(decimalPlaces64: precision, rounding: rounding, overflow: overflow, arithmeticMode: arithmetic);
                var state = new FixedRuntimeState(settings); long[] a = Input(sample, (long)state.Scale64);
                BigInteger scale = state.Scale64, denominator = 0;
                foreach (long c in a) denominator += (BigInteger)c * c;
                var actual = FixedQuaternionKernel.Inverse(Bits(a), state, out var status);
                FixedStatus expectedStatus = FixedStatus.None;
                for (int i = 0; i < 4; i++)
                {
                    BigInteger n = (BigInteger)a[i] * scale * scale * (i == 3 ? 1 : -1);
                    ulong expected = Rational(n, denominator, settings, ref expectedStatus);
                    Assert.AreEqual(expected, actual.Get(i), $"inverse p={precision}/{rounding}/{arithmetic}/{sample}/{i}");
                }
                Assert.AreEqual(expectedStatus, status);
            }
        }

        [TestCase(FixedOverflow.Infinity)] [TestCase(FixedOverflow.Saturate)]
        [TestCase(FixedOverflow.NaN)] [TestCase(FixedOverflow.WrapFinite)]
        public void RotationMatchesIndependentTripleCrossFormulaWithoutIntermediateRounding(FixedOverflow overflow)
        {
            for (int precision = 0; precision <= 18; precision++)
            foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
            foreach (FixedArithmeticMode arithmetic in Enum.GetValues(typeof(FixedArithmeticMode)))
            for (int sample = 0; sample < 12; sample++)
            {
                var settings = new FixedSettings(decimalPlaces64: precision, rounding: rounding, overflow: overflow, arithmeticMode: arithmetic);
                var state = new FixedRuntimeState(settings); long[] q = Input(sample, (long)state.Scale64), v = Input(11 - sample, (long)state.Scale64);
                BigInteger s2 = (BigInteger)state.Scale64 * state.Scale64;
                BigInteger[] u = { q[0], q[1], q[2] }, point = { v[0], v[1], v[2] };
                BigInteger[] cross = Cross(u, point), twiceCross = Cross(u, cross);
                var actual = FixedQuaternionKernel.Rotate(Bits(q), new FixedVectorBits(unchecked((ulong)v[0]), unchecked((ulong)v[1]), unchecked((ulong)v[2])), state, out var status);
                FixedStatus expectedStatus = FixedStatus.None;
                for (int i = 0; i < 3; i++)
                {
                    BigInteger n = point[i] * s2 + (BigInteger)2 * q[3] * cross[i] + 2 * twiceCross[i];
                    ulong expected = Rational(n, s2, settings, ref expectedStatus);
                    Equal(n, expected, actual.Get(i), $"rotate p={precision}/{rounding}/{arithmetic}/{sample}/{i}");
                }
                Assert.AreEqual(expectedStatus, status);
            }
        }
        private static BigInteger[] Cross(BigInteger[] a, BigInteger[] b) => new[]
        { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };

        [TestCase(FixedOverflow.Infinity)] [TestCase(FixedOverflow.Saturate)]
        [TestCase(FixedOverflow.NaN)] [TestCase(FixedOverflow.WrapFinite)]
        public void HamiltonOverflowAndCancellationMatchExactRationals(FixedOverflow overflow)
        {
            for (int precision = 0; precision <= 18; precision++)
            foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
            foreach (FixedArithmeticMode arithmetic in Enum.GetValues(typeof(FixedArithmeticMode)))
            for (int sample = 0; sample < 12; sample++)
            {
                var settings = new FixedSettings(decimalPlaces64: precision, rounding: rounding, overflow: overflow, arithmeticMode: arithmetic);
                var state = new FixedRuntimeState(settings); long[] a = Input(sample, (long)state.Scale64), b = Input(11 - sample, (long)state.Scale64);
                // Hamilton product expressed as scalar/vector dot and cross, independently of the runtime's component summation.
                BigInteger[] u = { a[0], a[1], a[2] }, v = { b[0], b[1], b[2] }, cross = Cross(u, v);
                BigInteger[] n = { a[3] * v[0] + b[3] * u[0] + cross[0], a[3] * v[1] + b[3] * u[1] + cross[1], a[3] * v[2] + b[3] * u[2] + cross[2],
                    (BigInteger)a[3] * b[3] - u[0] * v[0] - u[1] * v[1] - u[2] * v[2] };
                var actual = FixedQuaternionKernel.Multiply(Bits(a), Bits(b), state, out var status);
                FixedStatus expectedStatus = FixedStatus.None;
                for (int i = 0; i < 4; i++) Equal(n[i], Rational(n[i], state.Scale64, settings, ref expectedStatus), actual.Get(i), $"multiply p={precision}/{rounding}/{arithmetic}/{sample}/{i}");
                Assert.AreEqual(expectedStatus, status);
            }
        }
    }
}
