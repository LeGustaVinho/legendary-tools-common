using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using LegendaryTools.FixedPoint;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public unsafe class SpatialAdversarialQueryTests
    {
        [OneTimeSetUp] public void Initialize()
        { if (!FixedRuntime.IsInitialized) FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize(); }
        private readonly struct Fraction : IComparable<Fraction>
        {
            internal readonly BigInteger N, D;
            internal Fraction(BigInteger n, BigInteger d) { N = d.Sign < 0 ? -n : n; D = BigInteger.Abs(d); }
            public int CompareTo(Fraction other) => (N * other.D).CompareTo(other.N * D);
        }
        private struct Keep<TBounds> : ISpatialPredicate<int, int, TBounds> where TBounds : unmanaged
        { public bool Accept(in SpatialRecord<int, int, TBounds> record) => record.Key % 5 != 0; }
        private static BigInteger Number(long raw) => raw == Fixed64.NegativeZero.RawValue ? BigInteger.Zero : new BigInteger(raw);
        private static (BigInteger[] Min, BigInteger[] Max) Corners<TBounds>(TBounds bounds, int dimensions) where TBounds : unmanaged
        {
            FixedVector3 a = default, b = default;
            if (dimensions == 2) { (*(FixedBounds2*)&bounds).GetStorage(out var x, out var y); a = x; b = y; }
            else (*(FixedBounds3*)&bounds).GetStorage(out a, out b);
            var min = new BigInteger[dimensions]; var max = new BigInteger[dimensions];
            for (int i = 0; i < dimensions; i++)
            {
                var first = Number(a[i].RawValue); var second = Number(b[i].RawValue);
                min[i] = FixedBoundsRuntime.Settings.Representation == FixedBoundsRepresentation.MinMax ? first : first - second;
                max[i] = FixedBoundsRuntime.Settings.Representation == FixedBoundsRepresentation.MinMax ? second : first + second;
            }
            return (min, max);
        }
        private static BigInteger Distance((BigInteger[] Min, BigInteger[] Max) box, BigInteger[] point)
        {
            BigInteger sum = 0;
            for (int i = 0; i < point.Length; i++) { var d = point[i] < box.Min[i] ? box.Min[i] - point[i] : point[i] > box.Max[i] ? point[i] - box.Max[i] : BigInteger.Zero; sum += d * d; }
            return sum;
        }
        private static bool Ray((BigInteger[] Min, BigInteger[] Max) box, BigInteger[] origin, BigInteger[] direction, bool segment, out Fraction enter)
        {
            enter = new Fraction(0, 1); Fraction? exit = null;
            for (int i = 0; i < origin.Length; i++)
            {
                if (direction[i].IsZero) { if (origin[i] < box.Min[i] || origin[i] > box.Max[i]) return false; continue; }
                var a = new Fraction(box.Min[i] - origin[i], direction[i]); var b = new Fraction(box.Max[i] - origin[i], direction[i]);
                if (a.CompareTo(b) > 0) { var swap = a; a = b; b = swap; }
                if (a.CompareTo(enter) > 0) enter = a;
                if (!exit.HasValue || b.CompareTo(exit.Value) < 0) exit = b;
                if (exit.HasValue && enter.CompareTo(exit.Value) > 0) return false;
            }
            return !segment || enter.CompareTo(new Fraction(1, 1)) <= 0;
        }
        private static void Run<TBounds, TAccess>(SpatialIndexStrategy strategy)
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds>
        {
            int dimensions = default(TAccess).Dimensions; const int count = 96;
            using var index = new DeterministicSpatialIndex<int, int, TBounds, TAccess, Int32Policy>(new SpatialIndexOptions(count, strategy,
                cellSize: Fixed64.FromRaw(16), maxCellsPerObject: 8, maxCellsPerQuery: 32));
            var reference = new Dictionary<int, (BigInteger[] Min, BigInteger[] Max)>();
            for (int key = 0; key < count; key++)
            {
                ulong h = DeterministicHash.Mix((ulong)key + 234123);
                long x = (long)(h % 128) - 64, y = (long)((h >> 17) % 128) - 64, z = (long)((h >> 33) % 128) - 64;
                if (key == 0) x = y = z = Fixed64.MinValue.RawValue;
                if (key == 1) x = y = z = Fixed64.MaxValue.RawValue;
                int size = key < 2 ? 0 : key % 11;
                TBounds box = default;
                if (dimensions == 2) *(FixedBounds2*)&box = FixedBounds2.FromMinMax(FixedVector2.FromRaw(x, y), FixedVector2.FromRaw(x + size, y + size));
                else *(FixedBounds3*)&box = FixedBounds3.FromMinMax(FixedVector3.FromRaw(x, y, z), FixedVector3.FromRaw(x + size, y + size, z + size));
                index.TryAdd(key, box, key, new SpatialMetadata((uint)(key % 4), (uint)(key % 8))).ThrowIfFailure(); reference.Add(key, Corners(box, dimensions));
            }
            index.TryRebuild().ThrowIfFailure(); index.TryMeasureQueryScratch(out int bytes).ThrowIfFailure();
            var work = new byte[bytes]; var hits = new SpatialHit<int>[count];
            fixed (byte* scratch = work) fixed (SpatialHit<int>* output = hits)
            for (int sample = 0; sample < 120; sample++)
            {
                long lo = Fixed64.MinValue.RawValue, hi = Fixed64.MaxValue.RawValue;
                long p = sample < 8 ? new[] { 0L, -1, 1, -64, 64, lo, hi, 0 }[sample] : sample % 131 - 65;
                var point = FixedVector3.FromRaw(p, sample < 8 ? p : sample % 33 - 16, dimensions == 2 ? 0 : sample < 8 ? p : sample % 27 - 13);
                var end = FixedVector3.FromRaw(sample % 2 == 0 ? hi : lo, sample % 3 == 0 ? hi : point.y.RawValue, dimensions == 2 ? 0 : point.z.RawValue);
                var direction = sample % 4 == 0 ? FixedVector3.zero : FixedVector3.FromRaw(sample % 3 - 1, sample % 2, dimensions == 2 ? 0 : sample % 5 - 2);
                var origin = Enumerable.Range(0, dimensions).Select(i => Number(point[i].RawValue)).ToArray();
                var rayDirection = Enumerable.Range(0, dimensions).Select(i => Number(direction[i].RawValue)).ToArray();
                var segmentDirection = Enumerable.Range(0, dimensions).Select(i => Number(end[i].RawValue) - origin[i]).ToArray();
                bool filtered = sample % 3 == 0; var filter = filtered ? new SpatialFilter(10, requiredFlags: 1, excludedFlags: 4) : SpatialFilter.All;
                Func<int, bool> accepts = key => key % 5 != 0 && (!filtered || (key % 4 == 1 || key % 4 == 3) && (key % 8 & 1) != 0 && (key % 8 & 4) == 0);
                foreach (int kind in new[] { 0, 1, 2, 3, 4 })
                {
                    var expected = new List<(int Key, BigInteger Distance, Fraction Ray)>();
                    SpatialQuery query;
                    if (kind == 0) query = dimensions == 2 ? SpatialQuery.ForNearest((FixedVector2)point, 11) : SpatialQuery.ForNearest(point, 11);
                    else if (kind == 1) query = dimensions == 2 ? SpatialQuery.ForCircle((FixedVector2)point, Fixed64.FromRaw(12)) : SpatialQuery.ForSphere(point, Fixed64.FromRaw(12));
                    else if (kind == 2) query = dimensions == 2 ? SpatialQuery.ForRay(new FixedRay2((FixedVector2)point, (FixedVector2)direction)) : SpatialQuery.ForRay(new FixedRay3(point, direction));
                    else if (kind == 3) query = dimensions == 2 ? SpatialQuery.ForSegment((FixedVector2)point, (FixedVector2)end) : SpatialQuery.ForSegment(point, end);
                    else query = dimensions == 2 ? SpatialQuery.ForPoint((FixedVector2)point) : SpatialQuery.ForPoint(point);
                    foreach (var record in reference)
                    {
                        if (!accepts(record.Key)) continue;
                        var distance = Distance(record.Value, origin); var parameter = new Fraction(0, 1);
                        bool match = kind == 0 || kind == 1 && distance <= 144 || kind == 4 && distance == 0 ||
                            (kind == 2 || kind == 3) && Ray(record.Value, origin, kind == 2 ? rayDirection : segmentDirection, kind == 3, out parameter);
                        if (match) expected.Add((record.Key, distance, parameter));
                    }
                    IEnumerable<(int Key, BigInteger Distance, Fraction Ray)> sorted = kind == 0 ? expected.OrderBy(x => x.Distance).ThenBy(x => x.Key).Take(11) :
                        kind == 2 || kind == 3 ? expected.OrderBy(x => x.Ray).ThenBy(x => x.Key) : expected.OrderBy(x => x.Key);
                    index.TryQuery(query, new SpatialQueryScratch(scratch, bytes), output, count, out int written, filter, default(Keep<TBounds>)).ThrowIfFailure();
                    CollectionAssert.AreEqual(sorted.Select(x => x.Key).ToArray(), hits.Take(written).Select(x => x.Key).ToArray(), $"d={dimensions} strategy={strategy} sample={sample} kind={kind}");
                }
            }
        }
        [TestCase(SpatialIndexStrategy.DynamicBvh)] [TestCase(SpatialIndexStrategy.SparseGrid)] [TestCase(SpatialIndexStrategy.StaticBvh)]
        public void WideRaysSegmentsSpheresAndFiltersMatchIndependentBigIntegerOracle(SpatialIndexStrategy strategy)
        { Run<FixedBounds2, SpatialBounds2Access>(strategy); Run<FixedBounds3, SpatialBounds3Access>(strategy); }
    }
}
