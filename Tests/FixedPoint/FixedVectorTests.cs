using System;
using System.Numerics;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedVectorTests
    {
        [OneTimeSetUp] public void Initialize() => FixedRuntime.Initialize();
        [Test] public void LayoutEqualityAndMutableApiFollowTheScalarContract()
        {
            Assert.AreEqual(16, Marshal.SizeOf<FixedVector2>());
            Assert.AreEqual(24, Marshal.SizeOf<FixedVector3>());
            FixedVector2 a = new FixedVector2(3, 4);
            Assert.AreEqual(5, a.magnitude.ToInt64());
            Assert.AreEqual(FixedVector2.FromRaw(600, 800), a.normalized);
            a.Set(1, 2); a[1] = 3; a.Scale(new FixedVector2(2, 3));
            Assert.AreEqual(new FixedVector2(2, 9), a);
            Assert.IsFalse(a.TrySetComponent(2, 10));
            Assert.IsFalse(a.TryGetComponent(-1, out _));
            Assert.Throws<IndexOutOfRangeException>(() => { Fixed64 ignored = a[2]; });
            Assert.AreEqual(new FixedVector3(2, 9, 0), (FixedVector3)a);
            Assert.AreEqual(a, (FixedVector2)new FixedVector3(2, 9, 100));
            Assert.AreEqual(new FixedVector2(-9, 2), FixedVector2.Perpendicular(a));
            FixedVector2 z = new FixedVector2(Fixed64.NegativeZero, 0);
            Assert.IsTrue(z == FixedVector2.zero);
            Assert.AreEqual(z.GetHashCode(), FixedVector2.zero.GetHashCode());
            FixedVector2 nan = FixedVector2.NaN, otherNan = FixedVector2.NaN;
            Assert.IsFalse(nan == otherNan); Assert.IsTrue(nan.Equals(otherNan));
            Assert.IsFalse(FixedVector2.zero == FixedVector2.FromRaw(1, 0));
            Assert.IsTrue(FixedVector2.Approximately(FixedVector2.zero, FixedVector2.FromRaw(1, 0)));
        }
        [Test] public void GeometryUsesWideProductsAndHandlesDegenerateInputs()
        {
            Assert.AreEqual(FixedVector3.forward, FixedVector3.Cross(FixedVector3.right, FixedVector3.up));
            Assert.AreEqual(90, FixedVector3.Angle(FixedVector3.right, FixedVector3.up).ToInt64());
            Assert.AreEqual(-90, FixedVector3.SignedAngle(FixedVector3.right, FixedVector3.up, FixedVector3.back).ToInt64());
            Assert.AreEqual(0, FixedVector3.Angle(FixedVector3.zero, FixedVector3.up).RawValue);
            Assert.AreEqual(FixedVector3.zero, FixedVector3.zero.normalized);
            Assert.AreEqual(FixedVector3.right, FixedVector3.FromRaw(1, 0, 0).normalized);
            Assert.IsFalse(FixedVector3.TryNormalize(FixedVector3.positiveInfinity, out FixedVector3 invalid, out FixedStatus status));
            Assert.IsTrue(FixedVector3.IsNaN(invalid)); Assert.AreEqual(FixedStatus.InvalidDomain, status);
            var large = FixedVector3.FromRaw(long.MaxValue - 2, long.MaxValue - 2, long.MaxValue - 2);
            Assert.IsTrue(FixedVector3.IsFinite(large.normalized));
            Assert.AreEqual(FixedVector3.zero, FixedVector3.Cross(large, large));
            var a = FixedVector3.FromRaw(long.MaxValue - 2, long.MaxValue - 2, 0);
            var b = FixedVector3.FromRaw(long.MaxValue - 2, -(long.MaxValue - 2), 0);
            Assert.AreEqual(0, FixedVector3.Dot(a, b).RawValue);
            Assert.AreEqual(a, FixedVector3.Project(a, a));
            Assert.AreEqual(FixedVector3.zero, FixedVector3.ProjectOnPlane(a, a));
            Assert.AreEqual(FixedVector3.zero, FixedVector3.Project(a, FixedVector3.zero));
            Assert.AreEqual(a, FixedVector3.ProjectOnPlane(a, FixedVector3.zero));
            Assert.AreEqual(new FixedVector3(1, 2, 0), FixedVector3.Reflect(new FixedVector3(1, -2, 0), FixedVector3.up));
        }
        [Test] public void InterpolationRotationAndOrthogonalFallbacksAreDefined()
        {
            Assert.AreEqual(FixedVector3.right, FixedVector3.Lerp(FixedVector3.right, FixedVector3.up, -1));
            Assert.AreEqual(new FixedVector3(-1, 2, 0), FixedVector3.LerpUnclamped(FixedVector3.right, FixedVector3.up, 2));
            Assert.AreEqual(new FixedVector2(-1, 0), FixedVector2.MoveTowards(FixedVector2.zero, FixedVector2.right, -1));
            Assert.AreEqual(FixedVector2.right, FixedVector2.ClampMagnitude(new FixedVector2(3, 0), 1));
            Fixed64 half = Fixed64.FromRatio(1, 2);
            FixedVector3 middle = FixedVector3.Slerp(FixedVector3.right, FixedVector3.up, half);
            Assert.That(middle.x.RawValue, Is.EqualTo(707).Within(1));
            Assert.That(middle.y.RawValue, Is.EqualTo(707).Within(1));
            FixedVector3 opposite = FixedVector3.Slerp(FixedVector3.right, FixedVector3.left, half);
            Assert.That(opposite.z.RawValue, Is.EqualTo(1000).Within(1));
            Assert.That(FixedMath.Abs(opposite.x).RawValue, Is.LessThanOrEqualTo(1));
            FixedVector3 rotation = FixedVector3.RotateTowards(FixedVector3.right, FixedVector3.up, Fixed64.Pi / 4, 0);
            Assert.That(rotation.x.RawValue, Is.EqualTo(707).Within(2));
            Assert.That(rotation.y.RawValue, Is.EqualTo(707).Within(2));
            FixedVector3 n = default, t = default, b = default;
            Assert.IsTrue(FixedVector3.TryOrthoNormalize(ref n, ref t, ref b, out _));
            Assert.AreEqual(FixedVector3.right, n);
            Assert.AreEqual(1000, t.magnitude.RawValue); Assert.AreEqual(1000, b.magnitude.RawValue);
            Assert.AreEqual(0, FixedVector3.Dot(n, t).RawValue);
            Assert.AreEqual(0, FixedVector3.Dot(n, b).RawValue);
            Assert.AreEqual(0, FixedVector3.Dot(t, b).RawValue);
            FixedVector3 originalN = n; t = FixedVector3.NaN;
            Assert.IsFalse(FixedVector3.TryOrthoNormalize(ref n, ref t, out _)); Assert.AreEqual(originalN, n);
        }
        [Test] public void SmoothDampRequiresSimulationTimeAndCommitsVelocityAtomically()
        {
            FixedVector3 velocity = new FixedVector3(1, 2, 3), before = velocity;
            FixedVector3 position = FixedVector3.SmoothDamp(FixedVector3.zero, FixedVector3.right, ref velocity, 1, Fixed64.PositiveInfinity, 0);
            Assert.AreEqual(FixedVector3.zero, position); Assert.AreEqual(before, velocity);
            Assert.IsFalse(FixedVector3.TrySmoothDamp(FixedVector3.zero, FixedVector3.right, ref velocity, 1, 10, -1, out _, out FixedStatus status));
            Assert.AreEqual(FixedStatus.InvalidDomain, status); Assert.AreEqual(before, velocity);
            velocity = default; position = default;
            for (int i = 0; i < 100; i++)
            {
                position = FixedVector3.SmoothDamp(position, new FixedVector3(10, 0, 0), ref velocity,
                    Fixed64.FromRatio(1, 2), Fixed64.PositiveInfinity, Fixed64.FromRatio(1, 60), out status);
                Assert.IsTrue(status.IsSuccess()); Assert.LessOrEqual(position.x.RawValue, 10000);
            }
            Assert.Greater(position.x.RawValue, 9800);
        }
        [Test] public void ExactDotAndNormOracleCoversAllRoundingProfilesAndPrecisions()
        {
            var random = new Random(77129);
            foreach (int precision in new[] { 0, 3, 6, 9, 18 })
            foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
            foreach (FixedArithmeticMode backend in new[] { FixedArithmeticMode.Portable, FixedArithmeticMode.Intrinsic })
            {
                var state = new FixedRuntimeState(new FixedSettings(3, precision, rounding: rounding, arithmeticMode: backend));
                BigInteger scale = state.Scale64;
                for (int sample = 0; sample < 20; sample++)
                {
                    long x = random.Next(-1000000, 1000000), y = random.Next(-1000000, 1000000), z = random.Next(-1000000, 1000000);
                    long a = random.Next(-1000000, 1000000), b = random.Next(-1000000, 1000000), c = random.Next(-1000000, 1000000);
                    var first = new FixedVectorBits(unchecked((ulong)x), unchecked((ulong)y), unchecked((ulong)z));
                    var second = new FixedVectorBits(unchecked((ulong)a), unchecked((ulong)b), unchecked((ulong)c));
                    BigInteger numerator = (BigInteger)x * a + (BigInteger)y * b + (BigInteger)z * c;
                    BigInteger expected = RoundedRatio(numerator, scale, rounding);
                    ulong actual = FixedVectorKernel.Dot(first, second, 3, state, out FixedStatus status);
                    if (expected.IsZero) Assert.IsTrue(new FixedFormat(FixedKind.Signed64, state).Decode(actual).IsZero);
                    else Assert.AreEqual((long)expected, unchecked((long)actual));
                    Assert.IsTrue(status.IsSuccess());
                    BigInteger squares = (BigInteger)x * x + (BigInteger)y * y + (BigInteger)z * z;
                    long root = (long)Math.Sqrt((double)squares);
                    while ((BigInteger)(root + 1) * (root + 1) <= squares) root++;
                    while ((BigInteger)root * root > squares) root--;
                    bool exact = (BigInteger)root * root == squares;
                    bool increment = !exact && (rounding == FixedRounding.TowardPositiveInfinity ||
                        (rounding == FixedRounding.ToEven || rounding == FixedRounding.AwayFromZero) && squares * 4 > (BigInteger)(2 * root + 1) * (2 * root + 1));
                    Assert.AreEqual(root + (increment ? 1 : 0), unchecked((long)FixedVectorKernel.Magnitude(first, 3, false, state, out status)));
                }
            }
        }
        private static BigInteger RoundedRatio(BigInteger numerator, BigInteger denominator, FixedRounding rounding)
        {
            bool negative = numerator.Sign < 0;
            BigInteger q = BigInteger.DivRem(BigInteger.Abs(numerator), denominator, out BigInteger rem);
            int compare = (rem * 2).CompareTo(denominator);
            bool increment = !rem.IsZero && (rounding == FixedRounding.TowardPositiveInfinity && !negative ||
                rounding == FixedRounding.TowardNegativeInfinity && negative ||
                (rounding == FixedRounding.ToEven || rounding == FixedRounding.AwayFromZero) &&
                (compare > 0 || compare == 0 && (rounding == FixedRounding.AwayFromZero || !q.IsEven)));
            if (increment) q++; return negative ? -q : q;
        }
    }
}
