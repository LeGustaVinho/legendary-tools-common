using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using LegendaryTools.FixedPoint;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public unsafe class SpatialManagedIndexTests
    {
        [OneTimeSetUp] public void Initialize()
        { if (!FixedRuntime.IsInitialized) FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize(); }
        private static FixedBounds3 Box(long x, long y, long z, int size) => FixedBounds3.FromMinMax(
            FixedVector3.FromRaw(x, y, z), FixedVector3.FromRaw(x + size, y + size, z + size));
        private static BigInteger Integer(Fixed64 n) => n == Fixed64.Zero ? BigInteger.Zero : new BigInteger(n.RawValue);
        private static BigInteger Distance(FixedBounds3 box, FixedVector3 point)
        {
            var min = box.min; var max = box.max; BigInteger sum = 0;
            for (int i = 0; i < 3; i++)
            { var p = Integer(point[i]); var lo = Integer(min[i]); var hi = Integer(max[i]); var d = p < lo ? lo - p : p > hi ? p - hi : BigInteger.Zero; sum += d * d; }
            return sum;
        }
        private static bool Overlap(FixedBounds3 a, FixedBounds3 b)
        {
            for (int i = 0; i < 3; i++) if (Integer(a.min[i]) > Integer(b.max[i]) || Integer(a.max[i]) < Integer(b.min[i])) return false;
            return true;
        }
        private static void AssertTree(DeterministicSpatialIndex3<int, int, Int32Policy> index)
        {
            if (index.Options.Strategy == SpatialIndexStrategy.SparseGrid || index.Storage.State.Dirty) return;
            var seen = new HashSet<int>(); int leaves = 0;
            if (index.Storage.State.Root < 0) { Assert.AreEqual(0, index.Count); return; }
            var stack = new Stack<int>(); stack.Push(index.Storage.State.Root);
            Assert.AreEqual(-1, index.Storage.ReadNode(index.Storage.State.Root).Parent);
            while (stack.Count != 0)
            {
                int id = stack.Pop(); Assert.True(seen.Add(id), "Cycle or duplicate node"); var n = index.Storage.ReadNode(id);
                if (n.IsLeaf) { leaves++; Assert.AreEqual(1, index.Storage.ReadEntry(n.RecordSlot).Active); continue; }
                var a = index.Storage.ReadNode(n.Left); var b = index.Storage.ReadNode(n.Right);
                Assert.AreEqual(id, a.Parent); Assert.AreEqual(id, b.Parent); Assert.True(n.Bounds.Contains(a.Bounds, 3)); Assert.True(n.Bounds.Contains(b.Bounds, 3));
                Assert.AreEqual(1 + Math.Max(a.Height, b.Height), n.Height); stack.Push(n.Left); stack.Push(n.Right);
            }
            Assert.AreEqual(index.Count, leaves);
        }
        [TestCase(SpatialIndexStrategy.DynamicBvh)]
        [TestCase(SpatialIndexStrategy.SparseGrid)]
        [TestCase(SpatialIndexStrategy.StaticBvh)]
        public void RandomChangesMatchIndependentRangeAndNearestQueries(SpatialIndexStrategy strategy)
        {
            const int capacity = 128;
            var options = new SpatialIndexOptions(capacity, strategy, cellSize: Fixed64.FromRaw(8), maxCellsPerObject: 8, maxCellsPerQuery: 64);
            using var index = new DeterministicSpatialIndex3<int, int, Int32Policy>(options);
            var reference = new SortedDictionary<int, FixedBounds3>(); var random = new Random(72191);
            index.TryMeasureQueryScratch(out int bytes).ThrowIfFailure(); var arena = new byte[bytes]; var hits = new SpatialHit<int>[capacity];
            fixed (byte* work = arena) fixed (SpatialHit<int>* output = hits)
            {
                var scratch = new SpatialQueryScratch(work, bytes);
                for (int tick = 0; tick < 800; tick++)
                {
                    int key = random.Next(capacity), kind = random.Next(4);
                    var box = Box(random.Next(-50, 50), random.Next(-50, 50), random.Next(-50, 50), random.Next(0, 28));
                    if (!reference.ContainsKey(key)) { index.TryAdd(key, box, key).ThrowIfFailure(); reference.Add(key, box); }
                    else if (kind == 0) { index.TryRemove(key).ThrowIfFailure(); reference.Remove(key); }
                    else { index.TryUpdateBounds(key, box).ThrowIfFailure(); reference[key] = box; }
                    if (tick % 11 == 0) index.TryRebuild().ThrowIfFailure(); AssertTree(index);
                    var region = Box(random.Next(-50, 50), random.Next(-50, 50), random.Next(-50, 50), 30);
                    SpatialQuery.TryAabb(region, out var query).ThrowIfFailure();
                    index.TryQuery(query, scratch, output, capacity, out int count).ThrowIfFailure();
                    var expected = reference.Where(pair => Overlap(pair.Value, region)).Select(pair => pair.Key).ToArray();
                    CollectionAssert.AreEqual(expected, hits.Take(count).Select(hit => hit.Key).ToArray());
                    var point = FixedVector3.FromRaw(random.Next(-50, 50), random.Next(-50, 50), random.Next(-50, 50));
                    query = SpatialQuery.ForNearest(point, 7); index.TryQuery(query, scratch, output, capacity, out count).ThrowIfFailure();
                    expected = reference.OrderBy(pair => Distance(pair.Value, point)).ThenBy(pair => pair.Key).Take(7).Select(pair => pair.Key).ToArray();
                    CollectionAssert.AreEqual(expected, hits.Take(count).Select(hit => hit.Key).ToArray());
                }
            }
        }
        [TestCase(SpatialIndexStrategy.DynamicBvh)]
        [TestCase(SpatialIndexStrategy.SparseGrid)]
        [TestCase(SpatialIndexStrategy.StaticBvh)]
        public void BatchFailuresAndSnapshotsRestoreAllRelations(SpatialIndexStrategy strategy)
        {
            var options = new SpatialIndexOptions(new BufferOptions(16, snapshotSlots: 2), strategy, cellSize: Fixed64.FromRaw(4));
            using var index = new DeterministicSpatialIndex3<int, int, Int32Policy>(options);
            var a = Box(-10, -10, -10, 4); var b = Box(20, 20, 20, 2);
            index.TryAdd(1, a, 7).ThrowIfFailure(); index.TryAdd(2, b, 8).ThrowIfFailure(); index.TryRebuild().ThrowIfFailure();
            index.TryCapture(out var snapshot).ThrowIfFailure();
            var commands = new[] {
                new SpatialMutation<int, int, FixedBounds3> { Kind = SpatialMutationKind.UpdateBounds, Key = 1, Bounds = b },
                new SpatialMutation<int, int, FixedBounds3> { Kind = SpatialMutationKind.Remove, Key = 2 },
                new SpatialMutation<int, int, FixedBounds3> { Kind = SpatialMutationKind.Add, Key = 1, Bounds = a },
            };
            Assert.AreEqual(DeterministicStatus.DuplicateKey, index.TryApplyBatch(commands)); Assert.AreEqual(2, index.Count);
            index.TryGet(1, out var record).ThrowIfFailure(); Assert.AreEqual(a, record.Bounds);
            index.TryGet(2, out record).ThrowIfFailure(); Assert.AreEqual(b, record.Bounds); AssertTree(index);
            index.TryClear().ThrowIfFailure(); Assert.AreEqual(0, index.Count);
            index.TryRestore(snapshot).ThrowIfFailure(); snapshot.Dispose(); Assert.AreEqual(2, index.Count); AssertTree(index);
            index.TryMeasureQueryScratch(out int bytes).ThrowIfFailure(); var arena = new byte[bytes]; var hits = new SpatialHit<int>[16];
            fixed (byte* work = arena) fixed (SpatialHit<int>* output = hits)
            {
                SpatialQuery.TryAabb(a, out var query).ThrowIfFailure();
                index.TryQuery(query, new SpatialQueryScratch(work, bytes), output, 16, out int count).ThrowIfFailure();
                Assert.AreEqual(1, count); Assert.AreEqual(1, hits[0].Key);
            }
        }
        [Test] public void OutputBuffersAreUnchangedOnInsufficientCapacityAndTiesUseKeys()
        {
            using var index = new DeterministicSpatialIndex2<int, int, Int32Policy>(new SpatialIndexOptions(8));
            var box = FixedBounds2.FromMinMax(FixedVector2.FromRaw(-1, -1), FixedVector2.FromRaw(1, 1));
            index.TryAdd(7, box, 0).ThrowIfFailure(); index.TryAdd(3, box, 0).ThrowIfFailure();
            index.TryMeasureQueryScratch(out int bytes).ThrowIfFailure(); var arena = new byte[bytes]; var hits = new SpatialHit<int>[1]; hits[0].Key = 123;
            fixed (byte* work = arena) fixed (SpatialHit<int>* output = hits)
            {
                var scratch = new SpatialQueryScratch(work, bytes); SpatialQuery.TryAabb(box, out var query).ThrowIfFailure();
                Assert.AreEqual(DeterministicStatus.BufferTooSmall, index.TryQuery(query, scratch, output, 1, out int count));
                Assert.AreEqual(0, count); Assert.AreEqual(123, hits[0].Key);
                query = SpatialQuery.ForNearest(FixedVector2.zero); index.TryQuery(query, scratch, output, 1, out count).ThrowIfFailure();
                Assert.AreEqual(1, count); Assert.AreEqual(3, hits[0].Key);
            }
        }
    }
}
