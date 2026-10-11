using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class RuntimeDeterminismStressTests
    {
        private sealed class Simulation : IDeterministicSimulation<int, int>
        {
            internal readonly DeterministicList<int> Values;
            internal readonly DeterministicDictionary<int, int, Int32Policy> Map;
            internal readonly DeterministicSortedDictionary<int, int, Int32Policy> Tree;
            internal readonly DeterministicCell<ulong> Random;
            internal Simulation(DeterministicList<int> values, DeterministicDictionary<int, int, Int32Policy> map,
                DeterministicSortedDictionary<int, int, Int32Policy> tree, DeterministicCell<ulong> random)
            { Values = values; Map = map; Tree = tree; Random = random; }
            public DeterministicStatus TryStep(in DeterministicInputFrame<int> frame, DeterministicEventWriter<int> events)
            {
                ulong random = Random.Value; random ^= random << 13; random ^= random >> 7; random ^= random << 17; Random.Value = random;
                for (int i = 0; i < frame.Count; i++)
                {
                    int key = (int)((frame.Tick + (long)frame[i].Player.Value) % 16);
                    int value = unchecked((int)random + frame[i].Value * 31 + key);
                    if (Values.Count == 16) Values.RemoveAt(0); Values.Add(value);
                    Map[key] = value; Tree[key] = value;
                    if (frame.Tick % 5 == 0) { Map.Remove((key + 7) % 16); Tree.Remove((key + 7) % 16); }
                }
                return events.TryEmit(1, default, 0, Values[Values.Count - 1]);
            }
        }
        private sealed class Sink : IDeterministicEventSink<int>
        {
            internal readonly Dictionary<long, int> Confirmed = new Dictionary<long, int>();
            internal int Retracted;
            public void OnEvent(DeterministicEventDelivery delivery, in DeterministicEvent<int> value)
            {
                if (delivery == DeterministicEventDelivery.Confirmed) Confirmed.Add(value.Id.Tick, value.Payload);
                if (delivery == DeterministicEventDelivery.Retracted) Retracted++;
            }
        }
        private sealed class World : IDisposable
        {
            internal readonly DeterministicList<int> Values;
            internal readonly DeterministicDictionary<int, int, Int32Policy> Map;
            internal readonly DeterministicSortedDictionary<int, int, Int32Policy> Tree;
            internal readonly DeterministicCell<ulong> Random;
            internal readonly DeterministicStateRegistry State;
            internal readonly DeterministicSimulationRunner<int, Int32Policy, int> Runner;
            internal World(SnapshotPolicy snapshots, PredictionPolicy prediction, bool reverse, DeterministicRunnerMode mode, Sink sink, uint seed)
            {
                var context = new DeterministicRuntimeContext(DeterministicBackend.Managed);
                var options = new BufferOptions(16, snapshots: snapshots, snapshotSlots: 0, pageBytes: 64);
                Values = new DeterministicList<int>(options); Map = new DeterministicDictionary<int, int, Int32Policy>(options);
                Tree = new DeterministicSortedDictionary<int, int, Int32Policy>(options); Random = new DeterministicCell<ulong>(); Random.Value = seed;
                State = new DeterministicStateRegistry(context, new DeterministicCompatibilityProfile(101, 0, 2, 3, 4), 4);
                var bindings = new DeterministicStateBinding[] { ManagedStateBindings.List(1, Values, default(Int32Policy), 1, 2048, 8192),
                    ManagedStateBindings.Dictionary(2, Map, default(Int32Policy), default(Int32Policy), 2, 4096, 8192),
                    ManagedStateBindings.SortedDictionary(3, Tree, default(Int32Policy), default(Int32Policy), 3, 4096, 8192),
                    ManagedStateBindings.Cell(4, Random, default(UInt64Policy), 4, 128, 128) };
                for (int i = 0; i < bindings.Length; i++) State.TryRegister(bindings[reverse ? bindings.Length - 1 - i : i]).ThrowIfFailure();
                State.TryFreeze().ThrowIfFailure();
                var runtime = new DeterministicRuntimeOptions(3, prediction, snapshots, inputDelay: 0, historyTicks: 64, predictionTicks: 8, keyframeInterval: 8, pageBytes: 64);
                Runner = new DeterministicSimulationRunner<int, Int32Policy, int>(context, runtime, State,
                    new Simulation(Values, Map, Tree, Random), reverse ? new[] { new PlayerId(9), new PlayerId(3), new PlayerId(1) } : Players, 0, 2,
                    mode: mode, eventSink: sink);
            }
            public void Dispose() { Runner.Dispose(); State.Dispose(); Random.Dispose(); Tree.Dispose(); Map.Dispose(); Values.Dispose(); }
        }
        private static readonly PlayerId[] Players = { new PlayerId(1), new PlayerId(3), new PlayerId(9) };
        private static uint Next(ref uint seed) { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return seed; }
        private static IEnumerable<TestCaseData> Cases()
        {
            foreach (uint seed in new uint[] { 1, 537, 0x87654321, 0xffffffff })
            foreach (SnapshotPolicy snapshots in Enum.GetValues(typeof(SnapshotPolicy)))
            foreach (PredictionPolicy prediction in Enum.GetValues(typeof(PredictionPolicy)))
                yield return new TestCaseData(seed, snapshots, prediction);
        }
        [TestCaseSource(nameof(Cases))]
        public void ShuffledDelayedDuplicateInputsAndRegistryOrderConvergeAfterLongRollback(uint seed, SnapshotPolicy snapshots, PredictionPolicy prediction)
        {
            const int ticks = 320; var actual = new int[ticks, 3]; var arrival = new int[ticks, 3]; uint schedule = seed;
            for (int tick = 0; tick < ticks; tick++) for (int player = 0; player < 3; player++)
            { actual[tick, player] = unchecked((int)Next(ref schedule)); arrival[tick, player] = tick + (player == 0 ? 0 : (int)(Next(ref schedule) % 8)); }
            var expected = new Sink(); var delayed = new Sink();
            using (var ordered = new World(SnapshotPolicy.FullPerTick, prediction, false, DeterministicRunnerMode.Lockstep, expected, seed))
            using (var rollback = new World(snapshots, prediction, true, DeterministicRunnerMode.Rollback, delayed, seed))
            {
                Assert.AreEqual(ordered.Runner.Fingerprint, rollback.Runner.Fingerprint);
                for (int wall = 0; wall < ticks + 8; wall++)
                {
                    if (wall < ticks)
                    {
                        for (int player = 0; player < 3; player++) ordered.Runner.TrySubmitInput(Players[player], wall, actual[wall, player]).ThrowIfFailure();
                        ordered.Runner.TryAdvance(1, out int advanced).ThrowIfFailure(); Assert.AreEqual(1, advanced);
                    }
                    // Reverse player/tick delivery order, independent of the simulation order.
                    for (int tick = Math.Min(wall, ticks - 1); tick >= Math.Max(0, wall - 7); tick--)
                    for (int player = 2; player >= 0; player--) if (arrival[tick, player] == wall)
                    {
                        rollback.Runner.TrySubmitInput(Players[player], tick, actual[tick, player]).ThrowIfFailure();
                        rollback.Runner.TrySubmitInput(Players[player], tick, actual[tick, player]).ThrowIfFailure();
                    }
                    if (wall < ticks) { rollback.Runner.TryAdvance(1, out int advanced).ThrowIfFailure(); Assert.AreEqual(1, advanced); }
                    else rollback.Runner.TrySynchronizeRollback().ThrowIfFailure();
                }
                Assert.AreEqual(ticks, rollback.Runner.NextTick); Assert.AreEqual(ticks - 1, rollback.Runner.ConfirmedThrough);
                Assert.Greater(rollback.Runner.RollbackCount, 0); Assert.Greater(delayed.Retracted, 0);
                Assert.AreEqual(ticks, delayed.Confirmed.Count);
                for (int tick = 0; tick < ticks; tick++) Assert.AreEqual(expected.Confirmed[tick], delayed.Confirmed[tick], "event tick " + tick);
                ordered.State.TryEncode(ticks, out byte* a, out int aLength).ThrowIfFailure(); rollback.State.TryEncode(ticks, out byte* b, out int bLength).ThrowIfFailure();
                Assert.AreEqual(aLength, bLength); for (int i = 0; i < aLength; i++) Assert.AreEqual(a[i], b[i], "byte " + i);
                Assert.AreEqual(ordered.Random.Value, rollback.Random.Value);
            }
        }
    }
}
