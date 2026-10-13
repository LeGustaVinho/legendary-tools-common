using System;
using System.Numerics;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedBoundsTests
    {
        [OneTimeSetUp] public void Initialize()
        { FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize(); }
        internal static BigInteger Integer(ulong bits) => bits == 0x7FFFFFFFFFFFFFFEUL ? BigInteger.Zero : new BigInteger(unchecked((long)bits));
        internal static BigInteger Round(BigInteger n, BigInteger d, FixedRounding rounding)
        {
            bool negative = n.Sign < 0; n = BigInteger.Abs(n);
            BigInteger q = BigInteger.DivRem(n, d, out var rem);
            if (rem != 0 && (rounding == FixedRounding.TowardPositiveInfinity && !negative || rounding == FixedRounding.TowardNegativeInfinity && negative ||
                (rounding == FixedRounding.ToEven || rounding == FixedRounding.AwayFromZero) &&
                (rem * 2 > d || rem * 2 == d && (rounding == FixedRounding.AwayFromZero || !q.IsEven)))) q++;
            return negative ? -q : q;
        }
        private static FixedVectorBits V(long x, long y = 0, long z = 0) => new FixedVectorBits(unchecked((ulong)x), unchecked((ulong)y), unchecked((ulong)z));
        private static FixedBoundsRuntimeState Geometry(FixedBoundsRepresentation rep) => new FixedBoundsRuntimeState(new FixedBoundsSettings(rep));
        private static void SameRaw(FixedBounds3 a, FixedBounds3 b)
        {
            a.GetStorage(out var a0, out var a1); b.GetStorage(out var b0, out var b1);
            Assert.AreEqual(a0.x.RawValue, b0.x.RawValue); Assert.AreEqual(a0.y.RawValue, b0.y.RawValue); Assert.AreEqual(a0.z.RawValue, b0.z.RawValue);
            Assert.AreEqual(a1.x.RawValue, b1.x.RawValue); Assert.AreEqual(a1.y.RawValue, b1.y.RawValue); Assert.AreEqual(a1.z.RawValue, b1.z.RawValue);
        }
        [Test] public void StorageIsUnmanagedAndConfigurationIsFrozen()
        {
            Assert.AreEqual(32, Marshal.SizeOf<FixedBounds2>()); Assert.AreEqual(48, Marshal.SizeOf<FixedBounds3>());
            Assert.IsTrue(default(FixedBounds2).IsValid); Assert.IsTrue(default(FixedBounds3).Contains(FixedVector3.zero));
            var settings = FixedBoundsRuntime.Settings;
            Assert.IsTrue(FixedBoundsRuntime.TryInitialize(settings, out var same)); Assert.AreEqual(FixedStatus.None, same);
            Assert.IsFalse(FixedBoundsRuntime.TryInitialize(new FixedBoundsSettings((FixedBoundsRepresentation)9), out var invalid)); Assert.AreEqual(FixedStatus.InvalidConfiguration, invalid);
            var other = settings.Representation == FixedBoundsRepresentation.MinMax ? FixedBoundsRepresentation.CenterExtents : FixedBoundsRepresentation.MinMax;
            Assert.IsFalse(FixedBoundsRuntime.TryInitialize(new FixedBoundsSettings(other), out var frozen)); Assert.AreEqual(FixedStatus.ConfigurationFrozen, frozen);
            Assert.AreEqual(settings, FixedBoundsRuntime.Settings); Assert.AreNotEqual(settings.Fingerprint, new FixedBoundsSettings(other).Fingerprint);
        }
        [Test] public void PublicApiMatchesIntegerUnityStyleGeometry()
        {
            var box = new FixedBounds3(new FixedVector3(1, 2, 3), new FixedVector3(2, 4, 6));
            Assert.AreEqual(new FixedVector3(0, 0, 0), box.min); Assert.AreEqual(new FixedVector3(2, 4, 6), box.max);
            Assert.IsTrue(box.Contains(box.min)); Assert.IsTrue(box.Contains(box.max));
            Assert.AreEqual(new FixedVector3(2, 0, 3), box.ClosestPoint(new FixedVector3(4, -2, 3)));
            Assert.AreEqual((Fixed64)8, box.SqrDistance(new FixedVector3(4, -2, 3)));
            Assert.AreEqual(Fixed64.Zero, box.SqrDistance(box.center));
            var touching = FixedBounds3.FromMinMax(box.max, box.max + FixedVector3.one);
            Assert.IsTrue(box.Intersects(touching)); Assert.IsTrue(touching.Intersects(box));
            Assert.IsFalse(box.Intersects(FixedBounds3.FromMinMax(box.max + FixedVector3.one, box.max + FixedVector3.one)));
            box.Encapsulate(new FixedVector3(-2, 0, 0)); Assert.IsTrue(box.Contains(new FixedVector3(-2, 0, 0)));
            box.Encapsulate(touching); Assert.IsTrue(box.Contains(touching.max));
            var before = box.size; box.Expand(2); Assert.AreEqual(before + new FixedVector3(2, 2, 2), box.size);
            box.Expand(new FixedVector3(-2, -2, -2)); Assert.AreEqual(before, box.size);
            box.center = new FixedVector3(20, 20, 20); Assert.AreEqual(new FixedVector3(20, 20, 20), box.center);
            box.size = new FixedVector3(4, 4, 4); Assert.AreEqual(new FixedVector3(4, 4, 4), box.size);
            box.extents = FixedVector3.one; Assert.AreEqual(new FixedVector3(2, 2, 2), box.size);
            box.min = new FixedVector3(18, 18, 18); box.max = new FixedVector3(22, 22, 22);
            Assert.AreEqual(new FixedVector3(4, 4, 4), box.size);
            var flat = new FixedBounds2(FixedVector2.zero, new FixedVector2(2, 4));
            flat.Encapsulate(new FixedVector2(4, 5)); flat.Expand(Fixed64.Zero);
            Assert.IsTrue(flat.Contains(new FixedVector2(4, 5))); Assert.AreEqual(new FixedVector2(4, 5), flat.ClosestPoint(new FixedVector2(8, 9)));
            Assert.AreEqual((Fixed64)32, flat.SqrDistance(new FixedVector2(8, 9)));
        }
        [Test] public void SettersAndFailedMutationsPreserveExactStorage()
        {
            var box = FixedBounds3.FromMinMax(FixedVector3.FromRaw(0, 0, 0), FixedVector3.FromRaw(1, 1, 1)); var original = box;
            box.center = box.center; box.size = box.size; box.extents = box.extents; box.min = box.min; box.max = box.max; SameRaw(original, box);
            Assert.IsFalse(box.TrySetCenter(FixedVector3.NaN, out _)); SameRaw(original, box);
            Assert.IsFalse(box.TrySetSize(new FixedVector3(-1, 2, 3), out _)); SameRaw(original, box);
            Assert.IsFalse(box.TrySetExtents(new FixedVector3(1, -1, 1), out _)); SameRaw(original, box);
            Assert.IsFalse(box.TrySetMinMax(FixedVector3.one, FixedVector3.zero, out _)); SameRaw(original, box);
            Assert.IsFalse(box.TryExpand(-1, out _)); SameRaw(original, box);
            Assert.IsFalse(box.TryEncapsulate(FixedVector3.positiveInfinity, out _)); SameRaw(original, box);
            Assert.IsFalse(box.TryEncapsulate(FixedBounds3.Invalid, out _)); SameRaw(original, box);
            Assert.IsFalse(box.TryContains(FixedVector3.NaN, out _, out var status)); Assert.AreEqual(FixedStatus.InvalidDomain, status);
            Assert.IsFalse(new FixedBounds3(FixedVector3.zero, new FixedVector3(-1, 0, 0)).IsValid);
            Assert.IsFalse(FixedBounds3.FromCenterExtents(new FixedVector3(Fixed64.MaxValue, 0, 0), FixedVector3.one).IsValid);
        }
        [Test] public void GeometryRejectsUninitializedStateAndEveryNonfiniteComponent()
        {
            var numeric = new FixedRuntimeState(FixedSettings.Default); var geometry = Geometry(FixedBoundsRepresentation.MinMax);
            Assert.IsFalse(FixedBoundsKernel.FromMinMax(default, default, 3, default, geometry, out _, out var status)); Assert.AreEqual(FixedStatus.Uninitialized, status);
            Assert.IsFalse(FixedBoundsKernel.FromMinMax(default, default, 3, numeric, default, out _, out status)); Assert.AreEqual(FixedStatus.Uninitialized, status);
            foreach (ulong special in new[] { 0x8000000000000000UL, 0x8000000000000001UL, 0x7FFFFFFFFFFFFFFFUL })
                for (int axis = 0; axis < 3; axis++)
                {
                    var vector = default(FixedVectorBits); vector.Set(axis, special);
                    Assert.IsFalse(FixedBoundsKernel.FromMinMax(vector, default, 3, numeric, geometry, out _, out status)); Assert.AreEqual(FixedStatus.InvalidDomain, status);
                    Assert.IsFalse(FixedBoundsKernel.Contains(default, vector, 3, numeric, geometry, out _, out status)); Assert.AreEqual(FixedStatus.InvalidDomain, status);
                }
        }
        [Test] public void HalfRawConversionsFollowAnIndependentIntegerOracleAcrossEveryConfiguration()
        {
            foreach (int dimensions in new[] { 2, 3 }) foreach (int precision in new[] { 0, 3, 9, 18 })
            foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
            foreach (FixedBoundsRepresentation rep in Enum.GetValues(typeof(FixedBoundsRepresentation)))
            {
                var numeric = new FixedRuntimeState(new FixedSettings(3, precision, rounding)); var geometry = Geometry(rep);
                for (long lo = -7; lo <= 7; lo++) for (long width = 0; width <= 7; width++)
                {
                    Assert.IsTrue(FixedBoundsKernel.FromMinMax(V(lo, lo, lo), V(lo + width, lo + width, lo + width), dimensions, numeric, geometry, out var box, out var status));
                    BigInteger center = Round(2 * lo + width, 2, rounding); BigInteger extent = BigInteger.Max(center - lo, lo + width - center);
                    var min = FixedBoundsKernel.Get(box, FixedBoundsPart.Min, dimensions, numeric, geometry, out _);
                    var max = FixedBoundsKernel.Get(box, FixedBoundsPart.Max, dimensions, numeric, geometry, out _);
                    for (int axis = 0; axis < dimensions; axis++)
                    {
                        Assert.AreEqual(rep == FixedBoundsRepresentation.MinMax ? lo : center - extent, Integer(min.Get(axis)));
                        Assert.AreEqual(rep == FixedBoundsRepresentation.MinMax ? lo + width : center + extent, Integer(max.Get(axis)));
                    }
                    Assert.AreEqual(rep == FixedBoundsRepresentation.CenterExtents && (width & 1) != 0 ? FixedStatus.Inexact : FixedStatus.None, status);
                    Assert.IsTrue(FixedBoundsKernel.Contains(box, V(lo, lo, lo), dimensions, numeric, geometry, out bool contains, out _)); Assert.IsTrue(contains);
                    FixedBoundsKernel.Contains(box, V(lo + width, lo + width, lo + width), dimensions, numeric, geometry, out contains, out _); Assert.IsTrue(contains);
                    Assert.IsTrue(FixedBoundsKernel.FromCenterSize(V(lo, lo, lo), V(width, width, width), dimensions, numeric, geometry, out box, out status));
                    min = FixedBoundsKernel.Get(box, FixedBoundsPart.Min, dimensions, numeric, geometry, out _);
                    max = FixedBoundsKernel.Get(box, FixedBoundsPart.Max, dimensions, numeric, geometry, out _);
                    Assert.AreEqual(new BigInteger(lo - (width + 1) / 2), Integer(min.X)); Assert.AreEqual(new BigInteger(lo + (width + 1) / 2), Integer(max.X));
                    Assert.AreEqual((width & 1) == 0 ? FixedStatus.None : FixedStatus.Inexact, status);
                }
            }
        }
        [Test] public void WideGeometryAvoidsIntermediateOverflowAndMutationsNeverWrapOrSaturate()
        {
            foreach (FixedOverflow overflow in Enum.GetValues(typeof(FixedOverflow))) foreach (var rep in new[] { FixedBoundsRepresentation.MinMax, FixedBoundsRepresentation.CenterExtents })
            {
                var numeric = new FixedRuntimeState(new FixedSettings(3, 18, overflow: overflow)); var geometry = Geometry(rep);
                long hi = long.MaxValue - 2, lo = hi - 10;
                Assert.IsTrue(FixedBoundsKernel.FromMinMax(V(lo), V(hi), 2, numeric, geometry, out var box, out _));
                var center = FixedBoundsKernel.Get(box, FixedBoundsPart.Center, 2, numeric, geometry, out var status); Assert.AreEqual(new BigInteger(hi - 5), Integer(center.X)); Assert.AreEqual(FixedStatus.None, status);
                Assert.IsFalse(FixedBoundsKernel.Expand(box, V(30), 2, numeric, geometry, out var failed, out status)); Assert.IsTrue((status & FixedStatus.Overflow) != 0); Assert.AreEqual(box.First.X, failed.First.X); Assert.AreEqual(box.Second.X, failed.Second.X);
                if (rep == FixedBoundsRepresentation.MinMax)
                {
                    Assert.IsTrue(FixedBoundsKernel.FromMinMax(V(-hi), V(hi), 2, numeric, geometry, out box, out _));
                    FixedBoundsKernel.Get(box, FixedBoundsPart.Size, 2, numeric, geometry, out status); Assert.IsTrue((status & FixedStatus.Overflow) != 0);
                    Assert.IsTrue(FixedBoundsKernel.Contains(box, V(hi), 2, numeric, geometry, out bool contains, out _)); Assert.IsTrue(contains);
                }
            }
        }
        [Test] public void DistanceAndRaycastAgreeWithBigIntegerRationalsIncludingTinyDirections()
        {
            var random = new Random(61723);
            foreach (var rep in new[] { FixedBoundsRepresentation.MinMax, FixedBoundsRepresentation.CenterExtents })
            foreach (int dimensions in new[] { 2, 3 }) foreach (int precision in new[] { 0, 3, 9, 18 })
            foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
            {
                var numeric = new FixedRuntimeState(new FixedSettings(3, precision, rounding)); var geometry = Geometry(rep);
                for (int sample = 0; sample < 70; sample++)
                {
                    long[] lower = new long[3], upper = new long[3], point = new long[3], direction = new long[3];
                    for (int axis = 0; axis < dimensions; axis++)
                    { lower[axis] = random.Next(-1000, 1000); upper[axis] = lower[axis] + random.Next(0, 1000) * 2; point[axis] = random.Next(-2000, 2000); direction[axis] = random.Next(-3, 4); }
                    FixedBoundsKernel.FromMinMax(V(lower[0], lower[1], lower[2]), V(upper[0], upper[1], upper[2]), dimensions, numeric, geometry, out var box, out _);
                    var origin = V(point[0], point[1], point[2]); var dir = V(direction[0], direction[1], direction[2]);
                    BigInteger sum = 0;
                    for (int axis = 0; axis < dimensions; axis++) { long delta = point[axis] - Math.Max(lower[axis], Math.Min(upper[axis], point[axis])); sum += new BigInteger(delta) * delta; }
                    var expected = Round(sum, numeric.Scale64, rounding);
                    ulong actual = FixedBoundsKernel.SqrDistance(box, origin, dimensions, numeric, geometry, out var status);
                    Assert.IsTrue(status.IsSuccess()); Assert.AreEqual(expected, Integer(actual)); Assert.AreEqual(sum % numeric.Scale64 == 0 ? FixedStatus.None : FixedStatus.Inexact, status);
                    bool expectedHit = RayReference(lower, upper, point, direction, dimensions, out var numerator, out var denominator);
                    bool ok = FixedBoundsKernel.IntersectRay(box, origin, dir, dimensions, numeric, geometry, true, out bool hit, out ulong distance, out status);
                    BigInteger t = Round(numerator * numeric.Scale64, denominator, rounding);
                    if (expectedHit && t > long.MaxValue - 2) { Assert.IsFalse(ok); Assert.IsTrue((status & FixedStatus.Overflow) != 0); }
                    else { Assert.IsTrue(ok); Assert.AreEqual(expectedHit, hit); Assert.AreEqual(expectedHit ? t : BigInteger.Zero, Integer(distance)); }
                    Assert.IsTrue(FixedBoundsKernel.IntersectRay(box, origin, dir, dimensions, numeric, geometry, false, out hit, out _, out status)); Assert.AreEqual(expectedHit, hit); Assert.AreEqual(FixedStatus.None, status);
                }
            }
        }
        private static bool RayReference(long[] min, long[] max, long[] o, long[] d, int dimensions, out BigInteger enterN, out BigInteger enterD)
        {
            enterN = 0; enterD = 1; BigInteger? exitN = null; BigInteger exitD = 1;
            for (int i = 0; i < dimensions; i++)
            {
                if (d[i] == 0) { if (o[i] < min[i] || o[i] > max[i]) return false; continue; }
                BigInteger n = new BigInteger(min[i]) - o[i], f = new BigInteger(max[i]) - o[i], denominator = d[i];
                if (denominator < 0) { denominator = -denominator; var swap = n; n = -f; f = -swap; }
                if (n * enterD > enterN * denominator) { enterN = n; enterD = denominator; }
                if (!exitN.HasValue || f * exitD < exitN.Value * denominator) { exitN = f; exitD = denominator; }
                if (enterN * exitD > exitN.Value * enterD) return false;
            }
            return true;
        }
        [Test] public void PublicRaysDefineInteriorParallelTangentialBehindAndStationaryCases()
        {
            var box = new FixedBounds3(FixedVector3.zero, new FixedVector3(2, 2, 2));
            var ray = new FixedRay3(new FixedVector3(-3, 1, 0), new FixedVector3(2, 0, 0));
            Assert.IsTrue(box.IntersectRay(ray, out var t)); Assert.AreEqual((Fixed64)1, t); Assert.AreEqual(new FixedVector3(-1, 1, 0), ray.GetPoint(t)); Assert.AreEqual(new FixedVector3(2, 0, 0), ray.direction);
            Assert.IsTrue(box.TryIntersectRay(new FixedRay3(FixedVector3.zero, FixedVector3.right), out bool hit, out t, out var status)); Assert.IsTrue(hit); Assert.AreEqual(Fixed64.Zero, t);
            Assert.IsTrue(box.TryIntersectRay(new FixedRay3(new FixedVector3(3, 0, 0), FixedVector3.right), out hit, out t, out status)); Assert.IsFalse(hit); Assert.AreEqual(FixedStatus.None, status);
            Assert.IsFalse(box.IntersectRay(new FixedRay3(new FixedVector3(-3, 2, 0), FixedVector3.right)));
            Assert.IsTrue(box.IntersectRay(new FixedRay3(FixedVector3.zero, FixedVector3.zero))); Assert.IsFalse(box.IntersectRay(new FixedRay3(new FixedVector3(3, 0, 0), FixedVector3.zero)));
            Assert.IsFalse(box.TryIntersectRay(new FixedRay3(FixedVector3.zero, FixedVector3.NaN), out hit, out t, out status)); Assert.AreEqual(FixedStatus.InvalidDomain, status);
            var flat = new FixedBounds2(FixedVector2.zero, new FixedVector2(2, 2)); Assert.IsTrue(flat.IntersectRay(new FixedRay2(new FixedVector2(-2, 0), FixedVector2.right), out t)); Assert.AreEqual((Fixed64)1, t);
        }
        [Test] public void SignedZerosHaveNumericIdentityAndStoredBitsSurvive()
        {
            var a = new FixedVector3(Fixed64.NegativeZero, Fixed64.Zero, Fixed64.NegativeZero); var b = FixedVector3.zero;
            Assert.IsTrue(FixedBounds3.TryFromStorage(a, b, out var signed, out _)); Assert.AreEqual(default(FixedBounds3), signed); Assert.AreEqual(default(FixedBounds3).GetHashCode(), signed.GetHashCode());
            signed.GetStorage(out var first, out _); Assert.AreEqual(Fixed64.NegativeZero.RawValue, first.x.RawValue);
        }
        [Test] public void ReplayCorpusHasStableRawStatusAndNetworkChecksum()
        {
            ulong checksum = FixedBoundsReplayCorpus.Checksum();
            TestContext.WriteLine("Bounds corpus: cases=" + FixedBoundsReplayCorpus.Count + " checksum=" + checksum);
            Assert.AreEqual(FixedBoundsReplayCorpus.ExpectedChecksum, checksum);
        }
        [Test] public unsafe void GeometryAndPointerCodecsAllocateNoGcAfterWarmup()
        {
            var box = new FixedBounds3(FixedVector3.zero, new FixedVector3(2, 4, 6));
            var ray = new FixedRay3(new FixedVector3(-3, 0, 0), FixedVector3.right); var point = new FixedVector3(7, 8, 9);
            var profile = new FixedBoundsNetworkProfile(new FixedVectorNetworkProfile(new FixedNetworkProfile(FixedRuntime.Settings, FixedKind.Signed64), 3));
            byte* bytes = stackalloc byte[60]; ulong hash = 0;
            for (int i = 0; i < 100; i++) HotStep(box, point, ray, profile, bytes, ref hash);
            long before = GC.GetAllocatedBytesForCurrentThread(); var started = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int i = 0; i < 10000; i++) HotStep(box, point, ray, profile, bytes, ref hash);
            long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - started; long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated); Assert.AreNotEqual(0, hash);
            TestContext.WriteLine("Bounds hotpath: mode=" + FixedBoundsRuntime.Settings.Representation + " iterations=10000 elapsedTicks=" + elapsed + " frequency=" + System.Diagnostics.Stopwatch.Frequency + " allocated=" + allocated);
        }
        private static unsafe void HotStep(FixedBounds3 box, FixedVector3 point, FixedRay3 ray, FixedBoundsNetworkProfile profile, byte* bytes, ref ulong hash)
        {
            box.Contains(point); box.Intersects(box); var closest = box.ClosestPoint(point); var distance = box.SqrDistance(point);
            box.IntersectRay(ray, out var t); box.TrySetCenter(box.center, out _); box.TryExpand(Fixed64.Zero, out _);
            var writer = new FixedBufferWriter(bytes, 60); FixedBoundsNetwork.TryWrite(ref writer, box, profile, out _);
            var reader = new FixedBufferReader(bytes, writer.BytesWritten); FixedBoundsNetwork.TryRead(ref reader, profile, out FixedBounds3 restored, out _);
            hash = FixedBoundsReplayCorpus.Mix(hash, unchecked((ulong)(closest.x.RawValue ^ distance.RawValue ^ t.RawValue ^ restored.GetHashCode())));
        }
    }
}
