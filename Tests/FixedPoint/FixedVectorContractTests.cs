using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedVectorContractTests
    {
        [OneTimeSetUp] public void Initialize() => FixedRuntime.Initialize();
        [TestCase(FixedRounding.ToEven)] [TestCase(FixedRounding.AwayFromZero)] [TestCase(FixedRounding.TowardZero)]
        [TestCase(FixedRounding.TowardPositiveInfinity)] [TestCase(FixedRounding.TowardNegativeInfinity)]
        public void ExactCancellationAndNormalizationHaveExplicitSignedZeroBits(FixedRounding rounding)
        {
            for (int precision = 0; precision <= 18; precision++)
            {
                var state = new FixedRuntimeState(new FixedSettings(3, precision, rounding)); ulong scale = state.Scale64;
                var a = new FixedVectorBits(scale, scale, 0); var b = new FixedVectorBits(scale, unchecked(0UL - scale), 0);
                ulong expected = rounding == FixedRounding.TowardNegativeInfinity ? 0x7FFFFFFFFFFFFFFEUL : 0UL;
                Assert.AreEqual(expected, FixedVectorKernel.Dot(a, b, 2, state, out FixedStatus status)); Assert.AreEqual(FixedStatus.None, status);
                var cross = FixedVectorKernel.Cross(a, a, state, out status);
                Assert.AreEqual(expected, cross.X); Assert.AreEqual(expected, cross.Y); Assert.AreEqual(expected, cross.Z); Assert.AreEqual(FixedStatus.None, status);
                var normalized = FixedVectorKernel.Normalize(new FixedVectorBits(0x7FFFFFFFFFFFFFFEUL, 0x7FFFFFFFFFFFFFFEUL, 0x7FFFFFFFFFFFFFFEUL), 3, state, out status);
                Assert.AreEqual(0, normalized.X); Assert.AreEqual(0, normalized.Y); Assert.AreEqual(0, normalized.Z); Assert.AreEqual(FixedStatus.None, status);
            }
        }
        [Test] public void ExtrapolationAndNegativeRotationDeltasFollowTheDocumentedGeometry()
        {
            Assert.IsTrue(FixedVector3.Approximately(FixedVector3.left, FixedVector3.SlerpUnclamped(FixedVector3.right, FixedVector3.up, 2)));
            Assert.IsTrue(FixedVector3.Approximately(FixedVector3.down, FixedVector3.SlerpUnclamped(FixedVector3.right, FixedVector3.up, -1)));
            Assert.AreEqual(new FixedVector3(4, 0, 0), FixedVector3.SlerpUnclamped(new FixedVector3(3, 0, 0), FixedVector3.right, Fixed64.FromRatio(-1, 2)));
            Assert.AreEqual(new FixedVector3(0, 4, 0), FixedVector3.SlerpUnclamped(FixedVector3.zero, new FixedVector3(0, 2, 0), 2));
            var away = FixedVector3.RotateTowards(FixedVector3.right, FixedVector3.up, -Fixed64.Pi, 0);
            Assert.IsTrue(FixedVector3.Approximately(FixedVector3.down, away));
            Assert.IsTrue(FixedVector3.IsNaN(FixedVector3.RotateTowards(FixedVector3.right, FixedVector3.up, 1, -1, out FixedStatus status)));
            Assert.AreEqual(FixedStatus.InvalidDomain, status);
            Assert.AreEqual(FixedVector3.right, FixedVector3.RotateTowards(FixedVector3.right, FixedVector3.up, 0, 0));
        }
        [Test] public void SmoothDampHandlesOvershootSpeedZeroAndMinimumTime()
        {
            var velocity = new FixedVector3(1000, 0, 0);
            Assert.AreEqual(FixedVector3.right, FixedVector3.SmoothDamp(FixedVector3.zero, FixedVector3.right, ref velocity, 1, Fixed64.PositiveInfinity, 1));
            Assert.AreEqual(FixedVector3.zero, velocity);
            Assert.AreEqual(FixedVector3.zero, FixedVector3.SmoothDamp(FixedVector3.zero, FixedVector3.right, ref velocity, 1, 0, 1));
            Assert.AreEqual(FixedVector3.zero, velocity);
            FixedVector3 otherVelocity = default;
            var negativeTime = FixedVector3.SmoothDamp(FixedVector3.zero, FixedVector3.right, ref velocity, -1, Fixed64.PositiveInfinity, Fixed64.Epsilon, out FixedStatus first);
            var minimumTime = FixedVector3.SmoothDamp(FixedVector3.zero, FixedVector3.right, ref otherVelocity, Fixed64.Epsilon, Fixed64.PositiveInfinity, Fixed64.Epsilon, out FixedStatus second);
            Assert.AreEqual(minimumTime, negativeTime); Assert.AreEqual(otherVelocity, velocity); Assert.AreEqual(second, first);
        }
        [Test] public void EveryInvalidSmoothDampParameterPreservesTwoAndThreeDimensionalVelocity()
        {
            foreach (int invalid in new[] { 0, 1, 2, 3, 4, 5 })
            {
                Fixed64 time = invalid == 0 ? Fixed64.NaN : 1;
                Fixed64 speed = invalid == 1 ? -1 : invalid == 2 ? Fixed64.NaN : Fixed64.PositiveInfinity;
                Fixed64 delta = invalid == 3 ? -1 : invalid == 4 ? Fixed64.PositiveInfinity : Fixed64.Epsilon;
                FixedVector2 v2 = invalid == 5 ? FixedVector2.NaN : new FixedVector2(1, 2), before2 = v2;
                FixedVector3 v3 = invalid == 5 ? FixedVector3.NaN : new FixedVector3(1, 2, 3), before3 = v3;
                Assert.IsFalse(FixedVector2.TrySmoothDamp(FixedVector2.zero, FixedVector2.right, ref v2, time, speed, delta, out _, out FixedStatus s2));
                Assert.IsFalse(FixedVector3.TrySmoothDamp(FixedVector3.zero, FixedVector3.right, ref v3, time, speed, delta, out _, out FixedStatus s3));
                Assert.AreEqual(FixedStatus.InvalidDomain, s2); Assert.AreEqual(s2, s3); Assert.IsTrue(before2.Equals(v2)); Assert.IsTrue(before3.Equals(v3));
            }
        }
        [Test] public void ThirdOrthogonalInputIsAtomicAndItsOrientationIsPreserved()
        {
            FixedVector3 n = FixedVector3.right, t = FixedVector3.up, b = FixedVector3.back;
            Assert.IsTrue(FixedVector3.TryOrthoNormalize(ref n, ref t, ref b, out _)); Assert.AreEqual(FixedVector3.back, b);
            b = FixedVector3.NaN; FixedVector3 originalN = n, originalT = t;
            Assert.IsFalse(FixedVector3.TryOrthoNormalize(ref n, ref t, ref b, out FixedStatus status));
            Assert.AreEqual(FixedStatus.InvalidDomain, status); Assert.AreEqual(originalN, n); Assert.AreEqual(originalT, t); Assert.IsTrue(b.Equals(FixedVector3.NaN));
            n = t = b = default; Assert.IsTrue(FixedVector3.TryOrthoNormalize(ref n, ref t, ref b, out _));
            Assert.AreEqual(FixedVector3.right, n); Assert.AreEqual(FixedVector3.forward, t); Assert.AreEqual(FixedVector3.down, b);
        }
        [Test] public unsafe void UninitializedKernelsFailBeforeChangingRefStateOrNetworkBuffers()
        {
            var state = default(FixedRuntimeState); var a = new FixedVectorBits(1, 2, 3);
            Assert.AreEqual(FixedVectorKernel.NaN.X, FixedVectorKernel.Normalize(a, 3, state, out FixedStatus status).X);
            Assert.AreEqual(FixedStatus.Uninitialized, status);
            FixedVectorKernel.SmoothDamp(a, a, a, 1, 1, 1, 3, state, out FixedVectorBits velocity, out status);
            Assert.AreEqual(FixedStatus.Uninitialized, status); Assert.AreEqual(a.X, velocity.X); Assert.AreEqual(a.Y, velocity.Y); Assert.AreEqual(a.Z, velocity.Z);
            byte* bytes = stackalloc byte[8]; for (int i = 0; i < 8; i++) bytes[i] = 0xA5;
            var profile = new FixedVectorNetworkProfile(new FixedNetworkProfile(FixedRuntime.Settings, FixedKind.Signed64), 3);
            var writer = new FixedBufferWriter(bytes, 8);
            Assert.IsFalse(FixedVectorNetworkKernel.Write(ref writer, a, 3, profile, state, out status));
            Assert.AreEqual(FixedStatus.Uninitialized, status); Assert.AreEqual(0, writer.BitPosition);
            for (int i = 0; i < 8; i++) Assert.AreEqual(0xA5, bytes[i]);
            var reader = new FixedBufferReader(bytes, 8);
            Assert.IsFalse(FixedVectorNetworkKernel.Read(ref reader, 3, profile, state, out _, out status));
            Assert.AreEqual(FixedStatus.Uninitialized, status); Assert.AreEqual(0, reader.BitPosition);
        }
    }
}
