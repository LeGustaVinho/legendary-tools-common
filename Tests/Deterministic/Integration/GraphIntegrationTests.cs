using LegendaryTools.Deterministic.Samples;
using LegendaryTools.FixedPoint;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class GraphIntegrationTests
    {
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void ManagedAndBurstGraphPeersConvergeThroughNetworkSessions(SnapshotPolicy snapshots)
        {
            var managed = new DeterministicRuntimeContext(DeterministicBackend.Managed);
            var native = new DeterministicRuntimeContext(DeterministicBackend.BurstCompiler, new NativeDeterministicMemoryFactory());
            var graphOptions = new GraphOptions(new BufferOptions(4, snapshotSlots: 0), new BufferOptions(4, snapshotSlots: 0));
            using var leftGraph = new DeterministicGraph<int, int>(graphOptions); using var rightGraph = new NativeDeterministicGraph<int, int>(graphOptions, Allocator.Persistent);
            var a = leftGraph.AddNode(0); var b = leftGraph.AddNode(0); var na = rightGraph.AddNode(0); var nb = rightGraph.AddNode(0);
            var profile = new DeterministicCompatibilityProfile(1, 0, 0, default(Int32Policy).SchemaId, 1);
            using var leftState = new DeterministicStateRegistry(managed, profile, 1); using var rightState = new DeterministicStateRegistry(native, profile, 1);
            leftState.TryRegister(ManagedStateBindings.Graph(1, leftGraph, default(Int32Policy), default(Int32Policy), 1, 4096, 8192)).ThrowIfFailure(); leftState.TryFreeze().ThrowIfFailure();
            rightState.TryRegister(NativeStateBindings.Graph(1, rightGraph, default(Int32Policy), default(Int32Policy), 1, 4096, 8192)).ThrowIfFailure(); rightState.TryFreeze().ThrowIfFailure();
            using var simulation = new NativeGraphSimulation(rightGraph, na, nb, 2);
            using var network = new DeterministicLoopbackNetwork(managed, 2, 8192, 2048, new LoopbackFaults(0, 20, 30, 200, 752));
            var voters = new[] { new PeerId(new PlayerId(1), 1), new PeerId(new PlayerId(2), 1) };
            using var first = network.CreateEndpoint(voters[0]); using var second = network.CreateEndpoint(voters[1]);
            var options = new DeterministicRuntimeOptions(2, PredictionPolicy.HoldLast, snapshots, inputDelay: 0, historyTicks: 128, pageBytes: 64);
            var raft = new DeterministicRaftOptions(4, 128, 1024, 65536);
            using var left = new DeterministicNetworkSession<int, Int32Policy, int, Int32Policy>(managed, options, raft, first,
                new DeterministicSessionId(44), leftState, new GraphSimulation(leftGraph, a, b), voters, DeterministicSessionTopology.PeerToPeer, 0, 4, 4);
            using var right = new DeterministicNetworkSession<int, Int32Policy, int, Int32Policy>(native, options, raft, second,
                new DeterministicSessionId(44), rightState, simulation, voters, DeterministicSessionTopology.PeerToPeer, 0, 4, 4);
            Assert.AreEqual(left.Fingerprint, right.Fingerprint);
            for (int tick = 0; tick < 60; tick++) { left.TrySubmitLocalInput(tick, tick + 1).ThrowIfFailure(); right.TrySubmitLocalInput(tick, tick * 3 - 4).ThrowIfFailure(); }
            left.Raft.TryStartElection().ThrowIfFailure();
            for (int step = 0; step < 1000; step++)
            {
                long time = step * 10; network.TryAdvanceTime(time).ThrowIfFailure(); var x = left.TryPump(time); var y = right.TryPump(time);
                Assert.IsTrue(x.IsSuccess() || x == DeterministicStatus.Pending || x == DeterministicStatus.QuorumLost);
                Assert.IsTrue(y.IsSuccess() || y == DeterministicStatus.Pending || y == DeterministicStatus.QuorumLost);
            }
            Assert.IsTrue(simulation.LastStepUsedBurst); Assert.AreEqual(60, left.CommittedBoundary); Assert.AreEqual(60, right.CommittedBoundary);
            left.Runner.History.TryGetHash(60, out ulong lh).ThrowIfFailure(); right.Runner.History.TryGetHash(60, out ulong rh).ThrowIfFailure(); Assert.AreEqual(lh, rh);
            Assert.IsFalse(left.IsFaulted); Assert.IsFalse(right.IsFaulted); Assert.AreEqual(leftGraph.AddNode(99), rightGraph.AddNode(99));
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct ProducerJob : IJob
        {
            internal NativeDeterministicCommandWriter<GraphMutation<int, int>> Writer;
            internal GraphNodeHandle Node;
            internal int Value;
            internal NativeArray<int> Result;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed;
                var mutation = new GraphMutation<int, int> { Kind = GraphMutationKind.SetNode, Node = Node, NodeData = Value };
                Result[1] = (int)Writer.TryEnqueue(0, 1, Node.Value, mutation);
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [TestCase(false)]
        [TestCase(true)]
        public void ParallelGraphCommandsUseLogicalOrderInsteadOfSchedulingOrder(bool reverse)
        {
            var context = new DeterministicRuntimeContext(DeterministicBackend.BurstCompiler, new NativeDeterministicMemoryFactory());
            using var graph = new NativeDeterministicGraph<int, int>(2, 2, Allocator.Persistent); var node = graph.AddNode(0);
            using var commands = new NativeDeterministicCommandBuffer<GraphMutation<int, int>>(context, new uint[] { 2, 9 }, 4);
            commands.TryGetWriter(2, out var a).ThrowIfFailure(); commands.TryGetWriter(9, out var b).ThrowIfFailure();
            using var firstResult = new NativeArray<int>(2, Allocator.TempJob); using var secondResult = new NativeArray<int>(2, Allocator.TempJob);
            var firstJob = new ProducerJob { Writer = a, Node = node, Value = 10, Result = firstResult }; var secondJob = new ProducerJob { Writer = b, Node = node, Value = 20, Result = secondResult };
            JobHandle left, right; if (reverse) { right = secondJob.Schedule(); left = firstJob.Schedule(); } else { left = firstJob.Schedule(); right = secondJob.Schedule(); }
            JobHandle.CombineDependencies(left, right).Complete(); Assert.AreEqual(0, firstResult[0]); Assert.AreEqual(0, secondResult[0]); Assert.AreEqual(0, firstResult[1]); Assert.AreEqual(0, secondResult[1]);
            commands.TryMerge(out var merged).ThrowIfFailure(); Assert.AreEqual(2, merged.Length);
            for (int i = 0; i < merged.Length; i++) graph.TryApply(merged[i].Payload, out _).ThrowIfFailure();
            graph.TryGetNode(node, out int value).ThrowIfFailure(); Assert.AreEqual(20, value); commands.TryReset().ThrowIfFailure();
        }
        [Test]
        public void FixedPointPayloadsUseTheExistingExplicitCodecs()
        {
            if (!FixedRuntime.IsInitialized) FixedRuntime.Initialize(); var options = new GraphOptions(2, 2);
            using var managed = new DeterministicGraph<Fixed64, Fixed64>(options); using var native = new NativeDeterministicGraph<Fixed64, Fixed64>(options, Allocator.Persistent);
            var a = managed.AddNode(Fixed64.FromRaw(123)); var b = managed.AddNode(Fixed64.NegativeZero); managed.AddEdge(a, b, Fixed64.FromRaw(456));
            var na = native.AddNode(Fixed64.FromRaw(123)); var nb = native.AddNode(Fixed64.NegativeZero); native.AddEdge(na, nb, Fixed64.FromRaw(456));
            byte* first = stackalloc byte[2048]; byte* second = stackalloc byte[2048]; var x = new DeterministicWriter(first, 2048); var y = new DeterministicWriter(second, 2048);
            managed.TryWriteCanonical(ref x, default(Fixed64Policy), default(Fixed64Policy)).ThrowIfFailure(); native.TryWriteCanonical(ref y, default(Fixed64Policy), default(Fixed64Policy)).ThrowIfFailure();
            Assert.AreEqual(x.Position, y.Position); Assert.AreEqual(DeterministicHash.Xxh64(first, x.Position), DeterministicHash.Xxh64(second, y.Position));
        }
    }
}
