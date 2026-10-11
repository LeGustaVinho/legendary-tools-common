using System;

namespace LegendaryTools.Deterministic
{
    /// <summary>In-memory Raft for one process incarnation. Restart creates a learner; snapshots never restore voting credentials.</summary>
    public sealed unsafe partial class DeterministicRaftNode : IDisposable
    {
        private enum EntryKind : byte { Command = 1, Noop = 2, Joint = 3, Final = 4 }
        private enum Rpc : byte { Vote = 1, VoteReply = 2, Append = 3, AppendReply = 4, Snapshot = 5, SnapshotReply = 6 }
        private struct Entry { internal ulong Index, Term; internal int Length; internal EntryKind Kind; }
        private struct Peer
        {
            internal PeerId Id;
            internal ulong Match, Next, SnapshotIndex, Applied;
            internal int SnapshotOffset, SnapshotSent;
            internal long LastAck, LastSeen;
        }
        private readonly IDeterministicMemory memory;
        private readonly IDeterministicTransport transport;
        private readonly IDeterministicRaftStateMachine machine;
        private readonly DeterministicRaftOptions options;
        private readonly DeterministicSessionId session;
        private readonly ulong fingerprint;
        private Peer* peers;
        private Entry* entries;
        private byte* logData, receive, transmit, payload, snapshot, snapshotStage, snapshotIncoming;
        private int peerCount, self;
        private ulong oldVoters, newVoters, baseOldVoters, baseNewVoters, committedOldVoters, committedNewVoters;
        private ulong sequence, electionRound, votes, certifiedTerm, snapshotIncomingIndex, snapshotIncomingTerm;
        private int snapshotLength, incomingLength, incomingReceived;
        private long now, lastContact, lastHeartbeat, electionTimeout, lastCertificate = -1;
        private PeerId votedFor;
        private bool disposed, faulted, needsLeaderNoop;
        private IDeterministicRaftApplicationMessages application;
        public PeerId LocalPeer => transport.LocalPeer;
        public DeterministicSessionId SessionId => session;
        public PeerId Leader { get; private set; }
        public DeterministicRaftRole Role { get; private set; }
        public ulong CurrentTerm { get; private set; }
        public ulong CommitIndex { get; private set; }
        public ulong LastApplied { get; private set; }
        public ulong LastLogIndex { get; private set; }
        public ulong SnapshotIndex { get; private set; }
        public ulong SnapshotTerm { get; private set; }
        public bool IsJoint => newVoters != 0;
        public bool IsMembershipStable => newVoters == 0 && committedNewVoters == 0 && oldVoters == committedOldVoters;
        public bool IsFaulted => faulted;
        public bool IsVotingMember => ((oldVoters | newVoters) & (1UL << self)) != 0;
        public bool IsAdmitted => ((committedOldVoters | committedNewVoters) & (1UL << self)) != 0;
        public int KnownPeerCount => peerCount;
        public bool HasQuorum
        {
            get
            {
                if (disposed || faulted || !IsAdmitted) return false;
                if (Role == DeterministicRaftRole.Leader)
                {
                    if (CommitIndex == 0 || !TryTerm(CommitIndex, out ulong term) || term != CurrentTerm) return false;
                    ulong recent = 1UL << self;
                    for (int i = 0; i < peerCount; i++) if (peers[i].LastAck >= 0 && now - peers[i].LastAck <= options.QuorumTimeoutMs) recent |= 1UL << i;
                    return DeterministicQuorum.HasQuorum(recent, oldVoters, newVoters);
                }
                return Role == DeterministicRaftRole.Follower && certifiedTerm == CurrentTerm && lastCertificate >= 0 && now - lastCertificate <= options.QuorumTimeoutMs;
            }
        }
        public DeterministicRaftNode(DeterministicRuntimeContext context, IDeterministicTransport transport,
            DeterministicSessionId session, ulong fingerprint, DeterministicRaftOptions options,
            ReadOnlySpan<PeerId> initialVoters, IDeterministicRaftStateMachine stateMachine, bool startAsLearner = false)
        {
            if (context == null || transport == null || !transport.LocalPeer.IsValid || !session.IsValid || fingerprint == 0 ||
                !options.IsValid || stateMachine == null || initialVoters.Length == 0 || initialVoters.Length > options.MaxPeers ||
                options.MaxCommandBytes > transport.MaxDatagramBytes - DeterministicSessionProtocol.Overhead - 64)
                DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            bool included = false;
            for (int i = 0; i < initialVoters.Length; i++)
            {
                if (!initialVoters[i].IsValid) DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
                for (int j = 0; j < i; j++) if (initialVoters[i].Player == initialVoters[j].Player) DeterministicStatus.DuplicateKey.ThrowIfFailure();
                included |= initialVoters[i] == transport.LocalPeer;
            }
            if (included == startAsLearner || (!included && initialVoters.Length == options.MaxPeers)) DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            this.transport = transport; this.session = session; this.fingerprint = fingerprint; this.options = options; machine = stateMachine;
            long p = Align((long)sizeof(Peer) * options.MaxPeers), e = Align((long)sizeof(Entry) * options.LogCapacity);
            long logs = Align((long)options.LogCapacity * options.MaxCommandBytes), datagram = Align(transport.MaxDatagramBytes), snapshots = Align(options.MaxSnapshotBytes);
            long bytes = p + e + logs + datagram * 3 + snapshots * 3;
            if (bytes > int.MaxValue - 15) DeterministicStatus.CapacityExceeded.ThrowIfFailure();
            memory = context.Memory.Allocate((int)bytes); peers = (Peer*)memory.Pointer; entries = (Entry*)(memory.Pointer + p);
            logData = memory.Pointer + p + e; receive = logData + logs; transmit = receive + datagram; payload = transmit + datagram;
            snapshot = payload + datagram; snapshotStage = snapshot + snapshots; snapshotIncoming = snapshotStage + snapshots;
            for (int i = 0; i < initialVoters.Length; i++) { int slot = AddKnown(initialVoters[i]); oldVoters |= 1UL << slot; }
            self = Find(LocalPeer); if (self < 0) self = AddKnown(LocalPeer);
            baseOldVoters = committedOldVoters = oldVoters; Role = startAsLearner ? DeterministicRaftRole.Learner : DeterministicRaftRole.Follower;
            ResetElection();
        }
        private static long Align(long bytes) => (bytes + 15) & ~15L;
        private int Slot(ulong index) => (int)((index - 1) % (uint)options.LogCapacity);
        private int Find(PeerId peer) { for (int i = 0; i < peerCount; i++) if (peers[i].Id == peer) return i; return -1; }
        private int AddKnown(PeerId peer)
        {
            int found = Find(peer); if (found >= 0) return found;
            if (peerCount == options.MaxPeers) return -1;
            int index = peerCount++; peers[index] = new Peer { Id = peer, Next = LastLogIndex + 1, LastAck = -1, LastSeen = now }; return index;
        }
        private bool TryTerm(ulong index, out ulong term)
        {
            term = 0; if (index == SnapshotIndex) { term = SnapshotTerm; return true; }
            if (index < SnapshotIndex || index > LastLogIndex || index == 0) return false;
            var entry = entries[Slot(index)]; if (entry.Index != index) return false; term = entry.Term; return true;
        }
        public DeterministicStatus TryGetLog(ulong index, out DeterministicRaftLogInfo info, out ReadOnlySpan<byte> command)
        {
            info = default; command = default; if (disposed) return DeterministicStatus.Disposed;
            if (index <= SnapshotIndex || index > LastLogIndex) return DeterministicStatus.NotFound;
            var entry = entries[Slot(index)]; info = new DeterministicRaftLogInfo(index, entry.Term, entry.Length, entry.Kind == EntryKind.Command);
            command = new ReadOnlySpan<byte>(logData + (long)Slot(index) * options.MaxCommandBytes, entry.Length); return DeterministicStatus.None;
        }
        public DeterministicStatus TryGetPeer(int index, out PeerId peer, out bool voter)
        {
            peer = default; voter = false; if (disposed) return DeterministicStatus.Disposed;
            if ((uint)index >= (uint)peerCount) return DeterministicStatus.OutOfRange;
            peer = peers[index].Id; voter = ((oldVoters | newVoters) & (1UL << index)) != 0; return DeterministicStatus.None;
        }
        public void SetApplicationMessages(IDeterministicRaftApplicationMessages messages) { application = messages; }
        public DeterministicStatus TryGetLastContact(PeerId peer, out long timeMs)
        {
            timeMs = -1; if (disposed) return DeterministicStatus.Disposed;
            int index = Find(peer); if (index < 0) return DeterministicStatus.NotFound;
            timeMs = index == self ? now : peers[index].LastSeen; return DeterministicStatus.None;
        }
        private void ResetElection()
        {
            ulong seed = DeterministicHash.Mix(session.Value ^ LocalPeer.Player.Value ^ DeterministicHash.Mix(LocalPeer.Incarnation) ^ ++electionRound);
            electionTimeout = options.MinElectionMs + (long)(seed % (uint)(options.MaxElectionMs - options.MinElectionMs + 1)); lastContact = now;
        }
        private void Follow(ulong term, PeerId leader = default)
        {
            if (term > CurrentTerm) { CurrentTerm = term; votedFor = default; }
            Leader = leader; Role = IsVotingMember ? DeterministicRaftRole.Follower : DeterministicRaftRole.Learner;
            votes = 0; needsLeaderNoop = false; lastCertificate = -1; ResetElection();
        }
        public DeterministicStatus TryStartElection()
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (!IsVotingMember) return DeterministicStatus.Pending;
            if (CurrentTerm == ulong.MaxValue) return DeterministicStatus.GenerationExhausted;
            CurrentTerm++; Role = DeterministicRaftRole.Candidate; Leader = default; votedFor = LocalPeer; votes = 1UL << self; ResetElection();
            for (int i = 0; i < peerCount; i++) if (i != self && ((oldVoters | newVoters) & (1UL << i)) != 0) SendVote(i);
            if (DeterministicQuorum.HasQuorum(votes, oldVoters, newVoters)) BecomeLeader(); return DeterministicStatus.None;
        }
        private void BecomeLeader()
        {
            Role = DeterministicRaftRole.Leader; Leader = LocalPeer;
            for (int i = 0; i < peerCount; i++) { peers[i].Next = LastLogIndex + 1; peers[i].Match = 0; peers[i].SnapshotOffset = peers[i].SnapshotSent = 0; }
            needsLeaderNoop = !AppendLocal(EntryKind.Noop, ReadOnlySpan<byte>.Empty, out _).IsSuccess(); peers[self].Match = LastLogIndex;
            AdvanceCommit(); SendAll(); lastHeartbeat = now;
        }
        private DeterministicStatus AppendLocal(EntryKind kind, ReadOnlySpan<byte> command, out ulong index)
        {
            index = 0; if (command.Length > options.MaxCommandBytes) return DeterministicStatus.BufferTooSmall;
            if (LastLogIndex >= ulong.MaxValue - 1 || LastLogIndex - SnapshotIndex >= (uint)options.LogCapacity) return DeterministicStatus.CapacityExceeded;
            index = ++LastLogIndex; int slot = Slot(index);
            command.CopyTo(new Span<byte>(logData + (long)slot * options.MaxCommandBytes, command.Length));
            entries[slot] = new Entry { Index = index, Term = CurrentTerm, Kind = kind, Length = command.Length }; peers[self].Match = index;
            RefreshConfiguration(); return DeterministicStatus.None;
        }
        public DeterministicStatus TryPropose(ReadOnlySpan<byte> command, out ulong index)
        {
            index = 0; if (disposed) return DeterministicStatus.Disposed;
            if (faulted) return DeterministicStatus.ProtocolConflict;
            if (Role != DeterministicRaftRole.Leader) return DeterministicStatus.Pending;
            if (!HasQuorum) return DeterministicStatus.QuorumLost;
            var status = AppendLocal(EntryKind.Command, command, out index);
            if (status.IsSuccess()) { AdvanceCommit(); SendAll(); } return status;
        }
        public DeterministicStatus TryAddLearner(PeerId peer)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (!peer.IsValid) return DeterministicStatus.InvalidConfiguration;
            if (Role != DeterministicRaftRole.Leader) return DeterministicStatus.Pending;
            if (!HasQuorum) return DeterministicStatus.QuorumLost;
            int slot = AddKnown(peer); if (slot < 0) return DeterministicStatus.CapacityExceeded;
            if (((oldVoters | newVoters) & (1UL << slot)) != 0) return DeterministicStatus.DuplicateKey;
            peers[slot].Next = SnapshotIndex > 0 ? SnapshotIndex : 1; SendReplication(slot); return DeterministicStatus.None;
        }
        public DeterministicStatus TryReplicateTo(PeerId peer, bool forceSnapshot = false)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (Role != DeterministicRaftRole.Leader) return DeterministicStatus.Pending;
            if (!HasQuorum) return DeterministicStatus.QuorumLost;
            int slot = Find(peer); if (slot < 0) return DeterministicStatus.NotFound;
            if (forceSnapshot && SnapshotIndex > 0) peers[slot].Next = SnapshotIndex;
            SendReplication(slot); return DeterministicStatus.None;
        }
        public DeterministicStatus TrySendApplication(PeerId peer, byte kind, ReadOnlySpan<byte> message)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (kind < 32) return DeterministicStatus.InvalidConfiguration;
            return Send(peer, kind, message);
        }
        private DeterministicStatus Send(PeerId destination, byte kind, ReadOnlySpan<byte> message)
        {
            var writer = new DeterministicWriter(transmit, transport.MaxDatagramBytes);
            var header = new DeterministicMessageHeader(session, fingerprint, LocalPeer, destination, sequence++, kind);
            var status = DeterministicSessionProtocol.TryWrite(ref writer, header, message); if (!status.IsSuccess()) return status;
            return transport.TrySend(destination, new ReadOnlySpan<byte>(transmit, writer.Position));
        }
        private void SendAll() { for (int i = 0; i < peerCount; i++) if (i != self) SendReplication(i); }
        private DeterministicStatus ApplyCommitted()
        {
            while (LastApplied < CommitIndex)
            {
                ulong index = LastApplied + 1; var entry = entries[Slot(index)];
                if (entry.Index != index) return DeterministicStatus.ProtocolConflict;
                if (entry.Kind == EntryKind.Command)
                {
                    var status = machine.TryApply(index, entry.Term, new ReadOnlySpan<byte>(logData + (long)Slot(index) * options.MaxCommandBytes, entry.Length));
                    if (!status.IsSuccess()) return status;
                }
                LastApplied = index;
            }
            return DeterministicStatus.None;
        }
        public DeterministicStatus TryPump(long nowMs)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (faulted) return DeterministicStatus.ProtocolConflict;
            if (nowMs < now) return DeterministicStatus.OutOfRange; now = nowMs;
            for (int packet = 0; packet < options.MaxPacketsPerPump; packet++)
            {
                var status = transport.TryReceive(new Span<byte>(receive, transport.MaxDatagramBytes), out var sender, out int length);
                if (status == DeterministicStatus.Empty) break;
                if (!status.IsSuccess()) return status;
                var reader = new DeterministicReader(receive, length);
                status = DeterministicSessionProtocol.TryRead(ref reader, session, fingerprint, LocalPeer, sender, out var header, out var body);
                if (status == DeterministicStatus.InvalidEncoding || status == DeterministicStatus.ProfileMismatch || status == DeterministicStatus.BufferTooSmall) continue;
                if (!status.IsSuccess()) return status;
                if (reader.Position != length) continue;
                status = Handle(header, body);
                if (status == DeterministicStatus.None || status == DeterministicStatus.Pending)
                { int source = Find(header.Sender); if (source >= 0) peers[source].LastSeen = now; }
                if (status == DeterministicStatus.ProtocolConflict) { faulted = true; return status; }
                if (status != DeterministicStatus.None && status != DeterministicStatus.Pending && status != DeterministicStatus.NotFound && status != DeterministicStatus.InvalidEncoding) return status;
            }
            var applied = ApplyCommitted(); if (!applied.IsSuccess() && applied != DeterministicStatus.Pending) { faulted = true; return applied; }
            // Followers must also release applied log prefixes; a healthy leader may never need to send them a snapshot.
            if (LastApplied - SnapshotIndex >= (uint)options.LogCapacity / 2) TryCompact();
            if (Role != DeterministicRaftRole.Leader && IsVotingMember && now - lastContact >= electionTimeout) TryStartElection();
            if (Role == DeterministicRaftRole.Leader)
            {
                if (needsLeaderNoop)
                {
                    var status = AppendLocal(EntryKind.Noop, ReadOnlySpan<byte>.Empty, out _);
                    if (!status.IsSuccess()) return status;
                    needsLeaderNoop = false; SendAll();
                }
                AdvanceCommit(); FinalizeJoint();
                if (now - lastHeartbeat >= options.HeartbeatMs)
                {
                    for (int i = 0; i < peerCount; i++) peers[i].SnapshotSent = peers[i].SnapshotOffset;
                    SendAll(); lastHeartbeat = now;
                }
            }
            return applied == DeterministicStatus.Pending ? applied : DeterministicStatus.None;
        }
        public void Dispose()
        { if (disposed) return; disposed = true; memory.Dispose(); peers = null; entries = null; logData = receive = transmit = payload = snapshot = snapshotStage = snapshotIncoming = null; }
    }
}
