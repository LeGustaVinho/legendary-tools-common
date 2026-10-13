#if !FIXEDPOINT_STANDALONE
using LegendaryTools.FixedPoint;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public unsafe class SpatialNativeTests
    {
        [OneTimeSetUp] public void Initialize()
        { if (!FixedRuntime.IsInitialized) FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize(); }
        [BurstCompile(CompileSynchronously = true)]
        private struct MutateAndQuery : IJob
        {
            internal NativeDeterministicSpatialIndex3<int, int, Int32Policy> Index;
            internal NativeArray<byte> Workspace;
            internal NativeArray<SpatialHit<int>> Hits;
            internal NativeArray<int> Result;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed;
                for (int i = 0; i < 64; i++)
                {
                    var bounds = FixedBounds3.FromMinMax(FixedVector3.FromRaw(i * 3 - 100, -2, -2), FixedVector3.FromRaw(i * 3 - 96, 2, 2));
                    var s = Index.TryAdd(63 - i, bounds, i); if (!s.IsSuccess()) { Result[1] = (int)s; return; }
                }
                for (int i = 0; i < 12; i++) { var s = Index.TryRemove(i); if (!s.IsSuccess()) { Result[1] = (int)s; return; } }
                var rebuilt = Index.TryRebuild(); if (!rebuilt.IsSuccess()) { Result[1] = (int)rebuilt; return; }
                var query = SpatialQuery.ForNearest(FixedVector3.zero, 9);
                var scratch = new SpatialQueryScratch((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Workspace), Workspace.Length);
                var status = Index.TryQuery(query, scratch, (SpatialHit<int>*)NativeArrayUnsafeUtility.GetUnsafePtr(Hits), Hits.Length, out int count);
                Result[1] = (int)status; Result[2] = count;
            }
            [BurstDiscard] private static void MarkManaged(ref int value) => value = 1;
        }
        [TestCase(SpatialIndexStrategy.DynamicBvh)]
        [TestCase(SpatialIndexStrategy.SparseGrid)]
        [TestCase(SpatialIndexStrategy.StaticBvh)]
        public void NativeJobsUseBurstAndMatchManagedResults(SpatialIndexStrategy strategy)
        {
            var options = new SpatialIndexOptions(64, strategy, cellSize: Fixed64.FromRaw(8), maxCellsPerObject: 8);
            var native = new NativeDeterministicSpatialIndex3<int, int, Int32Policy>(options, Allocator.Persistent);
            using var managed = new DeterministicSpatialIndex3<int, int, Int32Policy>(options);
            native.TryMeasureQueryScratch(out int bytes).ThrowIfFailure();
            var workspace = new NativeArray<byte>(bytes, Allocator.TempJob); var hits = new NativeArray<SpatialHit<int>>(64, Allocator.TempJob);
            var result = new NativeArray<int>(3, Allocator.TempJob);
            try
            {
                new MutateAndQuery { Index = native, Workspace = workspace, Hits = hits, Result = result }.Schedule().Complete();
                Assert.AreEqual(0, result[0], "Managed fallback must not pass Burst validation"); Assert.AreEqual((int)DeterministicStatus.None, result[1]);
                for (int i = 0; i < 64; i++)
                { var box = FixedBounds3.FromMinMax(FixedVector3.FromRaw(i * 3 - 100, -2, -2), FixedVector3.FromRaw(i * 3 - 96, 2, 2)); managed.TryAdd(63 - i, box, i).ThrowIfFailure(); }
                for (int i = 0; i < 12; i++) managed.TryRemove(i).ThrowIfFailure(); managed.TryRebuild().ThrowIfFailure();
                var arena = new byte[bytes]; var expected = new SpatialHit<int>[64];
                fixed (byte* work = arena) fixed (SpatialHit<int>* output = expected)
                {
                    managed.TryQuery(SpatialQuery.ForNearest(FixedVector3.zero, 9), new SpatialQueryScratch(work, bytes), output, 64, out int count).ThrowIfFailure();
                    Assert.AreEqual(count, result[2]); for (int i = 0; i < count; i++)
                    { Assert.AreEqual(expected[i].Key, hits[i].Key); Assert.AreEqual(expected[i].Metric.RawValue, hits[i].Metric.RawValue); Assert.AreEqual(expected[i].MetricStatus, hits[i].MetricStatus); }
                }
            }
            finally { result.Dispose(); hits.Dispose(); workspace.Dispose(); native.Dispose(); }
        }
    }
}
#endif
