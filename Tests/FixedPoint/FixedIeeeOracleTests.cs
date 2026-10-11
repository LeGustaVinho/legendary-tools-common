using System;
using System.Numerics;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedIeeeOracleTests
    {
        private static void Ratio(ulong bits, int fractionBits, int exponentBits, out BigInteger numerator, out BigInteger denominator)
        {
            ulong fraction = bits & ((1UL << fractionBits) - 1);
            int exponent = (int)((bits >> fractionBits) & ((1U << exponentBits) - 1));
            int bias = (1 << (exponentBits - 1)) - 1;
            numerator = fraction | (exponent != 0 ? 1UL << fractionBits : 0);
            int shift = (exponent == 0 ? 1 - bias : exponent - bias) - fractionBits;
            denominator = BigInteger.One;
            if (shift >= 0) numerator <<= shift; else denominator <<= -shift;
        }
        private static void CheckNearest(ulong bits, ulong rawMagnitude, ulong scale, int fractionBits, int exponentBits)
        {
            Ratio(bits, fractionBits, exponentBits, out BigInteger n, out BigInteger d);
            BigInteger distanceNumerator = BigInteger.Abs((BigInteger)rawMagnitude * d - n * scale);
            foreach (ulong adjacent in new[] { bits - 1, bits + 1 })
            {
                Ratio(adjacent, fractionBits, exponentBits, out BigInteger neighborN, out BigInteger neighborD);
                BigInteger neighborDistance = BigInteger.Abs((BigInteger)rawMagnitude * neighborD - neighborN * scale);
                int compare = (distanceNumerator * neighborD).CompareTo(neighborDistance * d);
                Assert.LessOrEqual(compare, 0, "The chosen IEEE value must be at least as close as either neighbor.");
                if (compare == 0) Assert.AreEqual(0UL, bits & 1, "Midpoint chooses the even significand.");
            }
        }

        [Test]
        public void OutputIsTheNearestIeeeValueUsingExactRationalDistanceToBothNeighbors()
        {
            var integers = new FixedRuntimeState(new FixedSettings(0, 0));
            Assert.AreEqual(FixedIeee.Bits((double)(1UL << 53)), FixedIeee.Bits(FixedIeee.ToDouble((1UL << 53) + 1, FixedKind.Signed64, integers)));
            Assert.AreEqual(FixedIeee.Bits((double)((1UL << 53) + 4)), FixedIeee.Bits(FixedIeee.ToDouble((1UL << 53) + 3, FixedKind.Signed64, integers)));
            Assert.AreEqual(FixedIeee.Bits((float)(1UL << 24)), FixedIeee.Bits(FixedIeee.ToSingle((1UL << 24) + 1, FixedKind.Signed64, integers)));
            var random = new Random(529741);
            foreach (int p in new[] { 0, 1, 3, 9, 12, 18 })
            {
                var state = new FixedRuntimeState(new FixedSettings(Math.Min(9, p), p));
                foreach (FixedKind kind in Enum.GetValues(typeof(FixedKind)))
                {
                    var f = new FixedFormat(kind, state);
                    for (int i = 0; i < 400; i++)
                    {
                        ulong magnitude = i == 0 ? 1 : i == 1 ? f.MaxPositive :
                            ((((ulong)(uint)random.Next() << 33) ^ ((ulong)(uint)random.Next() << 2)) % f.MaxPositive) + 1;
                        CheckNearest(FixedIeee.Bits(FixedIeee.ToDouble(magnitude, kind, state)), magnitude, f.Scale, 52, 11);
                        CheckNearest(FixedIeee.Bits(FixedIeee.ToSingle(magnitude, kind, state)), magnitude, f.Scale, 23, 8);
                    }
                }
            }
        }

        [Test]
        public void IeeeInputsIncludingSubnormalsAndHugeExponentsMatchExactBigIntegerQuantization()
        {
            var random = new Random(83721);
            foreach (int p in new[] { 0, 3, 9, 18 })
            foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
            {
                var state = new FixedRuntimeState(new FixedSettings(Math.Min(9, p), p, rounding,
                    FixedOverflow.WrapFinite, unsignedNegative: FixedUnsignedNegative.WrapFinite));
                foreach (FixedKind kind in Enum.GetValues(typeof(FixedKind)))
                {
                    var f = new FixedFormat(kind, state);
                    for (int i = 0; i < 100; i++)
                    {
                        ulong bits = i == 0 ? 1 : i == 1 ? 0x7fefffffffffffff :
                            ((ulong)(uint)random.Next() << 32) ^ (uint)random.Next();
                        // Keep this rational oracle finite; NaN/infinity have separate classification tests.
                        if ((bits >> 52 & 2047) == 2047) bits -= 1UL << 52;
                        bool negative = (i & 1) != 0;
                        Ratio(bits, 52, 11, out BigInteger n, out BigInteger d);
                        BigInteger q = BigInteger.DivRem(n * f.Scale, d, out BigInteger remainder);
                        bool increment = rounding == FixedRounding.TowardPositiveInfinity && !negative || rounding == FixedRounding.TowardNegativeInfinity && negative;
                        if (rounding == FixedRounding.ToEven || rounding == FixedRounding.AwayFromZero)
                            increment = remainder * 2 > d || remainder * 2 == d && (rounding == FixedRounding.AwayFromZero || !q.IsEven);
                        if (!remainder.IsZero && increment) q++;
                        BigInteger signed = negative ? -q : q;
                        ulong expected;
                        if (q.IsZero) expected = f.Finite(0, negative);
                        else if (negative && f.Unsigned || q > (negative ? f.MaxNegative : f.MaxPositive))
                        {
                            BigInteger lo = f.Unsigned ? BigInteger.Zero : -(BigInteger)f.MaxNegative;
                            BigInteger residue = ((signed - lo) % f.FiniteCount + f.FiniteCount) % f.FiniteCount + lo;
                            expected = residue.Sign < 0 ? f.Finite((ulong)-residue, true) : (ulong)residue;
                        }
                        else expected = f.Finite((ulong)q, negative);
                        ulong actual = FixedIeee.From(FixedIeee.Double(bits | (negative ? 1UL << 63 : 0)), kind, state, out _);
                        Assert.AreEqual(expected, actual, kind + " p=" + p + " bits=" + bits + " rounding=" + rounding);
                    }
                }
            }
        }
    }
}
