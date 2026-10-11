using System;
using System.Numerics;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedKernelTests
    {
        [Test]
        public void WideMultiplyAndDivideMatchBigIntegerAcrossTheUnsignedRange()
        {
            var random = new Random(381718);
            for (int i = 0; i < 1800; i++)
            {
                ulong a = Next(random), b = Next(random), divisor = Next(random) | 1;
                BigInteger product = (BigInteger)a * b;
                foreach (FixedArithmeticMode mode in new[] { FixedArithmeticMode.Portable, FixedArithmeticMode.Intrinsic })
                {
                    FixedUInt128 wide = FixedUInt128.Multiply(a, b, mode);
                    Assert.AreEqual(product, ToBig(wide), "product " + i + " " + mode);
                    FixedUInt128 q = FixedUInt128.Divide(wide, divisor, out ulong remainder);
                    Assert.AreEqual(product / divisor, ToBig(q), "quotient " + i);
                    Assert.AreEqual(product % divisor, (BigInteger)remainder, "remainder " + i);
                }
            }
        }

        [TestCase(FixedKind.Signed32)]
        [TestCase(FixedKind.Unsigned32)]
        [TestCase(FixedKind.Signed64)]
        [TestCase(FixedKind.Unsigned64)]
        public void ArithmeticMatchesAnIndependentRationalOracle(FixedKind kind)
        {
            var state = new FixedRuntimeState(FixedSettings.Default);
            var format = new FixedFormat(kind, state);
            var random = new Random(53281 + (int)kind);
            foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
            {
                state = new FixedRuntimeState(new FixedSettings(rounding: rounding));
                for (int i = 0; i < 700; i++)
                {
                    ulong aMagnitude = Next(random) % 2000000000;
                    ulong bMagnitude = Next(random) % 2000000000 + 1;
                    bool aNegative = !format.Unsigned && (i & 1) != 0;
                    bool bNegative = !format.Unsigned && (i & 2) != 0;
                    ulong a = format.Finite(aMagnitude, aNegative), b = format.Finite(bMagnitude, bNegative);
                    foreach (FixedBinary operation in new[] { FixedBinary.Add, FixedBinary.Subtract, FixedBinary.Multiply, FixedBinary.Divide, FixedBinary.Remainder })
                    {
                        BigInteger av = aNegative ? -(BigInteger)aMagnitude : aMagnitude;
                        BigInteger bv = bNegative ? -(BigInteger)bMagnitude : bMagnitude;
                        BigInteger expected;
                        switch (operation)
                        {
                            case FixedBinary.Add: expected = av + bv; break;
                            case FixedBinary.Subtract: expected = av - bv; break;
                            case FixedBinary.Multiply: expected = Round(av * bv, format.Scale, rounding); break;
                            case FixedBinary.Divide: expected = Round(av * format.Scale, bv, rounding); break;
                            default: expected = av % bv; break;
                        }
                        ulong actual = FixedKernel.Binary(a, b, kind, state, operation, out FixedStatus status);
                        FixedDecoded decoded = format.Decode(actual);
                        if (format.Unsigned && expected < 0)
                        { Assert.IsTrue(decoded.IsNaN); Assert.IsTrue((status & FixedStatus.NegativeResult) != 0); }
                        else if (expected > format.MaxPositive || expected < -(BigInteger)format.MaxNegative)
                        { Assert.AreEqual(expected.Sign < 0 ? FixedClass.NegativeInfinity : FixedClass.PositiveInfinity, decoded.Class); }
                        else
                        {
                            Assert.IsTrue(decoded.IsFinite);
                            Assert.AreEqual(expected, decoded.Negative ? -(BigInteger)decoded.Magnitude : decoded.Magnitude,
                                kind + " " + operation + " " + rounding + " " + i);
                        }
                    }
                }
            }
        }

        [Test]
        public void Unsigned64RetainsValuesAboveLongMaxValue()
        {
            var state = new FixedRuntimeState(new FixedSettings(0, 0));
            var f = new FixedFormat(FixedKind.Unsigned64, state);
            ulong a = (1UL << 63) + 9321;
            Assert.AreEqual(a + 22, FixedKernel.Binary(a, 22, f.Kind, state, FixedBinary.Add, out _));
            Assert.AreEqual(a - 22, FixedKernel.Binary(a, 22, f.Kind, state, FixedBinary.Subtract, out _));
            Assert.AreEqual(a / 3 + (a % 3 >= 2 ? 1UL : 0), FixedKernel.Binary(a, 3, f.Kind, state, FixedBinary.Divide, out _));
            Assert.AreEqual(f.PositiveInfinity, FixedKernel.Binary(a, 2, f.Kind, state, FixedBinary.Multiply, out FixedStatus status));
            Assert.IsTrue((status & FixedStatus.Overflow) != 0);
        }

        [Test]
        public void FiniteWrapNeverProducesReservedCodesAndMatchesEuclideanModulo()
        {
            foreach (FixedKind kind in Enum.GetValues(typeof(FixedKind)))
            {
                var state = new FixedRuntimeState(new FixedSettings(0, 0, overflow: FixedOverflow.WrapFinite,
                    unsignedNegative: FixedUnsignedNegative.WrapFinite));
                var f = new FixedFormat(kind, state);
                BigInteger min = -(BigInteger)f.MaxNegative, count = f.FiniteCount;
                foreach (BigInteger value in new[] { min - 1, (BigInteger)f.MaxPositive + 1, BigInteger.One << 120, -(BigInteger.One << 120) })
                {
                    BigInteger magnitude = BigInteger.Abs(value);
                    var wide = new FixedUInt128((ulong)(magnitude & ulong.MaxValue), (ulong)(magnitude >> 64));
                    FixedStatus status = FixedStatus.None;
                    ulong raw = FixedKernel.Pack(wide, value.Sign < 0, f, state.Settings, ref status);
                    FixedDecoded actual = f.Decode(raw);
                    BigInteger expected = ((value - min) % count + count) % count + min;
                    Assert.IsTrue(actual.IsFinite);
                    Assert.AreEqual(expected, actual.Negative ? -(BigInteger)actual.Magnitude : actual.Magnitude);
                }
            }
        }

        [Test]
        public void SpecialValuesHaveFloatEqualityOrderingAndSignedZeros()
        {
            var state = new FixedRuntimeState(FixedSettings.Default);
            foreach (FixedKind kind in Enum.GetValues(typeof(FixedKind)))
            {
                var f = new FixedFormat(kind, state);
                Assert.IsFalse(FixedKernel.Equal(f.NaN, f.NaN, kind, state, false));
                Assert.IsTrue(FixedKernel.Equal(f.NaN, f.NaN, kind, state, true));
                Assert.IsTrue(FixedKernel.Equal(0, f.NegativeZero, kind, state, false));
                Assert.AreEqual(FixedKernel.Hash(0, kind, state), FixedKernel.Hash(f.NegativeZero, kind, state));
                Assert.Less(FixedKernel.Compare(f.NaN, f.NegativeInfinity, kind, state), 0);
                Assert.Less(FixedKernel.Compare(f.NegativeInfinity, f.MinValue, kind, state), 0);
                Assert.Less(FixedKernel.Compare(f.MaxPositive, f.PositiveInfinity, kind, state), 0);
                Assert.AreEqual(f.NegativeZero, FixedKernel.Binary(0, f.NegativeZero, kind, state, FixedBinary.Multiply, out _));
                Assert.AreEqual(f.NegativeInfinity, FixedKernel.Binary(f.Scale, f.NegativeZero, kind, state, FixedBinary.Divide, out _));
                Assert.AreEqual(f.NaN, FixedKernel.Binary(0, 0, kind, state, FixedBinary.Divide, out _));
            }
        }

        private static ulong Next(Random random) => ((ulong)(uint)random.Next() << 33) ^ ((ulong)(uint)random.Next() << 2) ^ (uint)random.Next(4);
        private static BigInteger ToBig(FixedUInt128 value) => ((BigInteger)value.Hi << 64) | value.Lo;

        private static BigInteger Round(BigInteger numerator, BigInteger denominator, FixedRounding rounding)
        {
            bool negative = numerator.Sign * denominator.Sign < 0;
            BigInteger q = BigInteger.DivRem(BigInteger.Abs(numerator), BigInteger.Abs(denominator), out BigInteger rem);
            BigInteger d = BigInteger.Abs(denominator);
            bool increment = rem != 0 && (rounding == FixedRounding.TowardPositiveInfinity && !negative ||
                rounding == FixedRounding.TowardNegativeInfinity && negative ||
                (rounding == FixedRounding.ToEven || rounding == FixedRounding.AwayFromZero) &&
                (rem * 2 > d || rem * 2 == d && (rounding == FixedRounding.AwayFromZero || !q.IsEven)));
            if (increment) q++;
            return negative ? -q : q;
        }
    }
}
