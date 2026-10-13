using System;
using System.Numerics;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedQuaternionTests
    {
        [OneTimeSetUp] public void Initialize() => FixedRuntime.Initialize();
        private static FixedQuaternionBits Bits(long x, long y, long z, long w) => new FixedQuaternionBits(unchecked((ulong)x), unchecked((ulong)y), unchecked((ulong)z), unchecked((ulong)w));
        private static void Near(long expected, ulong actual, long tolerance = 1) => Assert.LessOrEqual(BigInteger.Abs((BigInteger)unchecked((long)actual) - expected), (BigInteger)tolerance);
        private static void Same(FixedQuaternionBits a, FixedQuaternionBits b) { for (int i = 0; i < 4; i++) Assert.AreEqual(a.Get(i), b.Get(i)); }
        private static long Round(BigInteger numerator, long scale, FixedRounding mode)
        {
            bool negative = numerator.Sign < 0;
            BigInteger q = BigInteger.DivRem(BigInteger.Abs(numerator), scale, out BigInteger r);
            bool increment = !r.IsZero && (mode == FixedRounding.TowardPositiveInfinity && !negative || mode == FixedRounding.TowardNegativeInfinity && negative ||
                (mode == FixedRounding.ToEven || mode == FixedRounding.AwayFromZero) && (r * 2 > scale || r * 2 == scale && (mode == FixedRounding.AwayFromZero || !q.IsEven)));
            return (long)(negative ? -(q + (increment ? 1 : 0)) : q + (increment ? 1 : 0));
        }
        [Test] public void LayoutComponentsEqualityAndDefaultsHaveExplicitContracts()
        {
            Assert.AreEqual(32, Marshal.SizeOf<FixedQuaternion>());
            var q = FixedQuaternion.FromRaw(1, 2, 3, 4); for (int i = 0; i < 4; i++) Assert.AreEqual(i + 1, q[i].RawValue);
            var before = q; Assert.IsFalse(q.TrySetComponent(4, 0)); Assert.AreEqual(before, q);
            Assert.Throws<IndexOutOfRangeException>(() => { _ = q[4]; });
            Assert.AreNotEqual(default(FixedQuaternion), FixedQuaternion.identity);
            Assert.AreEqual(FixedQuaternion.identity, default(FixedQuaternion).normalized);
            Assert.IsFalse(FixedQuaternion.identity == -FixedQuaternion.identity);
            Assert.IsTrue(FixedQuaternion.ApproximatelyRotation(FixedQuaternion.identity, -FixedQuaternion.identity, 0));
            Assert.IsFalse(FixedQuaternion.NaN == FixedQuaternion.NaN); Assert.IsTrue(FixedQuaternion.NaN.Equals(FixedQuaternion.NaN));
            q = new FixedQuaternion(Fixed64.NegativeZero, Fixed64.Zero, Fixed64.Zero, Fixed64.One);
            Assert.AreEqual(FixedQuaternion.identity, q); Assert.AreEqual(FixedQuaternion.identity.GetHashCode(), q.GetHashCode());
            Assert.AreEqual(FixedQuaternion.identity, FixedQuaternion.CanonicalizeSign(-q));
        }
        [Test] public void QuaternionPolicyDoesNotChangeScalarCompatibilityButFreezesWithTheRuntime()
        {
            var a = FixedSettings.Default; var b = new FixedSettings(quaternionNormalization: FixedQuaternionNormalizationMode.NormalizeResult);
            Assert.AreEqual(a.NumericFingerprint, b.NumericFingerprint); Assert.IsTrue(a.NumericEquals(b));
            Assert.AreNotEqual(a.QuaternionFingerprint, b.QuaternionFingerprint); Assert.IsFalse(a.Equals(b));
            Assert.IsFalse(FixedRuntime.TryInitialize(b, out var status)); Assert.AreEqual(FixedStatus.ConfigurationFrozen, status);
            Assert.IsFalse(new FixedSettings(quaternionNormalization: (FixedQuaternionNormalizationMode)255).IsValid);
        }
        [Test] public void HamiltonProductMatchesIndependentExactIntegerArithmeticForAllRoundings()
        {
            var random = new Random(176);
            foreach (FixedRounding mode in Enum.GetValues(typeof(FixedRounding)))
            {
                var state = new FixedRuntimeState(new FixedSettings(decimalPlaces64: 6, rounding: mode));
                for (int sample = 0; sample < 100; sample++)
                {
                    long[] a = new long[4], b = new long[4]; for (int i = 0; i < 4; i++) { a[i] = random.Next(-2_000_000, 2_000_001); b[i] = random.Next(-2_000_000, 2_000_001); }
                    BigInteger[] n = {
                        (BigInteger)a[3]*b[0]+(BigInteger)a[0]*b[3]+(BigInteger)a[1]*b[2]-(BigInteger)a[2]*b[1],
                        (BigInteger)a[3]*b[1]+(BigInteger)a[1]*b[3]+(BigInteger)a[2]*b[0]-(BigInteger)a[0]*b[2],
                        (BigInteger)a[3]*b[2]+(BigInteger)a[2]*b[3]+(BigInteger)a[0]*b[1]-(BigInteger)a[1]*b[0],
                        (BigInteger)a[3]*b[3]-(BigInteger)a[0]*b[0]-(BigInteger)a[1]*b[1]-(BigInteger)a[2]*b[2] };
                    var result = FixedQuaternionKernel.Multiply(Bits(a[0],a[1],a[2],a[3]), Bits(b[0],b[1],b[2],b[3]), state, out var status);
                    Assert.IsTrue(status.IsSuccess()); for (int i = 0; i < 4; i++) Near(Round(n[i], 1_000_000, mode), result.Get(i), 0);
                }
            }
        }
        [Test] public void ExactProductCancellationAndTinyNormalizationDoNotOverflowEarly()
        {
            var state = new FixedRuntimeState(FixedSettings.Default); ulong huge = 0x7ffffffffffffffdUL;
            var a = new FixedQuaternionBits(huge, huge, huge, huge);
            var b = new FixedQuaternionBits(huge, unchecked(0UL - huge), unchecked(0UL - huge), huge);
            Assert.AreEqual(0, FixedQuaternionKernel.Dot(a, b, state, out var status)); Assert.AreEqual(FixedStatus.None, status);
            var normalized = FixedQuaternionKernel.Normalize(Bits(0, 0, 0, 1), state, out status);
            Same(FixedQuaternionKernel.Identity(state), normalized); Assert.IsTrue(status.IsSuccess());
            normalized = FixedQuaternionKernel.Normalize(a, state, out status); for (int i = 0; i < 4; i++) Near(500, normalized.Get(i));
            Assert.IsTrue(status.IsSuccess());
        }
        [Test] public void NormalizationPolicyRunsBeforeFinalDecimalQuantization()
        {
            var rawState = new FixedRuntimeState(new FixedSettings(decimalPlaces64: 6));
            var normalizedState = new FixedRuntimeState(new FixedSettings(decimalPlaces64: 6, quaternionNormalization: FixedQuaternionNormalizationMode.NormalizeResult));
            var a = Bits(3_000_000, 0, 0, 4_000_000);
            var raw = FixedQuaternionKernel.Multiply(a, a, rawState, out var sr); var unit = FixedQuaternionKernel.Multiply(a, a, normalizedState, out var sn);
            Near(24_000_000, raw.X, 0); Near(7_000_000, raw.W, 0); Near(960_000, unit.X); Near(280_000, unit.W);
            Assert.IsTrue(sr.IsSuccess() && sn.IsSuccess());
        }
        [Test] public void GeometryMatchesAxisConventionsAndEulerComposition()
        {
            FixedQuaternion q = FixedQuaternion.AngleAxis(90, FixedVector3.up);
            Assert.IsTrue(FixedVector3.Approximately(FixedVector3.right, q * FixedVector3.forward));
            Assert.IsTrue(FixedQuaternion.ApproximatelyRotation(q, FixedQuaternion.LookRotation(FixedVector3.right), 0));
            var e = FixedQuaternion.Euler(30, 50, 70);
            var composition = FixedQuaternion.AngleAxis(50, FixedVector3.up) * FixedQuaternion.AngleAxis(30, FixedVector3.right) * FixedQuaternion.AngleAxis(70, FixedVector3.forward);
            Assert.IsTrue(FixedQuaternion.ApproximatelyRotation(e, composition, Fixed64.FromRatio(1, 5)));
            var angles = e.eulerAngles; Assert.IsTrue(FixedQuaternion.ApproximatelyRotation(e, FixedQuaternion.Euler(angles), Fixed64.FromRatio(1, 5)));
            e.ToAngleAxis(out var angle, out var axis, out var status); Assert.IsTrue(status.IsSuccess());
            Assert.IsTrue(FixedQuaternion.ApproximatelyRotation(e, FixedQuaternion.AngleAxis(angle, axis), Fixed64.FromRatio(1, 5)));
        }
        [Test] public void DirectionDegeneraciesAndAntiparallelAxesAreStable()
        {
            var a = FixedQuaternion.FromToRotation(FixedVector3.right, FixedVector3.left);
            Assert.IsTrue(FixedVector3.Approximately(FixedVector3.left, a * FixedVector3.right));
            Assert.AreEqual(a, FixedQuaternion.FromToRotation(FixedVector3.right, FixedVector3.left));
            var q = FixedQuaternion.LookRotation(FixedVector3.right, FixedVector3.zero, out var status);
            Assert.IsTrue(status.IsSuccess()); Assert.IsTrue(FixedVector3.Approximately(FixedVector3.right, q * FixedVector3.forward));
            q = FixedQuaternion.identity; Assert.IsFalse(q.TrySetLookRotation(FixedVector3.zero, FixedVector3.up, out status)); Assert.AreEqual(FixedQuaternion.identity, q);
            Assert.AreEqual(FixedStatus.InvalidDomain, status); Assert.IsFalse(FixedQuaternion.TryInverse(default, out _, out status));
        }
        [Test] public void InterpolationHandlesHemisphereExtrapolationAndNegativeAngularSteps()
        {
            var a = FixedQuaternion.identity; var b = FixedQuaternion.AngleAxis(90, FixedVector3.up);
            Assert.IsTrue(FixedQuaternion.ApproximatelyRotation(FixedQuaternion.AngleAxis(45, FixedVector3.up), FixedQuaternion.Slerp(a, -b, Fixed64.FromRatio(1, 2)), Fixed64.FromRatio(1, 10)));
            Assert.IsTrue(FixedQuaternion.ApproximatelyRotation(FixedQuaternion.AngleAxis(180, FixedVector3.up), FixedQuaternion.SlerpUnclamped(a, b, 2), Fixed64.FromRatio(1, 10)));
            Assert.AreEqual(a, FixedQuaternion.Slerp(a, -a, Fixed64.FromRatio(1, 2)));
            var near = FixedQuaternion.RotateTowards(a, b, 30); Assert.IsTrue(FixedMath.Approximately(30, FixedQuaternion.Angle(a, near), Fixed64.FromRatio(1, 10)));
            var away = FixedQuaternion.RotateTowards(a, b, -180); Assert.IsTrue(FixedMath.Approximately(180, FixedQuaternion.Angle(away, b), Fixed64.FromRatio(1, 10)));
        }
        [Test] public void RadiansRemainUsableAtPrecision18AndCoarseQuantizationFailsExplicitly()
        {
            var state = new FixedRuntimeState(new FixedSettings(decimalPlaces64: 18));
            var q = FixedQuaternionKernel.AngleAxis(1_570_796_326_794_896_619UL, new FixedVectorBits(0, state.Scale64, 0), false, state, out var status);
            Assert.IsTrue(status.IsSuccess()); Near(707_106_781_186_547_524, q.Y); Near(707_106_781_186_547_525, q.W);
            var angle = FixedQuaternionKernel.Angle(FixedQuaternionKernel.Identity(state), q, false, state, out status); Assert.IsTrue(status.IsSuccess()); Near(1_570_796_326_794_896_619, angle, 2);
            FixedQuaternionKernel.Angle(FixedQuaternionKernel.Identity(state), q, true, state, out status); Assert.IsTrue((status & FixedStatus.Overflow) != 0);
            var coarse = new FixedRuntimeState(new FixedSettings(decimalPlaces64: 0));
            Assert.AreEqual(FixedQuaternionKernel.NaN.X, FixedQuaternionKernel.Normalize(Bits(1,1,1,1), coarse, out status).X); Assert.IsTrue((status & FixedStatus.InvalidDomain) != 0);
        }
    }
}
