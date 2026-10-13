using System;
using System.Collections.Generic;
using LegendaryTools.FixedPoint;
using LegendaryTools.Deterministic.Samples;
using NUnit.Framework;
#if !FIXEDPOINT_STANDALONE
using Unity.Collections;
#endif

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class FixedBoundsWorldIntegrationTests
    {
        [OneTimeSetUp] public void Initialize()
        { FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize(); }
        private sealed class EventProbe : IDeterministicEventSink<int>
        {
            internal readonly List<DeterministicEvent<int>> Confirmed = new List<DeterministicEvent<int>>();
            internal int Published, Retracted;
            public void OnEvent(DeterministicEventDelivery delivery, in DeterministicEvent<int> value)
            {
                if (delivery == DeterministicEventDelivery.Confirmed) Confirmed.Add(value);
                else if (delivery == DeterministicEventDelivery.Speculated) Published++;
                else Retracted++;
            }
        }
        private sealed class FailureSimulation : IDeterministicSimulation<int, int>
        {
            private readonly IDeterministicSimulation<int, int> inner;
            internal bool Fail = true;
            internal FailureSimulation(IDeterministicSimulation<int, int> inner) { this.inner = inner; }
            public DeterministicStatus TryStep(in DeterministicInputFrame<int> frame, DeterministicEventWriter<int> events)
            {
                var status = inner.TryStep(frame, events);
                return status.IsSuccess() && Fail ? DeterministicStatus.OutOfRange : status;
            }
        }
        private sealed class World : IDisposable
        {
            internal readonly DeterministicRuntimeContext Context;
            internal readonly DeterministicStateRegistry State;
            internal readonly DeterministicSimulationRunner<int, Int32Policy, int> Runner;
            internal readonly EventProbe Probe = new EventProbe();
            internal readonly FailureSimulation Failure;
            private readonly DeterministicCell<FixedBounds3> managed;
#if !FIXEDPOINT_STANDALONE
            private NativeDeterministicCell<FixedBounds3> native;
            private readonly NativeBoundsSimulation nativeSimulation;
#endif
            internal FixedBounds3 Value
            {
                get
                {
#if !FIXEDPOINT_STANDALONE
                    if (nativeSimulation != null) return native.Value;
#endif
                    return managed.Value;
                }
            }
            internal World(SnapshotPolicy snapshots, DeterministicRunnerMode mode, bool useNative = false,
                DeterministicReplayArchive<int, Int32Policy> archive = null, int checkpoint = 0, bool failAfterStep = false)
            {
                FixedBoundsDeterministicProfile.TryFingerprint(out ulong fingerprint).ThrowIfFailure();
                var profile = new DeterministicCompatibilityProfile(1, fingerprint, FixedRandom.AlgorithmId, default(Int32Policy).SchemaId, 1);
                Context = new DeterministicRuntimeContext(DeterministicBackend.Managed);
#if !FIXEDPOINT_STANDALONE
                if (useNative) Context = new DeterministicRuntimeContext(DeterministicBackend.BurstCompiler, new NativeDeterministicMemoryFactory());
#endif
                State = new DeterministicStateRegistry(Context, profile, 1);
                IDeterministicSimulation<int, int> simulation;
                var initial = new FixedBounds3(FixedVector3.zero, new FixedVector3(2, 4, 6));
                var options = new BufferOptions(1, snapshotSlots: 0);
#if !FIXEDPOINT_STANDALONE
                if (useNative)
                {
                    native = new NativeDeterministicCell<FixedBounds3>(options, Allocator.Persistent); native.TrySet(0, initial).ThrowIfFailure();
                    State.TryRegister(NativeStateBindings.Cell(1, native, default(FixedBounds3Policy), FixedBoundsRuntime.Settings.Fingerprint, 256, 256)).ThrowIfFailure();
                    nativeSimulation = new NativeBoundsSimulation(native, 2); simulation = nativeSimulation;
                }
                else
#endif
                {
                    managed = new DeterministicCell<FixedBounds3>(options); managed.TrySet(initial).ThrowIfFailure();
                    State.TryRegister(ManagedStateBindings.Cell(1, managed, default(FixedBounds3Policy), FixedBoundsRuntime.Settings.Fingerprint, 256, 256)).ThrowIfFailure();
                    simulation = new BoundsSimulation(managed);
                }
                State.TryFreeze().ThrowIfFailure(); long boundary = 0;
                if (archive != null) archive.TryRestoreCheckpoint(State, checkpoint, out boundary).ThrowIfFailure();
                if (failAfterStep) { Failure = new FailureSimulation(simulation); simulation = Failure; }
                var runtimeOptions = new DeterministicRuntimeOptions(2, PredictionPolicy.HoldLast, snapshots, inputDelay: 0,
                    historyTicks: 64, predictionTicks: 8, keyframeInterval: 8, pageBytes: 64);
                Runner = new DeterministicSimulationRunner<int, Int32Policy, int>(Context, runtimeOptions, State, simulation,
                    new[] { new PlayerId(1), new PlayerId(2) }, 0, 4, mode: mode, eventSink: Probe, initialTick: boundary);
            }
            internal void VerifyBurst()
            {
#if !FIXEDPOINT_STANDALONE
                if (nativeSimulation != null) Assert.IsTrue(nativeSimulation.LastStepUsedBurst);
#endif
            }
            public void Dispose()
            {
                Runner.Dispose(); State.Dispose(); managed?.Dispose();
#if !FIXEDPOINT_STANDALONE
                if (nativeSimulation != null) { nativeSimulation.Dispose(); native.Dispose(); }
#endif
            }
        }
        private static int Local(int tick) => tick % 3 - 1;
        private static int Remote(int tick) => tick % 5 - 2;
        [TestCase(SnapshotPolicy.FullPerTick, false)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas, false)]
        [TestCase(SnapshotPolicy.CopyOnWrite, false)]
