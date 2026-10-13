using System;
using System.Collections.Generic;
using LegendaryTools.FixedPoint;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public unsafe class SpatialPairTests
    {
        [OneTimeSetUp] public void Initialize()
        { if (!FixedRuntime.IsInitialized) FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize(); }
        private struct KeepPairs : ISpatialPairPredicate<int, int, FixedBounds2>
        { public bool Accept(in SpatialRecord<int, int, FixedBounds2> left, in SpatialRecord<int, int, FixedBounds2> right) => (left.Data + right.Data) % 3 == 0; }
        [TestCase(SpatialIndexStrategy.DynamicBvh)]
        [TestCase(SpatialIndexStrategy.SparseGrid)]
        [TestCase(SpatialIndexStrategy.StaticBvh)]
        public void StreamingPairsMatchIndependentIntegerOracleAndInvalidateOnChanges(SpatialIndexStrategy strategy)
        {
            using var index = new DeterministicSpatialIndex2<int, int, Int32Policy>(new SpatialIndexOptions(72, strategy, cellSize: Fixed64.FromRaw(4), maxCellsPerObject: 16));
            var oracle = new SortedDictionary<int, (int X, int Y, int Data, int Layer)>();
            for (int i = 0; i < 72; i++)
            {
                int j = i * 19 % 72, x = j / 9 * 4, y = j % 9 * 2, key = 400 - j * 7;
                var bounds = FixedBounds2.FromMinMax(FixedVector2.FromRaw(x, y), FixedVector2.FromRaw(x + 6, y + 6));
                index.TryAdd(key, bounds, j, new SpatialMetadata((uint)(j % 3))).ThrowIfFailure(); oracle.Add(key, (x, y, j, j % 3));
            }
            var expected = new List<(int, int)>();
            foreach (var left in oracle) foreach (var right in oracle)
                if (left.Key < right.Key && left.Value.Layer < 2 && right.Value.Layer < 2 && (left.Value.Data + right.Value.Data) % 3 == 0 &&
                    left.Value.X <= right.Value.X + 6 && right.Value.X <= left.Value.X + 6 && left.Value.Y <= right.Value.Y + 6 && right.Value.Y <= left.Value.Y + 6)
                    expected.Add((left.Key, right.Key));
            index.TryMeasurePairScratch(out int bytes).ThrowIfFailure(); var arena = new byte[bytes];
            fixed (byte* work = arena)
            {
                Assert.AreEqual(DeterministicStatus.BufferTooSmall, index.TryGetPairs(new SpatialPairScratch(work, 1), out _));
                index.TryGetPairs(new SpatialPairScratch(work, bytes), new SpatialFilter(3), default(KeepPairs), out var stream).ThrowIfFailure();
                var actual = new List<(int, int)>();
                while (true) { stream.TryNext(out var pair, out bool present).ThrowIfFailure(); if (!present) break; actual.Add((pair.First, pair.Second)); }
                CollectionAssert.AreEqual(expected, actual); Assert.Greater(actual.Count, 0);
                stream.TryNext(out _, out bool ended).ThrowIfFailure(); Assert.IsFalse(ended);
                index.TryGetPairs(new SpatialPairScratch(work, bytes), out var stale).ThrowIfFailure();
                index.TrySetData(400, 800).ThrowIfFailure(); Assert.AreEqual(DeterministicStatus.Invalidated, stale.TryNext(out _, out _));
                var reader = index.AsReadOnly(); index.Dispose(); Assert.AreEqual(DeterministicStatus.Disposed, reader.Check());
            }
        }
        [Test] public void DensePairsUseLinearWorkspaceAndNoGcAllocations()
        {
            using var index = new DeterministicSpatialIndex3<int, int, Int32Policy>(new SpatialIndexOptions(256));
            for (int i = 0; i < 256; i++) index.TryAdd(255 - i, default, i).ThrowIfFailure();
            index.TryMeasurePairScratch(out int bytes).ThrowIfFailure(); Assert.Less(bytes, 256 * 256 * sizeof(int));
            var arena = new byte[bytes];
            fixed (byte* work = arena)
            {
                var scratch = new SpatialPairScratch(work, bytes);
                index.TryGetPairs(scratch, out var warmup).ThrowIfFailure(); warmup.TryNext(out _, out _).ThrowIfFailure();
                long before = GC.GetAllocatedBytesForCurrentThread(); int pairs = 0, errors = 0;
                errors |= (int)index.TryGetPairs(scratch, out var stream);
                while (true) { errors |= (int)stream.TryNext(out _, out bool present); if (!present) break; pairs++; }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.AreEqual(32640, pairs); Assert.AreEqual(0, errors); Assert.AreEqual(0, allocated);
            }
        }
    }
}
