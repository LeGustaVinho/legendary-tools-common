using System;
using System.Globalization;
using System.Numerics;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedReviewRegressionTests
    {
        [TestCase(FixedKind.Signed32)]
        [TestCase(FixedKind.Unsigned32)]
        [TestCase(FixedKind.Signed64)]
        [TestCase(FixedKind.Unsigned64)]
        public void ExponentsBeyondInt32AndInt64KeepTheirFiniteWrapResidue(FixedKind kind)
        {
            bool unsigned = kind == FixedKind.Unsigned32 || kind == FixedKind.Unsigned64;
            int width = kind < FixedKind.Signed64 ? 32 : 64;
            BigInteger mask = (BigInteger.One << width) - 1;
            BigInteger min = unsigned ? 0 : -(BigInteger.One << (width - 1)) + 2;
            BigInteger count = (BigInteger.One << width) - 4;
            var state = new FixedRuntimeState(new FixedSettings(3, 3, overflow: FixedOverflow.WrapFinite,
                unsignedNegative: FixedUnsignedNegative.WrapFinite));
            foreach (string exponentText in new[] { "1000000000", "1000000001", "2147483647", "9223372036854775808",
                "18446744073709551615", "123456789012345678901234567890123456789012345678901234567890" })
            foreach (bool negative in new[] { false, true })
            {
                BigInteger exponent = BigInteger.Parse(exponentText, CultureInfo.InvariantCulture);
                BigInteger residue = 37 * BigInteger.ModPow(10, exponent + 3, count) % count;
                if (negative) residue = -residue;
                BigInteger wrapped = ((residue - min) % count + count) % count + min;
                ulong expected = (ulong)(wrapped & mask);
                string text = (negative ? "-" : "") + "37e" + exponentText;
                Assert.IsFalse(FixedManaged.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, kind, state,
                    out ulong actual, out FixedStatus status), text);
                Assert.AreEqual(expected, actual, kind + " " + text);
                Assert.AreEqual(negative && unsigned ? FixedStatus.NegativeResult : FixedStatus.Overflow, status, text);
            }
        }

        [Test]
        public void HugeNegativeExponentsRespectDirectedRoundingAndSignedZero()
        {
            foreach (FixedKind kind in Enum.GetValues(typeof(FixedKind)))
            foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
            foreach (bool negative in new[] { false, true })
            {
                var state = new FixedRuntimeState(new FixedSettings(3, 3, rounding));
                var format = new FixedFormat(kind, state);
                bool quantum = rounding == FixedRounding.TowardPositiveInfinity && !negative ||
                    rounding == FixedRounding.TowardNegativeInfinity && negative;
                ulong expected = negative && quantum && format.Unsigned ? format.NaN : format.Finite(quantum ? 1UL : 0UL, negative);
                FixedStatus expectedStatus = FixedStatus.Inexact |
                    (negative && quantum && format.Unsigned ? FixedStatus.NegativeResult : FixedStatus.None);
                FixedManaged.TryParse((negative ? "-" : "") + "9e-99999999999999999999999999999999999999999999999",
                    NumberStyles.Float, CultureInfo.InvariantCulture, kind, state, out ulong actual, out FixedStatus status);
                Assert.AreEqual(expected, actual, kind + " " + rounding + " negative=" + negative);
                Assert.AreEqual(expectedStatus, status);
            }
        }

        [TestCase(FixedKind.Signed32)]
        [TestCase(FixedKind.Unsigned32)]
        [TestCase(FixedKind.Signed64)]
        [TestCase(FixedKind.Unsigned64)]
        public void LogOfOneHandlesBothZeroSignsAndInfinityBases(FixedKind kind)
        {
            for (int p = 0; p <= (kind < FixedKind.Signed64 ? 9 : 18); p++)
            foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
            foreach (FixedMathProfile profile in Enum.GetValues(typeof(FixedMathProfile)))
            {
                var state = new FixedRuntimeState(new FixedSettings(Math.Min(9, p), p, rounding, mathProfile: profile));
                var format = new FixedFormat(kind, state);
                foreach (ulong zero in new[] { 0UL, format.NegativeZero })
                {
                    Assert.AreEqual(format.NegativeZero, FixedMathKernel.Log(format.Scale, zero, kind, state, out FixedStatus status));
                    Assert.AreEqual(FixedStatus.None, status);
                    Assert.AreEqual(format.NaN, FixedMathKernel.Log(0, zero, kind, state, out status));
                    Assert.AreEqual(FixedStatus.InvalidDomain, status);
                }
                Assert.AreEqual(0UL, FixedMathKernel.Log(format.Scale, format.PositiveInfinity, kind, state, out FixedStatus infinityStatus));
                Assert.AreEqual(FixedStatus.None, infinityStatus);
                Assert.AreEqual(format.NaN, FixedMathKernel.Log(format.Scale, format.NegativeInfinity, kind, state, out infinityStatus));
                Assert.AreEqual(FixedStatus.InvalidDomain, infinityStatus);
            }
        }

        [TestCase(FixedKind.Signed32)]
        [TestCase(FixedKind.Unsigned32)]
        [TestCase(FixedKind.Signed64)]
        [TestCase(FixedKind.Unsigned64)]
        public void AsymptoticTanhAndExponentialUnderflowKeepInexactAndRounding(FixedKind kind)
        {
            foreach (int p in new[] { 0, 3 })
            foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
            foreach (FixedMathProfile profile in Enum.GetValues(typeof(FixedMathProfile)))
            {
                var state = new FixedRuntimeState(new FixedSettings(p, p, rounding, mathProfile: profile));
                var f = new FixedFormat(kind, state);
                foreach (bool negative in new[] { false, true })
                {
                    if (negative && f.Unsigned) continue;
                    bool towardZero = rounding == FixedRounding.TowardZero ||
                        rounding == FixedRounding.TowardPositiveInfinity && negative ||
                        rounding == FixedRounding.TowardNegativeInfinity && !negative;
                    ulong tanh = FixedMathKernel.Unary(f.Finite(100 * f.Scale, negative), kind, state, FixedUnary.Tanh, out FixedStatus tanhStatus);
                    Assert.AreEqual(f.Finite(f.Scale - (towardZero ? 1UL : 0UL), negative), tanh, kind + " Tanh " + rounding + " " + profile);
                    Assert.AreEqual(FixedStatus.Inexact, tanhStatus);
                }
                Assert.AreEqual(f.Scale, FixedMathKernel.Unary(0, kind, state, FixedUnary.Exp, out FixedStatus zeroStatus));
                Assert.AreEqual(FixedStatus.None, zeroStatus);
                if (f.Unsigned) continue;
                foreach (ulong magnitude in new[] { 100UL, 177UL })
                {
                    ulong exponential = FixedMathKernel.Unary(f.Finite(magnitude * f.Scale, true), kind, state, FixedUnary.Exp, out FixedStatus status);
                    Assert.AreEqual(rounding == FixedRounding.TowardPositiveInfinity ? 1UL : 0UL, exponential, profile + " Exp " + rounding);
                    Assert.AreEqual(FixedStatus.Inexact, status);
                }
                ulong power = FixedMathKernel.Pow(f.Finite(2 * f.Scale, true), f.Finite(145 * f.Scale, true), kind, state, out FixedStatus powerStatus);
                Assert.AreEqual(f.Finite(rounding == FixedRounding.TowardNegativeInfinity ? 1UL : 0UL, true), power, profile + " Pow " + rounding);
                Assert.AreEqual(FixedStatus.Inexact, powerStatus);
            }
        }
    }
}
