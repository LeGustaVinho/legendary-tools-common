using System;
using System.Numerics;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedScalarOracleTests
    {
        private static BigInteger Rounded(BigInteger numerator, BigInteger denominator, FixedRounding rounding)
        {
            bool negative = numerator.Sign < 0;
            BigInteger q = BigInteger.DivRem(BigInteger.Abs(numerator), denominator, out BigInteger rem);
            bool increment = rounding == FixedRounding.TowardPositiveInfinity && !negative || rounding == FixedRounding.TowardNegativeInfinity && negative;
            if (rounding == FixedRounding.ToEven || rounding == FixedRounding.AwayFromZero)
                increment = rem * 2 > denominator || rem * 2 == denominator && (rounding == FixedRounding.AwayFromZero || !q.IsEven);
            if (!rem.IsZero && increment) q++;
            return negative ? -q : q;
        }
        [Test]
        public void LerpAndSmoothStepMatchExactRationalsForEveryRoundingDirection()
        {
            var random = new Random(24835);
            foreach (FixedKind kind in Enum.GetValues(typeof(FixedKind)))
            foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
            foreach (int p in new[] { 0, 3, 9, 18 })
            {
                var state = new FixedRuntimeState(new FixedSettings(Math.Min(9, p), p, rounding)); var f = new FixedFormat(kind, state);
                for (int i = 0; i < 80; i++)
                {
                    ulong am = (((ulong)(uint)random.Next() << 33) ^ (uint)random.Next()) % f.MaxPositive;
                    ulong bm = (((ulong)(uint)random.Next() << 33) ^ (uint)random.Next()) % f.MaxPositive;
                    bool an = !f.Unsigned && (i & 1) != 0, bn = !f.Unsigned && (i & 2) != 0;
                    ulong amount = (((ulong)(uint)random.Next() << 33) ^ (uint)random.Next()) % (f.Scale + 1);
                    BigInteger a = an ? -(BigInteger)am : am, b = bn ? -(BigInteger)bm : bm, t = amount, s = f.Scale;
                    foreach (FixedScalar operation in new[] { FixedScalar.Lerp, FixedScalar.SmoothStep })
                    {
                        BigInteger numerator = operation == FixedScalar.Lerp ? a * (s - t) + b * t :
                            a * s * s * s + (b - a) * t * t * (3 * s - 2 * t);
                        BigInteger denominator = operation == FixedScalar.Lerp ? s : s * s * s;
                        BigInteger expected = Rounded(numerator, denominator, rounding);
                        ulong actual = FixedScalarKernel.Evaluate(f.Finite(am, an), f.Finite(bm, bn), amount, operation, kind, state, out FixedStatus status);
                        FixedDecoded decoded = f.Decode(actual);
                        Assert.IsTrue(status.IsSuccess());
                        Assert.AreEqual(expected, decoded.Negative ? -(BigInteger)decoded.Magnitude : decoded.Magnitude, operation + " " + kind + " " + rounding);
                    }
                }
            }
        }
    }
}
