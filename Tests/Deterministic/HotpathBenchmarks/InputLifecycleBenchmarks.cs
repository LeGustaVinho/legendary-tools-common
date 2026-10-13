using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.PerformanceTesting;

namespace LegendaryTools.Deterministic.Tests.HotpathBenchmarks
{
    public sealed unsafe partial class NetworkLifecycleHotpathBenchmarks
    {
        private static int InputValue(int player, long tick) => Value(unchecked((int)tick * 67 + player));
        private static IEnumerable<TestCaseData> RosterCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.NativeHost })
            foreach (int history in new[] { 256, 4096 })
            foreach (int players in new[] { 16, 63 })
            foreach (bool prepend in new[] { false, true })
                yield return new TestCaseData(backend, history, players, prepend).SetName($"H14_InputAdmission_{backend}_History{history}_Players{players}_{(prepend ? "Head" : "Tail")}");
        }
        [TestCaseSource(nameof(RosterCases)), Performance]
        public void AdmissionPreservesWrappedRowsAndSortedPlayerColumns(Backend backend, int history, int players, bool prepend)
        {
            long initial = history * 2L + 7, boundary = initial + 16;
            var ids = new PlayerId[players]; for (int i = 0; i < players; i++) ids[i] = new PlayerId((ulong)(10 + i * 2));
            var joining = new PlayerId(prepend ? 1UL : 1000UL);
            DeterministicInputHistory<int, Int32Policy> inputs = null;
            TrackingFactory factory = null; DeterministicStatus status = default; ulong digest = 0;
            Action setup = () =>
            {
                inputs?.Dispose(); factory = new TrackingFactory(backend);
                inputs = new DeterministicInputHistory<int, Int32Policy>(Context(backend, factory), players + 1, history, PredictionPolicy.HoldLast, 0, initialTick: initial);
                foreach (var id in ids) inputs.TryRegisterPlayer(id, initial).ThrowIfFailure();
                // A nonzero, wrapped starting tick exercises column/presence and historic roster relocation.
                for (long tick = initial; tick < initial + 32; tick++)
                    for (int p = 0; p < players; p++) inputs.TrySubmit(ids[p], tick, InputValue(p, tick), out _).ThrowIfFailure();
                for (long tick = initial; tick < boundary; tick++)
                { inputs.TryResolve(tick, false, out _).ThrowIfFailure(); inputs.TryAdvance(tick).ThrowIfFailure(); }
            };
            try
            {
                Benchmark("H14", "InputHistory.RegisterPlayer", backend, (prepend ? "Head" : "Tail") + "_Players" + players + "_WrappedRows",
                    history, 1, 0, () => status = inputs.TryRegisterPlayer(joining, boundary + 1),
                    () =>
                    {
                        status.ThrowIfFailure(); Assert.AreEqual(boundary, inputs.NextTick); Assert.AreEqual(initial, inputs.OldestTick);
                        Assert.AreEqual(players + 1, inputs.PlayerCount); Assert.AreEqual(boundary, inputs.CompleteThrough);
                        digest = DigestSeed;
                        for (int i = 0; i <= players; i++)
                        {
                            inputs.TryGetPlayer(i, out var id).ThrowIfFailure();
                            var expected = prepend ? (i == 0 ? joining : ids[i - 1]) : (i == players ? joining : ids[i]);
                            Assert.AreEqual(expected, id); digest = Mix(digest, id.Value);
                        }
                        // Every historic actual stays associated with its original player after the shift.
                        for (long tick = initial; tick < boundary; tick++)
                        {
                            inputs.TryGetActualFrame(tick, out var frame).ThrowIfFailure(); Assert.AreEqual(players, frame.Count);
                            for (int p = 0; p < players; p++)
                            {
                                var sample = frame[p]; Assert.AreEqual(ids[p], sample.Player); Assert.IsFalse(sample.Predicted);
                                int expected = InputValue(p, tick); if (sample.Value != expected) Assert.Fail("Historic input moved to the wrong player.");
                                digest = Mix(digest, unchecked((uint)sample.Value));
                            }
                        }
                        for (long tick = boundary; tick < initial + 32; tick++)
                        {
                            inputs.TryResolve(tick, true, out var frame).ThrowIfFailure(); bool joined = tick > boundary;
                            Assert.AreEqual(players + (joined ? 1 : 0), frame.Count);
                            for (int p = 0; p < players; p++)
                            {
                                int rank = p + (joined && prepend ? 1 : 0); var sample = frame[rank];
                                if (sample.Player != ids[p] || sample.Value != InputValue(p, tick) || sample.Predicted) Assert.Fail("Future actual input changed after admission.");
                                digest = Mix(digest, unchecked((uint)sample.Value));
                            }
                            if (joined)
                            {
                                var sample = frame[prepend ? 0 : players]; Assert.AreEqual(joining, sample.Player);
                                Assert.AreEqual(0, sample.Value); Assert.IsTrue(sample.Predicted);
                            }
                            inputs.TryAdvance(tick).ThrowIfFailure();
                        }
                    }, setup,
                    () => new Diagnostics { reservedArenaBytes = factory.Reserved, active = players + 1, historyRows = history * 2,
                        scanRows = history * 2, touchedItems = history * 2 * (prepend ? players + 1 : 1), payloadElementBytes = sizeof(int),
                        externalBufferBytes = (long)players * sizeof(PlayerId) }, () => digest);
            }
            finally { inputs?.Dispose(); }
        }

        public enum InputInvalidation { RewindOne, RewindHistory, ForgetEmpty, Forget32Future }
        private static IEnumerable<TestCaseData> InputInvalidationCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.NativeHost })
            foreach (int history in new[] { 256, 4096 })
            foreach (int players in new[] { 1, 64 })
            foreach (InputInvalidation operation in Enum.GetValues(typeof(InputInvalidation)))
                yield return new TestCaseData(backend, history, players, operation).SetName($"H15_InputInvalidation_{backend}_History{history}_Players{players}_{operation}");
        }
        private static ulong InputDigest(DeterministicInputHistory<int, Int32Policy> inputs, PlayerId[] ids, long first, int count)
        {
            ulong hash = DigestSeed;
            for (long tick = first; tick < first + count; tick++)
            {
                inputs.TryGetActualFrame(tick, out var frame).ThrowIfFailure(); Assert.AreEqual(ids.Length, frame.Count);
                for (int p = 0; p < ids.Length; p++)
                {
                    var sample = frame[p];
                    if (sample.Player != ids[p] || sample.Value != InputValue(p, tick) || sample.Predicted) Assert.Fail("Actual history differs after invalidation.");
                    hash = Mix(hash, unchecked((uint)sample.Value));
                }
            }
            return hash;
        }
        [TestCaseSource(nameof(InputInvalidationCases)), Performance]
        public void RewindAndOwnershipChangesPreserveActualHistory(Backend backend, int history, int players, InputInvalidation operation)
        {
            bool rewind = operation == InputInvalidation.RewindOne || operation == InputInvalidation.RewindHistory;
            bool future = operation == InputInvalidation.Forget32Future;
            long initial = history * 2L + 7, boundary = initial + history;
            long target = operation == InputInvalidation.RewindHistory ? initial + 1 : boundary - 1;
            var ids = new PlayerId[players]; for (int p = 0; p < players; p++) ids[p] = new PlayerId((ulong)p + 1);
            var factory = new TrackingFactory(backend);
            using var inputs = new DeterministicInputHistory<int, Int32Policy>(Context(backend, factory), players, history, PredictionPolicy.HoldLast, 0, initialTick: initial);
            foreach (var id in ids) inputs.TryRegisterPlayer(id, initial).ThrowIfFailure();
            for (long tick = initial; tick < boundary; tick++)
            {
                for (int p = 0; p < players; p++) inputs.TrySubmit(ids[p], tick, InputValue(p, tick), out _).ThrowIfFailure();
                inputs.TryResolve(tick, false, out _).ThrowIfFailure(); inputs.TryAdvance(tick).ThrowIfFailure();
            }
            Assert.AreEqual(initial, inputs.OldestTick);
            int status = 0; ulong digest = 0;
            Action setup = () =>
            {
                // Rewind keeps actuals; replay only resets the used flags and NextTick through public APIs.
                while (inputs.NextTick < boundary)
                { long tick = inputs.NextTick; inputs.TryResolve(tick, false, out _).ThrowIfFailure(); inputs.TryAdvance(tick).ThrowIfFailure(); }
                if (future) for (long tick = boundary; tick < boundary + 32; tick++)
                    for (int p = 0; p < players; p++) inputs.TrySubmit(ids[p], tick, InputValue(p, tick), out _).ThrowIfFailure();
            };
            Benchmark("H15", "InputHistory." + operation, backend, "Players" + players + "_WrappedRows", history,
                rewind ? 1 : players, 0,
                () =>
                {
                    status = 0;
                    if (rewind) status = (int)inputs.TryRewind(target);
                    else for (int p = 0; p < players; p++) status |= (int)inputs.TryForgetFutureInputs(ids[p], boundary);
                },
                () =>
                {
                    Assert.AreEqual(0, status); Assert.AreEqual(rewind ? target : boundary, inputs.NextTick);
                    Assert.AreEqual(initial, inputs.OldestTick);
                    digest = InputDigest(inputs, ids, initial, history);
                    if (rewind)
                    {
                        Assert.AreEqual(DeterministicStatus.ProtocolConflict, inputs.TryAdvance(target), "Rewound tick must be resolved again before advancing.");
                        inputs.TryResolve(target, false, out var frame).ThrowIfFailure(); Assert.AreEqual(players, frame.Count);
                        // Every retained actual is immutable, including the part rewound and the wrapped rows.
                        Assert.AreEqual(boundary - 1, inputs.CompleteThrough);
                    }
                    else
                    {
                        Assert.AreEqual(boundary - 1, inputs.CompleteThrough);
                        for (long tick = boundary; tick < boundary + 32; tick++)
                        {
                            Assert.AreEqual(DeterministicStatus.Pending, inputs.TryGetActualFrame(tick, out _));
                            for (int p = 0; p < players; p++)
                                Assert.AreEqual(DeterministicStatus.None, inputs.CanSubmit(ids[p], tick, ~InputValue(p, tick)), "Forgotten actual must admit a different value.");
                        }
                        inputs.TryResolve(boundary, true, out var predicted).ThrowIfFailure();
                        for (int p = 0; p < players; p++)
                        {
                            Assert.IsTrue(predicted[p].Predicted); Assert.AreEqual(InputValue(p, boundary - 1), predicted[p].Value);
                        }
                    }
                    digest = Mix(digest, (ulong)inputs.NextTick);
                }, setup,
                () => new Diagnostics { reservedArenaBytes = factory.Reserved, active = players, historyRows = history * 2,
                    scanRows = history * 2 * (rewind ? 1 : players), touchedItems = rewind ? (int)(boundary - target) * players : (future ? 32 * players : 0),
                    payloadElementBytes = sizeof(int), externalBufferBytes = (long)players * sizeof(PlayerId) }, () => digest);
        }
    }
}
