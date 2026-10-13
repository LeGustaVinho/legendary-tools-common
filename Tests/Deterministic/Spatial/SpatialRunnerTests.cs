using System;
using System.Collections.Generic;
using LegendaryTools.FixedPoint;
using NUnit.Framework;
#if !FIXEDPOINT_STANDALONE
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
#endif

namespace LegendaryTools.Deterministic.Tests
{
    public unsafe class SpatialRunnerTests
    {
        [OneTimeSetUp] public void Initialize()
        { if (!FixedRuntime.IsInitialized) FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize(); }
        private static FixedBounds3 Box(int position, int key) => FixedBounds3.FromMinMax(FixedVector3.FromRaw(position * 16, key * 8, 0), FixedVector3.FromRaw(position * 16 + 8, key * 8 + 4, 4));
        private struct ManagedSource : IDeterministicSpatialSource<int, int, FixedBounds3>
        {
            internal DeterministicSpatialIndex3<int, int, Int32Policy> Index;
            public ulong Identity => Index.Identity;
            public int Count => Index.Count;
            public ulong Revision => Index.Revision;
            public DeterministicStatus TryGetAt(int ordinal, out SpatialRecord<int, int, FixedBounds3> record) => Index.TryGetAt(ordinal, out record);
        }
        private sealed class Probe : IDeterministicEventSink<int>
        {
            internal readonly List<DeterministicEvent<int>> Confirmed = new List<DeterministicEvent<int>>();
            public void OnEvent(DeterministicEventDelivery delivery, in DeterministicEvent<int> value)
            { if (delivery == DeterministicEventDelivery.Confirmed) Confirmed.Add(value); }
        }
        private sealed class World : IDisposable
        {
            internal readonly DeterministicRuntimeContext Context;
            internal readonly DeterministicStateRegistry Registry;
            internal readonly DeterministicSimulationRunner<int, Int32Policy, int> Runner;
            internal readonly Probe Probe = new Probe();
            internal readonly Simulation Simulation;
            internal DeterministicSpatialIndex3<int, int, Int32Policy> Managed, ManagedCache;
#if !FIXEDPOINT_STANDALONE
            internal NativeDeterministicSpatialIndex3<int, int, Int32Policy> Native, NativeCache;
#endif
            internal readonly bool UseNative;
            internal World(SnapshotPolicy history, SpatialRollbackPolicy rollback, DeterministicRunnerMode mode, bool native = false,
                DeterministicReplayArchive<int, Int32Policy> archive = null, int checkpoint = 0)
            {
                UseNative = native; Context = new DeterministicRuntimeContext(DeterministicBackend.Managed);
#if !FIXEDPOINT_STANDALONE
                if (native) Context = new DeterministicRuntimeContext(DeterministicBackend.BurstCompiler, new NativeDeterministicMemoryFactory());
#endif
                FixedBoundsDeterministicProfile.TryFingerprint(out ulong numeric).ThrowIfFailure();
                Registry = new DeterministicStateRegistry(Context, new DeterministicCompatibilityProfile(1, numeric, 1, 1, 1), 2);
                var sourceOptions = new SpatialIndexOptions(new BufferOptions(16, snapshotSlots: 4, pageBytes: 128), rollback: rollback);
                var cacheOptions = new SpatialIndexOptions(new BufferOptions(16, snapshotSlots: 4, pageBytes: 128), SpatialIndexStrategy.SparseGrid,
                    SpatialStateOwnership.Derived, rollback, Fixed64.FromRaw(8), maxCellsPerObject: 8);
#if !FIXEDPOINT_STANDALONE
                if (native)
                {
                    Native = new NativeDeterministicSpatialIndex3<int, int, Int32Policy>(sourceOptions, Allocator.Persistent);
                    NativeCache = new NativeDeterministicSpatialIndex3<int, int, Int32Policy>(cacheOptions, Allocator.Persistent);
                    for (int key = 0; key < 16; key++) Native.TryAdd(key, Box(key * 2, key), key * 2).ThrowIfFailure();
                    Registry.TryRegister(SpatialStateBindings.NativeDerived(1, NativeCache.Value, Native, new uint[] { 2 }, 1, 1)).ThrowIfFailure();
                    Registry.TryRegister(SpatialStateBindings.Native3(2, Native, default(Int32Policy), default(Int32Policy), 1, 16384, 32768)).ThrowIfFailure();
                }
                else
#endif
                {
                    Managed = new DeterministicSpatialIndex3<int, int, Int32Policy>(sourceOptions);
                    ManagedCache = new DeterministicSpatialIndex3<int, int, Int32Policy>(cacheOptions);
                    for (int key = 0; key < 16; key++) Managed.TryAdd(key, Box(key * 2, key), key * 2).ThrowIfFailure();
                    Registry.TryRegister(SpatialStateBindings.ManagedDerived(1, ManagedCache, new ManagedSource { Index = Managed }, new uint[] { 2 }, 1, 1)).ThrowIfFailure();
                    Registry.TryRegister(SpatialStateBindings.Managed3(2, Managed, default(Int32Policy), default(Int32Policy), 1, 16384, 32768)).ThrowIfFailure();
                }
                Registry.TryFreeze().ThrowIfFailure(); long boundary = 0;
                if (archive != null) archive.TryRestoreCheckpoint(Registry, checkpoint, out boundary).ThrowIfFailure();
                Simulation = new Simulation(this);
                Runner = new DeterministicSimulationRunner<int, Int32Policy, int>(Context,
                    new DeterministicRuntimeOptions(2, PredictionPolicy.HoldLast, history, inputDelay: 0, historyTicks: 32, predictionTicks: 8, keyframeInterval: 4, pageBytes: 128),
                    Registry, Simulation, new[] { new PlayerId(1), new PlayerId(2) }, 0, 4, mode: mode, eventSink: Probe, initialTick: boundary);
            }
            internal SpatialRecord<int, int, FixedBounds3> Get(int key)
            {
#if !FIXEDPOINT_STANDALONE
                if (UseNative) { Native.TryGet(key, out var native).ThrowIfFailure(); return native; }
#endif
                Managed.TryGet(key, out var managed).ThrowIfFailure(); return managed;
            }
            internal void CheckProjection()
            {
                for (int key = 0; key < 16; key++)
                {
                    var expected = Get(key); SpatialRecord<int, int, FixedBounds3> actual;
#if !FIXEDPOINT_STANDALONE
                    if (UseNative) NativeCache.AsReadOnly(Native).TryGet(key, out actual).ThrowIfFailure();
                    else
#endif
                        ManagedCache.AsReadOnly(new ManagedSource { Index = Managed }).TryGet(key, out actual).ThrowIfFailure();
                    Assert.AreEqual(expected.Data, actual.Data); Assert.AreEqual(expected.Bounds, actual.Bounds);
                }
            }
            public void Dispose()
            {
                Runner.Dispose(); Registry.Dispose(); Simulation.Dispose(); ManagedCache?.Dispose(); Managed?.Dispose();
#if !FIXEDPOINT_STANDALONE
                if (UseNative) { NativeCache.Dispose(); Native.Dispose(); }
#endif
            }
        }
        private sealed class Simulation : IDeterministicSimulation<int, int>, IDisposable
        {
            private readonly World world;
            private readonly DeterministicCommandBuffer<SpatialMutation<int, int, FixedBounds3>> commands;
            private readonly SpatialMutation<int, int, FixedBounds3>[] mutations = new SpatialMutation<int, int, FixedBounds3>[4];
            private readonly byte[] workspace;
            private readonly SpatialHit<int>[] hits = new SpatialHit<int>[16];
            internal bool Fail;
            internal bool LastStepUsedBurst = false;
#if !FIXEDPOINT_STANDALONE
            private NativeArray<SpatialMutation<int, int, FixedBounds3>> nativeMutations;
            private NativeArray<byte> nativeWorkspace;
            private NativeArray<SpatialHit<int>> nativeHits;
            private NativeArray<int> nativeResult;
#endif
            internal Simulation(World world)
            {
                this.world = world; commands = new DeterministicCommandBuffer<SpatialMutation<int, int, FixedBounds3>>(world.Context,
                    world.UseNative ? new uint[] { 9, 3 } : new uint[] { 3, 9 }, 4);
                int bytes;
#if !FIXEDPOINT_STANDALONE
                if (world.UseNative)
                {
                    world.NativeCache.TryMeasureQueryScratch(out bytes).ThrowIfFailure();
                    nativeMutations = new NativeArray<SpatialMutation<int, int, FixedBounds3>>(4, Allocator.Persistent);
                    nativeWorkspace = new NativeArray<byte>(bytes, Allocator.Persistent); nativeHits = new NativeArray<SpatialHit<int>>(16, Allocator.Persistent);
                    nativeResult = new NativeArray<int>(4, Allocator.Persistent); return;
                }
#endif
                world.ManagedCache.TryMeasureQueryScratch(out bytes).ThrowIfFailure(); workspace = new byte[bytes];
            }
            public DeterministicStatus TryStep(in DeterministicInputFrame<int> frame, DeterministicEventWriter<int> events)
            {
                int key = (int)(frame.Tick * 7 % 16), delta = 1;
                for (int i = 0; i < frame.Count; i++) delta += frame[i].Value * (int)frame[i].Player.Value;
                int position = world.Get(key).Data + delta;
                commands.TryReset().ThrowIfFailure(); commands.TryGetWriter(3, out var early).ThrowIfFailure(); commands.TryGetWriter(9, out var late).ThrowIfFailure();
                var entity = new StableHandle((uint)key, 1);
                if (frame.Tick % 11 == 0)
                {
                    // Deliberately produce Add before Remove; phase ordering determines the publication sequence.
                    late.TryEnqueue(1, 1, entity, 0, new SpatialMutation<int, int, FixedBounds3> { Kind = SpatialMutationKind.Add, Key = key, Bounds = Box(position, key), Data = position }).ThrowIfFailure();
                    early.TryEnqueue(0, 1, entity, 0, new SpatialMutation<int, int, FixedBounds3> { Kind = SpatialMutationKind.Remove, Key = key }).ThrowIfFailure();
                }
                else
                {
                    late.TryEnqueue(1, 1, entity, 0, new SpatialMutation<int, int, FixedBounds3> { Kind = SpatialMutationKind.SetData, Key = key, Data = position }).ThrowIfFailure();
                    early.TryEnqueue(0, 1, entity, 0, new SpatialMutation<int, int, FixedBounds3> { Kind = SpatialMutationKind.UpdateBounds, Key = key, Bounds = Box(position, key) }).ThrowIfFailure();
                }
                commands.TryMerge(out var merged).ThrowIfFailure();
                for (int i = 0; i < merged.Length; i++) mutations[i] = merged[i].Payload;
                DeterministicStatus status; int payload;
#if !FIXEDPOINT_STANDALONE
                if (world.UseNative)
                {
                    for (int i = 0; i < merged.Length; i++) nativeMutations[i] = mutations[i];
                    new StepJob { Index = world.Native, Cache = world.NativeCache, Mutations = nativeMutations, Count = merged.Length,
                        Workspace = nativeWorkspace, Hits = nativeHits, Result = nativeResult }.Schedule().Complete();
                    LastStepUsedBurst = nativeResult[0] == 0; status = (DeterministicStatus)nativeResult[1]; payload = nativeResult[3];
                }
                else
#endif
                {
                    status = world.Managed.TryApplyBatch(new ReadOnlySpan<SpatialMutation<int, int, FixedBounds3>>(mutations, 0, merged.Length));
                    var source = new ManagedSource { Index = world.Managed };
                    if (status.IsSuccess()) status = world.ManagedCache.TrySynchronize(source);
                    payload = 0;
                    if (status.IsSuccess()) fixed (byte* work = workspace) fixed (SpatialHit<int>* output = hits)
                    {
                        status = world.ManagedCache.AsReadOnly(source).TryQuery(SpatialQuery.ForNearest(FixedVector3.zero, 4), new SpatialQueryScratch(work, workspace.Length), output, hits.Length, out int count);
                        for (int i = 0; i < count; i++) payload = payload * 31 + hits[i].Key;
                    }
                }
                if (!status.IsSuccess()) return status;
                status = events.TryEmit(1, entity, 0, payload); return status.IsSuccess() && Fail ? DeterministicStatus.OutOfRange : status;
            }
#if !FIXEDPOINT_STANDALONE
            [BurstCompile(CompileSynchronously = true)] private struct StepJob : IJob
            {
                internal NativeDeterministicSpatialIndex3<int, int, Int32Policy> Index, Cache;
                [ReadOnly] internal NativeArray<SpatialMutation<int, int, FixedBounds3>> Mutations;
                internal NativeArray<byte> Workspace;
                internal NativeArray<SpatialHit<int>> Hits;
                internal NativeArray<int> Result;
                internal int Count;
                public void Execute()
                {
                    int managed = 0; MarkManaged(ref managed); Result[0] = managed; Result[3] = 0;
                    var status = Index.TryApplyBatch((SpatialMutation<int, int, FixedBounds3>*)Mutations.GetUnsafeReadOnlyPtr(), Count);
                    if (status.IsSuccess()) status = Cache.TrySynchronize(Index);
                    if (status.IsSuccess())
                    {
                        status = Cache.AsReadOnly(Index).TryQuery(SpatialQuery.ForNearest(FixedVector3.zero, 4), new SpatialQueryScratch((byte*)Workspace.GetUnsafePtr(), Workspace.Length),
                            (SpatialHit<int>*)Hits.GetUnsafePtr(), Hits.Length, out int count);
                        for (int i = 0; i < count; i++) Result[3] = Result[3] * 31 + Hits[i].Key;
                    }
                    Result[1] = (int)status;
                }
                [BurstDiscard] private static void MarkManaged(ref int value) => value = 1;
            }
#endif
            public void Dispose()
            {
                commands.Dispose();
#if !FIXEDPOINT_STANDALONE
                if (nativeMutations.IsCreated) { nativeMutations.Dispose(); nativeWorkspace.Dispose(); nativeHits.Dispose(); nativeResult.Dispose(); }
#endif
            }
        }
        private static int Local(int tick) => tick % 3 - 1;
        private static int Remote(int tick) => tick % 5 - 2;
        private static void Submit(World world, int tick)
        { world.Runner.TrySubmitInput(new PlayerId(2), tick, Remote(tick)).ThrowIfFailure(); world.Runner.TrySubmitInput(new PlayerId(1), tick, Local(tick)).ThrowIfFailure(); }
        private static byte[] Packet(World world, long tick)
        { world.Registry.TryEncode(tick, out byte* bytes, out int count).ThrowIfFailure(); var packet = new byte[count]; for (int i = 0; i < count; i++) packet[i] = bytes[i]; return packet; }
        private static void CompareBoundary(World left, World right, long tick)
        {
            left.Runner.History.TryGetHash(tick, out ulong a).ThrowIfFailure(); right.Runner.History.TryGetHash(tick, out ulong b).ThrowIfFailure(); Assert.AreEqual(a, b, "Boundary " + tick);
            left.Runner.History.TryRestore(tick, out byte* bytesA, out int lengthA).ThrowIfFailure(); right.Runner.History.TryRestore(tick, out byte* bytesB, out int lengthB).ThrowIfFailure();
            Assert.AreEqual(lengthA, lengthB); for (int i = 0; i < lengthA; i++) if (bytesA[i] != bytesB[i]) Assert.Fail("Checkpoint bytes differ at " + tick + ":" + i);
        }
        [TestCase(SnapshotPolicy.FullPerTick, SpatialRollbackPolicy.Compact, false)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas, SpatialRollbackPolicy.Compact, false)]
        [TestCase(SnapshotPolicy.CopyOnWrite, SpatialRollbackPolicy.Compact, false)]
        [TestCase(SnapshotPolicy.FullPerTick, SpatialRollbackPolicy.FastRestore, false)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas, SpatialRollbackPolicy.FastRestore, false)]
        [TestCase(SnapshotPolicy.CopyOnWrite, SpatialRollbackPolicy.FastRestore, false)]
