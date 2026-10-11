using System;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class ReplayArchiveTests
    {
        private sealed class Simulation : IDeterministicSimulation<int, int>
        {
            private readonly DeterministicCell<int> cell;
            internal Simulation(DeterministicCell<int> cell) { this.cell = cell; }
            public DeterministicStatus TryStep(in DeterministicInputFrame<int> inputs, DeterministicEventWriter<int> events)
            { cell.Value = unchecked(cell.Value * 17 + inputs[0].Value); return events.TryEmit(1, default, 0, cell.Value); }
        }
        private sealed class World : IDisposable
        {
            internal readonly DeterministicRuntimeContext Context = new DeterministicRuntimeContext(DeterministicBackend.Managed);
            internal readonly DeterministicCell<int> Cell = new DeterministicCell<int>();
            internal readonly DeterministicStateRegistry State;
            internal readonly DeterministicSimulationRunner<int, Int32Policy, int> Runner;
            internal World(long initial = 0, DeterministicReplayArchive<int, Int32Policy> archive = null, int checkpoint = 0)
            {
                State = new DeterministicStateRegistry(Context, new DeterministicCompatibilityProfile(1, 2, 3, 4, 5), 1);
                State.TryRegister(ManagedStateBindings.Cell(1, Cell, default(Int32Policy), 1, 128, 128)).ThrowIfFailure(); State.TryFreeze().ThrowIfFailure();
                if (archive != null) { archive.TryRestoreCheckpoint(State, checkpoint, out long tick).ThrowIfFailure(); Assert.AreEqual(initial, tick); }
                Runner = new DeterministicSimulationRunner<int, Int32Policy, int>(Context,
                    new DeterministicRuntimeOptions(1, PredictionPolicy.HoldLast, inputDelay: 0, historyTicks: 8, predictionTicks: 4, keyframeInterval: 4),
                    State, new Simulation(Cell), new[] { new PlayerId(1) }, 0, 2, initialTick: initial);
            }
            public void Dispose() { Runner.Dispose(); State.Dispose(); Cell.Dispose(); }
        }
        [Test]
        public void ReplayReloadAndCheckpointSeekReproduceWorldHashes()
        {
            using (var world = new World()) using (var archive = new DeterministicReplayArchive<int, Int32Policy>(world.Context, world.Runner.Fingerprint, 16384, 64, 1))
            using (var loaded = new DeterministicReplayArchive<int, Int32Policy>(world.Context, world.Runner.Fingerprint, 16384, 64, 1))
            {
                archive.TryBegin(world.State, 0).ThrowIfFailure(); var inputs = new DeterministicPlayerInput<int>[1];
                for (int tick = 0; tick < 20; tick++)
                {
                    int value = tick * 3 - 7; world.Runner.TrySubmitInput(new PlayerId(1), tick, value).ThrowIfFailure(); world.Runner.TryAdvance(1, out _).ThrowIfFailure();
                    world.Runner.History.TryGetHash(tick + 1, out ulong hash).ThrowIfFailure(); inputs[0] = new DeterministicPlayerInput<int>(new PlayerId(1), value);
                    archive.TryAppendInputs(tick, inputs, hash).ThrowIfFailure(); if ((tick + 1) % 4 == 0) archive.TryAppendCheckpoint(world.State, tick + 1).ThrowIfFailure();
                }
                byte* bytes = stackalloc byte[16384]; var writer = new DeterministicWriter(bytes, 16384); archive.TryWriteCanonical(ref writer).ThrowIfFailure();
                var reader = new DeterministicReader(bytes, writer.Position); loaded.TryReadCanonical(ref reader, world.State).ThrowIfFailure(); Assert.AreEqual(writer.Position, reader.Position);
                using (var replay = new World(0, loaded)) { loaded.TryReplayTo(replay.Runner, 20).ThrowIfFailure(); Assert.AreEqual(world.Cell.Value, replay.Cell.Value); }
                loaded.TryFindCheckpoint(13, out int checkpoint, out long boundary).ThrowIfFailure(); Assert.AreEqual(12, boundary);
                using (var seek = new World(boundary, loaded, checkpoint)) { loaded.TryReplayTo(seek.Runner, 20).ThrowIfFailure(); Assert.AreEqual(world.Cell.Value, seek.Cell.Value); }
            }
        }
        [Test]
        public void CorruptReplayDoesNotReplaceTheExistingArchiveOrAdvanceReader()
        {
            using (var world = new World()) using (var archive = new DeterministicReplayArchive<int, Int32Policy>(world.Context, world.Runner.Fingerprint, 2048, 8, 1))
            {
                archive.TryBegin(world.State, 0).ThrowIfFailure(); byte* bytes = stackalloc byte[2048]; var writer = new DeterministicWriter(bytes, 2048); archive.TryWriteCanonical(ref writer).ThrowIfFailure();
                bytes[writer.Position - 1] ^= 1; var reader = new DeterministicReader(bytes, writer.Position);
                Assert.AreEqual(DeterministicStatus.InvalidEncoding, archive.TryReadCanonical(ref reader, world.State)); Assert.AreEqual(0, reader.Position); Assert.AreEqual(1, archive.Count);
                archive.TryRestoreCheckpoint(world.State, 0, out long tick).ThrowIfFailure(); Assert.AreEqual(0, tick);
            }
        }
        [Test]
        public void ReplayDetectsDivergenceAtTheRecordedTick()
        {
            using (var world = new World()) using (var archive = new DeterministicReplayArchive<int, Int32Policy>(world.Context, world.Runner.Fingerprint, 2048, 8, 1))
            {
                archive.TryBegin(world.State, 0).ThrowIfFailure(); archive.TryAppendInputs(0, new[] { new DeterministicPlayerInput<int>(new PlayerId(1), 4) }, 123).ThrowIfFailure();
                Assert.AreEqual(DeterministicStatus.ProtocolConflict, archive.TryReplayTo(world.Runner, 1));
            }
        }
    }
}
