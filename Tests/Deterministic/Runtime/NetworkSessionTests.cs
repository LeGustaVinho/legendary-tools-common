using System;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class NetworkSessionTests
    {
        private sealed class Simulation : IDeterministicSimulation<int, int>
        {
            internal readonly DeterministicCell<int> Value;
            internal Simulation(DeterministicCell<int> value) { Value = value; }
            public DeterministicStatus TryStep(in DeterministicInputFrame<int> inputs, DeterministicEventWriter<int> events)
            {
                int value = Value.Value;
                for (int i = 0; i < inputs.Count; i++) value = unchecked(value * 31 + inputs[i].Value * (int)inputs[i].Player.Value);
                Value.Value = value; return events.TryEmit(1, default, 0, value);
            }
        }
        private sealed class Node : IDisposable
        {
            internal readonly DeterministicCell<int> Value = new DeterministicCell<int>();
            internal readonly DeterministicStateRegistry State;
            internal readonly IDeterministicTransport Transport;
            internal readonly DeterministicNetworkSession<int, Int32Policy, int, Int32Policy> Session;
            internal bool Active = true;
            internal long Capture;
            internal Node(DeterministicRuntimeContext context, IDeterministicTransport transport, ReadOnlySpan<PeerId> voters,
                DeterministicSessionTopology topology, SnapshotPolicy snapshots, bool learner, ulong sessionId = 1)
            {
                Transport = transport; State = new DeterministicStateRegistry(context, new DeterministicCompatibilityProfile(1, 2, 3, 4, 5), 1);
                State.TryRegister(ManagedStateBindings.Cell(1, Value, default(Int32Policy), 1, 128, 128)).ThrowIfFailure(); State.TryFreeze().ThrowIfFailure();
                var runtime = new DeterministicRuntimeOptions(8, PredictionPolicy.HoldLast, snapshots, historyTicks: 128, pageBytes: 64);
                var raft = new DeterministicRaftOptions(8, 256, 1024, 65536);
                Session = new DeterministicNetworkSession<int, Int32Policy, int, Int32Policy>(context, runtime, raft, transport,
                    new DeterministicSessionId(sessionId), State, new Simulation(Value), voters, topology, 0, 4, 4, startAsLearner: learner);
            }
            public void Dispose() { Session.Dispose(); Transport.Dispose(); State.Dispose(); Value.Dispose(); }
        }
        private sealed class Cluster : IDisposable
        {
            internal readonly DeterministicRuntimeContext Context = new DeterministicRuntimeContext(DeterministicBackend.Managed);
            internal readonly DeterministicLoopbackNetwork Network;
            internal readonly Node[] Nodes = new Node[8];
            internal readonly PeerId[] Voters;
            internal readonly DeterministicSessionTopology Topology;
            internal readonly SnapshotPolicy Snapshots;
            internal long Time;
            internal Cluster(int players = 3, DeterministicSessionTopology topology = DeterministicSessionTopology.PeerToPeer,
                SnapshotPolicy snapshots = SnapshotPolicy.KeyframesAndDeltas, LoopbackFaults faults = default)
            {
                Topology = topology; Snapshots = snapshots; Network = new DeterministicLoopbackNetwork(Context, 8, 8192, 2048, faults);
                Voters = new PeerId[players]; for (int i = 0; i < players; i++) Voters[i] = new PeerId(new PlayerId((ulong)i + 1), 1);
                for (int i = 0; i < players; i++) Add(i, Voters[i], false);
                Nodes[0].Session.Raft.TryStartElection().ThrowIfFailure(); Pump(100);
            }
            internal void Add(int slot, PeerId peer, bool learner)
            { Nodes[slot] = new Node(Context, Network.CreateEndpoint(peer), Voters, Topology, Snapshots, learner); }
            internal void Pump(int steps, long captureLimit = 0, bool speculate = true)
            {
                for (int step = 0; step < steps; step++)
                {
                    Time += 10; Network.TryAdvanceTime(Time).ThrowIfFailure();
                    for (int i = 0; i < Nodes.Length; i++) if (Nodes[i] != null && Nodes[i].Active)
                    {
                        var node = Nodes[i]; var session = node.Session;
                        if (node.Capture < session.EarliestLocalCaptureTick) node.Capture = session.EarliestLocalCaptureTick;
                        if (node.Capture < captureLimit && node.Capture <= session.CommittedBoundary + 8)
                        {
                            int input = (int)(node.Capture * 7 % 13) + (int)session.LocalPeer.Player.Value;
                            var submitted = session.TrySubmitLocalInput(node.Capture, input);
                            if (submitted.IsSuccess()) node.Capture++;
                            else Assert.AreEqual(DeterministicStatus.Pending, submitted, "submit slot " + i + " at " + node.Capture + " boundary=" + session.CommittedBoundary + " next=" + session.Runner.NextTick + " raft=" + session.Raft.LastApplied + "/" + session.Raft.CommitIndex + " stable=" + session.Raft.IsMembershipStable);
                        }
                        var status = session.TryPump(Time, speculate ? 1 : 0);
                        Assert.IsTrue(status == DeterministicStatus.None || status == DeterministicStatus.Pending || status == DeterministicStatus.QuorumLost,
                            "slot " + i + " term " + session.Raft.CurrentTerm + " boundary " + session.CommittedBoundary + " failed: " + status);
                        Assert.IsFalse(session.IsFaulted);
                    }
                }
            }
            internal int Leader()
            { for (int i = 0; i < Nodes.Length; i++) if (Nodes[i] != null && Nodes[i].Active && Nodes[i].Session.Raft.Role == DeterministicRaftRole.Leader && Nodes[i].Session.Raft.HasQuorum) return i; return -1; }
            internal void Isolate(int slot, bool isolated)
            { for (int i = 0; i < Nodes.Length; i++) if (Nodes[i] != null && i != slot) Network.TrySetConnection(Nodes[slot].Transport.LocalPeer, Nodes[i].Transport.LocalPeer, !isolated).ThrowIfFailure(); }
            internal void AssertSameCommittedWorld(params int[] slots)
            {
                long boundary = Nodes[slots[0]].Session.CommittedBoundary; Assert.Greater(boundary, 2);
                Nodes[slots[0]].Session.Runner.History.TryGetHash(boundary, out ulong expected).ThrowIfFailure();
                foreach (int slot in slots)
                { Assert.AreEqual(boundary, Nodes[slot].Session.CommittedBoundary); Nodes[slot].Session.Runner.History.TryGetHash(boundary, out ulong actual).ThrowIfFailure(); Assert.AreEqual(expected, actual); }
            }
            public void Dispose() { foreach (var node in Nodes) node?.Dispose(); Network.Dispose(); }
        }
        [TestCase(DeterministicSessionTopology.AuthoritativeHost, SnapshotPolicy.FullPerTick)]
        [TestCase(DeterministicSessionTopology.AuthoritativeHost, SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(DeterministicSessionTopology.PeerToPeer, SnapshotPolicy.CopyOnWrite)]
        public void SessionsCommitIdenticalWorldsUnderPacketFaults(DeterministicSessionTopology topology, SnapshotPolicy snapshots)
        {
            using (var cluster = new Cluster(topology: topology, snapshots: snapshots, faults: new LoopbackFaults(0, 30, 80, 250, 831)))
            {
                cluster.Pump(1200, 60); cluster.Pump(200, speculate: false); cluster.AssertSameCommittedWorld(0, 1, 2);
                Assert.AreEqual(62, cluster.Nodes[0].Session.CommittedBoundary);
                long rollbacks = 0; for (int i = 0; i < 3; i++) rollbacks += cluster.Nodes[i].Session.Runner.RollbackCount;
                Assert.Greater(rollbacks, 0);
            }
        }
        [Test]
        public void LateJoinInstallsCommittedCheckpointAndIsAdmittedByJointConsensus()
        {
            using (var cluster = new Cluster())
            {
                cluster.Pump(500, 40); var peer = new PeerId(new PlayerId(4), 1); int leader = cluster.Leader();
                cluster.Add(3, peer, true); cluster.Nodes[3].Session.TryRequestJoinOrReconnect(cluster.Nodes[leader].Transport.LocalPeer).ThrowIfFailure();
                cluster.Pump(400); Assert.IsTrue(cluster.Nodes[3].Session.Raft.IsAdmitted); Assert.Greater(cluster.Nodes[3].Session.Raft.SnapshotIndex, 0);
                cluster.Pump(900, 80); cluster.Pump(200, speculate: false); cluster.AssertSameCommittedWorld(0, 1, 2, 3);
                Assert.AreEqual(82, cluster.Nodes[3].Session.CommittedBoundary);
            }
        }
        [Test]
        public void MajorityMigratesHostAndCommitsRemovalBeforeContinuingGameplay()
        {
            using (var cluster = new Cluster(topology: DeterministicSessionTopology.AuthoritativeHost))
            {
                cluster.Pump(500, 30); int oldLeader = cluster.Leader(); cluster.Isolate(oldLeader, true); cluster.Nodes[oldLeader].Active = false;
                long boundary = cluster.Nodes[(oldLeader + 1) % 3].Session.CommittedBoundary;
                cluster.Pump(1000, 80); cluster.Pump(200, speculate: false); int next = cluster.Leader(); Assert.GreaterOrEqual(next, 0); Assert.AreNotEqual(oldLeader, next);
                int a = (oldLeader + 1) % 3, b = (oldLeader + 2) % 3; cluster.AssertSameCommittedWorld(a, b);
                Assert.Greater(cluster.Nodes[next].Session.CommittedBoundary, boundary); Assert.AreEqual(82, cluster.Nodes[next].Session.CommittedBoundary);
                var voters = new PeerId[8]; cluster.Nodes[next].Session.Raft.TryGetVoters(voters, out int count).ThrowIfFailure(); Assert.AreEqual(2, count);
            }
        }
        [Test]
        public void LiveReconnectAndFreshProcessRestartRecoverWithTheExistingQuorum()
        {
            using (var cluster = new Cluster())
            {
                cluster.Pump(400, 25); int disconnected = (cluster.Leader() + 1) % 3; cluster.Isolate(disconnected, true); cluster.Nodes[disconnected].Active = false;
                cluster.Pump(400, 40); cluster.Isolate(disconnected, false); cluster.Nodes[disconnected].Active = true;
                cluster.Nodes[disconnected].Session.TryRequestJoinOrReconnect(cluster.Nodes[cluster.Leader()].Transport.LocalPeer).ThrowIfFailure();
                cluster.Pump(900, 60); cluster.Pump(200, speculate: false); cluster.AssertSameCommittedWorld(0, 1, 2);
                cluster.Isolate(disconnected, true); cluster.Nodes[disconnected].Active = false; cluster.Nodes[disconnected].Dispose(); cluster.Nodes[disconnected] = null;
                var restarted = new PeerId(cluster.Voters[disconnected].Player, 2); cluster.Add(3, restarted, true);
                cluster.Nodes[3].Session.TryRequestJoinOrReconnect(cluster.Nodes[cluster.Leader()].Transport.LocalPeer).ThrowIfFailure();
                cluster.Pump(700); Assert.IsTrue(cluster.Nodes[3].Session.Raft.IsAdmitted);
                cluster.Pump(1000, 95); cluster.Pump(200, speculate: false);
                cluster.AssertSameCommittedWorld((disconnected + 1) % 3, (disconnected + 2) % 3, 3);
            }
        }
        [Test]
        public void TwoPlayerSurvivorPausesAndCanExplicitlyStartADifferentSessionFromCheckpoint()
        {
            using (var cluster = new Cluster(2))
            {
                cluster.Pump(300, 20); int survivor = cluster.Leader(), failed = 1 - survivor;
                cluster.Isolate(failed, true); cluster.Nodes[failed].Active = false; cluster.Pump(300);
                var old = cluster.Nodes[survivor].Session; Assert.IsFalse(old.IsReady); Assert.AreEqual(DeterministicStatus.QuorumLost, old.Runner.TryAdvance(1, out _));
                long boundary = old.CommittedBoundary; cluster.Pump(200, 40); Assert.AreEqual(boundary, old.CommittedBoundary);
                byte* checkpoint = stackalloc byte[4096]; var writer = new DeterministicWriter(checkpoint, 4096); old.TryWriteSnapshot(ref writer).ThrowIfFailure();
                var freshPeer = new PeerId(old.LocalPeer.Player, 2); using (var fresh = new Node(cluster.Context, cluster.Network.CreateEndpoint(freshPeer), new[] { freshPeer },
                    cluster.Topology, cluster.Snapshots, false, 2))
                {
                    var reader = new DeterministicReader(checkpoint, writer.Position); fresh.Session.TryRestoreAsNewSession(ref reader).ThrowIfFailure();
                    Assert.AreEqual(boundary, fresh.Session.CommittedBoundary); Assert.AreEqual(0, fresh.Session.Raft.CurrentTerm);
                    fresh.Session.Raft.TryStartElection().ThrowIfFailure();
                    for (int i = 0; i < 20; i++)
                    {
                        cluster.Time += 10; cluster.Network.TryAdvanceTime(cluster.Time).ThrowIfFailure();
                        var pumped = fresh.Session.TryPump(cluster.Time, 0); Assert.IsTrue(pumped.IsSuccess() || pumped == DeterministicStatus.Pending);
                        fresh.Session.TrySubmitLocalInput(boundary + i, i + 1).ThrowIfFailure();
                    }
                    Assert.IsTrue(fresh.Session.IsReady); Assert.Greater(fresh.Session.CommittedBoundary, boundary);
                    Assert.AreEqual(DeterministicStatus.Invalidated, fresh.Session.TryRestoreAsNewSession(ref reader));
                }
            }
        }
        [Test]
        public void InvalidInputBatchDoesNotPublishAnEarlierValidInput()
        {
            using (var cluster = new Cluster(2))
            {
                var receiver = cluster.Nodes[0].Session; var sender = cluster.Nodes[1].Session;
                sender.TrySubmitLocalInput(1, 23).ThrowIfFailure(); receiver.Runner.TrySubmitInput(sender.LocalPeer.Player, 3, 23).ThrowIfFailure();
                byte* bytes = stackalloc byte[64]; var writer = new DeterministicWriter(bytes, 64);
                writer.TryUInt(2, 4); writer.TryUInt(2, 8); default(Int32Policy).TryWrite(ref writer, 7); writer.TryUInt(3, 8); default(Int32Policy).TryWrite(ref writer, 99);
                Assert.AreEqual(DeterministicStatus.ProtocolConflict, receiver.TryHandle(sender.LocalPeer, 32, new ReadOnlySpan<byte>(bytes, writer.Position)));
                Assert.AreEqual(DeterministicStatus.None, receiver.Runner.Inputs.CanSubmit(sender.LocalPeer.Player, 2, 11));
                Assert.AreEqual(DeterministicStatus.BufferTooSmall, receiver.TryHandle(sender.LocalPeer, 32, new ReadOnlySpan<byte>(bytes, writer.Position - 3)));
                Assert.AreEqual(DeterministicStatus.None, receiver.Runner.Inputs.CanSubmit(sender.LocalPeer.Player, 2, 11));
            }
        }
        [Test]
        public void NetworkPumpReplicationRollbackAndCompactionAllocateNoSteadyGc()
        {
            using (var cluster = new Cluster())
            {
                cluster.Pump(200, 25);
                long before = GC.GetAllocatedBytesForCurrentThread(); int failures = 0;
                for (int capture = 25; capture < 400; capture++)
                {
                    for (int i = 0; i < 3; i++) if (!cluster.Nodes[i].Session.TrySubmitLocalInput(capture, capture + i).IsSuccess()) failures++;
                    for (int repeat = 0; repeat < 10; repeat++)
                    {
                        cluster.Time += 10; cluster.Network.TryAdvanceTime(cluster.Time);
                        for (int i = 0; i < 3; i++) { var status = cluster.Nodes[i].Session.TryPump(cluster.Time); if (!status.IsSuccess() && status != DeterministicStatus.Pending) failures++; }
                    }
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before; Assert.AreEqual(0, allocated);
                Assert.AreEqual(0, failures); Assert.AreEqual(402, cluster.Nodes[0].Session.CommittedBoundary); cluster.AssertSameCommittedWorld(0, 1, 2);
            }
        }
    }
}
