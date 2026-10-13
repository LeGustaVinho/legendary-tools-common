using LegendaryTools.FixedPoint;

namespace LegendaryTools.Deterministic
{
    public enum FixedQuaternionDeltaMode : byte { Forward, Reversible }
    public enum FixedQuaternionTransferKind : byte { Patch, Full }

    /// <summary>Borrowed, byte-aligned wire payload. Keep its buffer alive and immutable while it is a baseline.</summary>
    public readonly unsafe struct FixedQuaternionCheckpoint
    {
        public readonly byte* Buffer;
        public readonly int Length;
        public readonly ulong Id, ProfileFingerprint;
        public FixedQuaternionCheckpoint(byte* buffer, int length, ulong id, ulong profileFingerprint)
        { Buffer = buffer; Length = length; Id = id; ProfileFingerprint = profileFingerprint; }
        public bool IsValid => Buffer != null && Length >= 1 && Length <= 40;
        public ulong PayloadHash => IsValid ? DeterministicHash.Xxh64(Buffer, Length) : 0;
    }

    /// <summary>Atomic byte XOR patches. Reversal is exact within the selected wire encoding.</summary>
    public static unsafe class FixedQuaternionDelta
    {
        public const uint Magic = 0x31445146; // FQD1
        public const ushort Version = 1;
        public const int HeaderBytes = 50;
        public const int MaxPacketBytes = HeaderBytes + 80;

