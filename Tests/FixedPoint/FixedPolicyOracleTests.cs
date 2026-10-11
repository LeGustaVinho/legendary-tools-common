using System;
using System.Numerics;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedPolicyOracleTests
    {
        // Derive the reference domain from the documented wire layout, not FixedFormat/Pack.
        private sealed class Domain
        {
            internal readonly bool Unsigned;
            internal readonly ulong Mask, NaN, PositiveInfinity, NegativeInfinity, NegativeZero, Scale;
            internal readonly BigInteger Min, Max, Count;
            internal Domain(FixedKind kind, int precision)
            {
                int bits = kind < FixedKind.Signed64 ? 32 : 64;
                Unsigned = kind == FixedKind.Unsigned32 || kind == FixedKind.Unsigned64;
                Mask = bits == 32 ? uint.MaxValue : ulong.MaxValue;
                Scale = (ulong)BigInteger.Pow(10, precision);
                BigInteger half = BigInteger.One << (bits - 1);
                Min = Unsigned ? BigInteger.Zero : -half + 2;
                Max = Unsigned ? (BigInteger.One << bits) - 5 : half - 3;
                Count = Max - Min + 1;
                NaN = Unsigned ? Mask : (ulong)half;
                NegativeInfinity = Unsigned ? Mask - 1 : (ulong)half + 1;
                PositiveInfinity = Unsigned ? Mask - 2 : (ulong)half - 1;
                NegativeZero = Unsigned ? Mask - 3 : (ulong)half - 2;
            }
            internal ulong Raw(BigInteger value, bool negativeZero = false) => value.IsZero && negativeZero ? NegativeZero :
                (ulong)(value & Mask);
        }

        private static BigInteger Round(BigInteger numerator, BigInteger denominator, FixedRounding rounding,
            out bool negative, out bool inexact)
        {
            negative = numerator.Sign * denominator.Sign < 0;
            BigInteger d = BigInteger.Abs(denominator);
            BigInteger q = BigInteger.DivRem(BigInteger.Abs(numerator), d, out BigInteger r);
            inexact = !r.IsZero;
            bool increment = rounding == FixedRounding.TowardPositiveInfinity && !negative ||
                rounding == FixedRounding.TowardNegativeInfinity && negative;
            if (rounding == FixedRounding.ToEven || rounding == FixedRounding.AwayFromZero)
                increment = r * 2 > d || r * 2 == d && (rounding == FixedRounding.AwayFromZero || !q.IsEven);
            if (inexact && increment) q++;
            return negative ? -q : q;
        }

        private static ulong Encode(BigInteger value, bool negativeZero, Domain d, FixedSettings settings, ref FixedStatus status)
        {
            if (value.IsZero) return d.Raw(value, negativeZero);
            if (d.Unsigned && value.Sign < 0)
            {
                status |= FixedStatus.NegativeResult;
                if (settings.UnsignedNegative == FixedUnsignedNegative.NaN) return d.NaN;
                if (settings.UnsignedNegative == FixedUnsignedNegative.SaturateZero) return 0;
            }
            else if (value < d.Min || value > d.Max)
            {
                status |= FixedStatus.Overflow;
                if (settings.Overflow == FixedOverflow.NaN) return d.NaN;
                if (settings.Overflow == FixedOverflow.Infinity) return value.Sign < 0 ? d.NegativeInfinity : d.PositiveInfinity;
                if (settings.Overflow == FixedOverflow.Saturate) return d.Raw(value.Sign < 0 ? d.Min : d.Max);
            }
            else return d.Raw(value);
            BigInteger residue = ((value - d.Min) % d.Count + d.Count) % d.Count + d.Min;
            return d.Raw(residue);
        }

        private static BigInteger Input(Domain d, int index, bool negative)
        {
            BigInteger limit = negative ? -d.Min : d.Max;
            BigInteger magnitude = index % 8 == 0 ? 0 : index % 8 == 1 ? 1 : index % 8 == 2 ? d.Scale - 1 :
                index % 8 == 3 ? d.Scale : index % 8 == 4 ? d.Scale + 1 : index % 8 == 5 ? limit / 2 + 1 :
                index % 8 == 6 ? limit - 1 : limit;
            magnitude = BigInteger.Min(limit, magnitude);
            return negative ? -magnitude : magnitude;
        }

        [TestCase(FixedKind.Signed32)]
        [TestCase(FixedKind.Unsigned32)]
        [TestCase(FixedKind.Signed64)]
        [TestCase(FixedKind.Unsigned64)]
        public void FullRangeArithmeticAndFmaMatchRationalsForEveryPrecisionAndPolicy(FixedKind kind)
        {
            int maxPrecision = kind < FixedKind.Signed64 ? 9 : 18;
            for (int p = 0; p <= maxPrecision; p++)
            foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
            foreach (FixedOverflow overflow in Enum.GetValues(typeof(FixedOverflow)))
            foreach (FixedUnsignedNegative unsignedNegative in Enum.GetValues(typeof(FixedUnsignedNegative)))
            {
                var settings = new FixedSettings(Math.Min(9, p), p, rounding, overflow, unsignedNegative: unsignedNegative);
                var state = new FixedRuntimeState(settings); var d = new Domain(kind, p);
                for (int i = 0; i < 16; i++)
                {
                    bool an = !d.Unsigned && (i & 1) != 0, bn = !d.Unsigned && (i & 2) != 0, cn = !d.Unsigned && (i & 4) != 0;
                    BigInteger a = Input(d, i, an), b = Input(d, i + 5, bn), c = Input(d, i + 7, cn);
                    if (b.IsZero) { b = bn ? -BigInteger.One : BigInteger.One; }
                    ulong ar = d.Raw(a, an), br = d.Raw(b), cr = d.Raw(c, cn);
                    foreach (FixedBinary operation in Enum.GetValues(typeof(FixedBinary)))
                    {
                        BigInteger expected; bool negativeZero; bool inexact = false;
                        if (operation == FixedBinary.Add || operation == FixedBinary.Subtract)
                        {
                            bool effectiveBSign = bn ^ (operation == FixedBinary.Subtract);
                            expected = operation == FixedBinary.Add ? a + b : a - b;
                            negativeZero = an == effectiveBSign ? an : rounding == FixedRounding.TowardNegativeInfinity;
                        }
                        else if (operation == FixedBinary.Multiply || operation == FixedBinary.Divide)
                        {
                            expected = Round(operation == FixedBinary.Multiply ? a * b : a * d.Scale,
                                operation == FixedBinary.Multiply ? d.Scale : b, rounding, out bool sign, out inexact);
                            negativeZero = a.IsZero ? an ^ bn : sign;
                        }
                        else
                        {
                            expected = operation == FixedBinary.Remainder ? a % b :
                                a - Round(a, b, FixedRounding.ToEven, out _, out _) * b;
                            negativeZero = an;
                        }
                        FixedStatus expectedStatus = inexact ? FixedStatus.Inexact : FixedStatus.None;
                        ulong expectedRaw = Encode(expected, negativeZero, d, settings, ref expectedStatus);
                        ulong actualRaw = FixedKernel.Binary(ar, br, kind, state, operation, out FixedStatus actualStatus);
                        string context = kind + " p=" + p + " " + rounding + " " + overflow + " " + unsignedNegative + " " + operation + " i=" + i;
                        Assert.AreEqual(expectedRaw, actualRaw, context);
                        Assert.AreEqual(expectedStatus, actualStatus, context + " status");
                    }
                    BigInteger numerator = a * b + c * d.Scale;
                    BigInteger fused = Round(numerator, d.Scale, rounding, out bool fusedSign, out bool fusedInexact);
                    bool fusedZeroSign = numerator.IsZero ? an ^ bn == cn ? cn : rounding == FixedRounding.TowardNegativeInfinity : fusedSign;
                    FixedStatus fusedStatus = fusedInexact ? FixedStatus.Inexact : FixedStatus.None;
                    ulong fusedRaw = Encode(fused, fusedZeroSign, d, settings, ref fusedStatus);
                    Assert.AreEqual(fusedRaw, FixedMathKernel.FusedMultiplyAdd(ar, br, cr, kind, state, out FixedStatus actualFusedStatus),
                        kind + " FMA p=" + p + " " + rounding + " " + overflow + " " + unsignedNegative + " i=" + i);
                    Assert.AreEqual(fusedStatus, actualFusedStatus);
                }
            }
        }

        [Test]
        public void CrossWidthAndSignednessConversionsCoverEveryPairOfDecimalScales()
        {
            for (int p32 = 0; p32 <= 9; p32++)
            for (int p64 = 0; p64 <= 18; p64++)
            foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
            {
                var settings = new FixedSettings(p32, p64, rounding, (FixedOverflow)((p32 + p64) % 4),
                    unsignedNegative: (FixedUnsignedNegative)((p32 + p64 + (int)rounding) % 3));
                var state = new FixedRuntimeState(settings);
                foreach (FixedKind from in Enum.GetValues(typeof(FixedKind)))
                foreach (FixedKind to in Enum.GetValues(typeof(FixedKind)))
                {
                    var source = new Domain(from, from < FixedKind.Signed64 ? p32 : p64);
                    var destination = new Domain(to, to < FixedKind.Signed64 ? p32 : p64);
                    ulong[] sourceSpecials = { source.NaN, source.NegativeInfinity, source.PositiveInfinity, source.NegativeZero };
                    ulong[] destinationSpecials = { destination.NaN, destination.NegativeInfinity, destination.PositiveInfinity, destination.NegativeZero };
                    for (int j = 0; j < sourceSpecials.Length; j++)
                    {
                        Assert.AreEqual(destinationSpecials[j], FixedKernel.Convert(sourceSpecials[j], from, to, state, out FixedStatus specialStatus));
                        Assert.AreEqual(FixedStatus.None, specialStatus);
                    }
                    for (int i = 0; i < 8; i++)
                    {
                        bool negative = !source.Unsigned && (i & 1) != 0;
                        BigInteger input = Input(source, i, negative);
                        BigInteger q = Round(input * destination.Scale, source.Scale, rounding, out bool sign, out bool inexact);
                        FixedStatus expectedStatus = inexact ? FixedStatus.Inexact : FixedStatus.None;
                        ulong expected = Encode(q, input.IsZero ? negative : sign, destination, settings, ref expectedStatus);
                        ulong actual = FixedKernel.Convert(source.Raw(input, negative), from, to, state, out FixedStatus status);
                        Assert.AreEqual(expected, actual, from + " -> " + to + " p32=" + p32 + " p64=" + p64 + " " + rounding + " i=" + i);
                        Assert.AreEqual(expectedStatus, status);
                    }
                }
            }
        }

        [Test]
        public void FactoriesRetainFullUnsignedNumeratorsAndAllZeroDivisionPolicies()
        {
            foreach (FixedKind kind in Enum.GetValues(typeof(FixedKind)))
            for (int p = 0; p <= (kind < FixedKind.Signed64 ? 9 : 18); p++)
            foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
            foreach (FixedOverflow overflow in Enum.GetValues(typeof(FixedOverflow)))
            foreach (FixedUnsignedNegative unsignedNegative in Enum.GetValues(typeof(FixedUnsignedNegative)))
            {
                var settings = new FixedSettings(Math.Min(9, p), p, rounding, overflow, unsignedNegative: unsignedNegative);
                var state = new FixedRuntimeState(settings); var d = new Domain(kind, p);
                foreach (ulong divisor in new[] { 1UL, 2UL, 3UL, 1000UL, ulong.MaxValue })
                foreach (bool negative in new[] { false, true })
                {
                    BigInteger q = Round((negative ? -(BigInteger)ulong.MaxValue : ulong.MaxValue) * d.Scale, divisor,
                        rounding, out bool sign, out bool inexact);
                    FixedStatus status = inexact ? FixedStatus.Inexact : FixedStatus.None;
                    ulong expected = Encode(q, sign, d, settings, ref status);
                    Assert.AreEqual(expected, FixedKernel.FromRatio(ulong.MaxValue, divisor, negative, kind, state, out FixedStatus actualStatus));
                    Assert.AreEqual(status, actualStatus);
                }
                foreach (FixedZeroDivision zeroDivision in Enum.GetValues(typeof(FixedZeroDivision)))
                {
                    var zeroState = new FixedRuntimeState(new FixedSettings(Math.Min(9, p), p, zeroDivision: zeroDivision));
                    foreach (bool negative in new[] { false, true })
                    {
                        ulong expected = zeroDivision == FixedZeroDivision.NaN ? d.NaN : zeroDivision == FixedZeroDivision.Saturate ?
                            d.Raw(negative ? d.Min : d.Max) : negative ? d.NegativeInfinity : d.PositiveInfinity;
                        Assert.AreEqual(expected, FixedKernel.FromRatio(1, 0, negative, kind, zeroState, out FixedStatus status));
                        Assert.AreEqual(FixedStatus.DivisionByZero, status);
                        Assert.AreEqual(d.NaN, FixedKernel.FromRatio(0, 0, negative, kind, zeroState, out status));
                        Assert.AreEqual(FixedStatus.DivisionByZero | FixedStatus.InvalidDomain, status);
                    }
                }
            }
        }
    }
}
