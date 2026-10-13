using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using LegendaryTools.FixedPoint;
using LegendaryTools.Deterministic;
using NUnit.Framework;
#if !FIXEDPOINT_STANDALONE
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
[assembly: RegisterGenericJobType(typeof(LegendaryTools.Deterministic.Tests.SpatialScaleBenchmarks.BenchmarkOwner<FixedBounds2, SpatialBounds2Access, FixedBounds2Policy>.Batch))]
[assembly: RegisterGenericJobType(typeof(LegendaryTools.Deterministic.Tests.SpatialScaleBenchmarks.BenchmarkOwner<FixedBounds3, SpatialBounds3Access, FixedBounds3Policy>.Batch))]
#endif

namespace LegendaryTools.Deterministic.Tests
{
    [Explicit("Run scale measurements separately from correctness tests.")]
    public unsafe class SpatialScaleBenchmarks
    {
        private const int QueryCount = 12;
        [OneTimeSetUp] public void Initialize()
        { if (!FixedRuntime.IsInitialized) FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize(); }
        private static double Milliseconds(long start) => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        private static TBounds Box<TBounds, TAccess>(int ordinal, bool clustered, bool varied, int shift)
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds>
        {
            ulong hash = DeterministicHash.Mix((ulong)(uint)ordinal + 0x12349876ul);
            long spread = clustered ? 32 : 65536, cluster = clustered ? ordinal % 4 * 1024 : 0;
            long x = (long)(hash % (ulong)spread) + cluster + shift, y = (long)((hash >> 19) % (ulong)spread) + cluster;
            long z = (long)((hash >> 38) % (ulong)spread) + cluster, width = varied ? 1L << (int)((hash >> 58) % 9) : 16;
            TBounds result = default;
            if (default(TAccess).Dimensions == 2) *(FixedBounds2*)&result = FixedBounds2.FromMinMax(FixedVector2.FromRaw(x, y), FixedVector2.FromRaw(x + width, y + width));
            else *(FixedBounds3*)&result = FixedBounds3.FromMinMax(FixedVector3.FromRaw(x, y, z), FixedVector3.FromRaw(x + width, y + width, z + width));
            return result;
        }
        private static SpatialQuery[] Queries(int dimensions, bool clustered)
        {
            var queries = new SpatialQuery[QueryCount];
            for (int i = 0; i < queries.Length; i++)
            {
                long p = clustered ? i % 4 * 1024 : i * 5000;
                var point = FixedVector3.FromRaw(p, p, p); var two = FixedVector2.FromRaw(p, p);
                switch (i % 6)
                {
                    case 0: queries[i] = dimensions == 2 ? SpatialQuery.ForNearest(two, 8) : SpatialQuery.ForNearest(point, 8); break;
                    case 1: queries[i] = dimensions == 2 ? SpatialQuery.ForCircle(two, Fixed64.FromRaw(64)) : SpatialQuery.ForSphere(point, Fixed64.FromRaw(64)); break;
                    case 2: queries[i] = dimensions == 2 ? SpatialQuery.ForRay(new FixedRay2(two, FixedVector2.FromRaw(1, 1))) : SpatialQuery.ForRay(new FixedRay3(point, FixedVector3.FromRaw(1, 1, 1))); break;
                    case 3: queries[i] = dimensions == 2 ? SpatialQuery.ForPoint(two) : SpatialQuery.ForPoint(point); break;
                    case 4: queries[i] = dimensions == 2 ? SpatialQuery.ForSegment(two, FixedVector2.FromRaw(p + 500, p + 500)) : SpatialQuery.ForSegment(point, FixedVector3.FromRaw(p + 500, p + 500, p + 500)); break;
                    default: queries[i] = SpatialQuery.FromEnvelope(new SpatialRawBounds { MinX = p, MinY = p, MinZ = p, MaxX = p + 64, MaxY = p + 64, MaxZ = p + 64 }, dimensions); break;
                }
            }
            return queries;
        }
        private static ulong QueryManaged<TBounds, TAccess>(DeterministicSpatialIndex<int, int, TBounds, TAccess, Int32Policy> index, SpatialQuery[] queries, byte[] workspace, SpatialHit<int>[] hits)
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds>
        {
            ulong hash = 1;
            fixed (byte* bytes = workspace) fixed (SpatialHit<int>* output = hits)
                for (int q = 0; q < queries.Length; q++)
                {
                    index.TryQuery(queries[q], new SpatialQueryScratch(bytes, workspace.Length), output, hits.Length, out int count).ThrowIfFailure();
                    hash = DeterministicHash.Mix(hash ^ (uint)count);
                    for (int i = 0; i < count; i++) hash = DeterministicHash.Mix(hash ^ (uint)hits[i].Key ^ unchecked((ulong)hits[i].Metric.RawValue) ^ (uint)hits[i].MetricStatus);
                }
            return hash;
        }
        private static void Run<TBounds, TAccess, TCodec>(int count, bool native, StreamWriter report)
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds>
            where TCodec : unmanaged, IDeterministicCodec<TBounds>
        {
            foreach (SpatialIndexStrategy strategy in Enum.GetValues(typeof(SpatialIndexStrategy)))
            foreach (bool clustered in new[] { false, true }) foreach (bool varied in new[] { false, true })
            foreach (SpatialRollbackPolicy rollback in Enum.GetValues(typeof(SpatialRollbackPolicy)))
            {
                var options = new SpatialIndexOptions(new BufferOptions(count, snapshotSlots: 2), strategy, rollback: rollback,
                    cellSize: Fixed64.FromRaw(64), fatMargin: Fixed64.FromRaw(2), maxCellsPerObject: 8, maxCellsPerQuery: 512);
                SpatialMemory.TryMeasure<int, int, TBounds>(options, out var sizing).ThrowIfFailure();
                long start = Stopwatch.GetTimestamp();
                using var owner = new BenchmarkOwner<TBounds, TAccess, TCodec>(options, native, clustered, varied);
                double allocationMs = Milliseconds(start);
                start = Stopwatch.GetTimestamp(); owner.Build(); double buildMs = Milliseconds(start);
                start = Stopwatch.GetTimestamp(); ulong initial = owner.Query(); double initialQueryMs = Milliseconds(start);
                start = Stopwatch.GetTimestamp(); int canonicalBytes = owner.Write(); double canonicalMs = Milliseconds(start);
                start = Stopwatch.GetTimestamp(); owner.Capture(); double captureMs = Milliseconds(start);
                foreach (int percent in new[] { 1, 10, 100 })
                {
                    owner.Restore(); // Every density starts from the same checkpoint.
                    start = Stopwatch.GetTimestamp(); owner.Move(percent); double updateMs = Milliseconds(start);
                    start = Stopwatch.GetTimestamp(); ulong moved = owner.Query(); double queryMs = Milliseconds(start);
                    start = Stopwatch.GetTimestamp(); owner.Restore(); double restoreMs = Milliseconds(start);
                    Assert.AreEqual(initial, owner.Query(), "Restoration query digest");
                    start = Stopwatch.GetTimestamp(); owner.Move(percent); ulong replay = owner.Query(); double resimulationMs = Milliseconds(start);
                    Assert.AreEqual(moved, replay, "Resimulation query digest");
                    var actual = owner.Stats;
                    Assert.AreEqual(sizing.ReservedBytes, actual.ReservedBytes);
                    string row = string.Join("\t", native ? "Burst batches/host snapshots" : "Managed", FixedBoundsRuntime.Settings.Representation,
                        count, default(TAccess).Dimensions, strategy, rollback, clustered ? "clustered" : "uniform", varied ? "varied" : "similar", percent,
                        allocationMs.ToString("F3", CultureInfo.InvariantCulture), buildMs.ToString("F3", CultureInfo.InvariantCulture), initialQueryMs.ToString("F3", CultureInfo.InvariantCulture),
                        canonicalMs.ToString("F3", CultureInfo.InvariantCulture), canonicalBytes, captureMs.ToString("F3", CultureInfo.InvariantCulture), updateMs.ToString("F3", CultureInfo.InvariantCulture),
                        queryMs.ToString("F3", CultureInfo.InvariantCulture), restoreMs.ToString("F3", CultureInfo.InvariantCulture), resimulationMs.ToString("F3", CultureInfo.InvariantCulture),
                        actual.ReservedBytes, actual.UsedBytes, moved);
                    report.WriteLine(row); report.Flush();
                }
                TestContext.Progress.WriteLine($"Spatial benchmark: backend={(native ? "Burst" : "Managed")} n={count} d={default(TAccess).Dimensions} {strategy} {rollback} clustered={clustered} varied={varied}");
            }
        }
        internal sealed class BenchmarkOwner<TBounds, TAccess, TCodec> : IDisposable
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds> where TCodec : unmanaged, IDeterministicCodec<TBounds>
        {
            private readonly bool native;
            private readonly int count;
            private readonly TBounds[] bounds, moved;
            private readonly SpatialQuery[] queries;
            private readonly byte[] workspace, packet;
            private readonly SpatialHit<int>[] hits;
            private readonly DeterministicSpatialIndex<int, int, TBounds, TAccess, Int32Policy> managed;
            private ManagedSpatialSnapshot<int, int, TBounds, TAccess, Int32Policy> managedSnapshot;
#if !FIXEDPOINT_STANDALONE
            private NativeDeterministicSpatialIndex<int, int, TBounds, TAccess, Int32Policy> index;
            private NativeSpatialSnapshot<int, int, TBounds, TAccess, Int32Policy> snapshot;
            private NativeArray<TBounds> nativeBounds, nativeMoved;
            private NativeArray<SpatialQuery> nativeQueries;
            private NativeArray<byte> nativeWorkspace, nativePacket;
            private NativeArray<SpatialHit<int>> nativeHits;
            private NativeArray<ulong> nativeResult;
#endif
            internal BenchmarkOwner(SpatialIndexOptions options, bool native, bool clustered, bool varied)
            {
                this.native = native; count = options.Records.MaxCapacity;
                bounds = new TBounds[count]; moved = new TBounds[count];
                for (int i = 0; i < count; i++) { bounds[i] = Box<TBounds, TAccess>(i, clustered, varied, 0); moved[i] = Box<TBounds, TAccess>(i, clustered, varied, 4); }
                queries = Queries(default(TAccess).Dimensions, clustered);
                SpatialQueryScratch.TryMeasure<int>(count, count * 2, out int bytes).ThrowIfFailure();
#if !FIXEDPOINT_STANDALONE
                if (native)
                {
                    index = new NativeDeterministicSpatialIndex<int, int, TBounds, TAccess, Int32Policy>(options, Allocator.Persistent);
                    nativeBounds = new NativeArray<TBounds>(bounds, Allocator.Persistent); nativeMoved = new NativeArray<TBounds>(moved, Allocator.Persistent);
                    nativeQueries = new NativeArray<SpatialQuery>(queries, Allocator.Persistent); nativeWorkspace = new NativeArray<byte>(bytes, Allocator.Persistent);
                    nativePacket = new NativeArray<byte>(count * 128 + 1024, Allocator.Persistent); nativeHits = new NativeArray<SpatialHit<int>>(count, Allocator.Persistent);
                    nativeResult = new NativeArray<ulong>(3, Allocator.Persistent); return;
                }
#endif
                managed = new DeterministicSpatialIndex<int, int, TBounds, TAccess, Int32Policy>(options);
                workspace = new byte[bytes]; hits = new SpatialHit<int>[count]; packet = new byte[count * 128 + 1024];
            }
            internal SpatialMemoryStats Stats
            {
                get {
#if !FIXEDPOINT_STANDALONE
                    if (native) return index.MemoryStats;
#endif
                    return managed.MemoryStats;
                }
            }
            internal void Build()
            {
#if !FIXEDPOINT_STANDALONE
                if (native) { Execute(0, count); return; }
#endif
                for (int i = 0; i < count; i++) managed.TryAdd(i, bounds[i], i).ThrowIfFailure(); managed.TryRebuild().ThrowIfFailure();
            }
            internal void Move(int percent)
            {
                int n = Math.Max(1, count * percent / 100);
#if !FIXEDPOINT_STANDALONE
                if (native) { Execute(1, n); return; }
#endif
                for (int i = 0; i < n; i++) { int key = (int)((long)i * 48271 % count); managed.TryUpdateBounds(key, moved[key]).ThrowIfFailure(); }
                if (managed.Options.Strategy == SpatialIndexStrategy.StaticBvh) managed.TryRebuild().ThrowIfFailure();
            }
            internal ulong Query()
            {
#if !FIXEDPOINT_STANDALONE
                if (native) { Execute(2, count); return nativeResult[2]; }
#endif
                return QueryManaged(managed, queries, workspace, hits);
            }
            internal int Write()
            {
#if !FIXEDPOINT_STANDALONE
                if (native) { Execute(3, count); return (int)nativeResult[2]; }
#endif
                fixed (byte* bytes = packet) { var writer = new DeterministicWriter(bytes, packet.Length); managed.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy), default(TCodec)).ThrowIfFailure(); return writer.Position; }
            }
            internal void Capture()
            {
#if !FIXEDPOINT_STANDALONE
                if (native) { index.TryCapture(out snapshot).ThrowIfFailure(); return; }
#endif
                managed.TryCapture(out managedSnapshot).ThrowIfFailure();
            }
            internal void Restore()
            {
#if !FIXEDPOINT_STANDALONE
                if (native) { index.TryRestore(snapshot).ThrowIfFailure(); return; }
#endif
                managed.TryRestore(managedSnapshot).ThrowIfFailure();
            }
#if !FIXEDPOINT_STANDALONE
            private void Execute(int operation, int n)
            {
                new Batch { Index = index, Bounds = nativeBounds, Moved = nativeMoved, Queries = nativeQueries, Workspace = nativeWorkspace,
                    Hits = nativeHits, Packet = nativePacket, Result = nativeResult, Operation = operation, Count = n }.Schedule().Complete();
                Assert.AreEqual(0, nativeResult[0], "BurstDiscard marker"); ((DeterministicStatus)nativeResult[1]).ThrowIfFailure();
            }
            [BurstCompile(CompileSynchronously = true)] internal struct Batch : IJob
            {
                internal NativeDeterministicSpatialIndex<int, int, TBounds, TAccess, Int32Policy> Index;
                [ReadOnly] internal NativeArray<TBounds> Bounds, Moved;
                [ReadOnly] internal NativeArray<SpatialQuery> Queries;
                internal NativeArray<byte> Workspace, Packet;
                internal NativeArray<SpatialHit<int>> Hits;
                internal NativeArray<ulong> Result;
                internal int Operation, Count;
                public void Execute()
                {
                    int marker = 0; MarkManaged(ref marker); Result[0] = (ulong)marker; Result[1] = 0;
                    var status = DeterministicStatus.None;
                    if (Operation == 0)
                    { for (int i = 0; i < Bounds.Length && status.IsSuccess(); i++) status = Index.TryAdd(i, Bounds[i], i); if (status.IsSuccess()) status = Index.TryRebuild(); }
                    else if (Operation == 1)
                    { for (int i = 0; i < Count && status.IsSuccess(); i++) { int key = (int)((long)i * 48271 % Bounds.Length); status = Index.TryUpdateBounds(key, Moved[key]); } if (status.IsSuccess() && Index.Options.Strategy == SpatialIndexStrategy.StaticBvh) status = Index.TryRebuild(); }
                    else if (Operation == 3)
                    { var writer = new DeterministicWriter((byte*)Packet.GetUnsafePtr(), Packet.Length); status = Index.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy), default(TCodec)); Result[2] = (ulong)writer.Position; }
                    else
                    {
                        ulong hash = 1;
                        for (int q = 0; q < Queries.Length && status.IsSuccess(); q++)
                        {
                            status = Index.TryQuery(Queries[q], new SpatialQueryScratch((byte*)Workspace.GetUnsafePtr(), Workspace.Length), (SpatialHit<int>*)Hits.GetUnsafePtr(), Hits.Length, out int n);
                            hash = DeterministicHash.Mix(hash ^ (uint)n);
                            for (int i = 0; i < n; i++) hash = DeterministicHash.Mix(hash ^ (uint)Hits[i].Key ^ unchecked((ulong)Hits[i].Metric.RawValue) ^ (uint)Hits[i].MetricStatus);
                        }
                        Result[2] = hash;
                    }
                    Result[1] = (ulong)status;
                }
                [BurstDiscard] private static void MarkManaged(ref int value) => value = 1;
            }
