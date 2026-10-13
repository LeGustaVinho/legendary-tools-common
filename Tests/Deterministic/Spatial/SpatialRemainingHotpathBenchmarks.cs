using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using LegendaryTools.Deterministic;
using LegendaryTools.FixedPoint;
using NUnit.Framework;
#if !FIXEDPOINT_STANDALONE
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
[assembly: RegisterGenericJobType(typeof(LegendaryTools.Deterministic.Tests.SpatialRemainingHotpathBenchmarks.Owner<FixedBounds2, SpatialBounds2Access, FixedBounds2Policy>.Work))]
[assembly: RegisterGenericJobType(typeof(LegendaryTools.Deterministic.Tests.SpatialRemainingHotpathBenchmarks.Owner<FixedBounds3, SpatialBounds3Access, FixedBounds3Policy>.Work))]
[assembly: RegisterGenericJobType(typeof(LegendaryTools.Deterministic.Tests.SpatialRemainingHotpathBenchmarks.Owner<FixedBounds2, SpatialBounds2Access, FixedBounds2Policy>.RestoreWork))]
[assembly: RegisterGenericJobType(typeof(LegendaryTools.Deterministic.Tests.SpatialRemainingHotpathBenchmarks.Owner<FixedBounds3, SpatialBounds3Access, FixedBounds3Policy>.RestoreWork))]
#endif

namespace LegendaryTools.Deterministic.Tests
{
    /// <summary>Paired experiments for costs that the scale, opportunity and optimization fixtures did not isolate.</summary>
    [Explicit("Run remaining hotpath experiments separately from correctness tests.")]
    public unsafe class SpatialRemainingHotpathBenchmarks
    {
        internal enum Operation { Dispatch, Query, CreatePairs, FirstPair, AllPairs, RestoreCycle, CaptureRelease, Write, Import }
        private const int Samples = 9;
        private const int Warmups = 4;
        private const int DenseCount = 128;

        [OneTimeSetUp]
        public void Initialize()
        {
            if (!FixedRuntime.IsInitialized) FixedRuntime.Initialize();
            if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize();
        }

        internal struct CountQuery<TBounds> : ISpatialPredicate<int, int, TBounds> where TBounds : unmanaged
        {
            internal int* Calls;
            public bool Accept(in SpatialRecord<int, int, TBounds> record) { ++*Calls; return true; }
        }

        internal struct SelectPair<TBounds> : ISpatialPairPredicate<int, int, TBounds> where TBounds : unmanaged
        {
            internal int* Calls;
            internal int Mode, Count;
            public bool Accept(in SpatialRecord<int, int, TBounds> left, in SpatialRecord<int, int, TBounds> right)
            {
                ++*Calls;
                return Mode == 0 || Mode == 2 && left.Key == Count - 2 && right.Key == Count - 1;
            }
        }