#if !FIXEDPOINT_STANDALONE
        [TestCase(SnapshotPolicy.FullPerTick, SpatialRollbackPolicy.Compact, true)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas, SpatialRollbackPolicy.Compact, true)]
        [TestCase(SnapshotPolicy.CopyOnWrite, SpatialRollbackPolicy.Compact, true)]
        [TestCase(SnapshotPolicy.FullPerTick, SpatialRollbackPolicy.FastRestore, true)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas, SpatialRollbackPolicy.FastRestore, true)]
        [TestCase(SnapshotPolicy.CopyOnWrite, SpatialRollbackPolicy.FastRestore, true)]
#endif
        public void LateInputsCommandsAndHistoryRolloverMatchLockstepBytesAndEvents(SnapshotPolicy history, SpatialRollbackPolicy rollback, bool native)
        {
            using var ordered = new World(history, SpatialRollbackPolicy.Compact, DeterministicRunnerMode.Lockstep);
            using var delayed = new World(history, rollback, DeterministicRunnerMode.Rollback, native);
            for (int tick = 0; tick < 72; tick++)
            {
                Submit(ordered, tick); ordered.Runner.TryAdvance(1, out _).ThrowIfFailure();
                delayed.Runner.TrySubmitInput(new PlayerId(1), tick, Local(tick)).ThrowIfFailure();
                if (tick >= 3)
                { delayed.Runner.TrySubmitInput(new PlayerId(2), tick - 3, Remote(tick - 3)).ThrowIfFailure(); delayed.Runner.TrySubmitInput(new PlayerId(2), tick - 3, Remote(tick - 3)).ThrowIfFailure(); }
                delayed.Runner.TryAdvance(1, out _).ThrowIfFailure(); CompareBoundary(ordered, delayed, delayed.Runner.ConfirmedThrough + 1);
                if (native) Assert.IsTrue(delayed.Simulation.LastStepUsedBurst);
            }
            for (int tick = 69; tick < 72; tick++) delayed.Runner.TrySubmitInput(new PlayerId(2), tick, Remote(tick)).ThrowIfFailure();
            delayed.Runner.TrySynchronizeRollback().ThrowIfFailure(); Assert.Greater(delayed.Runner.RollbackCount, 0);
            CompareBoundary(ordered, delayed, 72); CollectionAssert.AreEqual(Packet(ordered, 72), Packet(delayed, 72));
            Assert.AreEqual(ordered.Probe.Confirmed.Count, delayed.Probe.Confirmed.Count);
            for (int i = 0; i < ordered.Probe.Confirmed.Count; i++)
            { Assert.AreEqual(ordered.Probe.Confirmed[i].Id, delayed.Probe.Confirmed[i].Id); Assert.AreEqual(ordered.Probe.Confirmed[i].Payload, delayed.Probe.Confirmed[i].Payload); }
            delayed.CheckProjection();
        }
        [TestCase(SpatialRollbackPolicy.Compact, false)] [TestCase(SpatialRollbackPolicy.FastRestore, false)]