#endif
            public void Dispose()
            {
#if !FIXEDPOINT_STANDALONE
                if (native)
                { snapshot.Dispose(); index.Dispose(); nativeBounds.Dispose(); nativeMoved.Dispose(); nativeQueries.Dispose(); nativeWorkspace.Dispose(); nativePacket.Dispose(); nativeHits.Dispose(); nativeResult.Dispose(); return; }
#endif
                managedSnapshot.Dispose(); managed.Dispose();
            }
        }
        [TestCase(1000, false)] [TestCase(10000, false)] [TestCase(100000, false)]
#if !FIXEDPOINT_STANDALONE
        [TestCase(1000, true)] [TestCase(10000, true)] [TestCase(100000, true)]
#endif
        [Timeout(1800000)]
        public void MeasureConstructionMovementQueriesSnapshotsAndResimulation(int count, bool native)
        {
            string path = Environment.GetEnvironmentVariable("LEGENDARY_SPATIAL_BENCHMARK_REPORT") ?? Path.Combine(TestContext.CurrentContext.WorkDirectory ?? Environment.CurrentDirectory,
                $"spatial-scale-{FixedBoundsRuntime.Settings.Representation}-{(native ? "burst" : "managed")}-{count}.tsv");
            using var report = new StreamWriter(path);
            report.WriteLine("backend\trepresentation\tcount\tdimensions\tstrategy\trollback\tdistribution\tsizes\tmovement_percent\tallocation_ms\tbuild_ms\tinitial_query_ms\tcanonical_ms\tcanonical_bytes\tcapture_ms\tupdate_ms\tquery_ms\trestore_ms\tresimulation_ms\treserved_payload_bytes\tlive_payload_bytes\tquery_digest");
            Run<FixedBounds2, SpatialBounds2Access, FixedBounds2Policy>(count, native, report);
            Run<FixedBounds3, SpatialBounds3Access, FixedBounds3Policy>(count, native, report);
            TestContext.Progress.WriteLine("Spatial benchmark report: " + path);
        }
    }
}
