using System;
using System.Numerics;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedScalarBoundaryTests
    {
        private sealed class Domain
        {
            internal readonly bool Unsigned;
            internal readonly int Bits;
            internal readonly BigInteger Mask, Min, Max, Count, Scale;
            internal readonly ulong NaN, NegativeInfinity, PositiveInfinity, NegativeZero;
            internal Domain(FixedKind kind, int p)
            {
                Bits = kind < FixedKind.Signed64 ? 32 : 64;
                Unsigned = kind == FixedKind.Unsigned32 || kind == FixedKind.Unsigned64;
                Mask = (BigInteger.One << Bits) - 1;
                Min = Unsigned ? 0 : -(BigInteger.One << (Bits - 1)) + 2;
                Count = (BigInteger.One << Bits) - 4; Max = Min + Count - 1;
                Scale = BigInteger.Pow(10, p);
                NaN = Unsigned ? (ulong)Mask : 1UL << (Bits - 1);
                NegativeInfinity = Unsigned ? (ulong)Mask - 1 : NaN + 1;
                PositiveInfinity = Unsigned ? (ulong)Mask - 2 : NaN - 1;
                NegativeZero = Unsigned ? (ulong)Mask - 3 : NaN - 2;
            }
            internal ulong Raw(BigInteger value, bool negativeZero = false) => value.IsZero && negativeZero ? NegativeZero : (ulong)(value & Mask);
            internal ulong Encode(BigInteger value, bool negative, FixedSettings settings, ref FixedStatus status, bool forceOverflow = false)
            {
                if (value.IsZero && !forceOverflow) return Raw(value, negative);
                if (Unsigned && negative)
                {
                    status |= FixedStatus.NegativeResult;
                    if (settings.UnsignedNegative == FixedUnsignedNegative.NaN) return NaN;
                    if (settings.UnsignedNegative == FixedUnsignedNegative.SaturateZero) return 0;
                }
                else if (forceOverflow || value < Min || value > Max)
                {
                    status |= FixedStatus.Overflow;
                    if (settings.Overflow == FixedOverflow.NaN) return NaN;
                    if (settings.Overflow == FixedOverflow.Infinity) return negative ? NegativeInfinity : PositiveInfinity;
                    if (settings.Overflow == FixedOverflow.Saturate) return Raw(negative ? Min : Max);
                }
                else return Raw(value);
                return Raw(((value - Min) % Count + Count) % Count + Min);
            }
        }

        private static BigInteger Quantize(BigInteger value, BigInteger divisor, FixedRounding rounding, out bool inexact)
        {
            bool negative = value.Sign < 0;
            BigInteger q = BigInteger.DivRem(BigInteger.Abs(value), divisor, out BigInteger rem);
            inexact = !rem.IsZero;
            bool increment = rounding == FixedRounding.TowardPositiveInfinity && !negative ||
                rounding == FixedRounding.TowardNegativeInfinity && negative;
            if (rounding == FixedRounding.ToEven || rounding == FixedRounding.AwayFromZero)
                increment = 2 * rem > divisor || 2 * rem == divisor && (rounding == FixedRounding.AwayFromZero || !q.IsEven);
            if (inexact && increment) q++;
            return negative ? -q : q;
        }

        [TestCase(FixedKind.Signed32)]
        [TestCase(FixedKind.Unsigned32)]
        [TestCase(FixedKind.Signed64)]
        [TestCase(FixedKind.Unsigned64)]
        public void ScaleBHandlesIntMinMaxWideShiftsHalfwayTiesAndFiniteOverflow(FixedKind kind)
        {
            foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
            foreach (FixedOverflow overflow in Enum.GetValues(typeof(FixedOverflow)))
            {
                var settings = new FixedSettings(3, 3, rounding, overflow); var state = new FixedRuntimeState(settings);
                var d = new Domain(kind, 3);
                foreach (BigInteger value in new[] { BigInteger.Zero, BigInteger.One, new BigInteger(2), new BigInteger(3),
                    d.Max, d.Max - 1, d.Max / 2, d.Min, d.Min + 1, d.Scale })
                foreach (int exponent in new[] { int.MinValue, -129, -128, -65, -64, -63, -32, -1, 0, 1, 31, 32, 63, 64, 127, 128, int.MaxValue })
                {
                    bool negative = value.Sign < 0; BigInteger expectedValue; bool forcedOverflow = false, inexact = false;
                    if (value.IsZero) expectedValue = 0;
                    else if (exponent >= 128)
                    {
                        forcedOverflow = true;
                        expectedValue = value * BigInteger.ModPow(2, exponent, d.Count) % d.Count;
                    }
                    else if (exponent >= 0) expectedValue = value << exponent;
                    else if (exponent >= -128) expectedValue = Quantize(value, BigInteger.One << -exponent, rounding, out inexact);
                    else
                    {
                        inexact = true;
                        expectedValue = rounding == FixedRounding.TowardPositiveInfinity && !negative ? 1 :
                            rounding == FixedRounding.TowardNegativeInfinity && negative ? -1 : 0;
                    }
                    FixedStatus expectedStatus = inexact ? FixedStatus.Inexact : FixedStatus.None;
                    ulong expected = d.Encode(expectedValue, negative, settings, ref expectedStatus, forcedOverflow);
                    ulong actual = FixedMathKernel.ScaleB(d.Raw(value), exponent, kind, state, out FixedStatus status);
                    string context = kind + " raw=" + value + " exponent=" + exponent + " " + rounding + " " + overflow;
                    Assert.AreEqual(expected, actual, context); Assert.AreEqual(expectedStatus, status, context);
                }
                foreach (ulong special in new[] { d.NaN, d.PositiveInfinity, d.NegativeInfinity, d.NegativeZero })
                foreach (int exponent in new[] { int.MinValue, -64, 0, 128, int.MaxValue })
                {
                    Assert.AreEqual(special, FixedMathKernel.ScaleB(special, exponent, kind, state, out FixedStatus status));
                    Assert.AreEqual(FixedStatus.None, status);
                }
            }
        }

        [TestCase(FixedKind.Signed32)]
        [TestCase(FixedKind.Unsigned32)]
        [TestCase(FixedKind.Signed64)]
        [TestCase(FixedKind.Unsigned64)]
        public void RoundMatchesExactDecimalTiesAndReportsRoundingPastFiniteEndpoints(FixedKind kind)
        {
            for (int p = 0; p <= (kind < FixedKind.Signed64 ? 9 : 18); p++)
            foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
            foreach (FixedOverflow overflow in Enum.GetValues(typeof(FixedOverflow)))
            {
                var settings = new FixedSettings(Math.Min(9, p), p, rounding, overflow); var state = new FixedRuntimeState(settings);
                var d = new Domain(kind, p);
                foreach (BigInteger value in new[] { BigInteger.Zero, BigInteger.One, d.Scale / 2 - 1, d.Scale / 2,
                    d.Scale / 2 + 1, 3 * d.Scale / 2, d.Max, d.Max - 1, d.Min, d.Min + 1 })
                {
                    if (value < d.Min || value > d.Max) continue;
                    for (int digits = 0; digits <= Math.Min(18, p + 1); digits++)
                    {
                        BigInteger factor = digits >= p ? BigInteger.One : BigInteger.Pow(10, p - digits);
                        BigInteger rounded = Quantize(value, factor, rounding, out bool inexact) * factor;
                        FixedStatus expectedStatus = inexact ? FixedStatus.Inexact : FixedStatus.None;
                        ulong expected = d.Encode(rounded, value.Sign < 0, settings, ref expectedStatus);
                        ulong actual = FixedMathKernel.Round(d.Raw(value), kind, state, digits, rounding, out FixedStatus status);
                        string context = kind + " p=" + p + " digits=" + digits + " raw=" + value + " " + rounding + " " + overflow;
                        Assert.AreEqual(expected, actual, context); Assert.AreEqual(expectedStatus, status, context);
                    }
                }
                foreach (int invalid in new[] { int.MinValue, -1, 19, int.MaxValue })
                {
                    Assert.AreEqual(d.NaN, FixedMathKernel.Round(1, kind, state, invalid, rounding, out FixedStatus status));
                    Assert.AreEqual(FixedStatus.InvalidDomain, status);
                }
            }
        }

        [TestCase(FixedKind.Signed32)]
        [TestCase(FixedKind.Unsigned32)]
        [TestCase(FixedKind.Signed64)]
        [TestCase(FixedKind.Unsigned64)]
        public void PeriodicHelpersMatchEuclideanModuloAtNegativeAndUnsignedExtremes(FixedKind kind)
        {
            foreach (int p in new[] { 0, 3, 9, kind < FixedKind.Signed64 ? 9 : 18 })
            foreach (FixedUnsignedNegative unsignedNegative in Enum.GetValues(typeof(FixedUnsignedNegative)))
            {
                var settings = new FixedSettings(Math.Min(9, p), p, unsignedNegative: unsignedNegative);
                var state = new FixedRuntimeState(settings); var d = new Domain(kind, p);
                foreach (BigInteger value in new[] { d.Min, d.Min + 1, BigInteger.Zero, BigInteger.One, d.Scale, d.Max - 1, d.Max })
                foreach (BigInteger length in new[] { BigInteger.One, d.Scale, d.Max })
                {
                    foreach (FixedScalar op in new[] { FixedScalar.Repeat, FixedScalar.PingPong })
                    {
                        BigInteger period = op == FixedScalar.Repeat ? length : 2 * length;
                        BigInteger expected = (value % period + period) % period;
                        if (op == FixedScalar.PingPong && expected > length) expected = period - expected;
                        Assert.AreEqual(d.Raw(expected), FixedScalarKernel.Evaluate(d.Raw(value), d.Raw(length), 0, op, kind, state, out FixedStatus status),
                            op + " " + kind + " p=" + p + " raw=" + value + " length=" + length);
                        Assert.AreEqual(FixedStatus.None, status);
                    }
                    BigInteger angle = (length - value) % (360 * d.Scale);
                    if (angle.Sign < 0) angle += 360 * d.Scale;
                    if (angle > 180 * d.Scale) angle -= 360 * d.Scale;
                    FixedStatus expectedStatus = FixedStatus.None;
                    ulong expectedAngle = d.Encode(angle, angle.Sign < 0, settings, ref expectedStatus);
                    Assert.AreEqual(expectedAngle, FixedScalarKernel.Evaluate(d.Raw(value), d.Raw(length), 0, FixedScalar.DeltaAngle, kind, state, out FixedStatus angleStatus));
                    Assert.AreEqual(expectedStatus, angleStatus);
                }
            }
        }
    }
}