#if !FIXEDPOINT_STANDALONE
        [TestCase(SpatialRollbackPolicy.Compact, true)] [TestCase(SpatialRollbackPolicy.FastRestore, true)]
#endif
        public void ReplaySeekRebuildsDerivedStateAndReplaysIdenticalQueries(SpatialRollbackPolicy rollback, bool native)
        {
            using var ordered = new World(SnapshotPolicy.CopyOnWrite, SpatialRollbackPolicy.Compact, DeterministicRunnerMode.Lockstep);
            using var archive = new DeterministicReplayArchive<int, Int32Policy>(ordered.Context, ordered.Runner.Fingerprint, 262144, 128, 2);
            archive.TryBegin(ordered.Registry, 0).ThrowIfFailure(); var inputs = new DeterministicPlayerInput<int>[2];
            for (int tick = 0; tick < 48; tick++)
            {
                Submit(ordered, tick); ordered.Runner.TryAdvance(1, out _).ThrowIfFailure(); ordered.Runner.History.TryGetHash(tick + 1, out ulong hash).ThrowIfFailure();
                inputs[0] = new DeterministicPlayerInput<int>(new PlayerId(1), Local(tick)); inputs[1] = new DeterministicPlayerInput<int>(new PlayerId(2), Remote(tick));
                archive.TryAppendInputs(tick, inputs, hash).ThrowIfFailure(); if ((tick + 1) % 8 == 0) archive.TryAppendCheckpoint(ordered.Registry, tick + 1).ThrowIfFailure();
            }
            archive.TryFindCheckpoint(29, out int checkpoint, out long boundary).ThrowIfFailure(); Assert.AreEqual(24, boundary);
            using var replay = new World(SnapshotPolicy.KeyframesAndDeltas, rollback, DeterministicRunnerMode.Lockstep, native, archive, checkpoint);
            replay.CheckProjection(); archive.TryReplayTo(replay.Runner, 48).ThrowIfFailure(); CompareBoundary(ordered, replay, 48); replay.CheckProjection();
            if (native) Assert.IsTrue(replay.Simulation.LastStepUsedBurst);
        }
        [TestCase(SpatialRollbackPolicy.Compact, false)] [TestCase(SpatialRollbackPolicy.FastRestore, false)]
