using LegendaryTools.FixedPoint;
using NUnit.Framework;
#if !FIXEDPOINT_STANDALONE
using Unity.Collections;
#endif

namespace LegendaryTools.Deterministic.Tests
{
    public class SpatialSnapshotAdmissionTests
    {
        [OneTimeSetUp] public void Initialize()
        { if (!FixedRuntime.IsInitialized) FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize(); }
        [TestCase(SpatialIndexStrategy.DynamicBvh, SpatialRollbackPolicy.Compact)]
        [TestCase(SpatialIndexStrategy.SparseGrid, SpatialRollbackPolicy.Compact)]
        [TestCase(SpatialIndexStrategy.StaticBvh, SpatialRollbackPolicy.Compact)]
        [TestCase(SpatialIndexStrategy.DynamicBvh, SpatialRollbackPolicy.FastRestore)]
        [TestCase(SpatialIndexStrategy.SparseGrid, SpatialRollbackPolicy.FastRestore)]
        [TestCase(SpatialIndexStrategy.StaticBvh, SpatialRollbackPolicy.FastRestore)]
        public void PublicSnapshotsLeaveTheSpareAvailableForAtomicBatches(SpatialIndexStrategy strategy, SpatialRollbackPolicy rollback)
        {
            var options = new SpatialIndexOptions(new BufferOptions(4, snapshotSlots: 2), strategy, rollback: rollback, cellSize: Fixed64.FromRaw(8));
            using var managed = new DeterministicSpatialIndex3<int, int, Int32Policy>(options);
            managed.TryAdd(1, default, 123).ThrowIfFailure(); managed.TryCapture(out var a).ThrowIfFailure(); managed.TryCapture(out var b).ThrowIfFailure();
            var commands = new[] { new SpatialMutation<int, int, FixedBounds3> { Kind = SpatialMutationKind.SetData, Key = 1, Data = 999 },
                new SpatialMutation<int, int, FixedBounds3> { Kind = SpatialMutationKind.Remove, Key = 42 } };
            try
            {
                Assert.AreEqual(DeterministicStatus.SnapshotCapacityExceeded, managed.TryCapture(out _));
                Assert.AreEqual(DeterministicStatus.NotFound, managed.TryApplyBatch(commands));
                managed.TryGet(1, out var kept).ThrowIfFailure(); Assert.AreEqual(123, kept.Data);
                managed.TryApplyBatch(new[] { commands[0] }).ThrowIfFailure(); managed.TryRestore(a).ThrowIfFailure();
            }
            finally { b.Dispose(); a.Dispose(); }
#if !FIXEDPOINT_STANDALONE
            var native = new NativeDeterministicSpatialIndex3<int, int, Int32Policy>(options, Allocator.Persistent);
            try
            {
                native.TryAdd(1, default, 123).ThrowIfFailure(); native.TryCapture(out var na).ThrowIfFailure(); native.TryCapture(out var nb).ThrowIfFailure();
                try
                {
                    Assert.AreEqual(DeterministicStatus.SnapshotCapacityExceeded, native.TryCapture(out _));
                    Assert.AreEqual(DeterministicStatus.NotFound, native.TryApplyBatch(commands));
                    native.TryGet(1, out var kept).ThrowIfFailure(); Assert.AreEqual(123, kept.Data);
                    native.TryApplyBatch(new[] { commands[0] }).ThrowIfFailure(); native.TryRestore(na).ThrowIfFailure();
                }
                finally { nb.Dispose(); na.Dispose(); }
            }
            finally { native.Dispose(); }
#endif
        }
    }
}
