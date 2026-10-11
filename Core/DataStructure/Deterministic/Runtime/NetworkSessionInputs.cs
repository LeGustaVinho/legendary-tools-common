using System;

namespace LegendaryTools.Deterministic
{
    public sealed unsafe partial class DeterministicNetworkSession<TInput, TEquality, TEvent, TCodec>
        where TInput : unmanaged where TEquality : unmanaged, IDeterministicEquality<TInput>
        where TEvent : unmanaged where TCodec : unmanaged, IDeterministicCodec<TInput>
    {
        private DeterministicStatus ProposeFrame()
        {
            if (proposedBoundary < appliedBoundary) proposedBoundary = appliedBoundary;
            var status = Runner.Inputs.TryGetActualFrame(proposedBoundary, out var frame); if (!status.IsSuccess()) return status;
            var measure = DeterministicWriter.CreateMeasure(); status = WriteFrame(ref measure, frame); if (!status.IsSuccess()) return status;
            if (measure.Position > raftOptions.MaxCommandBytes) return DeterministicStatus.CapacityExceeded;
            var writer = new DeterministicWriter(packet, maxDatagram); status = WriteFrame(ref writer, frame); if (!status.IsSuccess()) return status;
            status = Raft.TryPropose(new ReadOnlySpan<byte>(packet, writer.Position), out _); if (status.IsSuccess()) proposedBoundary++; return status;
        }
        private DeterministicStatus WriteFrame(ref DeterministicWriter writer, DeterministicInputFrame<TInput> frame)
        {
            writer.TryUInt(1, 1); writer.TryUInt((ulong)frame.Tick, 8); writer.TryUInt((byte)frame.Count, 1);
            for (int i = 0; i < frame.Count; i++)
            {
                int owner = OwnerIndex(frame[i].Player); if (owner < 0) return DeterministicStatus.ProtocolConflict;
                DeterministicSessionProtocol.WritePeer(ref writer, owners[owner].Peer);
                var status = codec.TryWrite(ref writer, frame[i].Value); if (!status.IsSuccess()) return status;
            }
            return DeterministicStatus.None;
        }
        public DeterministicStatus TryApply(ulong index, ulong term, ReadOnlySpan<byte> command)
        {
            if (disposed) return DeterministicStatus.Disposed;
            fixed (byte* bytes = command)
            {
                var reader = new DeterministicReader(bytes, command.Length);
                if (!reader.Has(10)) return DeterministicStatus.InvalidEncoding;
                reader.TryUInt(1, out ulong kind); reader.TryUInt(8, out ulong tick); reader.TryUInt(1, out ulong count);
                if (kind != 1 || tick >= long.MaxValue || tick != (ulong)appliedBoundary || count == 0 || count > (uint)runtimeOptions.MaxPlayers) return DeterministicStatus.ProtocolConflict;
                PeerId previous = default;
                for (int i = 0; i < (int)count; i++)
                {
                    if (!reader.Has(16)) return DeterministicStatus.InvalidEncoding;
                    DeterministicSessionProtocol.ReadPeer(ref reader, out var source);
                    if (!source.IsValid || (i > 0 && previous.Player.CompareTo(source.Player) >= 0)) return DeterministicStatus.InvalidEncoding;
                    previous = source; int start = reader.Position;
                    var status = codec.TryRead(ref reader, out TInput input); if (!status.IsSuccess()) return status;
                    if (reader.Position - start > maxInputBytes) return DeterministicStatus.InvalidEncoding;
                    stagedOwners[i] = new Owner { Peer = source, Active = 1, Carry = neutral }; frameInputs[i] = new DeterministicPlayerInput<TInput>(source.Player, input);
                }
                if (reader.Position != reader.Length) return DeterministicStatus.InvalidEncoding;
                Runner.SetSuspension(DeterministicStatus.None);
                bool changed = (int)count != ownerCount;
                for (int i = 0; i < (int)count && !changed; i++) changed = stagedOwners[i].Peer != owners[i].Peer;
                if (changed)
                {
                    var status = PrepareOwners(stagedOwners, (int)count, (long)tick); if (!status.IsSuccess()) return status;
                }
                for (int i = 0; i < (int)count; i++)
                { var status = Runner.TrySubmitInput(frameInputs[i].Player, (long)tick, frameInputs[i].Value); if (!status.IsSuccess()) return status; }
                var committed = Runner.TryCommitInputsThrough((long)tick); if (!committed.IsSuccess() && committed != DeterministicStatus.Pending) return committed;
                committed = Runner.TrySynchronizeRollback(); if (!committed.IsSuccess()) return committed;
                while (Runner.NextTick <= (long)tick)
                { committed = Runner.TryAdvance(1, out _); if (!committed.IsSuccess()) return committed; }
                appliedBoundary++; RememberOwners(true); return DeterministicStatus.None;
            }
        }
        private void SendInputs()
        {
            if (!Raft.IsAdmitted || !Raft.IsMembershipStable) return;
            var writer = new DeterministicWriter(packet, maxDatagram - DeterministicSessionProtocol.Overhead); writer.TryUInt(0, 4); uint count = 0;
            long limit = Runner.NextTick > long.MaxValue - runtimeOptions.HistoryTicks ? long.MaxValue : Runner.NextTick + runtimeOptions.HistoryTicks;
            for (long tick = appliedBoundary; tick < limit; tick++)
            {
                var value = outgoing[(int)(tick % bufferedRows)]; if (value.Tick != tick) continue;
                int bytes = codec.Measure(value.Value); if (bytes < 0 || bytes > maxInputBytes || !writer.Has(8 + bytes)) break;
                writer.TryUInt((ulong)tick, 8); if (!codec.TryWrite(ref writer, value.Value).IsSuccess()) break; count++;
            }
            if (count == 0) return;
            for (int i = 0; i < 4; i++) packet[i] = (byte)(count >> (8 * i));
            if (topology == DeterministicSessionTopology.AuthoritativeHost)
            { if (Raft.Leader.IsValid && Raft.Leader != LocalPeer) Raft.TrySendApplication(Raft.Leader, InputMessage, new ReadOnlySpan<byte>(packet, writer.Position)); }
            else for (int i = 0; i < ownerCount; i++) if (owners[i].Peer != LocalPeer) Raft.TrySendApplication(owners[i].Peer, InputMessage, new ReadOnlySpan<byte>(packet, writer.Position));
        }
        private DeterministicStatus ReceiveInputs(PeerId sender, ReadOnlySpan<byte> body)
        {
            int owner = OwnerIndex(sender.Player);
            if (owner < 0 || owners[owner].Peer != sender || !Raft.IsMembershipStable) return DeterministicStatus.NotFound;
            fixed (byte* bytes = body)
            {
                var reader = new DeterministicReader(bytes, body.Length); var status = reader.TryUInt(4, out ulong count); if (!status.IsSuccess()) return status;
                if (count > (uint)bufferedRows) return DeterministicStatus.InvalidEncoding;
                long previous = -1;
                for (int i = 0; i < (int)count; i++)
                {
                    status = reader.TryUInt(8, out ulong tick); if (!status.IsSuccess()) return status;
                    if (tick >= long.MaxValue || (long)tick <= previous) return DeterministicStatus.InvalidEncoding;
                    int start = reader.Position; status = codec.TryRead(ref reader, out TInput input); if (!status.IsSuccess()) return status;
                    if (reader.Position - start > maxInputBytes) return DeterministicStatus.InvalidEncoding;
                    incomingInputs[i] = new BufferedInput { Tick = (long)tick, Value = input }; previous = (long)tick;
                }
                if (reader.Position != reader.Length) return DeterministicStatus.InvalidEncoding;
                for (int i = 0; i < (int)count; i++)
                {
                    if (incomingInputs[i].Tick < appliedBoundary) continue;
                    status = Runner.Inputs.CanSubmit(sender.Player, incomingInputs[i].Tick, incomingInputs[i].Value);
                    if (status == DeterministicStatus.HistoryExhausted) continue;
                    if (!status.IsSuccess()) return status;
                }
                for (int i = 0; i < (int)count; i++) if (incomingInputs[i].Tick >= appliedBoundary)
                {
                    status = Runner.TrySubmitInput(sender.Player, incomingInputs[i].Tick, incomingInputs[i].Value);
                    if (!status.IsSuccess() && status != DeterministicStatus.HistoryExhausted) return status;
                }
                return DeterministicStatus.None;
            }
        }
        private void SendStateHash()
        {
            if (Raft.Role != DeterministicRaftRole.Leader || !Raft.HasQuorum || !Runner.History.TryGetHash(appliedBoundary, out ulong hash).IsSuccess()) return;
            var writer = new DeterministicWriter(packet, maxDatagram); writer.TryUInt(Raft.CurrentTerm, 8); writer.TryUInt((ulong)appliedBoundary, 8); writer.TryUInt(hash, 8);
            for (int i = 0; i < ownerCount; i++) if (owners[i].Peer != LocalPeer) Raft.TrySendApplication(owners[i].Peer, StateHashMessage, new ReadOnlySpan<byte>(packet, writer.Position));
        }
        private DeterministicStatus ReceiveHash(PeerId sender, ReadOnlySpan<byte> body)
        {
            if (sender != Raft.Leader || body.Length != 24) return DeterministicStatus.NotFound;
            fixed (byte* bytes = body)
            {
                var reader = new DeterministicReader(bytes, body.Length); reader.TryUInt(8, out ulong term); reader.TryUInt(8, out ulong tick); reader.TryUInt(8, out ulong hash);
                if (term != Raft.CurrentTerm || tick > long.MaxValue || (long)tick < Runner.History.OldestTick) return DeterministicStatus.None;
                expectedHashes[(int)(tick % (uint)(runtimeOptions.HistoryTicks + 1))] = new ExpectedHash { Boundary = (long)tick, Hash = hash, Term = term };
                return DeterministicStatus.None;
            }
        }
        private DeterministicStatus CheckHashes()
        {
            for (int i = 0; i <= runtimeOptions.HistoryTicks; i++)
            {
                var expected = expectedHashes[i];
                if (expected.Boundary < Runner.History.OldestTick || expected.Boundary > appliedBoundary || expected.Term != Raft.CurrentTerm) continue;
                var status = Runner.History.TryGetHash(expected.Boundary, out ulong actual); if (!status.IsSuccess()) return status;
                if (actual != expected.Hash) return DeterministicStatus.ProtocolConflict;
            }
            return DeterministicStatus.None;
        }
        public DeterministicStatus TryHandle(PeerId sender, byte kind, ReadOnlySpan<byte> body)
        {
            switch (kind)
            {
                case InputMessage: return ReceiveInputs(sender, body);
                case StateHashMessage: return ReceiveHash(sender, body);
                case JoinMessage: return body.Length == 0 ? ReceiveJoin(sender) : DeterministicStatus.InvalidEncoding;
                case RedirectMessage:
                    if (!joinRequested || body.Length != 16 || sender != joinTarget) return DeterministicStatus.NotFound;
                    fixed (byte* bytes = body)
                    { var reader = new DeterministicReader(bytes, 16); DeterministicSessionProtocol.ReadPeer(ref reader, out var target); if (!target.IsValid) return DeterministicStatus.InvalidEncoding; joinTarget = target; lastJoin = -1; return DeterministicStatus.None; }
                default: return DeterministicStatus.NotFound;
            }
        }
        private DeterministicStatus ReceiveJoin(PeerId sender)
        {
            if (Raft.Role != DeterministicRaftRole.Leader || !Raft.HasQuorum)
            {
                if (Raft.Leader.IsValid && Raft.Leader != LocalPeer)
                { var writer = new DeterministicWriter(packet, maxDatagram); DeterministicSessionProtocol.WritePeer(ref writer, Raft.Leader); Raft.TrySendApplication(sender, RedirectMessage, new ReadOnlySpan<byte>(packet, writer.Position)); }
                return DeterministicStatus.Pending;
            }
            PeerId* voters = stackalloc PeerId[64]; Raft.TryGetVoters(new Span<PeerId>(voters, 64), out int count);
            for (int i = 0; i < count; i++) if (voters[i] == sender) { Raft.TryCompact(); return Raft.TryReplicateTo(sender, true); }
            for (int i = 0; i < waitingCount; i++) if (waiting[i] == sender) return DeterministicStatus.None;
            if (waitingCount == raftOptions.MaxPeers) return DeterministicStatus.CapacityExceeded;
            bool knownPlayer = false; for (int i = 0; i < committedOwnerCount; i++) knownPlayer |= committedOwners[i].Peer.Player == sender.Player;
            if (!knownPlayer && committedOwnerCount == runtimeOptions.MaxPlayers) return DeterministicStatus.CapacityExceeded;
            var compact = Raft.TryCompact(); if (!compact.IsSuccess() && compact != DeterministicStatus.Pending) return compact;
            var status = Raft.TryAddLearner(sender); if (!status.IsSuccess()) return status;
            waiting[waitingCount++] = sender; return DeterministicStatus.None;
        }
        private void ProcessAdmissions()
        {
            if (!Raft.IsMembershipStable) return;
            PeerId* voters = stackalloc PeerId[64]; Raft.TryGetVoters(new Span<PeerId>(voters, 64), out int count);
            for (int pending = 0; pending < waitingCount; pending++)
            {
                bool admitted = false; for (int i = 0; i < count; i++) admitted |= voters[i] == waiting[pending];
                if (admitted) { waiting[pending--] = waiting[--waitingCount]; continue; }
                if (!Raft.TryGetProgress(waiting[pending], out ulong replicated, out ulong applied).IsSuccess() || replicated < Raft.CommitIndex || applied < Raft.CommitIndex) continue;
                int replace = -1; for (int i = 0; i < count; i++) if (voters[i].Player == waiting[pending].Player) { replace = i; break; }
                if (replace < 0) { if (count == runtimeOptions.MaxPlayers) continue; voters[count++] = waiting[pending]; }
                else voters[replace] = waiting[pending];
                Raft.TryBeginMembershipChange(new ReadOnlySpan<PeerId>(voters, count), out _); break;
            }
        }
    }
}
