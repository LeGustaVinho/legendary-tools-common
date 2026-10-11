using System;
using System.Numerics;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedVectorOracleTests
    {
        private const long Minimum = long.MinValue + 2;
        private const long Maximum = long.MaxValue - 2;

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
        [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)]
        [TestCase(10)] [TestCase(11)] [TestCase(12)] [TestCase(13)] [TestCase(14)]
        [TestCase(15)] [TestCase(16)] [TestCase(17)] [TestCase(18)]
        public void WideGeometryMatchesIndependentRationalsAndIntegerRoots(int precision)
        {
            long scale = (long)BigInteger.Pow(10, precision);
            long[][] inputs = {
                new[] { 1L, -1L, 0L }, new[] { scale, scale * 2, -scale * 3 },
                new[] { Maximum, Maximum, 1L }, new[] { Maximum, -Maximum, 1L },
                new[] { Minimum, Maximum, Minimum }, new[] { Maximum, Maximum - 1, -1L }
            };
            foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
            foreach (FixedOverflow overflow in Enum.GetValues(typeof(FixedOverflow)))
            foreach (FixedArithmeticMode mode in new[] { FixedArithmeticMode.Portable, FixedArithmeticMode.Intrinsic })
            for (int dimensions = 2; dimensions <= 3; dimensions++)
            for (int sample = 0; sample < inputs.Length; sample++)
            {
                var settings = new FixedSettings(3, precision, rounding, overflow, arithmeticMode: mode);
                var state = new FixedRuntimeState(settings);
                long[] a = (long[])inputs[sample].Clone(), b = (long[])inputs[(sample + 1) % inputs.Length].Clone();
                if (dimensions == 2) a[2] = b[2] = 0;
                var av = Bits(a); var bv = Bits(b); BigInteger dot = Dot(a, b), squares = Dot(a, a);
                string context = precision + "/" + rounding + "/" + overflow + "/" + mode + "/" + dimensions + "/" + sample;
                Check(Quotient(dot, scale, settings), FixedVectorKernel.Dot(av, bv, dimensions, state, out FixedStatus status), status, context + " Dot");
                Check(Quotient(squares, scale, settings), FixedVectorKernel.Magnitude(av, dimensions, true, state, out status), status, context + " sqrMagnitude");
                Check(Root(squares, 1, false, settings), FixedVectorKernel.Magnitude(av, dimensions, false, state, out status), status, context + " magnitude");
                var normalized = FixedVectorKernel.Normalize(av, dimensions, state, out status);
                FixedStatus expectedStatus = FixedStatus.None;
                for (int axis = 0; axis < dimensions; axis++)
                {
                    var expected = Root((BigInteger)a[axis] * a[axis] * scale * scale, squares, a[axis] < 0, settings);
                    CheckRaw(expected.Raw, normalized.Get(axis), context + " normalized " + axis); expectedStatus |= expected.Status;
                }
                Assert.AreEqual(expectedStatus, status, context + " normalized status");
                var reflected = FixedVectorKernel.Reflect(av, bv, dimensions, state, out status); expectedStatus = 0;
                for (int axis = 0; axis < dimensions; axis++)
                {
                    var expected = Quotient((BigInteger)a[axis] * scale * scale - 2 * (BigInteger)b[axis] * dot, (BigInteger)scale * scale, settings);
                    CheckRaw(expected.Raw, reflected.Get(axis), context + " reflect " + axis); expectedStatus |= expected.Status;
                }
                Assert.AreEqual(expectedStatus, status, context + " reflect status");
                if (dimensions != 3) continue;
                var cross = FixedVectorKernel.Cross(av, bv, state, out status); expectedStatus = 0;
                for (int axis = 0; axis < 3; axis++)
                {
                    int next = (axis + 1) % 3, last = (axis + 2) % 3;
                    var expected = Quotient((BigInteger)a[next] * b[last] - (BigInteger)a[last] * b[next], scale, settings);
                    CheckRaw(expected.Raw, cross.Get(axis), context + " cross " + axis); expectedStatus |= expected.Status;
                }
                Assert.AreEqual(expectedStatus, status, context + " cross status");
                BigInteger denominator = Dot(b, b);
                for (int plane = 0; plane < 2; plane++)
                {
                    var projected = FixedVectorKernel.Project(av, bv, plane != 0, state, out status); expectedStatus = 0;
                    for (int axis = 0; axis < 3; axis++)
                    {
                        BigInteger numerator = (BigInteger)b[axis] * dot;
                        if (plane != 0) numerator = (BigInteger)a[axis] * denominator - numerator;
                        var expected = Quotient(numerator, denominator, settings);
                        CheckRaw(expected.Raw, projected.Get(axis), context + " project " + plane + "/" + axis); expectedStatus |= expected.Status;
                    }
                    Assert.AreEqual(expectedStatus, status, context + " project status");
                }
            }
        }

        private struct Expected { internal ulong Raw; internal FixedStatus Status; }
        private static FixedVectorBits Bits(long[] value) => new FixedVectorBits(unchecked((ulong)value[0]), unchecked((ulong)value[1]), unchecked((ulong)value[2]));
        private static BigInteger Dot(long[] a, long[] b) => (BigInteger)a[0] * b[0] + (BigInteger)a[1] * b[1] + (BigInteger)a[2] * b[2];
        private static Expected Quotient(BigInteger numerator, BigInteger denominator, FixedSettings settings)
        {
            bool negative = numerator.Sign < 0;
            BigInteger whole = BigInteger.DivRem(BigInteger.Abs(numerator), denominator, out BigInteger remainder);
            bool inexact = !remainder.IsZero;
            if (Increment(whole, (remainder * 2).CompareTo(denominator), inexact, negative, settings.Rounding)) whole++;
            return Pack(whole, negative, inexact, settings.Overflow);
        }
        private static Expected Root(BigInteger numerator, BigInteger denominator, bool negative, FixedSettings settings)
        {
            BigInteger value = numerator / denominator, root = IntegerRoot(value);
            bool inexact = root * root * denominator != numerator;
            int midpoint = (numerator * 4).CompareTo((2 * root + 1) * (2 * root + 1) * denominator);
            if (Increment(root, midpoint, inexact, negative, settings.Rounding)) root++;
            return Pack(root, negative, inexact, settings.Overflow);
        }
        private static BigInteger IntegerRoot(BigInteger value)
        {
            if (value < 2) return value;
            BigInteger current = value, next = (current + 1) / 2;
            while (next < current) { current = next; next = (current + value / current) / 2; }
            return current;
        }
        private static bool Increment(BigInteger whole, int midpoint, bool inexact, bool negative, FixedRounding rounding) => inexact &&
            (rounding == FixedRounding.TowardPositiveInfinity && !negative || rounding == FixedRounding.TowardNegativeInfinity && negative ||
             (rounding == FixedRounding.ToEven || rounding == FixedRounding.AwayFromZero) &&
             (midpoint > 0 || midpoint == 0 && (rounding == FixedRounding.AwayFromZero || !whole.IsEven)));
        private static Expected Pack(BigInteger magnitude, bool negative, bool inexact, FixedOverflow overflow)
        {
            FixedStatus status = inexact ? FixedStatus.Inexact : FixedStatus.None;
            BigInteger signed = negative ? -magnitude : magnitude;
            if (signed >= Minimum && signed <= Maximum)
                return new Expected { Raw = magnitude.IsZero && negative ? (ulong)(long.MaxValue - 1) : unchecked((ulong)(long)signed), Status = status };
            status |= FixedStatus.Overflow;
            if (overflow == FixedOverflow.NaN) return new Expected { Raw = 0x8000000000000000UL, Status = status };
            if (overflow == FixedOverflow.Infinity) return new Expected { Raw = negative ? 0x8000000000000001UL : 0x7FFFFFFFFFFFFFFFUL, Status = status };
            if (overflow == FixedOverflow.Saturate) signed = negative ? Minimum : Maximum;
            else
            {
                BigInteger width = (BigInteger)Maximum - Minimum + 1;
                BigInteger residue = (signed - Minimum) % width;
                signed = Minimum + (residue.Sign < 0 ? residue + width : residue);
            }
            return new Expected { Raw = unchecked((ulong)(long)signed), Status = status };
        }
        private static void Check(Expected expected, ulong actual, FixedStatus status, string context)
        { CheckRaw(expected.Raw, actual, context); Assert.AreEqual(expected.Status, status, context + " status"); }
        private static void CheckRaw(ulong expected, ulong actual, string context)
        {
            // Rational zero has no sign; dedicated contract/replay tests verify signed-zero bits.
            if (expected == 0 || expected == 0x7FFFFFFFFFFFFFFEUL)
                Assert.IsTrue(actual == 0 || actual == 0x7FFFFFFFFFFFFFFEUL, context);
            else Assert.AreEqual(expected, actual, context);
        }
    }
}
