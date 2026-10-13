using System;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    [Category("DeterministicEdgeCase")]
    public sealed unsafe class RuntimeEdgeCaseTests
    {
        private static DeterministicRuntimeContext Context() => new DeterministicRuntimeContext(DeterministicBackend.Managed);

        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void MinimumHistoryHandlesEmptyAndPartialPagesEvictionTruncateAndReset(SnapshotPolicy policy)
        {
            using (var history = new DeterministicWorldHistory(Context(), 129, 2, policy, 1, 64))
            {
                byte* packet = stackalloc byte[130]; int[] sizes = { 0, 1, 63, 64, 65, 127, 128, 129 };
                for (int tick = 0; tick < 64; tick++)
                {
                    int length = sizes[tick % sizes.Length]; Fill(packet, length, tick);
                    history.TryCapture(tick, length == 0 ? null : packet, length).ThrowIfFailure();
                    Assert.AreEqual(tick, history.NewestTick); Assert.AreEqual(Math.Max(0, tick - 1), history.OldestTick);
                    Verify(history, tick, length, tick);
                    if (tick > 0) Verify(history, tick - 1, sizes[(tick - 1) % sizes.Length], tick - 1);
                    if (tick > 1) Assert.AreEqual(DeterministicStatus.HistoryExhausted, history.TryRestore(tick - 2, out _, out _));
                    history.TryGetHash(tick, out ulong before).ThrowIfFailure();
                    Assert.AreEqual(DeterministicStatus.OutOfRange, history.TryCapture(tick + 2, packet, 1));
                    Assert.AreEqual(DeterministicStatus.BufferTooSmall, history.TryCapture(tick + 1, packet, 130));
                    history.TryGetHash(tick, out ulong after).ThrowIfFailure(); Assert.AreEqual(before, after); Assert.AreEqual(tick, history.NewestTick);
                }
                history.TryTruncate(62).ThrowIfFailure(); Assert.AreEqual(62, history.NewestTick);
                Assert.AreEqual(DeterministicStatus.HistoryExhausted, history.TryGetHash(63, out _));
                Fill(packet, 33, 901); history.TryCapture(63, packet, 33).ThrowIfFailure(); Verify(history, 63, 33, 901);
                history.TryReset().ThrowIfFailure(); Assert.AreEqual(-1, history.NewestTick); Assert.AreEqual(-1, history.OldestTick); Assert.AreEqual(0, history.LivePages);
                Assert.AreEqual(DeterministicStatus.HistoryExhausted, history.TryRestore(63, out _, out _));
                Fill(packet, 8, 100); history.TryCapture(100, packet, 8).ThrowIfFailure(); Verify(history, 100, 8, 100);
            }
        }
        private static void Fill(byte* packet, int length, int seed)
        { for (int i = 0; i < length; i++) packet[i] = unchecked((byte)(seed * 37 + i * 13)); }
        private static void Verify(DeterministicWorldHistory history, long tick, int expectedLength, int seed)
        {
            history.TryRestore(tick, out byte* actual, out int length).ThrowIfFailure(); Assert.AreEqual(expectedLength, length);
            for (int i = 0; i < length; i++) Assert.AreEqual(unchecked((byte)(seed * 37 + i * 13)), actual[i], "tick " + tick + ", byte " + i);
        }

        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void HistoryAtMaximumTickCanRestoreAndBranchWithoutWrappingToNegative(SnapshotPolicy policy)
        {
            using (var history = new DeterministicWorldHistory(Context(), 64, 2, policy, 2, 64))
            {
                byte* packet = stackalloc byte[64];
                for (int i = 0; i < 3; i++) { Fill(packet, 64, i); history.TryCapture(long.MaxValue - 2 + i, packet, 64).ThrowIfFailure(); }
                Assert.AreEqual(long.MaxValue - 1, history.OldestTick); Verify(history, long.MaxValue, 64, 2);
                Assert.AreEqual(DeterministicStatus.OutOfRange, history.TryCapture(0, packet, 64));
                history.TryTruncate(long.MaxValue - 1).ThrowIfFailure(); Fill(packet, 64, 42);
                history.TryCapture(long.MaxValue, packet, 64).ThrowIfFailure(); Verify(history, long.MaxValue, 64, 42);
                history.TryReset().ThrowIfFailure(); history.TryCapture(0, null, 0).ThrowIfFailure(); Verify(history, 0, 0, 0);
            }
        }

        [TestCase(PredictionPolicy.HoldLast)]
        [TestCase(PredictionPolicy.Neutral)]
        public void SixtyFourPlayerMaskPreservesHighestBitAcrossSortedAdmissionAndEmptyRoster(PredictionPolicy policy)
        {
            using (var inputs = new DeterministicInputHistory<int, Int32Policy>(Context(), 64, 16, policy, -7))
            {
                for (ulong id = 64; id >= 2; id--) inputs.TryRegisterPlayer(new PlayerId(id), 0).ThrowIfFailure();
                for (int tick = 0; tick < 7; tick++)
                {
                    if (tick == 3)
                    { inputs.TryDeactivatePlayer(new PlayerId(64), 3).ThrowIfFailure(); inputs.TryRegisterPlayer(new PlayerId(1), 3).ThrowIfFailure(); }
                    if (tick == 4) inputs.TryApplyActiveRoster(Roster(2, 64), tick).ThrowIfFailure();
                    if (tick == 5) inputs.TryApplyActiveRoster(Array.Empty<PlayerId>(), tick).ThrowIfFailure();
                    if (tick == 6) inputs.TryApplyActiveRoster(Roster(1, 64), tick).ThrowIfFailure();
                    foreach (var player in ExpectedRoster(tick)) inputs.TrySubmit(player, tick, (int)player.Value * 100 + tick, out _).ThrowIfFailure();
                    inputs.TryResolve(tick, false, out var frame).ThrowIfFailure(); VerifyRoster(frame, tick); inputs.TryAdvance(tick).ThrowIfFailure();
                }
                Assert.AreEqual(64, inputs.PlayerCount);
                Assert.AreEqual(DeterministicStatus.CapacityExceeded, inputs.TryRegisterPlayer(new PlayerId(65), 7));
                Assert.AreEqual(DeterministicStatus.InvalidConfiguration, inputs.CanApplyActiveRoster(new[] { new PlayerId(1), new PlayerId(1) }, 7));
                inputs.TryRewind(0).ThrowIfFailure();
                for (int tick = 0; tick < 7; tick++)
                { inputs.TryResolve(tick, false, out var frame).ThrowIfFailure(); VerifyRoster(frame, tick); inputs.TryAdvance(tick).ThrowIfFailure(); }
                Assert.AreEqual(DeterministicStatus.NotFound, inputs.CanSubmit(new PlayerId(64), 3, 6403));
                Assert.AreEqual(DeterministicStatus.NotFound, inputs.CanSubmit(new PlayerId(1), 0, 100));
                inputs.TryGetActualFrame(0, out var historic).ThrowIfFailure(); Assert.AreEqual(64UL, historic[62].Player.Value);
                inputs.TryGetActualFrame(6, out var full).ThrowIfFailure(); Assert.AreEqual(64UL, full[63].Player.Value);
            }
        }
        private static PlayerId[] Roster(int first, int last)
        { var result = new PlayerId[last - first + 1]; for (int i = 0; i < result.Length; i++) result[i] = new PlayerId((ulong)(first + i)); return result; }
        private static PlayerId[] ExpectedRoster(int tick) => tick == 5 ? Array.Empty<PlayerId>() : tick == 3 ? Roster(1, 63) : tick == 6 ? Roster(1, 64) : Roster(2, 64);
        private static void VerifyRoster(DeterministicInputFrame<int> frame, int tick)
        {
            var expected = ExpectedRoster(tick); Assert.AreEqual(expected.Length, frame.Count);
            for (int i = 0; i < expected.Length; i++)
            { Assert.AreEqual(expected[i], frame[i].Player); Assert.AreEqual((int)expected[i].Value * 100 + tick, frame[i].Value); Assert.IsFalse(frame[i].Predicted); }
        }

        [TestCase(PredictionPolicy.HoldLast)]
        [TestCase(PredictionPolicy.Neutral)]
        public void ExactPastAndFutureInputWindowLimitsSurviveRowWrapping(PredictionPolicy policy)
        {
            using (var inputs = new DeterministicInputHistory<int, Int32Policy>(Context(), 1, 2, policy, 0, initialTick: 10))
            {
                var player = new PlayerId(ulong.MaxValue); inputs.TryRegisterPlayer(player, 10).ThrowIfFailure();
                inputs.TrySubmit(player, 10, 10, out _).ThrowIfFailure();
                for (int tick = 10; tick <= 20; tick++)
                {
                    inputs.TrySubmit(player, tick + 1, tick + 1, out _).ThrowIfFailure();
                    Assert.AreEqual(DeterministicStatus.HistoryExhausted, inputs.TrySubmit(player, tick + 2, 999, out _));
                    inputs.TryResolve(tick, false, out var frame).ThrowIfFailure(); Assert.AreEqual(tick, frame[0].Value);
                    inputs.TryAdvance(tick).ThrowIfFailure(); Assert.AreEqual(Math.Max(10, tick - 1), inputs.OldestTick);
                    Assert.AreEqual(DeterministicStatus.HistoryExhausted, inputs.TrySubmit(player, inputs.OldestTick - 1, 999, out _));
                    Assert.AreEqual(DeterministicStatus.None, inputs.CanSubmit(player, tick, tick));
                    Assert.AreEqual(DeterministicStatus.ProtocolConflict, inputs.CanSubmit(player, tick, tick + 1));
                }
                inputs.TryRewind(19).ThrowIfFailure(); inputs.TryResolve(19, false, out var retained).ThrowIfFailure(); Assert.AreEqual(19, retained[0].Value);
                Assert.AreEqual(DeterministicStatus.HistoryExhausted, inputs.TryRewind(18));
            }
        }

        [TestCase(PredictionPolicy.HoldLast)]
        [TestCase(PredictionPolicy.Neutral)]
        public void FinalInputTickAdvancesOnceAndRejectsOverflowingTick(PredictionPolicy policy)
        {
            using (var inputs = new DeterministicInputHistory<int, Int32Policy>(Context(), 1, 2, policy, 0, initialTick: long.MaxValue - 1))
            {
                var player = new PlayerId(1); inputs.TryRegisterPlayer(player, long.MaxValue - 1).ThrowIfFailure();
                inputs.TrySubmit(player, long.MaxValue - 1, 77, out _).ThrowIfFailure();
                inputs.TryResolve(long.MaxValue - 1, false, out var frame).ThrowIfFailure(); Assert.AreEqual(77, frame[0].Value);
                inputs.TryAdvance(long.MaxValue - 1).ThrowIfFailure(); Assert.AreEqual(long.MaxValue, inputs.NextTick);
                Assert.AreEqual(DeterministicStatus.HistoryExhausted, inputs.TrySubmit(player, long.MaxValue, 88, out _));
                Assert.AreEqual(DeterministicStatus.OutOfRange, inputs.TryResolve(long.MaxValue, true, out _));
                Assert.AreEqual(DeterministicStatus.OutOfRange, inputs.TryAdvance(long.MaxValue)); Assert.AreEqual(long.MaxValue, inputs.NextTick);
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void ReplayRecordOrByteExhaustionDoesNotAdvanceOrReplaceRecordedCheckpoint(bool limitBytes)
        {
            using (var cell = new DeterministicCell<int>(17))
            using (var registry = new DeterministicStateRegistry(Context(), new DeterministicCompatibilityProfile(1, 2, 3, 4, 5), 1))
            {
                registry.TryRegister(ManagedStateBindings.Cell(1, cell, default(Int32Policy), 1, 128, 128)).ThrowIfFailure(); registry.TryFreeze().ThrowIfFailure();
                byte* before = stackalloc byte[4096]; byte* after = stackalloc byte[4096]; int initialBytes;
                using (var measured = new DeterministicReplayArchive<int, Int32Policy>(Context(), registry.Fingerprint, 4096, 8, 2))
                { measured.TryBegin(registry, 9).ThrowIfFailure(); var measure = new DeterministicWriter(before, 4096); measured.TryWriteCanonical(ref measure).ThrowIfFailure(); initialBytes = measure.Position; }
                using (var archive = new DeterministicReplayArchive<int, Int32Policy>(Context(), registry.Fingerprint, limitBytes ? initialBytes : 4096, limitBytes ? 8 : 1, 2))
                {
                    archive.TryBegin(registry, 9).ThrowIfFailure(); var writer = new DeterministicWriter(before, 4096); archive.TryWriteCanonical(ref writer).ThrowIfFailure();
                    var reversed = new[] { new DeterministicPlayerInput<int>(new PlayerId(2), 2), new DeterministicPlayerInput<int>(new PlayerId(1), 1) };
                    Assert.AreEqual(DeterministicStatus.InvalidConfiguration, archive.TryAppendInputs(9, reversed, 123));
                    Assert.AreEqual(DeterministicStatus.OutOfRange, archive.TryAppendInputs(10, Array.Empty<DeterministicPlayerInput<int>>(), 123));
                    Assert.AreEqual(DeterministicStatus.CapacityExceeded, archive.TryAppendInputs(9, new[] { new DeterministicPlayerInput<int>(new PlayerId(1), 7) }, 123));
                    Assert.AreEqual(9, archive.NextTick); Assert.AreEqual(1, archive.Count);
                    var secondWriter = new DeterministicWriter(after, 4096); archive.TryWriteCanonical(ref secondWriter).ThrowIfFailure();
                    Assert.AreEqual(writer.Position, secondWriter.Position); for (int i = 0; i < writer.Position; i++) Assert.AreEqual(before[i], after[i]);
                    cell.Value = 99; archive.TryRestoreCheckpoint(registry, 0, out long boundary).ThrowIfFailure(); Assert.AreEqual(9, boundary); Assert.AreEqual(17, cell.Value);
                }
            }
        }
    }
}