        private static DeterministicStatus Map(FixedStatus status)
        {
            if (status.IsSuccess()) return DeterministicStatus.None;
            if ((status & FixedStatus.ProfileMismatch) != 0) return DeterministicStatus.ProfileMismatch;
            if ((status & (FixedStatus.Uninitialized | FixedStatus.InvalidConfiguration)) != 0) return DeterministicStatus.InvalidConfiguration;
            if ((status & FixedStatus.BufferTooSmall) != 0) return DeterministicStatus.BufferTooSmall;
            if ((status & (FixedStatus.OutOfRange | FixedStatus.Overflow)) != 0) return DeterministicStatus.OutOfRange;
            return DeterministicStatus.InvalidEncoding;
        }
        public static bool TryCreateCheckpoint(byte* buffer, int capacity, ulong id, in FixedQuaternion value,
            FixedQuaternionNetworkProfile profile, out FixedQuaternionCheckpoint checkpoint, out FixedStatus status)
        {
            checkpoint = default; var writer = new FixedBufferWriter(buffer, capacity);
            if (!FixedQuaternionNetwork.TryWrite(ref writer, value, profile, out status)) return false;
            int bits = (int)(writer.BitPosition & 7);
            if (bits != 0) buffer[writer.BytesWritten - 1] &= (byte)((1 << bits) - 1);
            checkpoint = new FixedQuaternionCheckpoint(buffer, writer.BytesWritten, id, profile.SchemaFingerprint); return true;
        }
        private static DeterministicStatus Decode(in FixedQuaternionCheckpoint checkpoint, FixedQuaternionNetworkProfile profile, out FixedQuaternion value)
        {
            value = FixedQuaternion.NaN;
            if (!checkpoint.IsValid || !profile.IsValid) return DeterministicStatus.InvalidConfiguration;
            if (checkpoint.ProfileFingerprint != profile.SchemaFingerprint) return DeterministicStatus.ProfileMismatch;
            var reader = new FixedBufferReader(checkpoint.Buffer, checkpoint.Length);
            if (!FixedQuaternionNetwork.TryRead(ref reader, profile, out value, out var status)) return Map(status);
            if ((reader.BitPosition + 7) / 8 != checkpoint.Length) return DeterministicStatus.InvalidEncoding;
            int bits = (int)(reader.BitPosition & 7);
            if (bits != 0 && (checkpoint.Buffer[checkpoint.Length - 1] & (0xff << bits)) != 0) return DeterministicStatus.InvalidEncoding;
            return DeterministicStatus.None;
        }
        private static int Changed(in FixedQuaternionCheckpoint a, in FixedQuaternionCheckpoint b, out ulong mask)
        {
            mask = 0; int count = 0;
            for (int i = 0; i < a.Length; i++) if (a.Buffer[i] != b.Buffer[i]) { mask |= 1UL << i; count++; }
            return count;
        }
        public static DeterministicStatus TryWrite(ref DeterministicWriter writer, in FixedQuaternionCheckpoint source,
            in FixedQuaternionCheckpoint target, FixedQuaternionNetworkProfile profile, out FixedQuaternionTransferKind kind,
            FixedQuaternionDeltaMode mode = FixedQuaternionDeltaMode.Forward)
        {
            kind = default;
            if (mode > FixedQuaternionDeltaMode.Reversible) return DeterministicStatus.InvalidConfiguration;
            var status = Decode(source, profile, out _); if (!status.IsSuccess()) return status;
            status = Decode(target, profile, out _); if (!status.IsSuccess()) return status;
            int fullBytes = target.Length + (mode == FixedQuaternionDeltaMode.Reversible ? source.Length : 0);
            ulong mask = 0; int maskBytes = (source.Length + 7) / 8;
            int patchBytes = source.Length == target.Length ? maskBytes + Changed(source, target, out mask) : int.MaxValue;
            var selected = patchBytes < fullBytes ? FixedQuaternionTransferKind.Patch : FixedQuaternionTransferKind.Full;
            int total = HeaderBytes + (selected == FixedQuaternionTransferKind.Patch ? patchBytes : fullBytes);
            if (!writer.Has(total)) return DeterministicStatus.BufferTooSmall;
            // Stage the complete packet even if caller buffers alias a checkpoint; publication cannot invalidate source reads.
            byte* packet = stackalloc byte[MaxPacketBytes]; var staging = new DeterministicWriter(packet, MaxPacketBytes);
            staging.TryUInt(Magic, 4); staging.TryUInt(Version, 2); staging.TryUInt((byte)mode, 1); staging.TryUInt((byte)selected, 1);
            staging.TryUInt(profile.SchemaFingerprint, 8); staging.TryUInt(source.Id, 8); staging.TryUInt(target.Id, 8);
            staging.TryUInt(source.PayloadHash, 8); staging.TryUInt(target.PayloadHash, 8);
            staging.TryUInt((uint)source.Length, 1); staging.TryUInt((uint)target.Length, 1);
            if (selected == FixedQuaternionTransferKind.Patch)
            {
                staging.TryUInt(mask, maskBytes);
                for (int i = 0; i < source.Length; i++) if ((mask & (1UL << i)) != 0) staging.TryUInt((uint)(source.Buffer[i] ^ target.Buffer[i]), 1);
            }
            else
            {
                staging.TryBytes(target.Buffer, target.Length);
                if (mode == FixedQuaternionDeltaMode.Reversible) staging.TryBytes(source.Buffer, source.Length);
            }
            var trial = writer; status = trial.TryBytes(packet, staging.Position);
            if (status.IsSuccess()) { writer = trial; kind = selected; }
            return status;
        }
        public static DeterministicStatus TryApply(ref DeterministicReader reader, in FixedQuaternionCheckpoint baseline,
            FixedQuaternionNetworkProfile profile, byte* destination, int capacity, out FixedQuaternionCheckpoint checkpoint,
            out FixedQuaternion value, bool reverse = false)
        {
            checkpoint = default; value = FixedQuaternion.NaN;
            var status = Decode(baseline, profile, out _); if (!status.IsSuccess()) return status;
            if (destination == null || capacity < 0) return DeterministicStatus.InvalidConfiguration;
            if (!reader.Has(HeaderBytes)) return DeterministicStatus.BufferTooSmall;
            var trial = reader;
            trial.TryUInt(4, out var magic); trial.TryUInt(2, out var version); trial.TryUInt(1, out var modeValue); trial.TryUInt(1, out var kindValue);
            trial.TryUInt(8, out var fingerprint); trial.TryUInt(8, out var sourceId); trial.TryUInt(8, out var targetId);
            trial.TryUInt(8, out var sourceHash); trial.TryUInt(8, out var targetHash); trial.TryUInt(1, out var sourceLength); trial.TryUInt(1, out var targetLength);
            if (magic != Magic || version != Version || modeValue > (byte)FixedQuaternionDeltaMode.Reversible || kindValue > (byte)FixedQuaternionTransferKind.Full ||
                sourceLength < 1 || sourceLength > 40 || targetLength < 1 || targetLength > 40) return DeterministicStatus.InvalidEncoding;
            if (fingerprint != profile.SchemaFingerprint) return DeterministicStatus.ProfileMismatch;
            var mode = (FixedQuaternionDeltaMode)modeValue; var kind = (FixedQuaternionTransferKind)kindValue;
            if (reverse && mode != FixedQuaternionDeltaMode.Reversible) return DeterministicStatus.InvalidConfiguration;
            if (baseline.Id != (reverse ? targetId : sourceId) || (ulong)baseline.Length != (reverse ? targetLength : sourceLength) || baseline.PayloadHash != (reverse ? targetHash : sourceHash)) return DeterministicStatus.ProtocolConflict;
            int length = (int)(reverse ? sourceLength : targetLength);
            if (capacity < length) return DeterministicStatus.BufferTooSmall;
            // Do not overwrite unread following fields in a borrowed datagram. In-place baseline replacement is allowed.
            if (destination < reader.Buffer + reader.Length && destination + length > reader.Buffer) return DeterministicStatus.InvalidConfiguration;
            byte* after = stackalloc byte[40]; byte* before = stackalloc byte[40]; byte* result;
            if (kind == FixedQuaternionTransferKind.Patch)
            {
                if (sourceLength != targetLength) return DeterministicStatus.InvalidEncoding;
                status = trial.TryUInt((length + 7) / 8, out var mask); if (!status.IsSuccess()) return status;
                if ((mask >> length) != 0) return DeterministicStatus.InvalidEncoding;
                for (int i = 0; i < length; i++)
                {
                    result = after; result[i] = baseline.Buffer[i];
                    if ((mask & (1UL << i)) == 0) continue;
                    status = trial.TryUInt(1, out var xor); if (!status.IsSuccess()) return status;
                    if (xor == 0) return DeterministicStatus.InvalidEncoding;
                    result[i] ^= (byte)xor;
                }
                result = after;
            }
            else
            {
                int bytes = (int)targetLength + (mode == FixedQuaternionDeltaMode.Reversible ? (int)sourceLength : 0);
                if (!trial.Has(bytes)) return DeterministicStatus.BufferTooSmall;
                for (int i = 0; i < (int)targetLength; i++) after[i] = trial.Buffer[trial.Position++];
                if (DeterministicHash.Xxh64(after, (int)targetLength) != targetHash) return DeterministicStatus.InvalidEncoding;
                var afterView = new FixedQuaternionCheckpoint(after, (int)targetLength, targetId, fingerprint);
                status = Decode(afterView, profile, out _); if (!status.IsSuccess()) return status;
                if (mode == FixedQuaternionDeltaMode.Reversible)
                {
                    for (int i = 0; i < (int)sourceLength; i++) before[i] = trial.Buffer[trial.Position++];
                    if (DeterministicHash.Xxh64(before, (int)sourceLength) != sourceHash) return DeterministicStatus.InvalidEncoding;
                    var beforeView = new FixedQuaternionCheckpoint(before, (int)sourceLength, sourceId, fingerprint);
                    status = Decode(beforeView, profile, out _); if (!status.IsSuccess()) return status;
                }
                result = reverse ? before : after;
            }
            if (DeterministicHash.Xxh64(result, length) != (reverse ? sourceHash : targetHash)) return DeterministicStatus.InvalidEncoding;
            var candidate = new FixedQuaternionCheckpoint(result, length, reverse ? sourceId : targetId, fingerprint);
            status = Decode(candidate, profile, out var decoded); if (!status.IsSuccess()) return status;
            for (int i = 0; i < length; i++) destination[i] = result[i];
            checkpoint = new FixedQuaternionCheckpoint(destination, length, candidate.Id, fingerprint); value = decoded; reader = trial;
            return DeterministicStatus.None;
        }
    }
}