#if !FIXEDPOINT_STANDALONE
        [TestCase(SnapshotPolicy.FullPerTick, true)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas, true)]
        [TestCase(SnapshotPolicy.CopyOnWrite, true)]
#endif
        public void DelayedBoundsStateMatchesLockstepAfterRollback(SnapshotPolicy snapshots, bool useNative)
        {
            using var ordered = new World(snapshots, DeterministicRunnerMode.Lockstep);
            using var delayed = new World(snapshots, DeterministicRunnerMode.Rollback, useNative);
            for (int tick = 0; tick < 36; tick++)
            {
                ordered.Runner.TrySubmitInput(new PlayerId(1), tick, Local(tick)).ThrowIfFailure(); ordered.Runner.TrySubmitInput(new PlayerId(2), tick, Remote(tick)).ThrowIfFailure();
                ordered.Runner.TryAdvance(1, out _).ThrowIfFailure();
                delayed.Runner.TrySubmitInput(new PlayerId(1), tick, Local(tick)).ThrowIfFailure();
                if (tick >= 3) delayed.Runner.TrySubmitInput(new PlayerId(2), tick - 3, Remote(tick - 3)).ThrowIfFailure();
                delayed.Runner.TryAdvance(1, out _).ThrowIfFailure(); delayed.VerifyBurst();
                long committedBoundary = delayed.Runner.ConfirmedThrough + 1;
                ordered.Runner.History.TryGetHash(committedBoundary, out ulong committedA).ThrowIfFailure();
                delayed.Runner.History.TryGetHash(committedBoundary, out ulong committedB).ThrowIfFailure(); Assert.AreEqual(committedA, committedB, "Committed boundary " + committedBoundary);
            }
            for (int tick = 33; tick < 36; tick++) delayed.Runner.TrySubmitInput(new PlayerId(2), tick, Remote(tick)).ThrowIfFailure();
            delayed.Runner.TrySynchronizeRollback().ThrowIfFailure(); delayed.VerifyBurst();
            Assert.AreEqual(ordered.Value, delayed.Value); Assert.Greater(delayed.Runner.RollbackCount, 0);
            ordered.Runner.History.TryGetHash(36, out ulong a).ThrowIfFailure(); delayed.Runner.History.TryGetHash(36, out ulong b).ThrowIfFailure(); Assert.AreEqual(a, b);
            ordered.State.TryEncode(36, out byte* bytesA, out int lengthA).ThrowIfFailure(); delayed.State.TryEncode(36, out byte* bytesB, out int lengthB).ThrowIfFailure(); Assert.AreEqual(lengthA, lengthB);
            for (int i = 0; i < lengthA; i++) Assert.AreEqual(bytesA[i], bytesB[i]);
        }
        [TestCase(false)]
