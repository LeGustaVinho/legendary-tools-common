using System;

namespace LegendaryTools.Deterministic
{
    public sealed unsafe partial class DeterministicRaftNode
    {
        private const uint SnapshotMagic = 0x3153444cu;
        private ulong incomingLeaderTerm;
        private DeterministicStatus WriteSnapshotBody(ref DeterministicWriter writer, ulong index, ulong term, ulong oldMask, ulong newMask)
        {
            writer.TryUInt(SnapshotMagic, 4); writer.TryUInt(1, 2); writer.TryUInt(fingerprint, 8); writer.TryUInt(index, 8); writer.TryUInt(term, 8);
            WriteSet(ref writer, oldMask); WriteSet(ref writer, newMask);
            var measure = DeterministicWriter.CreateMeasure(); var status = machine.TryWriteSnapshot(ref measure); if (!status.IsSuccess()) return status;
            if (measure.Position > options.MaxSnapshotBytes - 40) return DeterministicStatus.CapacityExceeded;
            writer.TryUInt((uint)measure.Position, 4); int start = writer.Position;
            status = machine.TryWriteSnapshot(ref writer); if (!status.IsSuccess()) return status;
            return writer.Position - start == measure.Position ? DeterministicStatus.None : DeterministicStatus.ProtocolConflict;
        }
        public DeterministicStatus TryCompact()
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (LastApplied <= SnapshotIndex) return DeterministicStatus.Pending;
            if (!TryTerm(LastApplied, out ulong term)) return DeterministicStatus.ProtocolConflict;
            ConfigurationAt(LastApplied, out ulong oldMask, out ulong newMask);
            var measure = DeterministicWriter.CreateMeasure(); var status = WriteSnapshotBody(ref measure, LastApplied, term, oldMask, newMask); if (!status.IsSuccess()) return status;
            if (measure.Position > options.MaxSnapshotBytes - 8) return DeterministicStatus.CapacityExceeded;
            var writer = new DeterministicWriter(snapshotStage, options.MaxSnapshotBytes);
            status = WriteSnapshotBody(ref writer, LastApplied, term, oldMask, newMask); if (!status.IsSuccess()) return status;
            writer.TryUInt(DeterministicHash.Xxh64(snapshotStage, writer.Position), 8);
            RuntimeBytes.Copy(snapshotStage, snapshot, writer.Position); snapshotLength = writer.Position;
            SnapshotIndex = LastApplied; SnapshotTerm = term; baseOldVoters = oldMask; baseNewVoters = newMask;
            RefreshConfiguration(); return DeterministicStatus.None;
        }
        private void SendSnapshot(int peer)
        {
            if (snapshotLength == 0) return;
            if (peers[peer].SnapshotIndex != SnapshotIndex)
            { peers[peer].SnapshotIndex = SnapshotIndex; peers[peer].SnapshotOffset = peers[peer].SnapshotSent = 0; }
            int chunk = transport.MaxDatagramBytes - DeterministicSessionProtocol.Overhead - 36;
            // A completed byte transfer may still wait for the application to install it. Repeat its final chunk.
            if (peers[peer].SnapshotOffset == snapshotLength) peers[peer].SnapshotSent = Math.Max(0, snapshotLength - chunk);
            long limit = Math.Min(snapshotLength, (long)peers[peer].SnapshotOffset + (long)chunk * options.SnapshotWindowChunks);
            while (peers[peer].SnapshotSent < limit)
            {
                int offset = peers[peer].SnapshotSent, bytes = Math.Min(snapshotLength - offset, chunk);
                var writer = new DeterministicWriter(payload, transport.MaxDatagramBytes - DeterministicSessionProtocol.Overhead);
                writer.TryUInt(CurrentTerm, 8); writer.TryUInt(SnapshotIndex, 8); writer.TryUInt(SnapshotTerm, 8); writer.TryUInt((uint)snapshotLength, 4);
                writer.TryUInt((uint)offset, 4); writer.TryUInt((uint)bytes, 4); writer.TryBytes(snapshot + offset, bytes);
                if (!Send(peers[peer].Id, (byte)Rpc.Snapshot, new ReadOnlySpan<byte>(payload, writer.Position)).IsSuccess()) break;
                peers[peer].SnapshotSent += bytes;
            }
        }
        private void ReplySnapshot(int peer, ulong index, int offset, bool complete)
        {
            var writer = new DeterministicWriter(payload, transport.MaxDatagramBytes);
            writer.TryUInt(CurrentTerm, 8); writer.TryUInt(index, 8); writer.TryUInt((uint)offset, 4); writer.TryUInt(complete ? 1UL : 0, 1); writer.TryUInt(LastApplied, 8);
            Send(peers[peer].Id, (byte)Rpc.SnapshotReply, new ReadOnlySpan<byte>(payload, writer.Position));
        }
        private DeterministicStatus HandleSnapshot(int peer, DeterministicReader reader)
        {
            if (!reader.Has(36)) return DeterministicStatus.InvalidEncoding;
            reader.TryUInt(8, out ulong term); reader.TryUInt(8, out ulong index); reader.TryUInt(8, out ulong includedTerm);
            reader.TryUInt(4, out ulong total); reader.TryUInt(4, out ulong offset); reader.TryUInt(4, out ulong bytes);
            if (term == 0 || index == 0 || index >= ulong.MaxValue - 1 || includedTerm == 0 || includedTerm > term ||
                total > (uint)options.MaxSnapshotBytes || total < 40 || offset > total || bytes > total - offset ||
                bytes == 0 || bytes != (uint)(reader.Length - reader.Position)) return DeterministicStatus.InvalidEncoding;
            if (term < CurrentTerm) { ReplySnapshot(peer, index, 0, false); return DeterministicStatus.None; }
            if (term > CurrentTerm || Role == DeterministicRaftRole.Candidate || Role == DeterministicRaftRole.Leader) Follow(term, peers[peer].Id);
            Leader = peers[peer].Id; ResetElection();
            if (index <= LastApplied) { ReplySnapshot(peer, index, (int)total, true); return DeterministicStatus.None; }
            bool same = snapshotIncomingIndex == index && snapshotIncomingTerm == includedTerm && incomingLeaderTerm == term && incomingLength == (int)total;
            if (!same)
            {
                if (offset != 0) { ReplySnapshot(peer, index, 0, false); return DeterministicStatus.None; }
                snapshotIncomingIndex = index; snapshotIncomingTerm = includedTerm; incomingLeaderTerm = term; incomingLength = (int)total; incomingReceived = 0;
            }
            if (offset > (uint)incomingReceived) { ReplySnapshot(peer, index, incomingReceived, false); return DeterministicStatus.None; }
            if (offset < (uint)incomingReceived)
            {
                if (bytes > (uint)incomingReceived - offset || !RuntimeBytes.Equal(snapshotIncoming + (int)offset, reader.Buffer + reader.Position, (int)bytes)) return DeterministicStatus.InvalidEncoding;
                if (incomingReceived < incomingLength)
                { ReplySnapshot(peer, index, incomingReceived, false); return DeterministicStatus.None; }
            }
            else { RuntimeBytes.Copy(reader.Buffer + reader.Position, snapshotIncoming + incomingReceived, (int)bytes); incomingReceived += (int)bytes; }
            if (incomingReceived < incomingLength) { ReplySnapshot(peer, index, incomingReceived, false); return DeterministicStatus.None; }
            var status = InstallSnapshot(index, includedTerm);
            if (status == DeterministicStatus.Pending) { ReplySnapshot(peer, index, incomingReceived, false); return status; }
            if (!status.IsSuccess()) return status;
            ReplySnapshot(peer, index, incomingReceived, true); return DeterministicStatus.None;
        }
        private DeterministicStatus InstallSnapshot(ulong expectedIndex, ulong expectedTerm)
        {
            var reader = new DeterministicReader(snapshotIncoming, incomingLength);
            if (!reader.Has(30)) return DeterministicStatus.InvalidEncoding;
            reader.TryUInt(4, out ulong magic); reader.TryUInt(2, out ulong version); reader.TryUInt(8, out ulong profile); reader.TryUInt(8, out ulong index); reader.TryUInt(8, out ulong term);
            if (magic != SnapshotMagic || version != 1 || index != expectedIndex || term != expectedTerm) return DeterministicStatus.InvalidEncoding;
            if (profile != fingerprint) return DeterministicStatus.ProfileMismatch;
            PeerId* known = stackalloc PeerId[64]; int knownCount = peerCount; for (int i = 0; i < peerCount; i++) known[i] = peers[i].Id;
            var status = ReadSet(ref reader, known, ref knownCount, false, out ulong oldMask, false); if (!status.IsSuccess()) return status;
            status = ReadSet(ref reader, known, ref knownCount, false, out ulong newMask, true); if (!status.IsSuccess()) return status;
            status = reader.TryUInt(4, out ulong length); if (!status.IsSuccess()) return status;
            if (length > (uint)options.MaxSnapshotBytes || !reader.Has((int)length + 8)) return DeterministicStatus.InvalidEncoding;
            var state = new DeterministicReader(snapshotIncoming + reader.Position, (int)length); var validate = state;
            status = machine.TryValidateSnapshot(ref validate); if (!status.IsSuccess()) return status;
            if (validate.Position != validate.Length) return DeterministicStatus.InvalidEncoding;
            reader.Position += (int)length; int checksumAt = reader.Position; reader.TryUInt(8, out ulong checksum);
            if (reader.Position != reader.Length || DeterministicHash.Xxh64(snapshotIncoming, checksumAt) != checksum) return DeterministicStatus.InvalidEncoding;
            bool keep = TryTerm(index, out ulong existingTerm) && existingTerm == term;
            if (index < CommitIndex && !keep) return DeterministicStatus.ProtocolConflict;
            status = machine.TryInstallSnapshot(ref state); if (!status.IsSuccess()) return status;
            if (state.Position != state.Length) return DeterministicStatus.ProtocolConflict;
            for (int i = peerCount; i < knownCount; i++) AddKnown(known[i]);
            if (!keep) LastLogIndex = index;
            SnapshotIndex = index; SnapshotTerm = term; LastApplied = index; CommitIndex = Math.Max(CommitIndex, index);
            baseOldVoters = oldMask; baseNewVoters = newMask;
            RuntimeBytes.Copy(snapshotIncoming, snapshot, incomingLength); snapshotLength = incomingLength;
            RefreshConfiguration(); return DeterministicStatus.None;
        }
        private DeterministicStatus HandleSnapshotReply(int peer, DeterministicReader reader)
        {
            if (reader.Length != 29) return DeterministicStatus.InvalidEncoding;
            reader.TryUInt(8, out ulong term); reader.TryUInt(8, out ulong index); reader.TryUInt(4, out ulong offset); reader.TryUInt(1, out ulong complete); reader.TryUInt(8, out ulong applied);
            if (complete > 1 || offset > (uint)options.MaxSnapshotBytes) return DeterministicStatus.InvalidEncoding;
            if (term > CurrentTerm) { Follow(term); return DeterministicStatus.None; }
            if (term != CurrentTerm || Role != DeterministicRaftRole.Leader || index != SnapshotIndex) return DeterministicStatus.None;
            if (offset > (uint)snapshotLength) return DeterministicStatus.InvalidEncoding;
            peers[peer].LastAck = now; peers[peer].SnapshotOffset = Math.Max(peers[peer].SnapshotOffset, (int)offset);
            peers[peer].SnapshotSent = Math.Max(peers[peer].SnapshotSent, peers[peer].SnapshotOffset);
            if (complete != 0)
            {
                peers[peer].Match = Math.Max(peers[peer].Match, index); peers[peer].Next = Math.Max(peers[peer].Next, index + 1);
                peers[peer].Applied = Math.Max(peers[peer].Applied, Math.Min(applied, peers[peer].Match)); AdvanceCommit();
            }
            if (Role == DeterministicRaftRole.Leader) SendReplication(peer); return DeterministicStatus.None;
        }
    }
}
