using System;
using LegendaryTools.Deterministic.Samples;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class GraphRuntimeTests
    {
        private sealed class Simulation : IDeterministicSimulation<int, int>
        {
            internal DeterministicGraph<int, int> Graph;
            internal GraphNodeHandle A, B;
            internal bool FailNext;
            public DeterministicStatus TryStep(in DeterministicInputFrame<int> frame, DeterministicEventWriter<int> events)
            {
                var s = Graph.TryGetNode(A, out int value); if (!s.IsSuccess()) return s;
                for (int i = 0; i < frame.Count; i++) value = GraphSimulationTransition<DeterministicGraph<int, int>>.Fold(value, frame[i].Value, frame[i].Player);
                s = GraphSimulationTransition<DeterministicGraph<int, int>>.TryApply(ref Graph, A, B, value); if (!s.IsSuccess()) return s;
                s = events.TryEmit(1, A.Value, 0, value);
                if (FailNext) { FailNext = false; return DeterministicStatus.CapacityExceeded; } return s;
            }
        }
        private sealed class World : IDisposable
        {
            internal readonly DeterministicRuntimeContext Context = new DeterministicRuntimeContext(DeterministicBackend.Managed);
            internal readonly DeterministicGraph<int, int> Graph;
            internal readonly DeterministicStateRegistry State;
            internal readonly DeterministicSimulationRunner<int, Int32Policy, int> Runner;
            internal readonly Simulation Simulation;
            internal World(SnapshotPolicy policy, DeterministicRunnerMode mode, long boundary = 0, DeterministicReplayArchive<int, Int32Policy> archive = null, int checkpoint = 0)
            {
                Graph = new DeterministicGraph<int, int>(new GraphOptions(new BufferOptions(4, snapshotSlots: 0), new BufferOptions(4, snapshotSlots: 0)));
                var a = Graph.AddNode(0); var b = Graph.AddNode(0); Simulation = new Simulation { Graph = Graph, A = a, B = b };
                State = new DeterministicStateRegistry(Context, new DeterministicCompatibilityProfile(1, 0, 0, default(Int32Policy).SchemaId, 1), 1);
                State.TryRegister(ManagedStateBindings.Graph(1, Graph, default(Int32Policy), default(Int32Policy), 1, 4096, 8192)).ThrowIfFailure(); State.TryFreeze().ThrowIfFailure();
                if (archive != null) archive.TryRestoreCheckpoint(State, checkpoint, out boundary).ThrowIfFailure();
                var options = new DeterministicRuntimeOptions(2, PredictionPolicy.HoldLast, snapshots: policy, inputDelay: 0, historyTicks: 32, predictionTicks: 8, keyframeInterval: 8, pageBytes: 64);
                Runner = new DeterministicSimulationRunner<int, Int32Policy, int>(Context, options, State, Simulation, new[] { new PlayerId(1), new PlayerId(2) }, 0, 4, mode: mode, initialTick: boundary);
            }
            public void Dispose() { Runner.Dispose(); State.Dispose(); Graph.Dispose(); }
        }
        private static byte[] Encode(DeterministicGraph<int, int> graph)
        {
            graph.TryMeasureCanonical(default(Int32Policy), default(Int32Policy), out int length).ThrowIfFailure(); var bytes = new byte[length];
            fixed (byte* p = bytes) { var writer = new DeterministicWriter(p, length); graph.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)).ThrowIfFailure(); } return bytes;
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void GraphTopologyAndFutureIdsConvergeAfterRollbackAgainstLockstep(SnapshotPolicy policy)
        {
            using var delayed = new World(policy, DeterministicRunnerMode.Rollback); using var ordered = new World(policy, DeterministicRunnerMode.Lockstep);
            for (int tick = 0; tick < 48; tick++)
            {
                ordered.Runner.TrySubmitInput(new PlayerId(1), tick, tick + 1).ThrowIfFailure(); ordered.Runner.TrySubmitInput(new PlayerId(2), tick, tick * 7 - 3).ThrowIfFailure(); ordered.Runner.TryAdvance(1, out _).ThrowIfFailure();
                delayed.Runner.TrySubmitInput(new PlayerId(1), tick, tick + 1).ThrowIfFailure();
                if (tick >= 3) delayed.Runner.TrySubmitInput(new PlayerId(2), tick - 3, (tick - 3) * 7 - 3).ThrowIfFailure(); delayed.Runner.TryAdvance(1, out _).ThrowIfFailure();
            }
            for (int tick = 45; tick < 48; tick++) delayed.Runner.TrySubmitInput(new PlayerId(2), tick, tick * 7 - 3).ThrowIfFailure();
            delayed.Runner.TrySynchronizeRollback().ThrowIfFailure(); Assert.Greater(delayed.Runner.RollbackCount, 0);
            CollectionAssert.AreEqual(Encode(ordered.Graph), Encode(delayed.Graph));
            ordered.Runner.History.TryGetHash(48, out ulong a).ThrowIfFailure(); delayed.Runner.History.TryGetHash(48, out ulong b).ThrowIfFailure(); Assert.AreEqual(a, b);
            Assert.AreEqual(ordered.Graph.AddNode(99), delayed.Graph.AddNode(99));
        }
        [Test]
        public void GraphReplaySeekAndFailedTickRestoreTheRegisteredState()
        {
            using var world = new World(SnapshotPolicy.KeyframesAndDeltas, DeterministicRunnerMode.Lockstep);
            using var archive = new DeterministicReplayArchive<int, Int32Policy>(world.Context, world.Runner.Fingerprint, 65536, 64, 2);
            archive.TryBegin(world.State, 0).ThrowIfFailure(); var inputs = new DeterministicPlayerInput<int>[2];
            for (int tick = 0; tick < 20; tick++)
            {
                world.Runner.TrySubmitInput(new PlayerId(1), tick, tick + 1).ThrowIfFailure(); world.Runner.TrySubmitInput(new PlayerId(2), tick, tick * 3).ThrowIfFailure(); world.Runner.TryAdvance(1, out _).ThrowIfFailure();
                world.Runner.History.TryGetHash(tick + 1, out ulong hash).ThrowIfFailure(); inputs[0] = new DeterministicPlayerInput<int>(new PlayerId(1), tick + 1); inputs[1] = new DeterministicPlayerInput<int>(new PlayerId(2), tick * 3);
                archive.TryAppendInputs(tick, inputs, hash).ThrowIfFailure(); if ((tick + 1) % 4 == 0) archive.TryAppendCheckpoint(world.State, tick + 1).ThrowIfFailure();
            }
            archive.TryFindCheckpoint(13, out int checkpoint, out long boundary).ThrowIfFailure();
            using var seek = new World(SnapshotPolicy.KeyframesAndDeltas, DeterministicRunnerMode.Lockstep, boundary, archive, checkpoint);
            archive.TryReplayTo(seek.Runner, 20).ThrowIfFailure(); CollectionAssert.AreEqual(Encode(world.Graph), Encode(seek.Graph));
            var original = Encode(world.Graph); world.Simulation.FailNext = true;
            world.Runner.TrySubmitInput(new PlayerId(1), 20, 4).ThrowIfFailure(); world.Runner.TrySubmitInput(new PlayerId(2), 20, 6).ThrowIfFailure();
            Assert.AreEqual(DeterministicStatus.CapacityExceeded, world.Runner.TryAdvance(1, out _)); Assert.AreEqual(20, world.Runner.NextTick); CollectionAssert.AreEqual(original, Encode(world.Graph));
        }
        [Test]
        public void PersistentBindingSynchronizesOnlyTheSelectedRoot()
        {
            var options = new GraphOptions(3, 4); var context = new DeterministicRuntimeContext(DeterministicBackend.Managed); var profile = new DeterministicCompatibilityProfile(1, 0, 0, default(Int32Policy).SchemaId, 1);
            using var graph = new DeterministicGraph<int, int>(options); using var versions = new PersistentDeterministicGraph<int, int>(options, new GraphPersistenceOptions(4));
            var node = graph.AddNode(10); versions.TryCaptureCurrent(out var empty).ThrowIfFailure(); versions.TryWithAddNode(empty, 10, out var selected, out var versionNode).ThrowIfFailure(); versions.TrySelectCurrent(selected).ThrowIfFailure();
            using var left = new DeterministicStateRegistry(context, profile, 1); using var right = new DeterministicStateRegistry(context, profile, 1);
            left.TryRegister(ManagedStateBindings.Graph(1, graph, default(Int32Policy), default(Int32Policy), 1, 4096, 8192)).ThrowIfFailure();
            right.TryRegister(ManagedStateBindings.PersistentGraph(1, versions, default(Int32Policy), default(Int32Policy), 1, 4096, 8192)).ThrowIfFailure(); left.TryFreeze().ThrowIfFailure(); right.TryFreeze().ThrowIfFailure(); Assert.AreEqual(left.Fingerprint, right.Fingerprint);
            versions.TryWithSetNode(selected, versionNode, 99, out var unselected).ThrowIfFailure(); left.TryEncode(0, out byte* bytes, out int length).ThrowIfFailure();
            right.TryEncode(0, out byte* peer, out int peerLength).ThrowIfFailure(); Assert.AreEqual(length, peerLength); Assert.AreEqual(DeterministicHash.Xxh64(bytes, length), DeterministicHash.Xxh64(peer, peerLength));
            graph.TrySetNode(node, 50).ThrowIfFailure(); left.TryEncode(1, out bytes, out length).ThrowIfFailure(); var reader = new DeterministicReader(bytes, length); right.TryReadCanonical(ref reader, out long boundary).ThrowIfFailure(); Assert.AreEqual(1, boundary);
            versions.Current.TryGetNode(node, out int current).ThrowIfFailure(); Assert.AreEqual(50, current); unselected.AsReadOnly().TryGetNode(node, out current).ThrowIfFailure(); Assert.AreEqual(99, current);
            empty.Dispose(); selected.Dispose(); unselected.Dispose();
        }
    }
}
