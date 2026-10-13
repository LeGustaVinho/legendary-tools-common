using System;
using System.Numerics;
using LegendaryTools.FixedPoint;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public class SpatialGeometryTests
    {
        [OneTimeSetUp] public void Initialize()
        { if (!FixedRuntime.IsInitialized) FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize(); }
        private static BigInteger Wide(SpatialDistanceKey key) => new BigInteger(key.Low) + (new BigInteger(key.Middle) << 64) + (new BigInteger(key.High) << 128);
        [Test] public void StoredNegativeZeroIsNormalizedOnlyInTheEnvelope()
        {
            var first = new FixedVector3(Fixed64.NegativeZero, Fixed64.Zero, Fixed64.Zero);
            var second = FixedBoundsRuntime.Settings.Representation == FixedBoundsRepresentation.MinMax ? FixedVector3.one : FixedVector3.zero;
            Assert.True(FixedBounds3.TryFromStorage(first, second, out var box, out _));
            Assert.AreEqual(DeterministicStatus.None, default(SpatialBounds3Access).TryRead(box, out var envelope));
            Assert.AreEqual(0, envelope.MinX); box.GetStorage(out var preserved, out _); Assert.AreEqual(Fixed64.NegativeZero.RawValue, preserved.x.RawValue);
        }
        [Test] public void EnvelopesUnionWithoutConvertingThroughTheGlobalRepresentation()
        {
            var a = new SpatialRawBounds { MinX = -3, MaxX = -1, MinY = 0, MaxY = 1 };
            var b = new SpatialRawBounds { MinX = 0, MaxX = 2, MinY = -1, MaxY = 0 };
            var union = SpatialRawBounds.Union(a, b);
            for (int i = 0; i < 100; i++) union = SpatialRawBounds.Union(union, a);
            Assert.AreEqual(-3, union.MinX); Assert.AreEqual(2, union.MaxX); Assert.AreEqual(-1, union.MinY); Assert.AreEqual(1, union.MaxY);
        }
        [Test] public void FullFiniteDomainHasExactWideCostAndSquaredDistance()
        {
            long lo = Fixed64.MinValue.RawValue, hi = Fixed64.MaxValue.RawValue;
            var point = FixedVector3.FromRaw(lo, lo, lo);
            var box = new SpatialRawBounds { MinX = hi, MaxX = hi, MinY = hi, MaxY = hi, MinZ = hi, MaxZ = hi };
            BigInteger delta = new BigInteger(hi) - lo;
            var distance = SpatialGeometry.Distance(box, point.Bits, 3);
            Assert.AreEqual(delta * delta * 3, Wide(distance)); Assert.AreNotEqual(0, distance.High);
            var domain = new SpatialRawBounds { MinX = lo, MinY = lo, MinZ = lo, MaxX = hi, MaxY = hi, MaxZ = hi };
            Assert.AreEqual(delta * delta * 6, Wide(SpatialGeometry.Cost(domain, 3)));
            Assert.IsTrue(SpatialGeometry.Expand(domain, hi, 3).Contains(domain, 3));
        }
        [Test] public void SelectionCanDistinguishDistancesWithTheSameRoundedMetric()
        {
            var a = new SpatialRawBounds { MinX = 1, MaxX = 1 };
            var b = new SpatialRawBounds { MinX = 2, MaxX = 2 };
            var da = SpatialGeometry.Distance(a, default, 2); var db = SpatialGeometry.Distance(b, default, 2);
            Assert.Less(da.CompareTo(db), 0); Assert.AreEqual(new BigInteger(1), Wide(da)); Assert.AreEqual(new BigInteger(4), Wide(db));
            if (FixedRuntime.Settings.DecimalPlaces64 >= 1)
            { da.TryGetSquaredDistance(out var ma, out _); db.TryGetSquaredDistance(out var mb, out _); Assert.AreEqual(ma, mb); }
        }
        [Test] public void RayHitRemainsValidWhenItsReportedParameterOverflows()
        {
            long hi = Fixed64.MaxValue.RawValue;
            var box = new SpatialRawBounds { MinX = hi, MaxX = hi };
            var ray = new FixedRay2(FixedVector2.zero, FixedVector2.FromRaw(1, 0));
            Assert.AreEqual(DeterministicStatus.None, SpatialGeometry.Prepare(SpatialQuery.ForRay(ray), 2, out var query));
            Assert.IsTrue(SpatialGeometry.Match(box, query, out _, out var t)); Assert.AreEqual((ulong)hi, t.Numerator); Assert.AreEqual(1UL, t.Denominator);
            t.TryGetParameter(out _, out var status);
            if (FixedRuntime.Settings.DecimalPlaces64 > 0) Assert.IsTrue((status & FixedStatus.Overflow) != 0);
        }
        [Test] public void SegmentCanSpanTheEntireFiniteCoordinateDomain()
        {
            var start = FixedVector3.FromRaw(Fixed64.MinValue.RawValue, 0, 0);
            var end = FixedVector3.FromRaw(Fixed64.MaxValue.RawValue, 0, 0);
            var query = SpatialQuery.ForSegment(start, end);
            Assert.AreEqual(DeterministicStatus.None, SpatialGeometry.Prepare(query, 3, out var prepared));
            Assert.True(SpatialGeometry.Match(default, prepared, out _, out var hit));
            Assert.LessOrEqual(hit.CompareTo(new SpatialRayParameter(1, 1)), 0);
            var reversed = SpatialQuery.ForSegment(end, start);
            Assert.AreEqual(DeterministicStatus.None, SpatialGeometry.Prepare(reversed, 3, out prepared));
            Assert.True(SpatialGeometry.Match(default, prepared, out _, out _));
        }
        [Test] public void ParallelRaysAndDegenerateQueriesHaveInclusiveBoundaries()
        {
            var box = new SpatialRawBounds { MinX = -1, MaxX = 1, MinY = -1, MaxY = 1 };
            var ray = new FixedRay2(FixedVector2.FromRaw(1, 1), FixedVector2.zero);
            SpatialGeometry.Prepare(SpatialQuery.ForRay(ray), 2, out var query);
            Assert.True(SpatialGeometry.Match(box, query, out _, out var t)); Assert.AreEqual(0UL, t.Numerator);
            ray.origin = FixedVector2.FromRaw(2, 1); SpatialGeometry.Prepare(SpatialQuery.ForRay(ray), 2, out query);
            Assert.False(SpatialGeometry.Match(box, query, out _, out _));
        }
        [Test] public void NegativeCellsUseFloorAndFiltersAreExplicit()
        {
            Assert.AreEqual(-1, SpatialGeometry.Cell(-1, 10)); Assert.AreEqual(-1, SpatialGeometry.Cell(-10, 10)); Assert.AreEqual(-2, SpatialGeometry.Cell(-11, 10));
            Assert.True(SpatialFilter.All.Accept(new SpatialMetadata(31, 7)));
            Assert.False(default(SpatialFilter).Accept(default));
            Assert.True(new SpatialFilter(1, 2, 4).Accept(new SpatialMetadata(0, 3)));
            Assert.False(new SpatialFilter(1, 2, 4).Accept(new SpatialMetadata(0, 7)));
        }
        [Test] public void QueryAndWorkspaceLimitsRejectInvalidInputs()
        {
            Assert.AreEqual(DeterministicStatus.OutOfRange, SpatialGeometry.Prepare(default, 2, out _));
            Assert.AreEqual(DeterministicStatus.OutOfRange, SpatialGeometry.Prepare(SpatialQuery.ForSphere(FixedVector3.NaN, 1), 3, out _));
            Assert.AreEqual(DeterministicStatus.OutOfRange, SpatialGeometry.Prepare(SpatialQuery.ForCircle(FixedVector2.zero, -1), 2, out _));
            Assert.AreEqual(DeterministicStatus.CapacityExceeded, SpatialQueryScratch.TryMeasure<StableHandle>(int.MaxValue, int.MaxValue, out _));
            Assert.False(new SpatialIndexOptions(8, SpatialIndexStrategy.SparseGrid).IsValid);
        }
    }
}
