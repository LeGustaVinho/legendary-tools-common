using System;
using LegendaryTools.FixedPoint;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public unsafe class SpatialCanonicalTests
    {
        [OneTimeSetUp] public void Initialize()
        { if (!FixedRuntime.IsInitialized) FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize(); }
        private static FixedBounds3 Box(int value) => FixedBounds3.FromMinMax(FixedVector3.FromRaw(value, 0, 0), FixedVector3.FromRaw(value + 4, 4, 4));
        private static int Encode(DeterministicSpatialIndex3<int, int, Int32Policy> index, byte[] packet)
        {
            fixed (byte* bytes = packet)
            {
                var writer = new DeterministicWriter(bytes, packet.Length);
                index.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy), default(FixedBounds3Policy)).ThrowIfFailure();
                var measure = DeterministicWriter.CreateMeasure();
                index.TryWriteCanonical(ref measure, default(Int32Policy), default(Int32Policy), default(FixedBounds3Policy)).ThrowIfFailure();
                Assert.AreEqual(writer.Position, measure.Position); return writer.Position;
            }
        }
        [TestCase(SpatialIndexStrategy.DynamicBvh)]
        [TestCase(SpatialIndexStrategy.SparseGrid)]
        [TestCase(SpatialIndexStrategy.StaticBvh)]
        public void LogicalBytesSurviveImportAndFutureChangesAcrossStrategies(SpatialIndexStrategy targetStrategy)
        {
            using var source = new DeterministicSpatialIndex3<int, int, Int32Policy>(new SpatialIndexOptions(32));
            using var target = new DeterministicSpatialIndex3<int, int, Int32Policy>(new SpatialIndexOptions(32, targetStrategy, cellSize: Fixed64.FromRaw(8)));
            for (int i = 0; i < 20; i++) source.TryAdd(i, Box(i * 3 - 30), i + 10).ThrowIfFailure();
            source.TryRemove(3).ThrowIfFailure(); source.TryRemove(15).ThrowIfFailure(); source.TryAdd(99, Box(0), 99).ThrowIfFailure();
            var packet = new byte[16384]; var after = new byte[16384]; var arena = new byte[32768]; int length = Encode(source, packet);
            fixed (byte* bytes = packet) fixed (byte* work = arena)
            {
                var reader = new DeterministicReader(bytes, length); var scratch = new CanonicalScratch(work, arena.Length);
                target.TryReadCanonical(ref reader, ref scratch, default(Int32Policy), default(Int32Policy), default(FixedBounds3Policy)).ThrowIfFailure();
                Assert.AreEqual(length, reader.Position);
            }
            int secondLength = Encode(target, after); Assert.AreEqual(length, secondLength);
            for (int i = 0; i < length; i++) Assert.AreEqual(packet[i], after[i]);
            source.TryAdd(88, Box(1), 7).ThrowIfFailure(); target.TryAdd(88, Box(1), 7).ThrowIfFailure();
            source.TryRemove(0).ThrowIfFailure(); target.TryRemove(0).ThrowIfFailure();
            source.TryUpdateBounds(5, Box(80)).ThrowIfFailure(); target.TryUpdateBounds(5, Box(80)).ThrowIfFailure();
            length = Encode(source, packet); secondLength = Encode(target, after); Assert.AreEqual(length, secondLength);
            for (int i = 0; i < length; i++) Assert.AreEqual(packet[i], after[i]);
            source.TryMeasureQueryScratch(out int scratchBytes).ThrowIfFailure(); var queryArena = new byte[scratchBytes];
            var a = new SpatialHit<int>[32]; var b = new SpatialHit<int>[32];
            fixed (byte* work = queryArena) fixed (SpatialHit<int>* left = a) fixed (SpatialHit<int>* right = b)
            {
                var scratch = new SpatialQueryScratch(work, scratchBytes); var query = SpatialQuery.ForNearest(FixedVector3.zero, 10);
                source.TryQuery(query, scratch, left, 32, out int countA).ThrowIfFailure(); target.TryQuery(query, scratch, right, 32, out int countB).ThrowIfFailure();
                Assert.AreEqual(countA, countB); for (int i = 0; i < countA; i++) Assert.AreEqual(a[i].Key, b[i].Key);
            }
        }
        [Test] public void TruncatedAndWrongSchemaImportsPreserveStateAndCursors()
        {
            using var source = new DeterministicSpatialIndex3<int, int, Int32Policy>(new SpatialIndexOptions(4));
            source.TryAdd(1, Box(0), 123).ThrowIfFailure(); var packet = new byte[2048]; int length = Encode(source, packet);
            using var target = new DeterministicSpatialIndex3<int, int, Int32Policy>(new SpatialIndexOptions(4));
            target.TryAdd(2, Box(100), 456).ThrowIfFailure(); var expected = new byte[2048]; var actual = new byte[2048]; int expectedLength = Encode(target, expected);
            var arena = new byte[8192];
            fixed (byte* bytes = packet) fixed (byte* work = arena)
            {
                for (int size = 0; size < length; size++)
                {
                    var reader = new DeterministicReader(bytes, size); var scratch = new CanonicalScratch(work, arena.Length);
                    Assert.AreNotEqual(DeterministicStatus.None, target.TryReadCanonical(ref reader, ref scratch, default(Int32Policy), default(Int32Policy), default(FixedBounds3Policy)));
                    Assert.AreEqual(0, reader.Position); Assert.AreEqual(0, scratch.Position); Assert.AreEqual(1, target.Count);
                    target.TryGet(2, out var kept).ThrowIfFailure(); Assert.AreEqual(456, kept.Data);
                }
                packet[18] ^= 1;
                var bad = new DeterministicReader(bytes, length); var ws = new CanonicalScratch(work, arena.Length);
                Assert.AreNotEqual(DeterministicStatus.None, target.TryReadCanonical(ref bad, ref ws, default(Int32Policy), default(Int32Policy), default(FixedBounds3Policy)));
                Assert.AreEqual(0, bad.Position);
            }
            int finalLength = Encode(target, actual); Assert.AreEqual(expectedLength, finalLength);
            for (int i = 0; i < expectedLength; i++) Assert.AreEqual(expected[i], actual[i]);
        }
    }
}
