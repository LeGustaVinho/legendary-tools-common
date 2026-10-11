using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class RaftTests
    {
        private sealed class Machine : IDeterministicRaftStateMachine
        {
            internal long Sum, Count;
            internal int PendingInstalls, InstallAttempts;
            internal readonly HashSet<ulong> Applied = new HashSet<ulong>();
            private readonly byte[] padding;
            internal Machine(int paddingBytes = 0) { padding = new byte[paddingBytes]; for (int i = 0; i < padding.Length; i++) padding[i] = (byte)(i * 13); }
            public DeterministicStatus TryApply(ulong index, ulong term, ReadOnlySpan<byte> command)
            {
                if (command.Length != 4 || !Applied.Add(index)) return DeterministicStatus.ProtocolConflict;
                int value = command[0] | command[1] << 8 | command[2] << 16 | command[3] << 24; Sum = unchecked(Sum + value); Count++; return DeterministicStatus.None;
            }
            public DeterministicStatus TryWriteSnapshot(ref DeterministicWriter writer)
            {
                if (!writer.Has(20 + padding.Length)) return DeterministicStatus.BufferTooSmall;
                writer.TryUInt(unchecked((ulong)Sum), 8); writer.TryUInt((ulong)Count, 8); writer.TryUInt((uint)padding.Length, 4);
                fixed (byte* pointer = padding) writer.TryBytes(pointer, padding.Length); return DeterministicStatus.None;
            }
            private DeterministicStatus Read(ref DeterministicReader reader, out long sum, out long count)
            {
                sum = count = 0; var trial = reader; if (!trial.Has(20)) return DeterministicStatus.BufferTooSmall;
                trial.TryUInt(8, out ulong value); trial.TryUInt(8, out ulong amount); trial.TryUInt(4, out ulong length);
                if (length != (uint)padding.Length || amount > long.MaxValue || !trial.Has(padding.Length)) return DeterministicStatus.InvalidEncoding;
                for (int i = 0; i < padding.Length; i++) if (trial.Buffer[trial.Position + i] != padding[i]) return DeterministicStatus.InvalidEncoding;
                trial.Position += padding.Length; sum = unchecked((long)value); count = (long)amount; reader = trial; return DeterministicStatus.None;
            }
            public DeterministicStatus TryValidateSnapshot(ref DeterministicReader reader) => Read(ref reader, out _, out _);
            public DeterministicStatus TryInstallSnapshot(ref DeterministicReader reader)
            { InstallAttempts++; if (PendingInstalls > 0) { PendingInstalls--; return DeterministicStatus.Pending; } var status = Read(ref reader, out long sum, out long count); if (status.IsSuccess()) { Sum = sum; Count = count; } return status; }
        }
        private sealed class Cluster : IDisposable
        {
            internal readonly DeterministicRuntimeContext Context = new DeterministicRuntimeContext(DeterministicBackend.Managed);
            internal readonly DeterministicLoopbackNetwork Network;
            internal readonly DeterministicRaftNode[] Nodes = new DeterministicRaftNode[8];
            internal readonly IDeterministicTransport[] Transports = new IDeterministicTransport[8];
            internal readonly Machine[] Machines = new Machine[8];
            internal readonly bool[] Active = new bool[8];
            internal readonly PeerId[] Initial;
            internal readonly int Padding;
            internal long Time;
            internal Cluster(int count, LoopbackFaults faults = default, int padding = 0)
            {
                Padding = padding; Network = new DeterministicLoopbackNetwork(Context, 8, 4096, 1024, faults);
                Initial = new PeerId[count]; for (int i = 0; i < count; i++) Initial[i] = new PeerId(new PlayerId((ulong)i + 1), 1);
                for (int i = 0; i < count; i++) Add(i, Initial[i], false);
                Nodes[0].TryStartElection().ThrowIfFailure(); Pump(100);
            }
            internal void Add(int slot, PeerId peer, bool learner)
            {
                Transports[slot] = Network.CreateEndpoint(peer); Machines[slot] = new Machine(Padding);
                Nodes[slot] = new DeterministicRaftNode(Context, Transports[slot], new DeterministicSessionId(1), 99,
                    new DeterministicRaftOptions(8, 256, 512, 65536), Initial, Machines[slot], learner); Active[slot] = true;
            }
            internal void Pump(int steps)
            {
                for (int step = 0; step < steps; step++)
                {
                    Time += 10; Network.TryAdvanceTime(Time).ThrowIfFailure();
                    for (int i = 0; i < Nodes.Length; i++) if (Active[i])
                    {
                        var status = Nodes[i].TryPump(Time); Assert.IsTrue(status == DeterministicStatus.None || status == DeterministicStatus.Pending, "Node " + i + " failed: " + status);
                    }
                }
            }
            internal int Leader()
            { for (int i = 0; i < Nodes.Length; i++) if (Active[i] && Nodes[i].Role == DeterministicRaftRole.Leader && Nodes[i].HasQuorum) return i; return -1; }
            internal void Connect(int a, int b, bool connected) => Network.TrySetConnection(Transports[a].LocalPeer, Transports[b].LocalPeer, connected).ThrowIfFailure();
            internal void Isolate(int index, bool isolated)
            { for (int i = 0; i < Nodes.Length; i++) if (i != index && Transports[i] != null) Connect(index, i, !isolated); }
            internal void Propose(int value)
            {
                int leader = Leader(); Assert.GreaterOrEqual(leader, 0); byte* bytes = stackalloc byte[4];
                for (int i = 0; i < 4; i++) bytes[i] = (byte)(value >> (8 * i)); Nodes[leader].TryPropose(new ReadOnlySpan<byte>(bytes, 4), out _).ThrowIfFailure(); Pump(100);
            }
            public void Dispose()
            { for (int i = 0; i < Nodes.Length; i++) { Nodes[i]?.Dispose(); Transports[i]?.Dispose(); } Network.Dispose(); }
        }
        [Test]
        public void QuorumIsStrictForTwoPlayersAndRequiresBothJointMajorities()
        {
            Assert.IsFalse(DeterministicQuorum.HasQuorum(1, 3)); Assert.IsTrue(DeterministicQuorum.HasQuorum(3, 3));
            Assert.IsFalse(DeterministicQuorum.HasQuorum(3, 3, 12)); Assert.IsTrue(DeterministicQuorum.HasQuorum(15, 3, 12));
            Assert.IsTrue(DeterministicQuorum.HasQuorum(3, 7));
        }
        [Test]
        public void ReplicationIsExactlyOnceUnderLossDuplicationDelayAndReordering()
        {
            using (var cluster = new Cluster(3, new LoopbackFaults(0, 40, 100, 500, 874)))
            {
                Assert.GreaterOrEqual(cluster.Leader(), 0);
                for (int value = 1; value <= 20; value++) cluster.Propose(value); cluster.Pump(200);
                for (int i = 0; i < 3; i++) { Assert.AreEqual(210, cluster.Machines[i].Sum); Assert.AreEqual(20, cluster.Machines[i].Count); Assert.AreEqual(cluster.Nodes[cluster.Leader()].CommitIndex, cluster.Nodes[i].LastApplied); }
            }
        }
        [Test]
        public void TwoPlayerPartitionCannotCommitOrUnilaterallyReduceMembership()
        {
            using (var cluster = new Cluster(2))
            {
                int leader = cluster.Leader(); Assert.GreaterOrEqual(leader, 0); cluster.Isolate(leader, true); cluster.Pump(120);
                Assert.IsFalse(cluster.Nodes[leader].HasQuorum); Assert.AreEqual(-1, cluster.Leader());
                Assert.AreEqual(DeterministicStatus.QuorumLost, cluster.Nodes[leader].TryBeginMembershipChange(new[] { cluster.Transports[leader].LocalPeer }, out _));
                Assert.AreEqual(0, cluster.Machines[0].Count); Assert.AreEqual(0, cluster.Machines[1].Count);
                cluster.Isolate(leader, false); cluster.Pump(200); cluster.Propose(9);
                Assert.AreEqual(9, cluster.Machines[0].Sum); Assert.AreEqual(9, cluster.Machines[1].Sum);
            }
        }
        [Test]
        public void NewLeaderReplacesMinorityUncommittedSuffixWithoutApplyingIt()
        {
            using (var cluster = new Cluster(3))
            {
                int isolated = cluster.Leader(); cluster.Isolate(isolated, true);
                byte* bytes = stackalloc byte[4]; bytes[0] = 0xe7; bytes[1] = 3; bytes[2] = bytes[3] = 0;
                cluster.Nodes[isolated].TryPropose(new ReadOnlySpan<byte>(bytes, 4), out _).ThrowIfFailure(); cluster.Pump(150);
                int leader = cluster.Leader(); Assert.GreaterOrEqual(leader, 0); Assert.AreNotEqual(isolated, leader); cluster.Propose(7);
                Assert.AreEqual(0, cluster.Machines[isolated].Count); cluster.Isolate(isolated, false); cluster.Pump(250);
                for (int i = 0; i < 3; i++) { Assert.AreEqual(7, cluster.Machines[i].Sum); Assert.AreEqual(1, cluster.Machines[i].Count); }
            }
        }
        [Test]
        public void ChunkedSnapshotCatchesLearnerUpBeforeJointAdmission()
        {
            using (var cluster = new Cluster(3, padding: 8192))
            {
                for (int value = 1; value <= 15; value++) cluster.Propose(value);
                int leader = cluster.Leader(); cluster.Nodes[leader].TryCompact().ThrowIfFailure(); var joining = new PeerId(new PlayerId(4), 1);
                cluster.Add(3, joining, true); cluster.Nodes[leader].TryAddLearner(joining).ThrowIfFailure();
                cluster.Network.TrySetFaults(new LoopbackFaults(0, 20, 50, 300, 821)).ThrowIfFailure(); cluster.Pump(500);
                Assert.AreEqual(DeterministicRaftRole.Learner, cluster.Nodes[3].Role); Assert.IsFalse(cluster.Nodes[3].IsAdmitted);
                Assert.AreEqual(120, cluster.Machines[3].Sum); Assert.Greater(cluster.Nodes[3].SnapshotIndex, 0);
                cluster.Nodes[leader].TryBeginMembershipChange(new[] { cluster.Initial[0], cluster.Initial[1], cluster.Initial[2], joining }, out _).ThrowIfFailure(); cluster.Pump(400);
                for (int i = 0; i < 4; i++) { Assert.IsTrue(cluster.Nodes[i].IsAdmitted); Assert.IsFalse(cluster.Nodes[i].IsJoint); }
                cluster.Propose(5); for (int i = 0; i < 4; i++) Assert.AreEqual(125, cluster.Machines[i].Sum);
            }
        }
        [Test]
        public void RestartedPlayerUsesNewLearnerIncarnationAndExistingQuorumAdmitsIt()
        {
            using (var cluster = new Cluster(3))
            {
                cluster.Propose(11); int old = (cluster.Leader() + 1) % 3; cluster.Active[old] = false; cluster.Isolate(old, true); cluster.Pump(150);
                int leader = cluster.Leader(); Assert.GreaterOrEqual(leader, 0); cluster.Nodes[leader].TryCompact().ThrowIfFailure();
                var restarted = new PeerId(cluster.Initial[old].Player, 2); cluster.Add(3, restarted, true); cluster.Nodes[leader].TryAddLearner(restarted).ThrowIfFailure(); cluster.Pump(200);
                Assert.IsFalse(cluster.Nodes[3].IsAdmitted); Assert.AreEqual(11, cluster.Machines[3].Sum);
                var voters = (PeerId[])cluster.Initial.Clone(); voters[old] = restarted;
                cluster.Nodes[leader].TryBeginMembershipChange(voters, out _).ThrowIfFailure(); cluster.Pump(200);
                Assert.IsTrue(cluster.Nodes[3].IsAdmitted); cluster.Propose(13);
                for (int i = 0; i < 4; i++) if (cluster.Active[i]) Assert.AreEqual(24, cluster.Machines[i].Sum);
                cluster.Isolate(old, false); cluster.Active[old] = true; cluster.Pump(200); Assert.IsFalse(cluster.Nodes[old].IsAdmitted); Assert.AreEqual(DeterministicRaftRole.Learner, cluster.Nodes[old].Role);
            }
        }
        [Test]
        public void LearnerRestartCannotManufactureAQuorumAfterTwoPlayerFailure()
        {
            using (var cluster = new Cluster(2))
            {
                int leader = cluster.Leader(), failed = 1 - leader; cluster.Active[failed] = false; cluster.Isolate(failed, true); cluster.Pump(150);
                var restarted = new PeerId(cluster.Initial[failed].Player, 2); cluster.Add(2, restarted, true); cluster.Pump(200);
                Assert.IsFalse(cluster.Nodes[leader].HasQuorum); Assert.IsFalse(cluster.Nodes[2].IsVotingMember);
                Assert.AreEqual(DeterministicStatus.QuorumLost, cluster.Nodes[leader].TryAddLearner(restarted)); Assert.AreEqual(-1, cluster.Leader());
            }
        }
        [Test]
        public void SnapshotInstallationRetriesAfterApplicationBackpressure()
        {
            using (var cluster = new Cluster(3, padding: 24000))
            {
                cluster.Propose(37); int leader = cluster.Leader(); cluster.Nodes[leader].TryCompact().ThrowIfFailure();
                var joining = new PeerId(new PlayerId(4), 1); cluster.Add(3, joining, true); cluster.Machines[3].PendingInstalls = 3;
                cluster.Nodes[leader].TryAddLearner(joining).ThrowIfFailure(); cluster.Pump(800);
                Assert.AreEqual(37, cluster.Machines[3].Sum); Assert.GreaterOrEqual(cluster.Machines[3].InstallAttempts, 4);
                Assert.AreEqual(cluster.Nodes[leader].SnapshotIndex, cluster.Nodes[3].SnapshotIndex);
            }
        }
        [Test]
        public void JointMembershipSurvivesLeaderFailureBeforeFinalConfiguration()
        {
            using (var cluster = new Cluster(3))
            {
                cluster.Propose(5); int old = cluster.Leader(); var joining = new PeerId(new PlayerId(4), 1);
                cluster.Add(3, joining, true); cluster.Nodes[old].TryAddLearner(joining).ThrowIfFailure(); cluster.Pump(200);
                cluster.Nodes[old].TryBeginMembershipChange(new[] { cluster.Initial[0], cluster.Initial[1], cluster.Initial[2], joining }, out _).ThrowIfFailure();
                // Deliver the joint entry once without letting the leader finalize it.
                cluster.Time += 10; cluster.Network.TryAdvanceTime(cluster.Time).ThrowIfFailure();
                for (int i = 0; i < 4; i++) if (i != old) cluster.Nodes[i].TryPump(cluster.Time).ThrowIfFailure();
                cluster.Active[old] = false; cluster.Isolate(old, true); cluster.Pump(600); Assert.GreaterOrEqual(cluster.Leader(), 0);
                cluster.Propose(9); for (int i = 0; i < 4; i++) if (cluster.Active[i])
                { Assert.AreEqual(14, cluster.Machines[i].Sum); Assert.IsFalse(cluster.Nodes[i].IsJoint); Assert.IsTrue(cluster.Nodes[i].IsMembershipStable); }
            }
        }
        [Test]
        public void NewLeaderRetriesCurrentTermNoopAfterCompactingAFullCommittedLog()
        {
            var context = new DeterministicRuntimeContext(DeterministicBackend.Managed);
            var peer = new PeerId(new PlayerId(1), 1); var machine = new Machine();
            using (var network = new DeterministicLoopbackNetwork(context, 1, 8, 256))
            using (var transport = network.CreateEndpoint(peer))
            using (var node = new DeterministicRaftNode(context, transport, new DeterministicSessionId(1), 3,
                new DeterministicRaftOptions(1, 2, 64, 1024), new[] { peer }, machine))
            {
                node.TryStartElection().ThrowIfFailure(); byte* command = stackalloc byte[4]; command[0] = 5; command[1] = command[2] = command[3] = 0;
                node.TryPropose(new ReadOnlySpan<byte>(command, 4), out _).ThrowIfFailure(); Assert.AreEqual(2, node.CommitIndex);
                node.TryStartElection().ThrowIfFailure(); Assert.IsFalse(node.HasQuorum);
                node.TryPump(10).ThrowIfFailure(); node.TryPump(20).ThrowIfFailure(); Assert.IsTrue(node.HasQuorum);
                Assert.AreEqual(5, machine.Sum); Assert.AreEqual(1, machine.Count); Assert.AreEqual(2, node.CurrentTerm);
            }
        }
        [Test]
        public void StaleSnapshotForAnAlreadyCompactedPrefixIsAcknowledgedWithoutReinstallingState()
        {
            using (var cluster = new Cluster(3))
            {
                cluster.Propose(37); int leader = cluster.Leader(), follower = (leader + 1) % 3;
                cluster.Nodes[follower].TryCompact().ThrowIfFailure(); ulong snapshot = cluster.Nodes[follower].SnapshotIndex;
                byte* body = stackalloc byte[37]; var writer = new DeterministicWriter(body, 37);
                writer.TryUInt(cluster.Nodes[leader].CurrentTerm, 8); writer.TryUInt(1, 8); writer.TryUInt(1, 8);
                writer.TryUInt(40, 4); writer.TryUInt(0, 4); writer.TryUInt(1, 4); writer.TryUInt(0xff, 1);
                byte* packet = stackalloc byte[256]; var envelope = new DeterministicWriter(packet, 256);
                var header = new DeterministicMessageHeader(new DeterministicSessionId(1), 99, cluster.Transports[leader].LocalPeer,
                    cluster.Transports[follower].LocalPeer, 9001, 5);
                DeterministicSessionProtocol.TryWrite(ref envelope, header, new ReadOnlySpan<byte>(body, writer.Position)).ThrowIfFailure();
                cluster.Transports[leader].TrySend(cluster.Transports[follower].LocalPeer, new ReadOnlySpan<byte>(packet, envelope.Position)).ThrowIfFailure(); cluster.Pump(20);
                Assert.IsFalse(cluster.Nodes[follower].IsFaulted); Assert.AreEqual(snapshot, cluster.Nodes[follower].SnapshotIndex);
                Assert.AreEqual(37, cluster.Machines[follower].Sum); Assert.AreEqual(0, cluster.Machines[follower].InstallAttempts);
            }
        }
    }
}