#if !FIXEDPOINT_STANDALONE
        [TestCase(true)]
#endif
        public void ReplaySeekRestoresBoundsAndContinuesWithIdenticalHashes(bool useNative)
        {
            using var world = new World(SnapshotPolicy.KeyframesAndDeltas, DeterministicRunnerMode.Lockstep);
            using var archive = new DeterministicReplayArchive<int, Int32Policy>(world.Context, world.Runner.Fingerprint, 65536, 64, 2);
            archive.TryBegin(world.State, 0).ThrowIfFailure(); var inputs = new DeterministicPlayerInput<int>[2];
            for (int tick = 0; tick < 20; tick++)
            {
                world.Runner.TrySubmitInput(new PlayerId(1), tick, Local(tick)).ThrowIfFailure(); world.Runner.TrySubmitInput(new PlayerId(2), tick, Remote(tick)).ThrowIfFailure(); world.Runner.TryAdvance(1, out _).ThrowIfFailure();
                world.Runner.History.TryGetHash(tick + 1, out ulong hash).ThrowIfFailure();
                inputs[0] = new DeterministicPlayerInput<int>(new PlayerId(1), Local(tick)); inputs[1] = new DeterministicPlayerInput<int>(new PlayerId(2), Remote(tick));
                archive.TryAppendInputs(tick, inputs, hash).ThrowIfFailure(); if ((tick + 1) % 4 == 0) archive.TryAppendCheckpoint(world.State, tick + 1).ThrowIfFailure();
            }
            archive.TryFindCheckpoint(13, out int checkpoint, out long boundary).ThrowIfFailure(); Assert.AreEqual(12, boundary);
            using var replay = new World(SnapshotPolicy.KeyframesAndDeltas, DeterministicRunnerMode.Lockstep, useNative, archive, checkpoint);
            archive.TryReplayTo(replay.Runner, 20).ThrowIfFailure(); replay.VerifyBurst(); Assert.AreEqual(world.Value, replay.Value);
            world.Runner.History.TryGetHash(20, out ulong a).ThrowIfFailure(); replay.Runner.History.TryGetHash(20, out ulong b).ThrowIfFailure(); Assert.AreEqual(a, b);
        }
        [Test] public void CanonicalPoliciesSupportMeasureSignedZeroAndAtomicReadWrite()
        {
            var codec = default(FixedBounds3Policy); var zero = new FixedVector3(Fixed64.NegativeZero, Fixed64.Zero, Fixed64.NegativeZero);
            Assert.IsTrue(FixedBounds3.TryFromStorage(zero, FixedVector3.zero, out var box, out _));
            var measure = DeterministicWriter.CreateMeasure(); codec.TryWrite(ref measure, box).ThrowIfFailure(); Assert.AreEqual(48, measure.Position);
            byte* bytes = stackalloc byte[48]; var writer = new DeterministicWriter(bytes, 48); codec.TryWrite(ref writer, box).ThrowIfFailure();
            var reader = new DeterministicReader(bytes, 48); codec.TryRead(ref reader, out var restored).ThrowIfFailure(); restored.GetStorage(out var first, out _); Assert.AreEqual(Fixed64.NegativeZero.RawValue, first.x.RawValue);
            Assert.AreEqual(codec.Hash(default), codec.Hash(box)); Assert.IsTrue(codec.Equals(default, box)); Assert.AreEqual(0, codec.Compare(default, box));
            for (int prefix = 0; prefix < 48; prefix++)
            {
                reader = new DeterministicReader(bytes, prefix); Assert.AreEqual(DeterministicStatus.BufferTooSmall, codec.TryRead(ref reader, out _)); Assert.AreEqual(0, reader.Position);
                writer = new DeterministicWriter(bytes, prefix); Assert.AreEqual(DeterministicStatus.BufferTooSmall, codec.TryWrite(ref writer, box)); Assert.AreEqual(0, writer.Position);
            }
            writer = new DeterministicWriter(bytes, 48); Assert.AreEqual(DeterministicStatus.InvalidEncoding, codec.TryWrite(ref writer, FixedBounds3.Invalid)); Assert.AreEqual(0, writer.Position);
            var c2 = default(FixedBounds2Policy); Assert.AreNotEqual(c2.SchemaId, codec.SchemaId); Assert.AreEqual(32, c2.Measure(default));
            var zero2 = new FixedVector2(Fixed64.NegativeZero, Fixed64.Zero);
            Assert.IsTrue(FixedBounds2.TryFromStorage(zero2, FixedVector2.zero, out var flat, out _));
            writer = new DeterministicWriter(bytes, 48); c2.TryWrite(ref writer, flat).ThrowIfFailure(); Assert.AreEqual(32, writer.Position);
            reader = new DeterministicReader(bytes, 32); c2.TryRead(ref reader, out var flatRestored).ThrowIfFailure(); flatRestored.GetStorage(out var flatFirst, out _); Assert.AreEqual(Fixed64.NegativeZero.RawValue, flatFirst.x.RawValue);
            Assert.IsTrue(c2.Equals(flat, default)); Assert.AreEqual(c2.Hash(flat), c2.Hash(default));
            // A syntactically complete payload with an invalid second vector must leave the reader unchanged.
            writer = new DeterministicWriter(bytes, 48); codec.TryWrite(ref writer, default).ThrowIfFailure();
            for (int i = 0; i < 8; i++) bytes[24 + i] = 0xFF; // raw -1: reversed MinMax or negative extents.
            reader = new DeterministicReader(bytes, 48); Assert.AreEqual(DeterministicStatus.InvalidEncoding, codec.TryRead(ref reader, out _)); Assert.AreEqual(0, reader.Position);
        }
        [Test] public void BoundsWorkAsPortableEcsComponentsAndOrderedKeys()
        {
            using var components = new DeterministicSparseSet<FixedBounds3>(4, 8); var entity = new StableHandle(2, 1);
            var box = new FixedBounds3(FixedVector3.zero, FixedVector3.one * 2); components.Add(entity, box);
            components.TryCapture(out var checkpoint).ThrowIfFailure(); components.TryGet(entity, out var copy).ThrowIfFailure();
            copy.center = FixedVector3.one; components.TrySet(entity, copy).ThrowIfFailure(); Assert.AreEqual(FixedVector3.one, components[entity].center);
            components.TryRestore(checkpoint).ThrowIfFailure(); checkpoint.Dispose(); Assert.AreEqual(box, components[entity]);
            using var keys = new DeterministicDictionary<FixedBounds3, int, FixedBounds3Policy>(4); keys.Add(box, 42); Assert.AreEqual(42, keys[box]);
            Assert.AreEqual(DeterministicStatus.DuplicateKey, keys.TryAdd(box, 0));
        }
        private static byte[] Checkpoint(World world, long boundary)
        {
            world.State.TryEncode(boundary, out byte* bytes, out int length).ThrowIfFailure(); var result = new byte[length];
            for (int i = 0; i < length; i++) result[i] = bytes[i]; return result;
        }
        private static void VerifyCheckpoint(World world, long boundary, byte[] expected)
        {
            world.Runner.History.TryRestore(boundary, out byte* bytes, out int length).ThrowIfFailure(); Assert.AreEqual(expected.Length, length);
            for (int i = 0; i < length; i++) if (bytes[i] != expected[i]) Assert.Fail("Checkpoint byte mismatch at boundary " + boundary + ", byte " + i);
        }
        [TestCase(SnapshotPolicy.FullPerTick, false)] [TestCase(SnapshotPolicy.KeyframesAndDeltas, false)] [TestCase(SnapshotPolicy.CopyOnWrite, false)]
