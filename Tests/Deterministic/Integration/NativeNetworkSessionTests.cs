using System;
using LegendaryTools.Deterministic.Samples;
using NUnit.Framework;
using Unity.Collections;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class NativeNetworkSessionTests
    {
        [TestCase(SnapshotPolicy.FullPerTick)] [TestCase(SnapshotPolicy.KeyframesAndDeltas)] [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void ManagedAndNativeSessionsAgreeWhileNativeStepsExecuteInBurst(SnapshotPolicy snapshots)
        {
            var managed = new DeterministicRuntimeContext(DeterministicBackend.Managed);
            var native = new DeterministicRuntimeContext(DeterministicBackend.BurstCompiler, new NativeDeterministicMemoryFactory());
            var voters = new[] { new PeerId(new PlayerId(1), 1), new PeerId(new PlayerId(2), 1) };
            var options = new DeterministicRuntimeOptions(2, PredictionPolicy.HoldLast, snapshots, inputDelay: 0, historyTicks: 128, pageBytes: 64);
            var profile = new DeterministicCompatibilityProfile(1, 2, 3, 4, 5);
            using (var gcValue = new DeterministicCell<int>())
            using (var nativeValue = new NativeDeterministicCell<int>(Allocator.Persistent))
            using (var leftState = new DeterministicStateRegistry(managed, profile, 1))
            using (var rightState = new DeterministicStateRegistry(native, profile, 1))
            using (var simulation = new NativeCounterSimulation(nativeValue, 2))
            using (var network = new DeterministicLoopbackNetwork(managed, 2, 8192, 2048, new LoopbackFaults(0, 20, 30, 200, 752)))
            using (var a = network.CreateEndpoint(voters[0])) using (var b = network.CreateEndpoint(voters[1]))
            {
                leftState.TryRegister(ManagedStateBindings.Cell(1, gcValue, default(Int32Policy), 1, 128, 128)).ThrowIfFailure(); leftState.TryFreeze().ThrowIfFailure();
                rightState.TryRegister(NativeStateBindings.Cell(1, nativeValue, default(Int32Policy), 1, 128, 128)).ThrowIfFailure(); rightState.TryFreeze().ThrowIfFailure();
                var raft = new DeterministicRaftOptions(4, 128, 1024, 65536);
                using (var left = new DeterministicNetworkSession<int, Int32Policy, int, Int32Policy>(managed, options, raft, a, new DeterministicSessionId(1),
                    leftState, new CounterSimulation(gcValue), voters, DeterministicSessionTopology.PeerToPeer, 0, 4, 4))
                using (var right = new DeterministicNetworkSession<int, Int32Policy, int, Int32Policy>(native, options, raft, b, new DeterministicSessionId(1),
                    rightState, simulation, voters, DeterministicSessionTopology.PeerToPeer, 0, 4, 4))
                {
                    Assert.AreEqual(left.Fingerprint, right.Fingerprint);
                    for (int tick = 0; tick < 90; tick++) { left.TrySubmitLocalInput(tick, tick + 1).ThrowIfFailure(); right.TrySubmitLocalInput(tick, tick * 3).ThrowIfFailure(); }
                    left.Raft.TryStartElection().ThrowIfFailure();
                    for (int step = 0; step < 1200; step++)
                    {
                        long time = step * 10; network.TryAdvanceTime(time).ThrowIfFailure();
                        var x = left.TryPump(time); var y = right.TryPump(time);
                        Assert.IsTrue(x.IsSuccess() || x == DeterministicStatus.Pending || x == DeterministicStatus.QuorumLost);
                        Assert.IsTrue(y.IsSuccess() || y == DeterministicStatus.Pending || y == DeterministicStatus.QuorumLost);
                    }
                    Assert.IsTrue(simulation.LastStepUsedBurst); Assert.AreEqual(90, left.CommittedBoundary); Assert.AreEqual(90, right.CommittedBoundary);
                    left.Runner.History.TryGetHash(90, out ulong xHash).ThrowIfFailure(); right.Runner.History.TryGetHash(90, out ulong yHash).ThrowIfFailure(); Assert.AreEqual(xHash, yHash);
                    Assert.IsFalse(left.IsFaulted); Assert.IsFalse(right.IsFaulted);
                    Assert.Greater(left.Raft.SnapshotIndex, 0); Assert.Greater(right.Raft.SnapshotIndex, 0);
                }
            }
        }
    }
}