        internal sealed class Owner<TBounds, TAccess, TCodec> : IDisposable
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds>
            where TCodec : unmanaged, IDeterministicCodec<TBounds>
        {
            internal readonly SpatialIndexOptions Options;
            internal readonly bool Native;
            internal readonly int Capacity, QueryBytes, PairBytes, CanonicalBytes, PacketCapacity;
            private DeterministicSpatialIndex<int, int, TBounds, TAccess, Int32Policy> managed;
            private readonly byte[] queryWork, pairWork, canonicalWork, packet;
            private readonly SpatialHit<int>[] hits;
            private readonly ManagedSpatialSnapshot<int, int, TBounds, TAccess, Int32Policy>[] roots;
            private readonly ulong[] rootDigests = new ulong[4];
            private readonly SpatialRecord<int, int, TBounds>[] expected;
            internal int Active, Extent, Retained, Version, EncodedBytes;
            internal int ScratchLength, OutputCapacity, PacketLength, PairMode;
            internal SpatialQuery Query;
            internal SpatialRawBounds Envelope;
            internal SpatialFilter Filter = SpatialFilter.All;
            internal DeterministicStatus Status;
            internal int LastCount, LastCalls, LastPosition, LastRoot, LastMarker;
            internal ulong LastPairDigest;
#if !FIXEDPOINT_STANDALONE
            private NativeDeterministicSpatialIndex<int, int, TBounds, TAccess, Int32Policy> index;
            private NativeArray<byte> nativeQuery, nativePair, nativeCanonical, nativePacket;
            private NativeArray<SpatialHit<int>> nativeHits;
            private NativeArray<ulong> result;
            private readonly NativeSpatialSnapshot<int, int, TBounds, TAccess, Int32Policy>[] nativeRoots;
#endif

            internal Owner(SpatialIndexOptions options, bool native)
            {
                Options = options; Native = native; Capacity = options.Records.MaxCapacity;
                SpatialQueryScratch.TryMeasure<int>(Capacity, Capacity * 2, out QueryBytes).ThrowIfFailure();
                SpatialPairScratch.TryMeasure<int>(Capacity, Capacity * 2, out PairBytes).ThrowIfFailure();
                CanonicalBytes = checked(Capacity * 160 + 4096);
                PacketCapacity = checked(Capacity * 128 + 4096);
                ScratchLength = QueryBytes; OutputCapacity = Capacity; PacketLength = PacketCapacity;
                expected = new SpatialRecord<int, int, TBounds>[Capacity];
                roots = new ManagedSpatialSnapshot<int, int, TBounds, TAccess, Int32Policy>[4];
#if !FIXEDPOINT_STANDALONE
                if (native)
                {
                    index = new NativeDeterministicSpatialIndex<int, int, TBounds, TAccess, Int32Policy>(options, Allocator.Persistent);
                    nativeQuery = new NativeArray<byte>(QueryBytes, Allocator.Persistent);
                    nativePair = new NativeArray<byte>(PairBytes, Allocator.Persistent);
                    nativeCanonical = new NativeArray<byte>(CanonicalBytes, Allocator.Persistent);
                    nativePacket = new NativeArray<byte>(PacketCapacity, Allocator.Persistent);
                    nativeHits = new NativeArray<SpatialHit<int>>(Capacity, Allocator.Persistent);
                    result = new NativeArray<ulong>(7, Allocator.Persistent);
                    nativeRoots = new NativeSpatialSnapshot<int, int, TBounds, TAccess, Int32Policy>[4];
                    return;
                }
#endif
                managed = new DeterministicSpatialIndex<int, int, TBounds, TAccess, Int32Policy>(options);
                queryWork = new byte[QueryBytes]; pairWork = new byte[PairBytes]; canonicalWork = new byte[CanonicalBytes];
                packet = new byte[PacketCapacity]; hits = new SpatialHit<int>[Capacity];
            }

            internal static TBounds Box(long x, long width = 4)
            {
                TBounds bounds = default;
                if (default(TAccess).Dimensions == 2)
                    *(FixedBounds2*)&bounds = FixedBounds2.FromMinMax(FixedVector2.FromRaw(x, 0), FixedVector2.FromRaw(x + width, 4));
                else
                    *(FixedBounds3*)&bounds = FixedBounds3.FromMinMax(FixedVector3.FromRaw(x, 0, 0), FixedVector3.FromRaw(x + width, 4, 4));
                return bounds;
            }

            internal void SetEnvelope(long x, long width, bool point = false)
            {
                default(TAccess).TryRead(Box(x, width), out Envelope).ThrowIfFailure();
                if (point)
                    Query = default(TAccess).Dimensions == 2 ? SpatialQuery.ForPoint(FixedVector2.FromRaw(x, 0)) : SpatialQuery.ForPoint(FixedVector3.FromRaw(x, 0, 0));
                else
                {
                    TBounds box = Box(x, width);
                    var status = default(TAccess).Dimensions == 2 ? SpatialQuery.TryAabb(*(FixedBounds2*)&box, out Query) : SpatialQuery.TryAabb(*(FixedBounds3*)&box, out Query);
                    status.ThrowIfFailure();
                }
                if (point) Envelope.MaxX = Envelope.MinX = x;
            }

            internal void Populate(int active, int extent, bool dense = false, int cells = 1, int overflow = 0)
            {
                Active = active; Extent = extent;
                for (int i = 0; i < extent; ++i)
                {
                    var row = new SpatialRecord<int, int, TBounds>(i, Box(dense ? 0 : i * 32L, i >= active - overflow && i < active ? 160 : (cells - 1) * 16L + 4), i);
                    expected[i] = row;
#if !FIXEDPOINT_STANDALONE
                    if (Native) { index.TryAdd(i, row.Bounds, row.Data).ThrowIfFailure(); continue; }
#endif
                    managed.TryAdd(i, row.Bounds, row.Data).ThrowIfFailure();
                }
                for (int i = active; i < extent; ++i)
                {
#if !FIXEDPOINT_STANDALONE
                    if (Native) { index.TryRemove(i).ThrowIfFailure(); continue; }
#endif
                    managed.TryRemove(i).ThrowIfFailure();
                }
                Rebuild();
            }

            private void Rebuild()
            {
#if !FIXEDPOINT_STANDALONE
                if (Native) { index.TryRebuild().ThrowIfFailure(); return; }
#endif
                managed.TryRebuild().ThrowIfFailure();
            }

            internal SpatialMemoryStats Stats
            {
                get
                {
#if !FIXEDPOINT_STANDALONE
                    if (Native) return index.MemoryStats;
#endif
                    return managed.MemoryStats;
                }
            }
            // Payload only. Snapshot handles and runtime/array headers are excluded.
            internal long CallerBytes
            {
                get
                {
                    long bytes = (long)QueryBytes + PairBytes + CanonicalBytes + PacketCapacity + Capacity * (long)(sizeof(SpatialHit<int>) + sizeof(SpatialRecord<int, int, TBounds>));
#if !FIXEDPOINT_STANDALONE
                    if (Native) bytes += 7 * sizeof(ulong);
#endif
                    return bytes;
                }
            }

            internal void BuildHistory(bool privatePages)
            {
                for (int r = 0; r < 4; ++r)
                {
                    rootDigests[r] = StateDigest();
#if !FIXEDPOINT_STANDALONE
                    if (Native)
                    {
                        index.TryCapture(out nativeRoots[r]).ThrowIfFailure();
                    }
                    else
#endif
                    managed.TryCapture(out roots[r]).ThrowIfFailure();
                    ++Retained;
                    if (!privatePages) continue;
                    Version = r + 1;
                    for (int i = 0; i < Active; ++i)
                    {
                        var bounds = Box(i * 32L + Version * 32L);
                        int data = i + Version * Capacity;
#if !FIXEDPOINT_STANDALONE
                        if (Native) { index.TryUpdateBounds(i, bounds).ThrowIfFailure(); index.TrySetData(i, data).ThrowIfFailure(); continue; }
#endif
                        managed.TryUpdateBounds(i, bounds).ThrowIfFailure(); managed.TrySetData(i, data).ThrowIfFailure();
                    }
                    Rebuild();
                }
            }

            internal void RestoreNewest()
            {
#if !FIXEDPOINT_STANDALONE
                if (Native) { index.TryRestore(nativeRoots[3]).ThrowIfFailure(); return; }
#endif
                managed.TryRestore(roots[3]).ThrowIfFailure();
            }

            private static ulong BytesDigest(byte* bytes, int length)
            {
                ulong hash = 1;
                for (int i = 0; i < length; ++i) hash = DeterministicHash.Mix(hash ^ bytes[i]);
                return hash;
            }

            internal ulong StateDigest()
            {
#if !FIXEDPOINT_STANDALONE
                if (Native)
                {
                    var writer = new DeterministicWriter((byte*)nativePacket.GetUnsafePtr(), PacketCapacity);
                    index.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy), default(TCodec)).ThrowIfFailure();
                    EncodedBytes = writer.Position;
                    return BytesDigest((byte*)nativePacket.GetUnsafePtr(), EncodedBytes);
                }
#endif
                fixed (byte* p = packet)
                {
                    var writer = new DeterministicWriter(p, PacketCapacity);
                    managed.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy), default(TCodec)).ThrowIfFailure();
                    EncodedBytes = writer.Position; return BytesDigest(p, EncodedBytes);
                }
            }

            internal void CorruptMagic()
            {
#if !FIXEDPOINT_STANDALONE
                if (Native) { nativePacket[0] ^= 1; return; }
#endif
                packet[0] ^= 1;
            }

            internal ulong PacketDigest()
            {
#if !FIXEDPOINT_STANDALONE
                if (Native) return BytesDigest((byte*)nativePacket.GetUnsafePtr(), EncodedBytes);
#endif
                fixed (byte* p = packet) return BytesDigest(p, EncodedBytes);
            }

            internal void FillSentinels()
            {
                for (int i = 0; i < Capacity; ++i)
                {
                    var hit = new SpatialHit<int> { Key = -123, Metric = Fixed64.FromRaw(-456), MetricStatus = FixedStatus.None };
#if !FIXEDPOINT_STANDALONE
                    if (Native) { nativeHits[i] = hit; continue; }
#endif
                    hits[i] = hit;
                }
            }

            internal void AssertUnpublishedHits()
            {
                for (int i = 0; i < Capacity; ++i)
                {
                    SpatialHit<int> hit;
#if !FIXEDPOINT_STANDALONE
                    if (Native) hit = nativeHits[i]; else
#endif
                    hit = hits[i];
                    Assert.AreEqual(-123, hit.Key); Assert.AreEqual(-456, hit.Metric.RawValue);
                    Assert.AreEqual(FixedStatus.None, hit.MetricStatus);
                }
            }

            internal ulong AssertQuery(int expectedCalls)
            {
                Assert.AreEqual(expectedCalls, LastCalls, "Predicate evaluations per query");
                int count = 0; ulong hash = 1;
                for (int i = 0; i < Active; ++i)
                {
                    default(TAccess).TryRead(expected[i].Bounds, out var b).ThrowIfFailure();
                    bool match = b.MinX <= Envelope.MaxX && b.MaxX >= Envelope.MinX && b.MinY <= Envelope.MaxY && b.MaxY >= Envelope.MinY;
                    if (default(TAccess).Dimensions == 3) match &= b.MinZ <= Envelope.MaxZ && b.MaxZ >= Envelope.MinZ;
                    if (!match || !Filter.Accept(expected[i].Metadata)) continue;
                    SpatialHit<int> hit;
#if !FIXEDPOINT_STANDALONE
                    if (Native) hit = nativeHits[count]; else
#endif
                    hit = hits[count];
                    Assert.AreEqual(i, hit.Key, "Full query output differs from brute-force AABB intersection");
                    Assert.AreEqual(0, hit.Metric.RawValue); Assert.AreEqual(FixedStatus.None, hit.MetricStatus);
                    hash = DeterministicHash.Mix(hash ^ (uint)hit.Key); ++count;
                }
                Assert.AreEqual(count, LastCount); return hash;
            }

            internal ulong AssertPairs(Operation op)
            {
                int count = 0, calls = 0; ulong hash = 1;
                if (op != Operation.CreatePairs && Filter.LayerMask != 0)
                    for (int a = 0; a < Active; ++a)
                        for (int b = a + 1; b < Active; ++b)
                        {
                            ++calls;
                            if (PairMode == 1 || PairMode == 2 && (a != Active - 2 || b != Active - 1)) continue;
                            hash = PairDigest(hash, a, b); ++count;
                            if (op == Operation.FirstPair) goto Done;
                        }
                Done:
                Assert.AreEqual(count, LastCount); Assert.AreEqual(calls, LastCalls);
                Assert.AreEqual(hash, LastPairDigest, "Complete consumed pair stream"); return hash;
            }

            internal void AssertRestored()
            {
                Assert.AreEqual(3, LastRoot); Assert.AreEqual(rootDigests[3], StateDigest(), "Full canonical snapshot state");
            }

            internal void Execute(Operation op, int iterations)
            {
#if !FIXEDPOINT_STANDALONE
                if (Native)
                {
                    // Detect a Burst abort before result publication instead of accepting a stale previous sample.
                    result[0] = ulong.MaxValue;
                    if (op == Operation.RestoreCycle)
                        new RestoreWork
                        {
                            Index = index, Result = result, Iterations = iterations,
                            Root0 = nativeRoots[0], Root1 = nativeRoots[1], Root2 = nativeRoots[2], Root3 = nativeRoots[3]
                        }.Schedule().Complete();
                    else new Work
                    {
                        Index = index, QueryWork = nativeQuery, PairWork = nativePair, CanonicalWork = nativeCanonical, Packet = nativePacket, Hits = nativeHits, Result = result,
                        Op = op, Iterations = iterations, Query = Query, Filter = Filter, Active = Active, PairMode = PairMode,
                        ScratchLength = ScratchLength, OutputCapacity = OutputCapacity, PacketLength = PacketLength
                    }.Schedule().Complete();
                    LastMarker = (int)result[0];
                    Status = (DeterministicStatus)result[1]; LastCount = (int)result[2]; LastCalls = (int)result[3];
                    LastPosition = (int)result[4]; LastPairDigest = result[5]; LastRoot = (int)result[6]; return;
                }
#endif
                fixed (byte* q = queryWork) fixed (byte* p = pairWork) fixed (byte* c = canonicalWork) fixed (byte* bytes = packet) fixed (SpatialHit<int>* h = hits)
                {
                    for (int repeat = 0; repeat < iterations; ++repeat)
                    {
                        int calls = 0; LastCount = 0; LastPosition = 0; LastPairDigest = 1; LastRoot = 0; Status = DeterministicStatus.None;
                        switch (op)
                        {
                            case Operation.Dispatch: break;
                            case Operation.Query:
                                Status = managed.TryQuery(Query, new SpatialQueryScratch(q, ScratchLength), h, OutputCapacity, out LastCount, Filter, new CountQuery<TBounds> { Calls = &calls }); break;
                            case Operation.CreatePairs: case Operation.FirstPair: case Operation.AllPairs:
                                Status = managed.TryGetPairs(new SpatialPairScratch(p, PairBytes), Filter, new SelectPair<TBounds> { Calls = &calls, Mode = PairMode, Count = Active }, out var pairs);
                                while (Status.IsSuccess() && op != Operation.CreatePairs)
                                {
                                    Status = pairs.TryNext(out var pair, out bool has); if (!has) break;
                                    ++LastCount; LastPairDigest = PairDigest(LastPairDigest, pair.First, pair.Second);
                                    if (op == Operation.FirstPair) break;
                                }
                                break;
                            case Operation.RestoreCycle:
                                LastRoot = repeat % 4; Status = managed.TryRestore(roots[LastRoot]); break;
                            case Operation.CaptureRelease:
                                Status = managed.TryCapture(out var snapshot); if (Status.IsSuccess()) snapshot.Dispose(); break;
                            case Operation.Write:
                                var writer = new DeterministicWriter(bytes, PacketLength);
                                Status = managed.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy), default(TCodec)); LastPosition = writer.Position; break;
                            case Operation.Import:
                                var reader = new DeterministicReader(bytes, PacketLength); var arena = new CanonicalScratch(c, CanonicalBytes);
                                Status = managed.TryReadCanonical(ref reader, ref arena, default(Int32Policy), default(Int32Policy), default(TCodec)); LastPosition = reader.Position; break;
                        }
                        LastCalls = calls;
                    }
                }
            }

