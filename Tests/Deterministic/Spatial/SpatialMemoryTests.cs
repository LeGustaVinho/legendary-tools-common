using LegendaryTools.FixedPoint;
using NUnit.Framework;
#if !FIXEDPOINT_STANDALONE
using Unity.Collections;
#endif

namespace LegendaryTools.Deterministic.Tests
{
    public class SpatialMemoryTests
    {
        [OneTimeSetUp] public void Initialize()
        { if (!FixedRuntime.IsInitialized) FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize(); }
        [TestCase(SpatialIndexStrategy.DynamicBvh, SpatialRollbackPolicy.Compact)]
        [TestCase(SpatialIndexStrategy.SparseGrid, SpatialRollbackPolicy.Compact)]
        [TestCase(SpatialIndexStrategy.StaticBvh, SpatialRollbackPolicy.Compact)]
        [TestCase(SpatialIndexStrategy.DynamicBvh, SpatialRollbackPolicy.FastRestore)]
        [TestCase(SpatialIndexStrategy.SparseGrid, SpatialRollbackPolicy.FastRestore)]
        [TestCase(SpatialIndexStrategy.StaticBvh, SpatialRollbackPolicy.FastRestore)]
        public void SizingMatchesActualFixedPayloadIncludingCowWorstCase(SpatialIndexStrategy strategy, SpatialRollbackPolicy rollback)
        {
            var options = new SpatialIndexOptions(new BufferOptions(64, snapshotSlots: 3, pageBytes: 128), strategy,
                rollback: rollback, cellSize: Fixed64.FromRaw(8), maxCellsPerObject: 8);
            SpatialMemory.TryMeasure<long, int, FixedBounds2>(options, out var expected).ThrowIfFailure();
            using var managed = new DeterministicSpatialIndex2<long, int, Int64Policy>(options);
            var actual = managed.MemoryStats;
            Assert.AreEqual(expected.RecordsReservedBytes, actual.RecordsReservedBytes);
            Assert.AreEqual(expected.AccelerationReservedBytes, actual.AccelerationReservedBytes);
            Assert.AreEqual(expected.WorkspaceReservedBytes, actual.WorkspaceReservedBytes);
            Assert.AreEqual(expected.HistoryReservedBytes, actual.HistoryReservedBytes);
            Assert.Greater(actual.UsedBytes, 0); Assert.Less(actual.UsedBytes, actual.ReservedBytes);
#if !FIXEDPOINT_STANDALONE
            var native = new NativeDeterministicSpatialIndex2<long, int, Int64Policy>(options, Allocator.Persistent);
            try
            {
                Assert.AreEqual(expected.ReservedBytes, native.MemoryStats.ReservedBytes);
                Assert.AreEqual(actual.UsedBytes, native.MemoryStats.UsedBytes);
            }
            finally { native.Dispose(); }
#endif
            managed.TryAdd(1, default, 123).ThrowIfFailure(); managed.TryCapture(out var snapshot).ThrowIfFailure();
            try
            {
                long before = managed.MemoryStats.UsedBytes;
                managed.TrySetData(1, 999).ThrowIfFailure();
                Assert.Greater(managed.MemoryStats.UsedBytes, before); Assert.AreEqual(expected.ReservedBytes, managed.MemoryStats.ReservedBytes);
                managed.TryRestore(snapshot).ThrowIfFailure(); managed.TryGet(1, out var record).ThrowIfFailure(); Assert.AreEqual(123, record.Data);
            }
            finally { snapshot.Dispose(); }
        }
        [Test] public void InvalidLimitsFailAdmissionWithoutAllocatingThePools()
        {
            Assert.AreEqual(DeterministicStatus.InvalidConfiguration, SpatialMemory.TryMeasure<int, int, FixedBounds3>(
                new SpatialIndexOptions(1 << 27, SpatialIndexStrategy.SparseGrid, cellSize: Fixed64.One), out _));
            Assert.AreEqual(DeterministicStatus.CapacityExceeded, SpatialMemory.TryMeasure<int, int, FixedBounds3>(
                new SpatialIndexOptions(new BufferOptions(8, snapshotSlots: int.MaxValue)), out _));
            Assert.AreEqual(DeterministicStatus.CapacityExceeded, SpatialMemory.TryMeasure<int, int, FixedBounds3>(new SpatialIndexOptions(1 << 27), out _));
            Assert.AreEqual(DeterministicStatus.InvalidConfiguration, SpatialMemory.TryMeasure<int, int, FixedBounds3>(default, out _));
        }
    }
}
