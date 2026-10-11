using System;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedIeeeTests
    {
        [Test]
        public void InputConversionQuantizesTheExactIeeeValue()
        {
            var state = new FixedRuntimeState(FixedSettings.Default);
            Assert.AreEqual(1234UL, FixedIeee.From(1.234f, FixedKind.Signed32, state, out _));
            Assert.AreEqual(1234UL, FixedIeee.From(1.234, FixedKind.Signed32, state, out _));
            Assert.AreEqual(1234UL, FixedIeee.From(1.2345, FixedKind.Signed32, state, out _));
            Assert.AreEqual(1236UL, FixedIeee.From(1.2355, FixedKind.Signed32, state, out _));
            foreach (FixedKind kind in Enum.GetValues(typeof(FixedKind)))
            {
                var f = new FixedFormat(kind, state);
                Assert.AreEqual(f.NegativeZero, FixedIeee.From(FixedIeee.Single(0x80000000), kind, state, out _));
                Assert.AreEqual(f.NegativeZero, FixedIeee.From(-double.Epsilon, kind, state, out _));
                Assert.AreEqual(f.PositiveInfinity, FixedIeee.From(float.PositiveInfinity, kind, state, out _));
                Assert.AreEqual(f.NegativeInfinity, FixedIeee.From(double.NegativeInfinity, kind, state, out _));
                Assert.AreEqual(f.NaN, FixedIeee.From(float.NaN, kind, state, out _));
                Assert.AreEqual(0x80000000U, FixedIeee.Bits(FixedIeee.ToSingle(f.NegativeZero, kind, state)));
                Assert.AreEqual(0x8000000000000000UL, FixedIeee.Bits(FixedIeee.ToDouble(f.NegativeZero, kind, state)));
            }
        }

        [Test]
        public void OutputConversionsUseCorrectlyRoundedIntegerRatios()
        {
            var random = new Random(42431);
            foreach (int digits in new[] { 0, 3, 9, 18 })
            {
                var state = new FixedRuntimeState(new FixedSettings(Math.Min(9, digits), digits));
                foreach (FixedKind kind in Enum.GetValues(typeof(FixedKind)))
                {
                    var f = new FixedFormat(kind, state);
                    for (int i = 0; i < 600; i++)
                    {
                        ulong magnitude = (((ulong)(uint)random.Next() << 33) ^ ((ulong)(uint)random.Next() << 2)) % f.MaxPositive;
                        bool negative = !f.Unsigned && (i & 1) != 0;
                        ulong raw = f.Finite(magnitude, negative);
                        decimal exact = (decimal)magnitude / f.Scale;
                        if (negative) exact = -exact;
                        Assert.AreEqual(FixedIeee.Bits((float)exact), FixedIeee.Bits(FixedIeee.ToSingle(raw, kind, state)));
                        // decimal->double in Mono may double-round; the exact-ratio oracle is tested separately for hard cases.
                        double expected = (double)exact, actual = FixedIeee.ToDouble(raw, kind, state);
                        Assert.LessOrEqual(Math.Abs(actual - expected), Math.Abs(expected) * 3e-16 + 1e-30);
                    }
                }
            }
        }

        [Test]
        public void HugeIeeeInputsHonorFiniteWrapWithoutAllocatingHugeIntegers()
        {
            var state = new FixedRuntimeState(new FixedSettings(0, 0, overflow: FixedOverflow.WrapFinite,
                unsignedNegative: FixedUnsignedNegative.WrapFinite));
            foreach (FixedKind kind in Enum.GetValues(typeof(FixedKind)))
            {
                var f = new FixedFormat(kind, state);
                ulong result = FixedIeee.From(double.MaxValue, kind, state, out FixedStatus status);
                Assert.IsTrue(f.Decode(result).IsFinite);
                Assert.IsTrue((status & FixedStatus.Overflow) != 0);
                // The same exact power-of-two input must be independent of the multiplication backend.
                var portable = new FixedRuntimeState(new FixedSettings(0, 0, overflow: FixedOverflow.WrapFinite,
                    unsignedNegative: FixedUnsignedNegative.WrapFinite, arithmeticMode: FixedArithmeticMode.Portable));
                Assert.AreEqual(result, FixedIeee.From(double.MaxValue, kind, portable, out _));
            }
        }
    }
}