#if !FIXEDPOINT_STANDALONE
            [BurstCompile(CompileSynchronously = true)]
            internal struct Work : IJob
            {
                internal NativeDeterministicSpatialIndex<int, int, TBounds, TAccess, Int32Policy> Index;
                internal NativeArray<byte> QueryWork, PairWork, CanonicalWork, Packet;
                internal NativeArray<SpatialHit<int>> Hits;
                internal NativeArray<ulong> Result;
                internal Operation Op;
                internal int Iterations, Active, PairMode, ScratchLength, OutputCapacity, PacketLength;
                internal SpatialQuery Query;
                internal SpatialFilter Filter;
                public void Execute()
                {
                    byte* q = (byte*)QueryWork.GetUnsafePtr(), p = (byte*)PairWork.GetUnsafePtr(), c = (byte*)CanonicalWork.GetUnsafePtr(), bytes = (byte*)Packet.GetUnsafePtr();
                    var h = (SpatialHit<int>*)Hits.GetUnsafePtr();
                    var status = DeterministicStatus.None; int count = 0, calls = 0, position = 0, root = 0; ulong digest = 1;
                    for (int repeat = 0; repeat < Iterations; ++repeat)
                    {
                        calls = 0; count = 0; position = 0; digest = 1; root = 0; status = DeterministicStatus.None;
                        switch (Op)
                        {
                            case Operation.Dispatch: break;
                            case Operation.Query:
                                status = Index.TryQuery(Query, new SpatialQueryScratch(q, ScratchLength), h, OutputCapacity, out count, Filter, new CountQuery<TBounds> { Calls = &calls }); break;
                            case Operation.CreatePairs: case Operation.FirstPair: case Operation.AllPairs:
                                status = Index.TryGetPairs(new SpatialPairScratch(p, PairWork.Length), Filter, new SelectPair<TBounds> { Calls = &calls, Mode = PairMode, Count = Active }, out var pairs);
                                while (status.IsSuccess() && Op != Operation.CreatePairs)
                                {
                                    status = pairs.TryNext(out var pair, out bool has); if (!has) break;
                                    ++count; digest = PairDigest(digest, pair.First, pair.Second);
                                    if (Op == Operation.FirstPair) break;
                                }
                                break;
                            case Operation.CaptureRelease:
                                status = Index.TryCapture(out var snapshot); if (status.IsSuccess()) snapshot.Dispose(); break;
                            case Operation.Write:
                                var writer = new DeterministicWriter(bytes, PacketLength);
                                status = Index.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy), default(TCodec)); position = writer.Position; break;
                            case Operation.Import:
                                var reader = new DeterministicReader(bytes, PacketLength); var arena = new CanonicalScratch(c, CanonicalWork.Length);
                                status = Index.TryReadCanonical(ref reader, ref arena, default(Int32Policy), default(Int32Policy), default(TCodec)); position = reader.Position; break;
                        }
                    }
                    int marker = 0; MarkManaged(ref marker);
                    Result[0] = (ulong)marker; Result[1] = (ulong)status; Result[2] = (ulong)count; Result[3] = (ulong)calls;
                    Result[4] = (ulong)position; Result[5] = digest; Result[6] = (ulong)root;
                }
                [BurstDiscard] private static void MarkManaged(ref int marker) => marker = 1;
            }

            // Separate job: only restore operations borrow snapshots, and every scheduled handle is valid.
            // All four snapshots intentionally alias Index's owner. Index retains the job's exclusive
            // write dependency; the host keeps every snapshot leased until this job completes.
            [BurstCompile(CompileSynchronously = true)]
            internal struct RestoreWork : IJob
            {
                internal NativeDeterministicSpatialIndex<int, int, TBounds, TAccess, Int32Policy> Index;
                [NativeDisableContainerSafetyRestriction]
                internal NativeSpatialSnapshot<int, int, TBounds, TAccess, Int32Policy> Root0, Root1, Root2, Root3;
                internal NativeArray<ulong> Result;
                internal int Iterations;
                public void Execute()
                {
                    var status = DeterministicStatus.None; int root = 0;
                    for (int repeat = 0; repeat < Iterations; ++repeat)
                    {
                        root = repeat % 4;
                        var snapshot = root == 0 ? Root0 : root == 1 ? Root1 : root == 2 ? Root2 : Root3;
                        status = Index.TryRestore(snapshot);
                        if (!status.IsSuccess()) break;
                    }
                    int marker = 0; MarkManaged(ref marker);
                    Result[0] = (ulong)marker; Result[1] = (ulong)status; Result[2] = 0; Result[3] = 0;
                    Result[4] = 0; Result[5] = 1; Result[6] = (ulong)root;
                }
                [BurstDiscard] private static void MarkManaged(ref int marker) => marker = 1;
            }
