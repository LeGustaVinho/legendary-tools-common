using System;

namespace LegendaryTools.Deterministic
{
    public sealed unsafe partial class DeterministicNetworkSession<TInput, TEquality, TEvent, TCodec>
        where TInput : unmanaged where TEquality : unmanaged, IDeterministicEquality<TInput>
        where TEvent : unmanaged where TCodec : unmanaged, IDeterministicCodec<TInput>
    {
        private const uint CheckpointMagic = 0x31434453u;
        private ulong CheckpointFingerprint => DeterministicHash.Mix(Runner.Fingerprint ^ ((ulong)codec.SchemaId << 16 | codec.Version));
        private DeterministicStatus WriteCheckpointBody(ref DeterministicWriter writer)
        {
            var status = Runner.History.TryRestore(appliedBoundary, out byte* world, out int length); if (!status.IsSuccess()) return status;
            writer.TryUInt(CheckpointMagic, 4); writer.TryUInt(1, 2); writer.TryUInt(Raft.SessionId.Value, 8);
            writer.TryUInt(CheckpointFingerprint, 8); writer.TryUInt((uint)length, 4);
            status = writer.TryBytes(world, length); if (!status.IsSuccess()) return status;
            writer.TryUInt((byte)committedOwnerCount, 1);
            for (int i = 0; i < committedOwnerCount; i++)
            {
                DeterministicSessionProtocol.WritePeer(ref writer, committedOwners[i].Peer); writer.TryUInt(committedOwners[i].Active, 1);
                status = codec.TryWrite(ref writer, committedOwners[i].Carry); if (!status.IsSuccess()) return status;
            }
            return DeterministicStatus.None;
        }
        public DeterministicStatus TryWriteSnapshot(ref DeterministicWriter writer)
        {
            if (disposed) return DeterministicStatus.Disposed;
            var measure = DeterministicWriter.CreateMeasure(); var status = WriteCheckpointBody(ref measure); if (!status.IsSuccess()) return status;
            if (measure.Position > int.MaxValue - 8 || !writer.Has(measure.Position + 8)) return DeterministicStatus.BufferTooSmall;
            if (!writer.IsMeasuring)
            {
                status = Runner.History.TryRestore(appliedBoundary, out byte* world, out int length); if (!status.IsSuccess()) return status;
                byte* output = writer.Buffer + writer.Position;
                if ((ulong)world < (ulong)output + (uint)(measure.Position + 8) && (ulong)output < (ulong)world + (uint)length)
                    return DeterministicStatus.InvalidConfiguration;
            }
            var trial = writer; int start = trial.Position; status = WriteCheckpointBody(ref trial); if (!status.IsSuccess()) return status;
            trial.TryUInt(trial.IsMeasuring ? 0 : DeterministicHash.Xxh64(trial.Buffer + start, trial.Position - start), 8);
            writer = trial; return DeterministicStatus.None;
        }
        private DeterministicStatus ValidateCheckpoint(ref DeterministicReader reader, bool foreignSession,
            out long boundary, out int count, out int worldOffset, out int worldLength)
        {
            boundary = 0; count = worldOffset = worldLength = 0; var trial = reader; int start = trial.Position;
            if (!trial.Has(26)) return DeterministicStatus.BufferTooSmall;
            trial.TryUInt(4, out ulong magic); trial.TryUInt(2, out ulong version); trial.TryUInt(8, out ulong origin);
            trial.TryUInt(8, out ulong profile); trial.TryUInt(4, out ulong length);
            if (magic != CheckpointMagic || version != 1 || origin == 0 || length > (uint)state.MaxCanonicalBytes) return DeterministicStatus.InvalidEncoding;
            if (profile != CheckpointFingerprint) return DeterministicStatus.ProfileMismatch;
            if (foreignSession == (origin == Raft.SessionId.Value)) return DeterministicStatus.ProfileMismatch;
            if (!trial.Has((int)length)) return DeterministicStatus.BufferTooSmall;
            worldOffset = trial.Position - start; worldLength = (int)length;
            var world = new DeterministicReader(trial.Buffer + trial.Position, (int)length);
            var status = state.TryValidateCanonical(ref world, out boundary); if (!status.IsSuccess()) return status;
            if (world.Position != world.Length) return DeterministicStatus.InvalidEncoding;
            trial.Position += (int)length; status = trial.TryUInt(1, out ulong owners); if (!status.IsSuccess()) return status;
            if (owners == 0 || owners > (uint)runtimeOptions.MaxPlayers) return DeterministicStatus.InvalidEncoding;
            int active = 0; PlayerId previous = default; count = (int)owners;
            for (int i = 0; i < count; i++)
            {
                if (!trial.Has(17)) return DeterministicStatus.BufferTooSmall;
                DeterministicSessionProtocol.ReadPeer(ref trial, out var peer); trial.TryUInt(1, out ulong enabled);
                if (!peer.IsValid || enabled > 1 || (i > 0 && previous.CompareTo(peer.Player) >= 0)) return DeterministicStatus.InvalidEncoding;
                int before = trial.Position; status = codec.TryRead(ref trial, out TInput carry); if (!status.IsSuccess()) return status;
                if (trial.Position - before > maxInputBytes) return DeterministicStatus.InvalidEncoding;
                stagedOwners[i] = new Owner { Peer = peer, Carry = carry, Active = (byte)enabled }; active += (int)enabled; previous = peer.Player;
            }
            if (active == 0) return DeterministicStatus.InvalidEncoding;
            int checksumAt = trial.Position; status = trial.TryUInt(8, out ulong checksum); if (!status.IsSuccess()) return status;
            if (DeterministicHash.Xxh64(trial.Buffer + start, checksumAt - start) != checksum) return DeterministicStatus.InvalidEncoding;
            reader = trial; return DeterministicStatus.None;
        }
        DeterministicStatus IDeterministicRaftStateMachine.TryValidateSnapshot(ref DeterministicReader reader)
            => ValidateCheckpoint(ref reader, false, out _, out _, out _, out _);
        private DeterministicStatus InstallCheckpoint(ref DeterministicReader reader, bool foreignSession)
        {
            var trial = reader; var status = ValidateCheckpoint(ref trial, foreignSession, out long boundary, out int count, out int worldOffset, out int worldLength);
            if (!status.IsSuccess()) return status;
            if (boundary <= Runner.ConfirmedThrough) return DeterministicStatus.ProtocolConflict;
            var replacement = CreateRunner(stagedOwners, count, boundary, false);
            var world = new DeterministicReader(reader.Buffer + reader.Position + worldOffset, worldLength);
            status = state.TryReadCanonical(ref world, out long restored);
            if (!status.IsSuccess()) { replacement.Dispose(); return status; }
            status = replacement.History.TryReset(); if (status.IsSuccess()) status = replacement.History.TryCapture(state, boundary);
            if (!status.IsSuccess() || restored != boundary)
            {
                Runner.History.TryRestore(state, Runner.NextTick); replacement.Dispose(); return status.IsSuccess() ? DeterministicStatus.ProtocolConflict : status;
            }
            status = Runner.Events.TryRetractFrom(Runner.ConfirmedThrough + 1);
            if (!status.IsSuccess()) { Runner.History.TryRestore(state, Runner.NextTick); replacement.Dispose(); return status; }
            Runner.Dispose(); Runner = replacement; appliedBoundary = proposedBoundary = localCaptureStart = boundary;
            committedOwnerCount = count; ownerCount = 0;
            for (int i = 0; i < count; i++) { committedOwners[i] = stagedOwners[i]; if (stagedOwners[i].Active != 0) owners[ownerCount++] = stagedOwners[i]; }
            for (int i = 0; i <= runtimeOptions.HistoryTicks; i++) expectedHashes[i].Boundary = -1;
            int local = OwnerIndex(LocalPeer.Player);
            if (local >= 0 && owners[local].Peer == LocalPeer) for (int row = 0; row < bufferedRows; row++)
                if (outgoing[row].Tick >= boundary) Runner.TrySubmitInput(LocalPeer.Player, outgoing[row].Tick, outgoing[row].Value);
            Runner.SetSuspension(DeterministicStatus.QuorumLost); reader = trial; observer?.OnCheckpointInstalled(boundary); return DeterministicStatus.None;
        }
        DeterministicStatus IDeterministicRaftStateMachine.TryInstallSnapshot(ref DeterministicReader reader) => InstallCheckpoint(ref reader, false);
        /// <summary>Explicitly bootstrap a different session from a portable checkpoint before any election. Voting credentials are never imported.</summary>
        public DeterministicStatus TryRestoreAsNewSession(ref DeterministicReader reader)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (Raft.CurrentTerm != 0 || Raft.LastLogIndex != 0) return DeterministicStatus.Invalidated;
            return InstallCheckpoint(ref reader, true);
        }
    }
}