#if !FIXEDPOINT_STANDALONE
        [TestCase(SnapshotPolicy.FullPerTick, true)] [TestCase(SnapshotPolicy.KeyframesAndDeltas, true)] [TestCase(SnapshotPolicy.CopyOnWrite, true)]
#endif
        public void JitterReorderedDuplicatesAndHistoryRolloverMatchEveryCommittedCheckpointAndEvent(SnapshotPolicy snapshots, bool useNative)
        {
            const int ticks = 96; using var reference = new World(snapshots, DeterministicRunnerMode.Lockstep);
            var hashes = new ulong[ticks + 1]; var checkpoints = new byte[ticks + 1][];
            reference.Runner.History.TryGetHash(0, out hashes[0]).ThrowIfFailure(); checkpoints[0] = Checkpoint(reference, 0);
            for (int tick = 0; tick < ticks; tick++)
            {
                // Opposite submission order must not change the canonical PlayerId order in the simulation frame.
                reference.Runner.TrySubmitInput(new PlayerId(2), tick, Remote(tick)).ThrowIfFailure(); reference.Runner.TrySubmitInput(new PlayerId(1), tick, Local(tick)).ThrowIfFailure();
                reference.Runner.TryAdvance(1, out int executed).ThrowIfFailure(); Assert.AreEqual(1, executed);
                reference.Runner.History.TryGetHash(tick + 1, out hashes[tick + 1]).ThrowIfFailure(); checkpoints[tick + 1] = Checkpoint(reference, tick + 1);
            }
            using var delayed = new World(snapshots, DeterministicRunnerMode.Rollback, useNative); var delivered = new bool[ticks]; long verified = 0;
            for (int tick = 0; tick < ticks; tick++)
            {
                delayed.Runner.TrySubmitInput(new PlayerId(1), tick, Local(tick)).ThrowIfFailure();
                for (int at = tick; at >= 0; at--)
                {
                    int delay = (at * 11 + at / 3) % 6;
                    if (delivered[at] || at + delay > tick) continue;
                    delayed.Runner.TrySubmitInput(new PlayerId(2), at, Remote(at)).ThrowIfFailure();
                    delayed.Runner.TrySubmitInput(new PlayerId(2), at, Remote(at)).ThrowIfFailure(); delivered[at] = true;
                }
                delayed.Runner.TryAdvance(1, out int executed).ThrowIfFailure(); Assert.AreEqual(1, executed); delayed.VerifyBurst();
                long boundary = delayed.Runner.ConfirmedThrough + 1;
                for (long at = verified + 1; at <= boundary; at++)
                { delayed.Runner.History.TryGetHash(at, out ulong actual).ThrowIfFailure(); Assert.AreEqual(hashes[at], actual, "Confirmed boundary " + at); VerifyCheckpoint(delayed, at, checkpoints[at]); }
                verified = boundary;
            }
            for (int at = ticks - 1; at >= 0; at--) if (!delivered[at]) delayed.Runner.TrySubmitInput(new PlayerId(2), at, Remote(at)).ThrowIfFailure();
            delayed.Runner.TrySynchronizeRollback().ThrowIfFailure(); delayed.VerifyBurst();
            for (long at = verified + 1; at <= ticks; at++) { delayed.Runner.History.TryGetHash(at, out ulong actual).ThrowIfFailure(); Assert.AreEqual(hashes[at], actual); VerifyCheckpoint(delayed, at, checkpoints[at]); }
            CollectionAssert.AreEqual(checkpoints[ticks], Checkpoint(delayed, ticks)); Assert.AreEqual(reference.Value, delayed.Value);
            Assert.Greater(delayed.Runner.RollbackCount, 10); Assert.Greater(delayed.Runner.ReplayedTicks, delayed.Runner.RollbackCount); Assert.Greater(delayed.Runner.History.OldestTick, 0);
            Assert.AreEqual(ticks, delayed.Probe.Confirmed.Count); Assert.AreEqual(ticks, reference.Probe.Confirmed.Count);
            for (int i = 0; i < ticks; i++) { Assert.AreEqual(reference.Probe.Confirmed[i].Id, delayed.Probe.Confirmed[i].Id); Assert.AreEqual(reference.Probe.Confirmed[i].Payload, delayed.Probe.Confirmed[i].Payload); }
            Assert.Greater(delayed.Probe.Retracted, 0);
        }
        [TestCase(SnapshotPolicy.FullPerTick, false)] [TestCase(SnapshotPolicy.KeyframesAndDeltas, false)] [TestCase(SnapshotPolicy.CopyOnWrite, false)]
