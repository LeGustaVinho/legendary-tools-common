using System;
using System.Numerics;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedRandomGeometryTests
    {
        [OneTimeSetUp] public void Configure() => FixedRuntime.Initialize();
        private static BigInteger Squared(FixedPointVector3 point) => (BigInteger)point.X.RawValue * point.X.RawValue + (BigInteger)point.Y.RawValue * point.Y.RawValue + (BigInteger)point.Z.RawValue * point.Z.RawValue;

        [TestCase(0, FixedMathProfile.Accurate)] [TestCase(3, FixedMathProfile.Accurate)]
        [TestCase(18, FixedMathProfile.Accurate)] [TestCase(0, FixedMathProfile.Fast)] [TestCase(18, FixedMathProfile.Fast)]
        public void SpatialSamplersHandlePrecisionExtremesWithoutSpecialValues(int precision, FixedMathProfile profile)
        {
            var state = new FixedRuntimeState(new FixedSettings(Math.Min(precision, 9), precision, mathProfile: profile));
            var f = new FixedFormat(FixedKind.Signed64, state); var r = FixedRandom.Create(101);
            BigInteger scale = f.Scale, squared = scale * scale;
            for (int i = 0; i < 80; i++)
            {
                Assert.IsTrue(FixedRandomGeometry.Inside(ref r, 0, 0, 0, f.Scale, true, state, out var inside, out var status));
                Assert.IsTrue(status.IsSuccess()); Assert.LessOrEqual(Squared(inside), squared);
                Assert.IsTrue(FixedRandomGeometry.On(ref r, 0, 0, 0, f.Scale, true, state, out var surface, out status));
                Assert.IsTrue(status.IsSuccess()); Assert.LessOrEqual(BigInteger.Abs(Squared(surface) - squared), 6 * scale + 3);
                foreach (var v in new[] { surface.X, surface.Y, surface.Z })
                { Assert.IsTrue(f.Decode(unchecked((ulong)v.RawValue)).IsFinite); Assert.AreNotEqual(f.NegativeZero, unchecked((ulong)v.RawValue)); }
            }
        }

        [Test]
        public void InteriorAndSurfaceDistributionsHaveExpectedMomentsAndSymmetry()
        {
            const int count = 5000; double scale = Fixed64.Scale;
            var r = FixedRandom.Create(234); double sphereSquared = 0, diskSquared = 0, surfaceZSquared = 0, sx = 0, sy = 0, sz = 0;
            int[] quadrants = new int[4];
            for (int i = 0; i < count; i++)
            {
                var p = r.InsideUnitSphere(); Assert.LessOrEqual(Squared(p), (BigInteger)Fixed64.Scale * Fixed64.Scale);
                double x = p.X.RawValue / scale, y = p.Y.RawValue / scale, z = p.Z.RawValue / scale;
                sphereSquared += x * x + y * y + z * z; sx += x; sy += y; sz += z;
                var q = r.InsideUnitCircle(); double dx = q.X.RawValue / scale, dy = q.Y.RawValue / scale; diskSquared += dx * dx + dy * dy;
                Assert.LessOrEqual((BigInteger)q.X.RawValue * q.X.RawValue + (BigInteger)q.Y.RawValue * q.Y.RawValue, (BigInteger)Fixed64.Scale * Fixed64.Scale);
                var surface = r.OnUnitSphere(); double height = surface.Z.RawValue / scale; surfaceZSquared += height * height;
                var circle = r.OnUnitCircle(); quadrants[(circle.X.RawValue >= 0 ? 1 : 0) | (circle.Y.RawValue >= 0 ? 2 : 0)]++;
            }
            Assert.That(sphereSquared / count, Is.EqualTo(0.6).Within(0.025)); Assert.That(diskSquared / count, Is.EqualTo(0.5).Within(0.025));
            Assert.That(surfaceZSquared / count, Is.EqualTo(1.0 / 3).Within(0.025));
            foreach (double sum in new[] { sx, sy, sz }) Assert.That(sum / count, Is.EqualTo(0).Within(0.035));
            foreach (int quadrant in quadrants) Assert.That(quadrant, Is.InRange(1050, 1450));
        }

        [Test]
        public void TranslatedShapesBoxesAndTriangleRespectBoundsAndAreaSampling()
        {
            var r = FixedRandom.Create(22); var center = new FixedPointVector3(Fixed64.FromInt(2), Fixed64.FromInt(3), Fixed64.FromInt(-4));
            var radius = Fixed64.FromRatio(1, 2); var min = new FixedPointVector3(Fixed64.FromInt(-2), Fixed64.Zero, Fixed64.FromInt(1));
            var max = new FixedPointVector3(Fixed64.FromInt(2), Fixed64.One, Fixed64.FromInt(3));
            var a = default(FixedPointVector3); var b = new FixedPointVector3(Fixed64.One, Fixed64.Zero, Fixed64.Zero); var c = new FixedPointVector3(Fixed64.Zero, Fixed64.One, Fixed64.Zero);
            double tx = 0, ty = 0; const int count = 2500;
            for (int i = 0; i < count; i++)
            {
                var p = r.InsideSphere(center, radius);
                var delta = new FixedPointVector3(Fixed64.FromRaw(p.X.RawValue - center.X.RawValue), Fixed64.FromRaw(p.Y.RawValue - center.Y.RawValue), Fixed64.FromRaw(p.Z.RawValue - center.Z.RawValue));
                Assert.LessOrEqual(Squared(delta), (BigInteger)radius.RawValue * radius.RawValue);
                var box = r.InsideBox(min, max); Assert.That(box.X.RawValue, Is.InRange(min.X.RawValue, max.X.RawValue - 1));
                Assert.That(box.Y.RawValue, Is.InRange(min.Y.RawValue, max.Y.RawValue - 1)); Assert.That(box.Z.RawValue, Is.InRange(min.Z.RawValue, max.Z.RawValue - 1));
                var t = r.InsideTriangle(a, b, c); Assert.GreaterOrEqual(t.X.RawValue, 0); Assert.GreaterOrEqual(t.Y.RawValue, 0);
                Assert.LessOrEqual(t.X.RawValue + t.Y.RawValue, (long)Fixed64.Scale + 1); Assert.AreEqual(0, t.Z.RawValue);
                tx += t.X.RawValue / (double)Fixed64.Scale; ty += t.Y.RawValue / (double)Fixed64.Scale;
            }
            Assert.That(tx / count, Is.EqualTo(1.0 / 3).Within(0.025)); Assert.That(ty / count, Is.EqualTo(1.0 / 3).Within(0.025));
            var saved = r.CaptureState(); Assert.IsFalse(r.TryInsideTriangle(a, a, b, out _, out _));
            Assert.IsFalse(r.TryInsideSphere(center, Fixed64.FromInt(-1), out _, out _)); Assert.IsFalse(r.TryOnCircle(Fixed64.NaN, out _, out _));
            Assert.IsFalse(r.TryInsideSphere(new FixedPointVector3(Fixed64.MaxValue, Fixed64.Zero, Fixed64.Zero), Fixed64.One, out _, out var status)); Assert.AreEqual(FixedStatus.OutOfRange, status);
            Assert.AreEqual(center, r.InsideSphere(center, Fixed64.Zero)); Assert.AreEqual(saved, r.CaptureState());
        }
    }
}