#if !FIXEDPOINT_STANDALONE
        [TestCase(SpatialRollbackPolicy.Compact, true)] [TestCase(SpatialRollbackPolicy.FastRestore, true)]
#endif
        public void FailedTicksRestoreBoundsQueriesAndAbandonEventsBeforeRetry(SpatialRollbackPolicy rollback, bool native)
        {
            using var failed = new World(SnapshotPolicy.CopyOnWrite, rollback, DeterministicRunnerMode.Lockstep, native);
            using var ordered = new World(SnapshotPolicy.CopyOnWrite, SpatialRollbackPolicy.Compact, DeterministicRunnerMode.Lockstep);
            var expected = Packet(failed, 0); Submit(failed, 0); failed.Simulation.Fail = true;
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(DeterministicStatus.OutOfRange, failed.Runner.TryAdvance(1, out _)); Assert.AreEqual(0, failed.Runner.NextTick); Assert.IsFalse(failed.Runner.IsFaulted);
                CollectionAssert.AreEqual(expected, Packet(failed, 0)); failed.CheckProjection(); Assert.AreEqual(0, failed.Probe.Confirmed.Count);
                if (native) Assert.IsTrue(failed.Simulation.LastStepUsedBurst);
            }
            failed.Simulation.Fail = false; failed.Runner.TryAdvance(1, out _).ThrowIfFailure(); Submit(ordered, 0); ordered.Runner.TryAdvance(1, out _).ThrowIfFailure();
            CompareBoundary(ordered, failed, 1); Assert.AreEqual(1, failed.Probe.Confirmed.Count);
        }
    }
}