#if !FIXEDPOINT_STANDALONE
        [TestCase(SnapshotPolicy.FullPerTick, true)] [TestCase(SnapshotPolicy.KeyframesAndDeltas, true)] [TestCase(SnapshotPolicy.CopyOnWrite, true)]
#endif
        public void FailureAfterStateMutationRestoresBoundsAndAbandonsEventsBeforeRetry(SnapshotPolicy snapshots, bool useNative)
        {
            using var failed = new World(snapshots, DeterministicRunnerMode.Lockstep, useNative, failAfterStep: true);
            byte[] initial = Checkpoint(failed, 0); var bounds = failed.Value;
            failed.Runner.TrySubmitInput(new PlayerId(1), 0, 3).ThrowIfFailure(); failed.Runner.TrySubmitInput(new PlayerId(2), 0, -2).ThrowIfFailure();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Assert.AreEqual(DeterministicStatus.OutOfRange, failed.Runner.TryAdvance(1, out int executed)); Assert.AreEqual(0, executed);
                Assert.AreEqual(0, failed.Runner.NextTick); Assert.AreEqual(-1, failed.Runner.Events.NewestTick); Assert.AreEqual(-1, failed.Runner.ConfirmedThrough); Assert.IsFalse(failed.Runner.IsFaulted);
                Assert.AreEqual(bounds, failed.Value); CollectionAssert.AreEqual(initial, Checkpoint(failed, 0)); Assert.AreEqual(0, failed.Probe.Published); Assert.AreEqual(0, failed.Probe.Confirmed.Count); failed.VerifyBurst();
            }
            failed.Failure.Fail = false; failed.Runner.TryAdvance(1, out int retried).ThrowIfFailure(); Assert.AreEqual(1, retried); failed.VerifyBurst();
            using var reference = new World(snapshots, DeterministicRunnerMode.Lockstep);
            reference.Runner.TrySubmitInput(new PlayerId(2), 0, -2).ThrowIfFailure(); reference.Runner.TrySubmitInput(new PlayerId(1), 0, 3).ThrowIfFailure(); reference.Runner.TryAdvance(1, out _).ThrowIfFailure();
            CollectionAssert.AreEqual(Checkpoint(reference, 1), Checkpoint(failed, 1)); Assert.AreEqual(1, failed.Probe.Published); Assert.AreEqual(1, failed.Probe.Confirmed.Count);
            Assert.AreEqual(reference.Probe.Confirmed[0].Id, failed.Probe.Confirmed[0].Id); Assert.AreEqual(reference.Probe.Confirmed[0].Payload, failed.Probe.Confirmed[0].Payload);
        }
        [Test] public void EverySignedZeroCombinationHasNumericIdentityAndLosslessCanonicalStorage()
        {
            byte* bytes = stackalloc byte[64]; var codec = default(FixedBounds3Policy); var equality = default(FixedBounds2Policy);
            for (int mask = 0; mask < 64; mask++)
            {
                Fixed64 Scalar(int bit) => (mask & (1 << bit)) != 0 ? Fixed64.NegativeZero : Fixed64.Zero;
                var a = new FixedVector3(Scalar(0), Scalar(1), Scalar(2)); var b = new FixedVector3(Scalar(3), Scalar(4), Scalar(5));
                Assert.IsTrue(FixedBounds3.TryFromStorage(a, b, out var box, out _));
                Assert.IsTrue(codec.Equals(box, default)); Assert.AreEqual(0, codec.Compare(box, default)); Assert.AreEqual(codec.Hash(default), codec.Hash(box));
                box.center = box.center; box.size = box.size; box.extents = box.extents; box.min = box.min; box.max = box.max;
                box.Expand(Fixed64.NegativeZero); box.Encapsulate(FixedVector3.zero);
                for (int i = 0; i < 64; i++) bytes[i] = 0xA5; var writer = new DeterministicWriter(bytes, 64, 7); codec.TryWrite(ref writer, box).ThrowIfFailure(); Assert.AreEqual(55, writer.Position);
                for (int i = 0; i < 7; i++) Assert.AreEqual(0xA5, bytes[i]); for (int i = 55; i < 64; i++) Assert.AreEqual(0xA5, bytes[i]);
                var reader = new DeterministicReader(bytes, 55, 7); codec.TryRead(ref reader, out var restored).ThrowIfFailure(); Assert.AreEqual(55, reader.Position);
                restored.GetStorage(out var c, out var d); Assert.AreEqual(a.x.RawValue, c.x.RawValue); Assert.AreEqual(a.y.RawValue, c.y.RawValue); Assert.AreEqual(a.z.RawValue, c.z.RawValue); Assert.AreEqual(b.x.RawValue, d.x.RawValue); Assert.AreEqual(b.y.RawValue, d.y.RawValue); Assert.AreEqual(b.z.RawValue, d.z.RawValue);
                Assert.IsTrue(FixedBounds2.TryFromStorage(new FixedVector2(a.x, a.y), new FixedVector2(b.x, b.y), out var flat, out _));
                writer = new DeterministicWriter(bytes, 64, 7); equality.TryWrite(ref writer, flat).ThrowIfFailure(); Assert.AreEqual(39, writer.Position);
                reader = new DeterministicReader(bytes, 39, 7); equality.TryRead(ref reader, out var flatRestored).ThrowIfFailure(); flatRestored.GetStorage(out var e, out var f);
                Assert.AreEqual(a.x.RawValue, e.x.RawValue); Assert.AreEqual(a.y.RawValue, e.y.RawValue); Assert.AreEqual(b.x.RawValue, f.x.RawValue); Assert.AreEqual(b.y.RawValue, f.y.RawValue);
                Assert.AreEqual(0, equality.Compare(flat, default)); Assert.AreEqual(equality.Hash(default), equality.Hash(flat));
            }
        }
        [Test] public void RepeatedEcsCaptureRestoreAndEntityGenerationReusePreserveFutureUpdates()
        {
            using var components = new DeterministicSparseSet<FixedBounds3>(4, 8);
            for (uint id = 0; id < 4; id++) components.Add(new StableHandle(id, 1), new FixedBounds3(new FixedVector3((int)id, 0, 0), FixedVector3.one * 2));
            for (int cycle = 0; cycle < 24; cycle++)
            {
                components.TryCapture(out var snapshot).ThrowIfFailure();
                try
                {
                    for (uint id = 0; id < 4; id++) { var entity = new StableHandle(id, 1); var value = components[entity]; value.Expand(2); value.center += FixedVector3.one; components.TrySet(entity, value).ThrowIfFailure(); }
                    var stale = new StableHandle(2, 1); components.TryRemove(stale).ThrowIfFailure(); components.Add(new StableHandle(2, 2), default);
                    Assert.AreEqual(DeterministicStatus.NotFound, components.TryGet(stale, out _));
                    components.TryRestore(snapshot).ThrowIfFailure(); Assert.AreEqual(DeterministicStatus.NotFound, components.TryGet(new StableHandle(2, 2), out _));
                    for (uint id = 0; id < 4; id++)
                    {
                        var entity = new StableHandle(id, 1); var value = components[entity]; Assert.AreEqual(new FixedVector3((int)id + cycle, cycle, cycle), value.center); Assert.AreEqual(FixedVector3.one * 2, value.size);
                        value.center += FixedVector3.one; components.TrySet(entity, value).ThrowIfFailure();
                    }
                }
                finally { snapshot.Dispose(); }
            }
        }
    }
}
