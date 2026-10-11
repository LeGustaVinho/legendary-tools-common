using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class SimulationRunnerTests
    {
        private sealed class Simulation : IDeterministicSimulation<int, int>
        {
            internal readonly DeterministicCell<int> Value;
            internal bool FailNext;
            internal Simulation(DeterministicCell<int> value) { Value = value; }
            public DeterministicStatus TryStep(in DeterministicInputFrame<int> inputs, DeterministicEventWriter<int> events)
            {
                int value = Value.Value;
                for (int i = 0; i < inputs.Count; i++) value = unchecked(value * 31 + inputs[i].Value * (int)inputs[i].Player.Value);
                Value.Value = value; var status = events.TryEmit(1, default, 0, value);
                if (FailNext) { FailNext = false; return DeterministicStatus.CapacityExceeded; } return status;
            }
        }
        private sealed class Sink : IDeterministicEventSink<int>
        {
            internal readonly List<(DeterministicEventDelivery Delivery, DeterministicEvent<int> Event)> Deliveries = new List<(DeterministicEventDelivery, DeterministicEvent<int>)>();
            public void OnEvent(DeterministicEventDelivery delivery, in DeterministicEvent<int> value) => Deliveries.Add((delivery, value));
        }
        private sealed class World : IDisposable
        {
            internal readonly DeterministicCell<int> Value = new DeterministicCell<int>();
            internal readonly DeterministicStateRegistry State;
            internal readonly DeterministicSimulationRunner<int, Int32Policy, int> Runner;
            internal readonly Simulation Simulation;
            internal World(SnapshotPolicy snapshots = SnapshotPolicy.KeyframesAndDeltas, PredictionPolicy prediction = PredictionPolicy.HoldLast,
                DeterministicRunnerMode mode = DeterministicRunnerMode.Rollback, DeterministicConfirmationMode confirmation = DeterministicConfirmationMode.InputCompletion, Sink sink = null)
            {
                var context = new DeterministicRuntimeContext(DeterministicBackend.Managed);
                State = new DeterministicStateRegistry(context, new DeterministicCompatibilityProfile(1, 2, 3, 4, 5), 1);
                State.TryRegister(ManagedStateBindings.Cell(1, Value, default(Int32Policy), 1, 128, 128)).ThrowIfFailure(); State.TryFreeze().ThrowIfFailure();
                Simulation = new Simulation(Value);
                var options = new DeterministicRuntimeOptions(2, prediction, snapshots, inputDelay: 0, historyTicks: 32, predictionTicks: 8, keyframeInterval: 8, pageBytes: 64);
                Runner = new DeterministicSimulationRunner<int, Int32Policy, int>(context, options, State, Simulation,
                    new[] { new PlayerId(2), new PlayerId(1) }, 0, 4, mode: mode, confirmation: confirmation, eventSink: sink);
            }
            public void Dispose() { Runner.Dispose(); State.Dispose(); Value.Dispose(); }
        }
        private static int Remote(long tick) => (int)(tick * 7 % 13) - 4;
        [TestCase(SnapshotPolicy.FullPerTick, PredictionPolicy.HoldLast)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas, PredictionPolicy.HoldLast)]
        [TestCase(SnapshotPolicy.CopyOnWrite, PredictionPolicy.HoldLast)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas, PredictionPolicy.Neutral)]
        public void DelayedInputsRollbackToTheSameWorldAsOrderedLockstep(SnapshotPolicy snapshots, PredictionPolicy prediction)
        {
            var sink = new Sink();
            using (var delayed = new World(snapshots, prediction, sink: sink)) using (var ordered = new World(snapshots, prediction, DeterministicRunnerMode.Lockstep))
            {
                for (int tick = 0; tick < 80; tick++)
                {
                    ordered.Runner.TrySubmitInput(new PlayerId(1), tick, tick + 1).ThrowIfFailure(); ordered.Runner.TrySubmitInput(new PlayerId(2), tick, Remote(tick)).ThrowIfFailure();
                    ordered.Runner.TryAdvance(1, out int baseline).ThrowIfFailure(); Assert.AreEqual(1, baseline);
                    delayed.Runner.TrySubmitInput(new PlayerId(1), tick, tick + 1).ThrowIfFailure();
                    if (tick >= 4) delayed.Runner.TrySubmitInput(new PlayerId(2), tick - 4, Remote(tick - 4)).ThrowIfFailure();
                    delayed.Runner.TryAdvance(1, out int advanced).ThrowIfFailure(); Assert.AreEqual(1, advanced);
                }
                for (int tick = 76; tick < 80; tick++) delayed.Runner.TrySubmitInput(new PlayerId(2), tick, Remote(tick)).ThrowIfFailure();
                delayed.Runner.TrySynchronizeRollback().ThrowIfFailure(); Assert.AreEqual(80, delayed.Runner.NextTick); Assert.AreEqual(79, delayed.Runner.ConfirmedThrough);
                Assert.AreEqual(ordered.Value.Value, delayed.Value.Value); Assert.Greater(delayed.Runner.RollbackCount, 0); Assert.Greater(delayed.Runner.ReplayedTicks, 0);
                ordered.Runner.History.TryGetHash(80, out ulong a).ThrowIfFailure(); delayed.Runner.History.TryGetHash(80, out ulong b).ThrowIfFailure(); Assert.AreEqual(a, b);
                var confirmed = new HashSet<long>(); int retracted = 0;
                foreach (var delivery in sink.Deliveries)
                    if (delivery.Delivery == DeterministicEventDelivery.Confirmed) Assert.IsTrue(confirmed.Add(delivery.Event.Id.Tick));
                    else if (delivery.Delivery == DeterministicEventDelivery.Retracted) retracted++;
                Assert.AreEqual(80, confirmed.Count); Assert.Greater(retracted, 0);
            }
        }
        [Test]
        public void LockstepWaitsForEveryInputAndExternalCommit()
        {
            using (var world = new World(mode: DeterministicRunnerMode.Lockstep, confirmation: DeterministicConfirmationMode.ExternalCommit))
            {
                Assert.AreEqual(DeterministicStatus.Pending, world.Runner.TryAdvance(1, out int count)); Assert.AreEqual(0, count); Assert.AreEqual(0, world.Value.Value);
                world.Runner.TrySubmitInput(new PlayerId(1), 0, 3); world.Runner.TrySubmitInput(new PlayerId(2), 0, 4);
                Assert.AreEqual(DeterministicStatus.Pending, world.Runner.TryAdvance(1, out _));
                world.Runner.TryCommitInputsThrough(0).ThrowIfFailure(); world.Runner.TryAdvance(1, out count).ThrowIfFailure();
                Assert.AreEqual(1, count); Assert.AreEqual(0, world.Runner.ConfirmedThrough); Assert.AreEqual(101, world.Value.Value);
                world.Runner.SetSuspension(DeterministicStatus.QuorumLost); Assert.AreEqual(DeterministicStatus.QuorumLost, world.Runner.TryAdvance(1, out count)); Assert.AreEqual(0, count);
            }
        }
        [Test]
        public void PredictionStopsAtItsBoundUntilCommittedInputsArrive()
        {
            using (var world = new World(confirmation: DeterministicConfirmationMode.ExternalCommit))
            {
                Assert.AreEqual(DeterministicStatus.Pending, world.Runner.TryAdvance(12, out int count)); Assert.AreEqual(8, count); Assert.AreEqual(8, world.Runner.NextTick);
                Assert.AreEqual(-1, world.Runner.ConfirmedThrough);
                for (int tick = 0; tick < 8; tick++) { world.Runner.TrySubmitInput(new PlayerId(1), tick, 1).ThrowIfFailure(); world.Runner.TrySubmitInput(new PlayerId(2), tick, 2).ThrowIfFailure(); }
                Assert.AreEqual(DeterministicStatus.Pending, world.Runner.TryCommitInputsThrough(7));
                world.Runner.TrySynchronizeRollback().ThrowIfFailure(); Assert.AreEqual(7, world.Runner.ConfirmedThrough); world.Runner.TryAdvance(1, out count).ThrowIfFailure(); Assert.AreEqual(1, count);
            }
        }
        [Test]
        public void FailedTickRestoresTheWorldAndNeverPublishesEvents()
        {
            var sink = new Sink(); using (var world = new World(sink: sink))
            {
                world.Runner.TrySubmitInput(new PlayerId(1), 0, 4); world.Runner.TrySubmitInput(new PlayerId(2), 0, 8); world.Simulation.FailNext = true;
                Assert.AreEqual(DeterministicStatus.CapacityExceeded, world.Runner.TryAdvance(1, out int count));
                Assert.AreEqual(0, count); Assert.AreEqual(0, world.Runner.NextTick); Assert.AreEqual(0, world.Value.Value); Assert.AreEqual(0, sink.Deliveries.Count);
                world.Runner.TryAdvance(1, out count).ThrowIfFailure(); Assert.AreEqual(1, count); Assert.AreEqual(140, world.Value.Value); Assert.AreEqual(2, sink.Deliveries.Count);
            }
        }
        [Test]
        public void SteadySimulationAndCheckpointHistoryAllocateNoGc()
        {
            using (var world = new World(SnapshotPolicy.CopyOnWrite))
            {
                for (int tick = 0; tick < 35; tick++) { world.Runner.TrySubmitInput(new PlayerId(1), tick, tick); world.Runner.TrySubmitInput(new PlayerId(2), tick, tick); world.Runner.TryAdvance(1, out _).ThrowIfFailure(); }
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int tick = 35; tick < 250; tick++) { world.Runner.TrySubmitInput(new PlayerId(1), tick, tick); world.Runner.TrySubmitInput(new PlayerId(2), tick, tick); world.Runner.TryAdvance(1, out _); }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before; Assert.AreEqual(0, allocated); Assert.AreEqual(250, world.Runner.NextTick);
            }
        }
    }
}
