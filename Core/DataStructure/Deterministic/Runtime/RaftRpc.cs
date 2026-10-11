using System;

namespace LegendaryTools.Deterministic
{
    public sealed unsafe partial class DeterministicRaftNode
    {
        private DeterministicStatus Handle(DeterministicMessageHeader header, ReadOnlySpan<byte> body)
        {
            if (header.Kind >= 32) return application == null ? DeterministicStatus.NotFound : application.TryHandle(header.Sender, header.Kind, body);
            int peer = Find(header.Sender); if (peer < 0 || peer == self) return DeterministicStatus.NotFound;
            bool voter = ((oldVoters | newVoters) & (1UL << peer)) != 0;
            if ((header.Kind == (byte)Rpc.Vote || header.Kind == (byte)Rpc.VoteReply) && !voter) return DeterministicStatus.NotFound;
            fixed (byte* bytes = body)
            {
                var reader = new DeterministicReader(bytes, body.Length);
                switch ((Rpc)header.Kind)
                {
                    case Rpc.Vote: return HandleVote(peer, reader);
                    case Rpc.VoteReply: return HandleVoteReply(peer, reader);
                    case Rpc.Append: return voter || Leader == header.Sender ? HandleAppend(peer, reader) : DeterministicStatus.NotFound;
                    case Rpc.AppendReply: return HandleAppendReply(peer, reader);
                    case Rpc.Snapshot: return voter || Leader == header.Sender ? HandleSnapshot(peer, reader) : DeterministicStatus.NotFound;
                    case Rpc.SnapshotReply: return HandleSnapshotReply(peer, reader);
                    default: return DeterministicStatus.InvalidEncoding;
                }
            }
        }
        private void SendVote(int peer)
        {
            TryTerm(LastLogIndex, out ulong lastTerm); var writer = new DeterministicWriter(payload, transport.MaxDatagramBytes);
            writer.TryUInt(CurrentTerm, 8); writer.TryUInt(LastLogIndex, 8); writer.TryUInt(lastTerm, 8);
            Send(peers[peer].Id, (byte)Rpc.Vote, new ReadOnlySpan<byte>(payload, writer.Position));
        }
        private DeterministicStatus HandleVote(int peer, DeterministicReader reader)
        {
            if (reader.Length != 24) return DeterministicStatus.InvalidEncoding;
            reader.TryUInt(8, out ulong term); reader.TryUInt(8, out ulong lastIndex); reader.TryUInt(8, out ulong lastTerm);
            if (term == 0 || lastIndex == ulong.MaxValue || lastTerm > term || (lastIndex == 0) != (lastTerm == 0)) return DeterministicStatus.InvalidEncoding;
            if (term > CurrentTerm) Follow(term);
            TryTerm(LastLogIndex, out ulong ownTerm);
            bool upToDate = lastTerm > ownTerm || (lastTerm == ownTerm && lastIndex >= LastLogIndex);
            bool granted = IsVotingMember && term == CurrentTerm && upToDate && (!votedFor.IsValid || votedFor == peers[peer].Id);
            if (granted) { votedFor = peers[peer].Id; ResetElection(); }
            var writer = new DeterministicWriter(payload, transport.MaxDatagramBytes); writer.TryUInt(CurrentTerm, 8); writer.TryUInt(granted ? 1UL : 0, 1);
            Send(peers[peer].Id, (byte)Rpc.VoteReply, new ReadOnlySpan<byte>(payload, writer.Position)); return DeterministicStatus.None;
        }
        private DeterministicStatus HandleVoteReply(int peer, DeterministicReader reader)
        {
            if (reader.Length != 9) return DeterministicStatus.InvalidEncoding;
            reader.TryUInt(8, out ulong term); reader.TryUInt(1, out ulong granted); if (granted > 1) return DeterministicStatus.InvalidEncoding;
            if (term > CurrentTerm) { Follow(term); return DeterministicStatus.None; }
            if (term != CurrentTerm || Role != DeterministicRaftRole.Candidate || granted == 0) return DeterministicStatus.None;
            votes |= 1UL << peer; peers[peer].LastAck = now;
            if (DeterministicQuorum.HasQuorum(votes, oldVoters, newVoters)) BecomeLeader(); return DeterministicStatus.None;
        }
        private void SendReplication(int peer)
        {
            if (Role != DeterministicRaftRole.Leader || peer == self) return;
            if (peers[peer].Next <= SnapshotIndex && SnapshotIndex > 0) { SendSnapshot(peer); return; }
            ulong next = Math.Max(1UL, Math.Min(peers[peer].Next, LastLogIndex + 1)), previous = next - 1;
            if (!TryTerm(previous, out ulong previousTerm)) return;
            var writer = new DeterministicWriter(payload, transport.MaxDatagramBytes - DeterministicSessionProtocol.Overhead);
            writer.TryUInt(CurrentTerm, 8); writer.TryUInt(previous, 8); writer.TryUInt(previousTerm, 8); writer.TryUInt(CommitIndex, 8);
            writer.TryUInt(HasQuorum ? 1UL : 0, 1); int countOffset = writer.Position; writer.TryUInt(0, 4); uint count = 0;
            for (ulong index = next; index <= LastLogIndex; index++)
            {
                var entry = entries[Slot(index)]; if (!writer.Has(21 + entry.Length)) break;
                writer.TryUInt(index, 8); writer.TryUInt(entry.Term, 8); writer.TryUInt((byte)entry.Kind, 1); writer.TryUInt((uint)entry.Length, 4);
                writer.TryBytes(logData + (long)Slot(index) * options.MaxCommandBytes, entry.Length); count++;
            }
            for (int i = 0; i < 4; i++) payload[countOffset + i] = (byte)(count >> (8 * i));
            Send(peers[peer].Id, (byte)Rpc.Append, new ReadOnlySpan<byte>(payload, writer.Position));
        }
        private void ReplyAppend(int peer, bool success, ulong previous, ulong matched, ulong hint)
        {
            var writer = new DeterministicWriter(payload, transport.MaxDatagramBytes);
            writer.TryUInt(CurrentTerm, 8); writer.TryUInt(success ? 1UL : 0, 1); writer.TryUInt(previous, 8);
            writer.TryUInt(matched, 8); writer.TryUInt(hint, 8); writer.TryUInt(LastApplied, 8);
            Send(peers[peer].Id, (byte)Rpc.AppendReply, new ReadOnlySpan<byte>(payload, writer.Position));
        }
        private DeterministicStatus HandleAppend(int peer, DeterministicReader reader)
        {
            if (!reader.Has(37)) return DeterministicStatus.InvalidEncoding;
            reader.TryUInt(8, out ulong term); reader.TryUInt(8, out ulong previous); reader.TryUInt(8, out ulong previousTerm); reader.TryUInt(8, out ulong leaderCommit);
            reader.TryUInt(1, out ulong certificate); reader.TryUInt(4, out ulong count);
            if (term == 0 || previousTerm > term || certificate > 1 || count > (uint)options.LogCapacity || previous >= ulong.MaxValue - count ||
                (previous == 0) != (previousTerm == 0)) return DeterministicStatus.InvalidEncoding;
            int bodyStart = reader.Position; PeerId* known = stackalloc PeerId[64]; int knownCount = peerCount;
            for (int i = 0; i < peerCount; i++) known[i] = peers[i].Id;
            ulong expected = previous, earlierTerm = previousTerm, conflict = 0;
            bool matchesPrevious = TryTerm(previous, out ulong actualPreviousTerm) && actualPreviousTerm == previousTerm;
            for (int i = 0; i < (int)count; i++)
            {
                if (!reader.Has(21)) return DeterministicStatus.InvalidEncoding;
                reader.TryUInt(8, out ulong index); reader.TryUInt(8, out ulong entryTerm); reader.TryUInt(1, out ulong kind); reader.TryUInt(4, out ulong length);
                if (index != ++expected || index == ulong.MaxValue || entryTerm == 0 || entryTerm < earlierTerm || entryTerm > term ||
                    kind < 1 || kind > 4 || length > (uint)options.MaxCommandBytes || !reader.Has((int)length)) return DeterministicStatus.InvalidEncoding;
                earlierTerm = entryTerm;
                if (kind == (byte)EntryKind.Noop && length != 0) return DeterministicStatus.InvalidEncoding;
                if (kind == (byte)EntryKind.Joint || kind == (byte)EntryKind.Final)
                {
                    var valid = ReadConfiguration(reader.Buffer + reader.Position, (int)length, known, ref knownCount, false, out _, out ulong newMask);
                    if (!valid.IsSuccess()) return valid;
                    if ((kind == (byte)EntryKind.Joint) != (newMask != 0)) return DeterministicStatus.InvalidEncoding;
                }
                if (matchesPrevious && index <= LastLogIndex)
                {
                    var existing = entries[Slot(index)];
                    if (existing.Term != entryTerm && conflict == 0) conflict = index;
                    if (existing.Term == entryTerm && (existing.Length != (int)length || existing.Kind != (EntryKind)kind ||
                        !RuntimeBytes.Equal(logData + (long)Slot(index) * options.MaxCommandBytes, reader.Buffer + reader.Position, (int)length))) return DeterministicStatus.ProtocolConflict;
                }
                reader.Position += (int)length;
            }
            if (reader.Position != reader.Length) return DeterministicStatus.InvalidEncoding;
            if (term < CurrentTerm) { ReplyAppend(peer, false, previous, 0, LastLogIndex + 1); return DeterministicStatus.None; }
            if (conflict != 0 && conflict <= CommitIndex) return DeterministicStatus.ProtocolConflict;
            if (matchesPrevious && expected - SnapshotIndex > (uint)options.LogCapacity) return DeterministicStatus.CapacityExceeded;
            if (term > CurrentTerm || Role == DeterministicRaftRole.Candidate || (Role == DeterministicRaftRole.Leader && peers[peer].Id != LocalPeer)) Follow(term, peers[peer].Id);
            Leader = peers[peer].Id; ResetElection();
            if (!matchesPrevious)
            {
                ulong hint = previous < SnapshotIndex ? SnapshotIndex + 1 : Math.Min(previous, LastLogIndex + 1);
                ReplyAppend(peer, false, previous, 0, Math.Max(1UL, hint)); return DeterministicStatus.None;
            }
            if (conflict != 0) LastLogIndex = conflict - 1;
            for (int i = peerCount; i < knownCount; i++) AddKnown(known[i]);
            reader.Position = bodyStart;
            for (int i = 0; i < (int)count; i++)
            {
                reader.TryUInt(8, out ulong index); reader.TryUInt(8, out ulong entryTerm); reader.TryUInt(1, out ulong kind); reader.TryUInt(4, out ulong length);
                if (index > LastLogIndex)
                {
                    int slot = Slot(index); RuntimeBytes.Copy(reader.Buffer + reader.Position, logData + (long)slot * options.MaxCommandBytes, (int)length);
                    entries[slot] = new Entry { Index = index, Term = entryTerm, Kind = (EntryKind)kind, Length = (int)length }; LastLogIndex = index;
                }
                reader.Position += (int)length;
            }
            if (leaderCommit > CommitIndex) CommitIndex = Math.Max(CommitIndex, Math.Min(leaderCommit, expected));
            RefreshConfiguration();
            if (certificate != 0) { certifiedTerm = CurrentTerm; lastCertificate = now; }
            var applied = ApplyCommitted(); if (!applied.IsSuccess() && applied != DeterministicStatus.Pending) return applied;
            ReplyAppend(peer, true, previous, expected, expected + 1); return DeterministicStatus.None;
        }
        private DeterministicStatus HandleAppendReply(int peer, DeterministicReader reader)
        {
            if (reader.Length != 41) return DeterministicStatus.InvalidEncoding;
            reader.TryUInt(8, out ulong term); reader.TryUInt(1, out ulong success); reader.TryUInt(8, out ulong previous);
            reader.TryUInt(8, out ulong matched); reader.TryUInt(8, out ulong hint); reader.TryUInt(8, out ulong applied);
            if (success > 1 || matched > LastLogIndex) return DeterministicStatus.InvalidEncoding;
            if (term > CurrentTerm) { Follow(term); return DeterministicStatus.None; }
            if (term != CurrentTerm || Role != DeterministicRaftRole.Leader) return DeterministicStatus.None;
            peers[peer].LastAck = now;
            if (success != 0)
            {
                peers[peer].Match = Math.Max(peers[peer].Match, matched); peers[peer].Next = Math.Max(peers[peer].Next, matched + 1);
                peers[peer].Applied = Math.Max(peers[peer].Applied, Math.Min(applied, peers[peer].Match)); AdvanceCommit();
            }
            else if (previous + 1 == peers[peer].Next && previous > peers[peer].Match)
                peers[peer].Next = Math.Max(peers[peer].Match + 1, Math.Min(previous, Math.Max(1UL, hint)));
            if (Role == DeterministicRaftRole.Leader && (success == 0 || peers[peer].Next <= LastLogIndex)) SendReplication(peer);
            return DeterministicStatus.None;
        }
    }
}