#endif

            public void Dispose()
            {
#if !FIXEDPOINT_STANDALONE
                if (Native)
                {
                    for (int i = 0; i < Retained; ++i) nativeRoots[i].Dispose();
                    index.Dispose(); nativeQuery.Dispose(); nativePair.Dispose(); nativeCanonical.Dispose(); nativePacket.Dispose(); nativeHits.Dispose(); result.Dispose(); return;
                }
#endif
                for (int i = 0; i < Retained; ++i) roots[i].Dispose(); managed.Dispose();
            }
        }

        private static ulong PairDigest(ulong hash, int first, int second) => DeterministicHash.Mix(hash ^ (uint)first ^ ((ulong)(uint)second << 32));

        private static SpatialIndexOptions Options(int capacity, SpatialIndexStrategy strategy, int page = 4096,
            SpatialRollbackPolicy rollback = SpatialRollbackPolicy.Compact, int slots = 0, int queryCells = 32)
            => new SpatialIndexOptions(new BufferOptions(capacity, snapshotSlots: slots, pageBytes: page), strategy, rollback: rollback,
                cellSize: Fixed64.FromRaw(16), fatMargin: Fixed64.FromRaw(8), maxCellsPerObject: 8, maxCellsPerQuery: queryCells);

        private static void Measure<TBounds, TAccess, TCodec>(StreamWriter report, string family, string label, Owner<TBounds, TAccess, TCodec> owner,
            Operation op, int iterations, DeterministicStatus expectedStatus = DeterministicStatus.None, Action before = null,
            Func<ulong> check = null, int overflow = 0, int cells = 1, string history = "none")
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds> where TCodec : unmanaged, IDeterministicCodec<TBounds>
        {
            var times = new double[Samples]; var allocated = new long[Samples]; long maxGrowth = 0;
            ulong baseline = owner.StateDigest(), outputDigest = 0;
            for (int sample = -Warmups; sample < Samples; ++sample)
            {
                before?.Invoke(); long live = owner.Stats.UsedBytes;
                long gc = GC.GetAllocatedBytesForCurrentThread(), started = Stopwatch.GetTimestamp();
                owner.Execute(op, iterations);
                long elapsed = Stopwatch.GetTimestamp() - started, bytes = GC.GetAllocatedBytesForCurrentThread() - gc;
                Assert.AreEqual(expectedStatus, owner.Status, label);
                Assert.AreEqual(0, owner.LastMarker, "BurstDiscard marker: benchmark must execute compiled Burst");
                if (op == Operation.Write && !expectedStatus.IsSuccess())
                    Assert.AreEqual(baseline, owner.PacketDigest(), "Failed write published destination bytes");
                if (check != null) outputDigest = check();
                if (sample >= 0)
                {
                    times[sample] = elapsed * 1000000.0 / Stopwatch.Frequency / iterations; allocated[sample] = bytes;
                    maxGrowth = Math.Max(maxGrowth, owner.Stats.UsedBytes - live);
                }
                ulong after = owner.StateDigest();
                if (op != Operation.RestoreCycle) Assert.AreEqual(baseline, after, "Operation changed the full canonical state");
            }
            Array.Sort(times); Array.Sort(allocated); var stats = owner.Stats;
            report.WriteLine(string.Join("\t", owner.Native ? "Burst scheduled batches" : "Managed", FixedBoundsRuntime.Settings.Representation,
                family, label, default(TAccess).Dimensions, owner.Options.Strategy, owner.Options.Rollback, owner.Capacity, owner.Active, owner.Extent,
                owner.Retained, owner.Options.Records.SnapshotSlots, history, owner.Options.Records.PageBytes, overflow, cells, owner.Options.MaxCellsPerQuery,
                owner.ScratchLength, owner.OutputCapacity, owner.PacketLength, iterations, Samples, Warmups,
                times[Samples / 2].ToString("F3", CultureInfo.InvariantCulture), times[Samples - 1].ToString("F3", CultureInfo.InvariantCulture), allocated[Samples / 2],
                stats.ReservedBytes, stats.UsedBytes, maxGrowth, owner.CallerBytes, owner.EncodedBytes, expectedStatus, owner.LastCount, owner.LastCalls, owner.LastPosition,
                outputDigest, owner.StateDigest()));
            report.Flush();
        }

        private static void Grid<TBounds, TAccess, TCodec>(int capacity, bool native, StreamWriter report)
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds> where TCodec : unmanaged, IDeterministicCodec<TBounds>
        {
            foreach (int extent in new[] { 64, capacity })
                foreach (int overflow in new[] { 0, 1, 8 })
                {
                    using var owner = new Owner<TBounds, TAccess, TCodec>(Options(capacity, SpatialIndexStrategy.SparseGrid), native);
                    owner.Populate(64, extent, overflow: overflow); owner.SetEnvelope(0, 0, true);
                    Measure(report, "grid", "point_overflow" + overflow, owner, Operation.Query, 128, check: () => owner.AssertQuery(1 + overflow), overflow: overflow);
                }
            foreach (int budget in new[] { 32, 31 })
                foreach (int extent in new[] { 64, capacity })
                {
                    using var owner = new Owner<TBounds, TAccess, TCodec>(Options(capacity, SpatialIndexStrategy.SparseGrid, queryCells: budget), native);
                    owner.Populate(64, extent); owner.SetEnvelope(0, 500);
                    Measure(report, "grid", budget == 32 ? "envelope_at_budget" : "envelope_over_budget", owner, Operation.Query, 128,
                        check: () => owner.AssertQuery(budget == 32 ? 16 : 64));
                }
            foreach (int cells in new[] { 1, 2, 4, 8 })
            {
                using var owner = new Owner<TBounds, TAccess, TCodec>(Options(capacity, SpatialIndexStrategy.SparseGrid), native);
                owner.Populate(DenseCount, DenseCount, true, cells); owner.SetEnvelope(0, (cells - 1) * 16L + 4);
                Measure(report, "grid", "dense_multicell", owner, Operation.Query, 32, check: () => owner.AssertQuery(DenseCount), cells: cells);
            }
            using var dispatch = new Owner<TBounds, TAccess, TCodec>(Options(capacity, SpatialIndexStrategy.SparseGrid), native);
            Measure(report, "grid", "dispatch", dispatch, Operation.Dispatch, 128);
        }

        private static void Pairs<TBounds, TAccess, TCodec>(int capacity, bool native, StreamWriter report)
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds> where TCodec : unmanaged, IDeterministicCodec<TBounds>
        {
            foreach (SpatialIndexStrategy strategy in Enum.GetValues(typeof(SpatialIndexStrategy)))
            {
                using var owner = new Owner<TBounds, TAccess, TCodec>(Options(capacity, strategy), native);
                owner.Populate(DenseCount, DenseCount, true);
                Measure(report, "pairs", "create_only", owner, Operation.CreatePairs, 32, check: () => owner.AssertPairs(Operation.CreatePairs));
                foreach (int mode in new[] { 0, 1, 2 })
                {
                    owner.PairMode = mode;
                    Measure(report, "pairs", mode == 0 ? "first_accept_all" : mode == 1 ? "first_reject_all" : "first_accept_last", owner, Operation.FirstPair, 4,
                        check: () => owner.AssertPairs(Operation.FirstPair));
                }
                owner.PairMode = 0;
                Measure(report, "pairs", "consume_all_digest", owner, Operation.AllPairs, 2, check: () => owner.AssertPairs(Operation.AllPairs));
                owner.Filter = default;
                Measure(report, "pairs", "first_filter_none", owner, Operation.FirstPair, 32, check: () => owner.AssertPairs(Operation.FirstPair));
                Measure(report, "pairs", "dispatch", owner, Operation.Dispatch, 128);
            }
        }

        private static void History<TBounds, TAccess, TCodec>(int capacity, bool native, StreamWriter report)
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds> where TCodec : unmanaged, IDeterministicCodec<TBounds>
        {
            foreach (SpatialIndexStrategy strategy in Enum.GetValues(typeof(SpatialIndexStrategy)))
                foreach (SpatialRollbackPolicy rollback in Enum.GetValues(typeof(SpatialRollbackPolicy)))
                    foreach (int page in new[] { 256, 4096 })
                        foreach (bool privatePages in new[] { false, true })
                        {
                            using var owner = new Owner<TBounds, TAccess, TCodec>(Options(capacity, strategy, page, rollback, 5), native);
                            owner.Populate(capacity, capacity); owner.BuildHistory(privatePages);
                            string history = privatePages ? "all_records_and_bounds_changed" : "shared_roots";
                            Measure(report, "history", "capture_release", owner, Operation.CaptureRelease, 64, history: history);
                            Measure(report, "history", "restore_cycle4", owner, Operation.RestoreCycle, 4, before: owner.RestoreNewest,
                                check: () => { owner.AssertRestored(); return 0; }, history: history);
                        }
        }

        private static void Failure<TBounds, TAccess, TCodec>(int capacity, bool native, StreamWriter report)
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds> where TCodec : unmanaged, IDeterministicCodec<TBounds>
        {
            foreach (SpatialIndexStrategy strategy in Enum.GetValues(typeof(SpatialIndexStrategy)))
            {
                using var owner = new Owner<TBounds, TAccess, TCodec>(Options(capacity, strategy), native);
                owner.Populate(capacity, capacity, true); owner.SetEnvelope(0, 4);
                Measure(report, "failure", "query_full_output", owner, Operation.Query, 16, check: () => owner.AssertQuery(capacity));
                foreach (int output in new[] { 0, 1 })
                {
                    owner.OutputCapacity = output;
                    Measure(report, "failure", "query_output_small", owner, Operation.Query, 16, DeterministicStatus.BufferTooSmall, owner.FillSentinels,
                        () => { Assert.AreEqual(capacity, owner.LastCalls); Assert.AreEqual(0, owner.LastCount); owner.AssertUnpublishedHits(); return 0; });
                }
                owner.OutputCapacity = capacity; owner.ScratchLength = 0;
                Measure(report, "failure", "query_scratch_empty", owner, Operation.Query, 128, DeterministicStatus.BufferTooSmall, owner.FillSentinels,
                    () => { Assert.AreEqual(0, owner.LastCalls); Assert.AreEqual(0, owner.LastCount); owner.AssertUnpublishedHits(); return 0; });
                owner.ScratchLength = owner.QueryBytes;
                owner.PacketLength = owner.EncodedBytes;
                Measure(report, "failure", "write_exact_output", owner, Operation.Write, 2, check: () => { Assert.AreEqual(owner.EncodedBytes, owner.LastPosition); return 0; });
                foreach (int length in new[] { 0, owner.EncodedBytes - 1 })
                {
                    owner.PacketLength = length;
                    Measure(report, "failure", "write_output_small", owner, Operation.Write, 2, DeterministicStatus.BufferTooSmall,
                        check: () => { Assert.AreEqual(0, owner.LastPosition); return 0; });
                }
                owner.PacketLength = owner.EncodedBytes;
                Measure(report, "failure", "import_valid", owner, Operation.Import, 1, check: () => { Assert.AreEqual(owner.EncodedBytes, owner.LastPosition); return 0; });
                Measure(report, "failure", "import_bad_magic", owner, Operation.Import, 32, DeterministicStatus.ProfileMismatch, owner.CorruptMagic,
                    () => { Assert.AreEqual(0, owner.LastPosition); return 0; });
                owner.PacketLength = owner.EncodedBytes - 1;
                Measure(report, "failure", "import_truncated_last_byte", owner, Operation.Import, 2, DeterministicStatus.BufferTooSmall,
                    check: () => { Assert.AreEqual(0, owner.LastPosition); return 0; });
                Measure(report, "failure", "dispatch", owner, Operation.Dispatch, 128);
            }
        }

        [TestCase("grid", 1024, false)] [TestCase("grid", 8192, false)]
        [TestCase("pairs", 1024, false)] [TestCase("pairs", 8192, false)]
        [TestCase("history", 1024, false)] [TestCase("history", 8192, false)]
        [TestCase("failure", 1024, false)] [TestCase("failure", 8192, false)]
