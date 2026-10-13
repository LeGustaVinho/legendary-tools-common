using System;
using LegendaryTools.FixedPoint;
using LegendaryTools.Deterministic.Samples;
using NUnit.Framework;
#if !FIXEDPOINT_STANDALONE
using Unity.Collections;
#endif

namespace LegendaryTools.Deterministic.Tests
{
    public unsafe class SpatialGrowthAndSampleTests
    {
        [OneTimeSetUp] public void Initialize()
        { if (!FixedRuntime.IsInitialized) FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize(); }
        [TestCase(SpatialIndexStrategy.DynamicBvh)] [TestCase(SpatialIndexStrategy.SparseGrid)] [TestCase(SpatialIndexStrategy.StaticBvh)]
        public void GrowthRestoreAndMaximumAdmissionPreserveAtomicLogicalState(SpatialIndexStrategy strategy)
        {
            var options = new SpatialIndexOptions(new BufferOptions(1, 16, AllocationPolicy.Grow, snapshotSlots: 2, pageBytes: 128), strategy,
                rollback: SpatialRollbackPolicy.Compact, cellSize: Fixed64.FromRaw(8), maxCellsPerObject: 8);
            using var index = new DeterministicSpatialIndex3<int, int, Int32Policy>(options);
            index.TryAdd(1, default, 1).ThrowIfFailure(); index.TryCapture(out var original).ThrowIfFailure();
            try
            {
                for (int key = 2; key <= 16; key++) index.TryAdd(key, default, key).ThrowIfFailure(); Assert.AreEqual(16, index.Count);
                Assert.AreEqual(DeterministicStatus.CapacityExceeded, index.TryAdd(17, default, 17)); Assert.AreEqual(16, index.Count);
                index.TryRestore(original).ThrowIfFailure(); Assert.AreEqual(1, index.Count); Assert.AreEqual(1, index.Capacity);
                for (int key = 2; key <= 16; key++) index.TryAdd(key, default, key).ThrowIfFailure(); index.TryRebuild().ThrowIfFailure();
                index.TryMeasureQueryScratch(out int bytes).ThrowIfFailure(); byte* scratch = stackalloc byte[bytes]; SpatialHit<int>* hits = stackalloc SpatialHit<int>[16];
                index.TryQuery(SpatialQuery.ForNearest(FixedVector3.zero, 16), new SpatialQueryScratch(scratch, bytes), hits, 16, out int count).ThrowIfFailure();
                Assert.AreEqual(16, count); for (int i = 0; i < count; i++) Assert.AreEqual(i + 1, hits[i].Key);
                Assert.AreEqual(DeterministicStatus.NotFound, index.TryApplyBatch(new[] { new SpatialMutation<int, int, FixedBounds3> { Kind = SpatialMutationKind.Remove, Key = 1 },
                    new SpatialMutation<int, int, FixedBounds3> { Kind = SpatialMutationKind.Remove, Key = 99 } })); Assert.AreEqual(16, index.Count);
            }
            finally { original.Dispose(); }
#if !FIXEDPOINT_STANDALONE
            var native = new NativeDeterministicSpatialIndex3<int, int, Int32Policy>(options, Allocator.Persistent);
            try
            {
                native.TryAdd(1, default, 1).ThrowIfFailure(); native.TryCapture(out var first).ThrowIfFailure();
                try
                {
                    for (int key = 2; key <= 16; key++) native.TryAdd(key, default, key).ThrowIfFailure();
                    Assert.AreEqual(DeterministicStatus.CapacityExceeded, native.TryAdd(17, default, 17));
                    native.TryRestore(first).ThrowIfFailure(); Assert.AreEqual(1, native.Count); Assert.AreEqual(1, native.Capacity);
                    for (int key = 2; key <= 16; key++) native.TryAdd(key, default, key).ThrowIfFailure();
                    Assert.AreEqual(16, native.Count);
                    Assert.AreEqual(DeterministicStatus.NotFound, native.TryApplyBatch(new[] { new SpatialMutation<int, int, FixedBounds3> { Kind = SpatialMutationKind.Remove, Key = 1 },
                        new SpatialMutation<int, int, FixedBounds3> { Kind = SpatialMutationKind.Remove, Key = 99 } })); Assert.AreEqual(16, native.Count);
                }
                finally { first.Dispose(); }
            }
            finally { native.Dispose(); }
#endif
        }
        [Test] public void MultipleVolumesAndEntityGenerationKeysUseThePortableSamplePolicy()
        {
            using var index = new DeterministicSpatialIndex2<SpatialVolumeKey, int, SpatialVolumePolicy>(new SpatialIndexOptions(8));
            var first = new SpatialVolumeKey(new StableHandle(7, 1), 0); var second = new SpatialVolumeKey(first.Entity, 1); var reused = new SpatialVolumeKey(new StableHandle(7, 2), 0);
            index.TryAdd(second, default, 2).ThrowIfFailure(); index.TryAdd(reused, default, 3).ThrowIfFailure(); index.TryAdd(first, default, 1).ThrowIfFailure();
            Assert.AreEqual(3, index.Count); Assert.AreEqual(DeterministicStatus.DuplicateKey, index.TryAdd(first, default, 9));
            var codec = default(SpatialVolumePolicy); byte* bytes = stackalloc byte[10]; var writer = new DeterministicWriter(bytes, 10);
            codec.TryWrite(ref writer, reused).ThrowIfFailure(); var reader = new DeterministicReader(bytes, 10); codec.TryRead(ref reader, out var decoded).ThrowIfFailure();
            Assert.IsTrue(codec.Equals(reused, decoded)); Assert.Less(codec.Compare(first, second), 0); Assert.Less(codec.Compare(second, reused), 0);
            var rows = new[] { new SpatialRecord<SpatialVolumeKey, int, FixedBounds2>(first, default, 1), new SpatialRecord<SpatialVolumeKey, int, FixedBounds2>(second, default, 2) };
            var owner = new SpatialArraySourceOwner<SpatialVolumeKey, int, FixedBounds2>(rows, 2);
            using var cache = new DeterministicSpatialIndex2<SpatialVolumeKey, int, SpatialVolumePolicy>(new SpatialIndexOptions(8, ownership: SpatialStateOwnership.Derived));
            cache.TrySynchronize(owner.AsSource()).ThrowIfFailure(); var view = cache.AsReadOnly(owner.AsSource());
            rows[0].Data = 100; owner.NotifyChanged(2).ThrowIfFailure(); Assert.AreEqual(DeterministicStatus.Invalidated, view.Check());
            cache.TrySynchronize(owner.AsSource()).ThrowIfFailure(); cache.AsReadOnly(owner.AsSource()).TryGet(first, out var updated).ThrowIfFailure(); Assert.AreEqual(100, updated.Data);
        }
    }
}
