using System;
using System.Globalization;
using System.Numerics;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedBinaryMathEdgeTests
    {
        [TestCase(FixedMathProfile.Accurate, 1)]
        [TestCase(FixedMathProfile.Fast, 4)]
        public void PowLogBaseAndAtan2MatchIndependentTwoHundredDigitReferences(FixedMathProfile profile, int budget)
        {
            int checkedCount = 0, failures = 0;
            var examples = new List<string>();
            foreach (string line in FixedBinaryMathReference.Vectors.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = line.Split(',');
                FixedKind kind = (FixedKind)int.Parse(fields[0], CultureInfo.InvariantCulture);
                int p = int.Parse(fields[1], CultureInfo.InvariantCulture);
                FixedRounding rounding = (FixedRounding)int.Parse(fields[2], CultureInfo.InvariantCulture);
                var state = new FixedRuntimeState(new FixedSettings(Math.Min(9, p), p, rounding, mathProfile: profile));
                var f = new FixedFormat(kind, state);
                ulong a = ulong.Parse(fields[4], CultureInfo.InvariantCulture), b = ulong.Parse(fields[5], CultureInfo.InvariantCulture);
                ulong expected = ulong.Parse(fields[6], CultureInfo.InvariantCulture);
                FixedStatus expectedStatus = (FixedStatus)uint.Parse(fields[7], CultureInfo.InvariantCulture);
                FixedStatus status;
                ulong actual = fields[3] == "Pow" ? FixedMathKernel.Pow(a, b, kind, state, out status) :
                    fields[3] == "Log" ? FixedMathKernel.Log(a, b, kind, state, out status) : FixedMathKernel.Atan2(a, b, kind, state, out status);
                FixedDecoded x = f.Decode(actual), e = f.Decode(expected);
                bool same;
                if (e.IsFinite && x.IsFinite)
                {
                    BigInteger av = x.Negative ? -(BigInteger)x.Magnitude : x.Magnitude;
                    BigInteger ev = e.Negative ? -(BigInteger)e.Magnitude : e.Magnitude;
                    same = BigInteger.Abs(av - ev) <= budget && (status & ~FixedStatus.Inexact) == expectedStatus;
                }
                else same = actual == expected && (status & ~FixedStatus.Inexact) == expectedStatus;
                if (!same)
                {
                    failures++;
                    if (examples.Count < 30) examples.Add(line + " actual=" + actual + " status=" + status);
                }
                checkedCount++;
            }
            TestContext.Progress.WriteLine("FIXED_BINARY_REFERENCE profile=" + profile + " checked=" + checkedCount + " failures=" + failures);
            Assert.Greater(checkedCount, 10000);
            Assert.AreEqual(0, failures, string.Join("\n", examples));
        }

        [TestCase(FixedKind.Signed32)]
        [TestCase(FixedKind.Unsigned32)]
        [TestCase(FixedKind.Signed64)]
        [TestCase(FixedKind.Unsigned64)]
        public void BinarySpecialValuesAndSignedZerosMatchFloatRules(FixedKind kind)
        {
            foreach (int p in new[] { 0, 3, 9, kind < FixedKind.Signed64 ? 9 : 18 })
            {
                var state = new FixedRuntimeState(new FixedSettings(Math.Min(9, p), p)); var f = new FixedFormat(kind, state);
                foreach (ulong zero in new[] { 0UL, f.NegativeZero })
                {
                    Assert.AreEqual(f.Scale, FixedMathKernel.Pow(f.NaN, zero, kind, state, out FixedStatus status));
                    Assert.AreEqual(FixedStatus.None, status);
                    Assert.AreEqual(f.Scale, FixedMathKernel.Pow(f.Scale, f.NaN, kind, state, out status));
                    Assert.AreEqual(FixedStatus.None, status);
                    foreach (ulong sign in new[] { 0UL, f.NegativeZero })
                    {
                        ulong result = FixedMathKernel.Atan2(zero, sign, kind, state, out status);
                        if (sign == 0) { Assert.AreEqual(zero, result); Assert.AreEqual(FixedStatus.None, status); }
                    }
                }
                foreach (ulong a in new[] { f.NaN, f.PositiveInfinity, f.NegativeInfinity, 0UL, f.NegativeZero, f.Scale })
                {
                    Assert.AreEqual(f.NaN, FixedMathKernel.Atan2(a, f.NaN, kind, state, out FixedStatus status));
                    Assert.AreEqual(FixedStatus.InvalidDomain, status);
                    Assert.AreEqual(f.NaN, FixedMathKernel.Log(a, f.Scale, kind, state, out status));
                    Assert.AreEqual(FixedStatus.InvalidDomain, status);
                }
                Assert.AreEqual(f.NegativeZero, FixedMathKernel.Log(f.Scale, 0, kind, state, out FixedStatus logStatus));
                Assert.AreEqual(FixedStatus.None, logStatus);
            }
        }
    }
}