#if !FIXEDPOINT_STANDALONE
        [TestCase("grid", 1024, true)] [TestCase("grid", 8192, true)]
        [TestCase("pairs", 1024, true)] [TestCase("pairs", 8192, true)]
        [TestCase("history", 1024, true)] [TestCase("history", 8192, true)]
        [TestCase("failure", 1024, true)] [TestCase("failure", 8192, true)]
#endif
        [Timeout(1800000)]
        public void RemainingCosts(string family, int capacity, bool native)
        {
            string folder = Environment.GetEnvironmentVariable("LEGENDARY_SPATIAL_REMAINING_REPORT_DIR") ?? TestContext.CurrentContext.WorkDirectory;
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"spatial-remaining-{FixedBoundsRuntime.Settings.Representation}-{(native ? "burst" : "managed")}-{family}-{capacity}.tsv");
            using var report = new StreamWriter(path);
            report.WriteLine("backend\trepresentation\tfamily\tcase\tdimensions\tstrategy\trollback\tcapacity\tactive\tused\tretained_roots\tsnapshot_slots\thistory\tpage_bytes\toverflow_objects\tcells_per_object\tquery_cell_budget\tquery_scratch_bytes\toutput_capacity\tpacket_capacity\titerations\tsamples\twarmups\tmedian_us_per_op\tp95_us_per_op\thost_gc_bytes_median_batch\treserved_payload_bytes\tlive_payload_bytes\tmax_live_growth_bytes\tcaller_buffer_bytes\tcanonical_bytes\tstatus\tresult_count\tpredicate_calls\tcursor_position\toutput_digest\tstate_digest");
            Run<FixedBounds2, SpatialBounds2Access, FixedBounds2Policy>(family, capacity, native, report);
            Run<FixedBounds3, SpatialBounds3Access, FixedBounds3Policy>(family, capacity, native, report);
            TestContext.Progress.WriteLine("Remaining spatial benchmark report: " + path);
        }

        private static void Run<TBounds, TAccess, TCodec>(string family, int capacity, bool native, StreamWriter report)
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds> where TCodec : unmanaged, IDeterministicCodec<TBounds>
        {
            switch (family)
            {
                case "grid": Grid<TBounds, TAccess, TCodec>(capacity, native, report); break;
                case "pairs": Pairs<TBounds, TAccess, TCodec>(capacity, native, report); break;
                case "history": History<TBounds, TAccess, TCodec>(capacity, native, report); break;
                case "failure": Failure<TBounds, TAccess, TCodec>(capacity, native, report); break;
                default: Assert.Fail("Unknown benchmark family: " + family); break;
            }
        }
    }
}
