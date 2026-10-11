using System;
using System.Numerics;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedMathTests
    {
        private static BigInteger Number(FixedBigUInt value)
        {
            BigInteger n = 0;
            for (int i = 15; i >= 0; i--) n = (n << 32) | value.Get(i);
            return n;
        }
        private static FixedBigUInt Wide(BigInteger n)
        {
            FixedBigUInt r = default;
            for (int i = 0; i < 16; i++) { r.Set(i, (uint)(n & uint.MaxValue)); n >>= 32; }
            return r;
        }

        [Test]
        public void GuardPrecisionMultiplyDivideShiftsAndSquareRootMatchBigInteger()
        {
            var random = new Random(48139);
            byte[] bytes = new byte[49];
            BigInteger mask = (BigInteger.One << 512) - 1;
            for (int i = 0; i < 160; i++)
            {
                random.NextBytes(bytes); bytes[48] = 0; BigInteger a = new BigInteger(bytes);
                random.NextBytes(bytes); bytes[48] = 0; BigInteger b = new BigInteger(bytes) + 1;
                FixedBigUInt x = Wide(a), y = Wide(b);
                Assert.AreEqual((a + b) & mask, Number(FixedBigUInt.Add(x, y)));
                Assert.AreEqual((a * b >> 256) & mask, Number(FixedBigUInt.Multiply(x, y, 256)));
                Assert.AreEqual(a * (BigInteger.One << 128) / b, Number(FixedBigUInt.Divide(x, y, 128)));
                Assert.AreEqual(a % b, Number(FixedBigUInt.Mod(x, y)));
                BigInteger n = a << 256, root = Number(FixedBigUInt.Sqrt(x, 256));
                Assert.LessOrEqual(root * root, n);
                Assert.Greater((root + 1) * (root + 1), n);
            }
        }

        [TestCase(FixedMathProfile.Accurate, 1)]
        [TestCase(FixedMathProfile.Fast, 4)]
        public void TranscendentalsMeetTheRawErrorBoundAgainstHighPrecisionReferences(FixedMathProfile profile, int error)
        {
            string[] lines = FixedMathReference.Vectors.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int checkedCount = 0;
            foreach (string line in lines)
            {
                string[] fields = line.Split(',');
                FixedKind kind = (FixedKind)int.Parse(fields[0]); int p = int.Parse(fields[1]);
                var state = new FixedRuntimeState(new FixedSettings(Math.Min(9, p), p, mathProfile: profile));
                var format = new FixedFormat(kind, state);
                var operation = (FixedUnary)Enum.Parse(typeof(FixedUnary), fields[2]);
                ulong input = ulong.Parse(fields[3]), expected = ulong.Parse(fields[4]);
                FixedStatus expectedStatus = (FixedStatus)uint.Parse(fields[5]);
                ulong actual = FixedMathKernel.Unary(input, kind, state, operation, out FixedStatus status);
                FixedDecoded e = format.Decode(expected), a = format.Decode(actual);
                string context = profile + ": " + line + " actual=" + actual + " status=" + status;
                if (e.IsFinite && a.IsFinite)
                {
                    BigInteger signedExpected = e.Negative ? -(BigInteger)e.Magnitude : e.Magnitude;
                    BigInteger signedActual = a.Negative ? -(BigInteger)a.Magnitude : a.Magnitude;
                    Assert.LessOrEqual(BigInteger.Abs(signedActual - signedExpected), new BigInteger(error), context);
                }
                else Assert.AreEqual(expected, actual, context);
                // Bounds deliberately include a tolerance of one raw unit around zero and the finite endpoints.
                if (e.IsFinite && a.IsFinite || expectedStatus != FixedStatus.Overflow)
                    Assert.AreEqual(expectedStatus, status & ~FixedStatus.Inexact, context);
                checkedCount++;
            }
            Assert.Greater(checkedCount, 5000);
        }

        [Test]
        public void FusedMultiplyAddRoundsOnceAndCanCancelAnOverflowingProduct()
        {
            var state = new FixedRuntimeState(FixedSettings.Default);
            var f = new FixedFormat(FixedKind.Signed64, state);
            // 1.001 * 1.500 + .001 = 1.5025: one ties-to-even rounding produces 1.502.
            Assert.AreEqual(1502UL, FixedMathKernel.FusedMultiplyAdd(1001, 1500, 1, FixedKind.Signed64, state, out _));
            ulong max = f.MaxPositive;
            ulong result = FixedMathKernel.FusedMultiplyAdd(max, 2000, f.Finite(max, true), FixedKind.Signed64, state, out FixedStatus status);
            Assert.AreEqual(max, result); Assert.IsTrue(status.IsSuccess());
            var unsigned = new FixedRuntimeState(new FixedSettings(3, 3, overflow: FixedOverflow.WrapFinite));
            var uf = new FixedFormat(FixedKind.Unsigned64, unsigned);
            ulong actual = FixedMathKernel.FusedMultiplyAdd(uf.MaxPositive, uf.MaxPositive, uf.MaxPositive, FixedKind.Unsigned64, unsigned, out _);
            BigInteger numerator = (BigInteger)uf.MaxPositive * uf.MaxPositive + (BigInteger)uf.MaxPositive * uf.Scale;
            Assert.Greater(numerator, BigInteger.One << 128, "Exercise the carry beyond UInt128 before scaling.");
            BigInteger q = BigInteger.DivRem(numerator, uf.Scale, out BigInteger remainder);
            if (remainder * 2 > uf.Scale || remainder * 2 == uf.Scale && !q.IsEven) q++;
            Assert.AreEqual((ulong)(q % uf.FiniteCount), actual);
        }

        [Test]
        public void UnsignedInterpolationUsesSignedIntermediateValuesAndSpecialCasesAreExplicit()
        {
            FixedRuntime.Initialize();
            Assert.AreEqual(7000UL, FixedMath.Lerp(UFixed64.FromInt(10), UFixed64.FromInt(4), UFixed64.FromDecimal(.5m)).RawValue);
            Assert.AreEqual(124, FixedMath.Lerp(Fixed32.FromRaw(123), Fixed32.FromRaw(124), Fixed32.FromDecimal(.5m)).RawValue);
            Assert.AreEqual(124, FixedMath.SmoothStep(Fixed32.FromRaw(123), Fixed32.FromRaw(124), Fixed32.FromDecimal(.5m)).RawValue);
            Assert.AreEqual(0, FixedMath.Repeat(Fixed32.FromRaw(1234), Fixed32.FromRaw(617), out FixedStatus repeatStatus).RawValue);
            Assert.AreEqual(FixedStatus.None, repeatStatus);
            Assert.AreEqual(1571, FixedMath.Atan2(Fixed32.One, Fixed32.Zero).RawValue);
            Assert.AreEqual(-3142, FixedMath.Atan2(Fixed32.NegativeZero, -Fixed32.One).RawValue);
            Assert.AreEqual(Fixed32.NegativeZero.RawValue, FixedMath.Sin(Fixed32.NegativeZero).RawValue);
            Assert.AreEqual(0, FixedMath.Sign(Fixed32.NaN, out FixedStatus status));
            Assert.AreEqual(FixedStatus.InvalidDomain, status);
            Assert.AreEqual(1, FixedMath.BitIncrement(Fixed32.Zero).RawValue);
            Assert.IsTrue(UFixed32.IsNegativeInfinity(FixedMath.BitDecrement(UFixed32.Zero)));
            Assert.AreEqual(2000, FixedMath.Pow((Fixed32)4, Fixed32.FromDecimal(.5m)).RawValue);
            Assert.AreEqual(3000, FixedMath.Repeat(Fixed32.FromInt(-1), Fixed32.FromInt(4)).RawValue);
            Assert.AreEqual(20000, FixedMath.DeltaAngle(Fixed32.FromInt(350), Fixed32.FromInt(10)).RawValue);
        }
    }
}
