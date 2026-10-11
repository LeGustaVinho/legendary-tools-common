using System;

namespace LegendaryTools.Deterministic
{
    public sealed unsafe partial class DeterministicRaftNode
    {
        private static void SortPeers(PeerId* values, int count)
        {
            for (int i = 1; i < count; i++) { var value = values[i]; int j = i - 1; while (j >= 0 && values[j].CompareTo(value) > 0) { values[j + 1] = values[j]; j--; } values[j + 1] = value; }
        }
        public DeterministicStatus TryGetVoters(Span<PeerId> destination, out int count, bool committed = true)
        {
            count = 0; if (disposed) return DeterministicStatus.Disposed;
            ulong mask = committed ? committedOldVoters | committedNewVoters : oldVoters | newVoters;
            count = DeterministicQuorum.Count(mask); if (destination.Length < count) return DeterministicStatus.BufferTooSmall;
            PeerId* ids = stackalloc PeerId[64]; int used = 0;
            for (int i = 0; i < peerCount; i++) if ((mask & (1UL << i)) != 0) ids[used++] = peers[i].Id;
            SortPeers(ids, used); for (int i = 0; i < used; i++) destination[i] = ids[i]; return DeterministicStatus.None;
        }
        private void WriteSet(ref DeterministicWriter writer, ulong mask)
        {
            PeerId* ids = stackalloc PeerId[64]; int count = 0;
            for (int i = 0; i < peerCount; i++) if ((mask & (1UL << i)) != 0) ids[count++] = peers[i].Id;
            SortPeers(ids, count); writer.TryUInt((byte)count, 1);
            for (int i = 0; i < count; i++) DeterministicSessionProtocol.WritePeer(ref writer, ids[i]);
        }
        private DeterministicStatus ReadSet(ref DeterministicReader reader, PeerId* known, ref int knownCount, bool publish, out ulong mask, bool allowEmpty)
        {
            mask = 0; var status = reader.TryUInt(1, out ulong count); if (!status.IsSuccess()) return status;
            if (count > (uint)options.MaxPeers || (!allowEmpty && count == 0) || !reader.Has((int)count * 16)) return DeterministicStatus.InvalidEncoding;
            PeerId previous = default;
            for (int i = 0; i < (int)count; i++)
            {
                DeterministicSessionProtocol.ReadPeer(ref reader, out var peer);
                if (!peer.IsValid || (i > 0 && (previous.CompareTo(peer) >= 0 || previous.Player == peer.Player))) return DeterministicStatus.InvalidEncoding;
                previous = peer; int slot = -1;
                for (int j = 0; j < knownCount; j++) if (known[j] == peer) { slot = j; break; }
                if (slot < 0)
                {
                    if (knownCount == options.MaxPeers) return DeterministicStatus.CapacityExceeded;
                    slot = knownCount++; known[slot] = peer; if (publish && AddKnown(peer) != slot) return DeterministicStatus.ProtocolConflict;
                }
                mask |= 1UL << slot;
            }
            return DeterministicStatus.None;
        }
        private DeterministicStatus ReadConfiguration(byte* bytes, int length, PeerId* known, ref int knownCount, bool publish, out ulong oldMask, out ulong newMask)
        {
            oldMask = newMask = 0; var reader = new DeterministicReader(bytes, length);
            var status = ReadSet(ref reader, known, ref knownCount, publish, out oldMask, false); if (!status.IsSuccess()) return status;
            status = ReadSet(ref reader, known, ref knownCount, publish, out newMask, true); if (!status.IsSuccess()) return status;
            return reader.Position == length ? DeterministicStatus.None : DeterministicStatus.InvalidEncoding;
        }
        private void ConfigurationAt(ulong index, out ulong oldMask, out ulong newMask)
        {
            oldMask = baseOldVoters; newMask = baseNewVoters;
            PeerId* known = stackalloc PeerId[64]; for (int i = 0; i < peerCount; i++) known[i] = peers[i].Id; int knownCount = peerCount;
            for (ulong at = SnapshotIndex + 1; at <= index; at++)
            {
                var entry = entries[Slot(at)]; if (entry.Kind != EntryKind.Joint && entry.Kind != EntryKind.Final) continue;
                var status = ReadConfiguration(logData + (long)Slot(at) * options.MaxCommandBytes, entry.Length, known, ref knownCount, true, out oldMask, out newMask);
                if (!status.IsSuccess()) { faulted = true; return; }
            }
        }
        private void RefreshConfiguration()
        {
            ConfigurationAt(LastLogIndex, out oldVoters, out newVoters);
            ConfigurationAt(CommitIndex, out committedOldVoters, out committedNewVoters);
            if (!IsVotingMember && Role != DeterministicRaftRole.Leader) Role = DeterministicRaftRole.Learner;
            else if (IsVotingMember && Role == DeterministicRaftRole.Learner) Role = DeterministicRaftRole.Follower;
        }
        public DeterministicStatus TryGetProgress(PeerId peer, out ulong replicated, out ulong applied)
        {
            replicated = applied = 0; if (disposed) return DeterministicStatus.Disposed;
            int index = Find(peer); if (index < 0) return DeterministicStatus.NotFound;
            replicated = index == self ? LastLogIndex : peers[index].Match;
            applied = index == self ? LastApplied : peers[index].Applied; return DeterministicStatus.None;
        }
        public DeterministicStatus TryBeginMembershipChange(ReadOnlySpan<PeerId> voters, out ulong index)
        {
            index = 0; if (disposed) return DeterministicStatus.Disposed;
            if (Role != DeterministicRaftRole.Leader) return DeterministicStatus.Pending;
            if (!HasQuorum) return DeterministicStatus.QuorumLost;
            if (newVoters != 0 || voters.Length == 0 || voters.Length > options.MaxPeers) return DeterministicStatus.Pending;
            ulong proposed = 0;
            for (int i = 0; i < voters.Length; i++)
            {
                if (!voters[i].IsValid) return DeterministicStatus.InvalidConfiguration;
                for (int j = 0; j < i; j++) if (voters[i].Player == voters[j].Player) return DeterministicStatus.DuplicateKey;
                int slot = Find(voters[i]); if (slot < 0) return DeterministicStatus.NotFound;
                if ((oldVoters & (1UL << slot)) == 0 && (peers[slot].Match < CommitIndex || peers[slot].Applied < CommitIndex)) return DeterministicStatus.Pending;
                proposed |= 1UL << slot;
            }
            if (proposed == oldVoters) return DeterministicStatus.DuplicateKey;
            var writer = new DeterministicWriter(payload, transport.MaxDatagramBytes);
            WriteSet(ref writer, oldVoters); WriteSet(ref writer, proposed);
            var status = AppendLocal(EntryKind.Joint, new ReadOnlySpan<byte>(payload, writer.Position), out index);
            if (status.IsSuccess()) { AdvanceCommit(); SendAll(); } return status;
        }
        private void FinalizeJoint()
        {
            if (Role != DeterministicRaftRole.Leader || newVoters == 0) return;
            ulong joint = 0;
            for (ulong at = SnapshotIndex + 1; at <= LastLogIndex; at++) if (entries[Slot(at)].Kind == EntryKind.Joint) joint = at;
            if (joint == 0 && baseNewVoters != 0) joint = SnapshotIndex;
            if (joint == 0 || joint > CommitIndex) return;
            var writer = new DeterministicWriter(payload, transport.MaxDatagramBytes); WriteSet(ref writer, newVoters); WriteSet(ref writer, 0);
            if (AppendLocal(EntryKind.Final, new ReadOnlySpan<byte>(payload, writer.Position), out _).IsSuccess()) { AdvanceCommit(); SendAll(); }
        }
        private void AdvanceCommit()
        {
            if (Role != DeterministicRaftRole.Leader) return;
            for (ulong index = LastLogIndex; index > CommitIndex; index--)
            {
                if (!TryTerm(index, out ulong term) || term != CurrentTerm) continue;
                ulong matched = 1UL << self;
                for (int i = 0; i < peerCount; i++) if (peers[i].Match >= index) matched |= 1UL << i;
                if (!DeterministicQuorum.HasQuorum(matched, oldVoters, newVoters)) continue;
                CommitIndex = index; RefreshConfiguration();
                SendAll();
                if (!IsAdmitted) Follow(CurrentTerm);
                break;
            }
        }
    }
}
