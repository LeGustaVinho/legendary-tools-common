using System;
using System.Globalization;
using System.Numerics;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedQuaternionReferenceTests
    {
        private static ulong Parse(string text) => unchecked((ulong)long.Parse(text, CultureInfo.InvariantCulture));
        private static ulong[] Components(string text) { string[] parts = text.Split(','); var result = new ulong[parts.Length]; for (int i = 0; i < result.Length; i++) result[i] = Parse(parts[i]); return result; }
        private static FixedQuaternionBits Q(string text) { var v = Components(text); return new FixedQuaternionBits(v[0], v[1], v[2], v[3]); }
        private static FixedVectorBits V(string text) { var v = Components(text); return new FixedVectorBits(v[0], v[1], v[2]); }
        [TestCase(FixedMathProfile.Accurate, 1)] [TestCase(FixedMathProfile.Fast, 4)]
        public void GeometryMatchesSixHundredIndependent160DigitReferences(FixedMathProfile profile, int tolerance)
        {
            Assert.AreEqual(600, FixedQuaternionMathReference.Cases.Length); int checkedCases = 0;
            foreach (string row in FixedQuaternionMathReference.Cases)
            {
                string[] p = row.Split('|'); int precision = int.Parse(p[0], CultureInfo.InvariantCulture), operation = int.Parse(p[1], CultureInfo.InvariantCulture);
                var settings = new FixedSettings(decimalPlaces64: precision, mathProfile: profile); var state = new FixedRuntimeState(settings);
                var sample = new FixedQuaternionReplayCorpus.Sample { Settings = settings, Operation = operation, A = Q(p[2]), B = Q(p[3]), Vector = V(p[4]), Scalar = Parse(p[5]) };
                var result = FixedQuaternionReplayCorpus.Evaluate(sample); Assert.IsTrue(result.Status.IsSuccess(), "status " + profile + "/" + row);
                var expected = Components(p[6]); ulong[] actual;
                if (operation == 3 || operation == 5) actual = new[] { result.Scalar };
                else if (operation == 4 || operation == 13) actual = new[] { result.Vector.X, result.Vector.Y, result.Vector.Z };
                else if (operation == 12) actual = new[] { result.Scalar, result.Vector.X, result.Vector.Y, result.Vector.Z };
                else actual = new[] { result.Quaternion.X, result.Quaternion.Y, result.Quaternion.Z, result.Quaternion.W };
                var format = new FixedFormat(FixedKind.Signed64, state);
                bool negate = false;
                if (operation == 10 || operation == 11)
                {
                    BigInteger agreement = 0; for (int i = 0; i < 4; i++) agreement += (BigInteger)unchecked((long)actual[i]) * unchecked((long)expected[i]);
                    negate = agreement.Sign < 0;
                }
                for (int i = 0; i < expected.Length; i++)
                {
                    long raw = format.Decode(actual[i]).IsZero ? 0 : unchecked((long)actual[i]);
                    if (negate) raw = -raw;
                    Assert.LessOrEqual(BigInteger.Abs((BigInteger)raw - unchecked((long)expected[i])), (BigInteger)tolerance, profile + "/component=" + i + "/" + row);
                }
                checkedCases++;
            }
            TestContext.Progress.WriteLine("FIXED_QUATERNION_REFERENCE profile=" + profile + " cases=" + checkedCases + " sha256=" + FixedQuaternionMathReference.Sha256);
        }
    }
}
