using System;

namespace LegendaryTools.Deterministic
{
    public readonly struct DeterministicPlayerInput<T> where T : unmanaged
    {
        public readonly PlayerId Player;
        public readonly T Value;
        public DeterministicPlayerInput(PlayerId player, T value) { Player = player; Value = value; }
    }
    /// <summary>Versioned, bounded canonical replay: initial/keyframe checkpoints and ordered actual input frames.</summary>
    public sealed unsafe class DeterministicReplayArchive<TInput, TCodec> : IDisposable
        where TInput : unmanaged where TCodec : unmanaged, IDeterministicCodec<TInput>
    {
        private const uint Magic = 0x3152444cu;
        private const int HeaderBytes = 32, RecordHeaderBytes = 5, ChecksumBytes = 8;
        private enum Kind : byte { Input = 1, Checkpoint = 2 }
        // The derived checkpoint index fits the old alignment padding; it never enters canonical bytes.
        private struct Record { internal Kind Kind; internal int Offset, Bytes, Checkpoint; internal long Tick; }
        private readonly IDeterministicMemory memory;
        private readonly int maxBytes, maxRecords, maxPlayers;
        private readonly ulong fingerprint;
        private readonly TCodec codec;
        private byte* data;
        private Record* records, stagedRecords;
        private DeterministicPlayerInput<TInput>* decoded;
        private int used = HeaderBytes, count;
        private long initialTick, nextTick;
        private bool initialized, disposed;
        public int Count => count;
        public long InitialTick => initialTick;
        public long NextTick => nextTick;
        public ulong Fingerprint => fingerprint;
        public DeterministicReplayArchive(DeterministicRuntimeContext context, ulong fingerprint,
            int maxArchiveBytes, int maxRecords, int maxPlayers, TCodec codec = default)
        {
            if (context == null || fingerprint == 0 || maxArchiveBytes <= HeaderBytes + 32 || maxRecords <= 0 ||
                maxPlayers <= 0 || maxPlayers > 64 || codec.SchemaId == 0 || codec.Version == 0)
                DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            this.fingerprint = fingerprint; maxBytes = maxArchiveBytes; this.maxRecords = maxRecords;
            this.maxPlayers = maxPlayers; this.codec = codec;
            long bytesOffset = ((long)maxBytes + 15) & ~15L;
            long recordsBytes = ((long)sizeof(Record) * maxRecords + 15) & ~15L;
            long total = bytesOffset + recordsBytes * 2 + (long)sizeof(DeterministicPlayerInput<TInput>) * maxPlayers;
            if (total > int.MaxValue - 15) DeterministicStatus.CapacityExceeded.ThrowIfFailure();
            memory = context.Memory.Allocate((int)total); data = memory.Pointer;
            records = (Record*)(data + bytesOffset); stagedRecords = (Record*)((byte*)records + recordsBytes);
            decoded = (DeterministicPlayerInput<TInput>*)((byte*)stagedRecords + recordsBytes);
        }
        private void WriteHeader(ref DeterministicWriter writer, int recordCount, long tick)
        {
            writer.TryUInt(Magic, 4); writer.TryUInt(1, 2); writer.TryUInt(fingerprint, 8);
            writer.TryUInt(codec.SchemaId, 4); writer.TryUInt(codec.Version, 2);
            writer.TryUInt((ulong)tick, 8); writer.TryUInt((uint)recordCount, 4);
        }
        public DeterministicStatus TryBegin(DeterministicStateRegistry registry, long tick)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (initialized) return DeterministicStatus.Invalidated;
            if (registry == null || tick < 0 || tick == long.MaxValue) return DeterministicStatus.InvalidConfiguration;
            var status = registry.TryEncode(tick, out byte* packet, out int length); if (!status.IsSuccess()) return status;
            if (length > maxBytes - HeaderBytes - RecordHeaderBytes - ChecksumBytes * 2) return DeterministicStatus.CapacityExceeded;
            initialTick = nextTick = tick; var writer = new DeterministicWriter(data, maxBytes); WriteHeader(ref writer, 0, tick);
            initialized = true; status = Append(Kind.Checkpoint, tick, packet, length);
            if (!status.IsSuccess()) initialized = false; return status;
        }
        private DeterministicStatus Append(Kind kind, long tick, byte* payload, int bytes)
        {
            if (count == maxRecords || bytes > maxBytes - used - RecordHeaderBytes - ChecksumBytes * 2) return DeterministicStatus.CapacityExceeded;
            int offset = used; var writer = new DeterministicWriter(data, maxBytes, used);
            writer.TryUInt((byte)kind, 1); writer.TryUInt((uint)bytes, 4); writer.TryBytes(payload, bytes);
            writer.TryUInt(DeterministicHash.Xxh64(data + offset, RecordHeaderBytes + bytes), ChecksumBytes);
            records[count] = new Record { Kind = kind, Tick = tick, Offset = offset + RecordHeaderBytes, Bytes = bytes,
                Checkpoint = kind == Kind.Checkpoint ? count : records[count - 1].Checkpoint }; count++;
            used = writer.Position; return DeterministicStatus.None;
        }
        public DeterministicStatus TryAppendCheckpoint(DeterministicStateRegistry registry, long tick)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (!initialized || registry == null || tick != nextTick) return DeterministicStatus.OutOfRange;
            if (count > 0 && records[count - 1].Kind == Kind.Checkpoint && records[count - 1].Tick == tick) return DeterministicStatus.DuplicateKey;
            var status = registry.TryEncode(tick, out byte* packet, out int length); return status.IsSuccess() ? Append(Kind.Checkpoint, tick, packet, length) : status;
        }
        private DeterministicStatus WriteInputs(ref DeterministicWriter writer, long tick, ReadOnlySpan<DeterministicPlayerInput<TInput>> inputs, ulong stateHashAfter)
        {
            var status = writer.TryUInt((ulong)tick, 8); if (!status.IsSuccess()) return status;
            status = writer.TryUInt((byte)inputs.Length, 1); if (!status.IsSuccess()) return status;
            for (int i = 0; i < inputs.Length; i++)
            {
                status = writer.TryUInt(inputs[i].Player.Value, 8); if (!status.IsSuccess()) return status;
                status = codec.TryWrite(ref writer, inputs[i].Value); if (!status.IsSuccess()) return status;
            }
            return writer.TryUInt(stateHashAfter, 8);
        }
        public DeterministicStatus TryAppendInputs(long tick, ReadOnlySpan<DeterministicPlayerInput<TInput>> inputs, ulong stateHashAfter)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (!initialized || tick != nextTick || tick == long.MaxValue || inputs.Length > maxPlayers) return DeterministicStatus.OutOfRange;
            for (int i = 0; i < inputs.Length; i++) if (!inputs[i].Player.IsValid || (i > 0 && inputs[i - 1].Player.CompareTo(inputs[i].Player) >= 0)) return DeterministicStatus.InvalidConfiguration;
            var measure = DeterministicWriter.CreateMeasure(); var status = WriteInputs(ref measure, tick, inputs, stateHashAfter); if (!status.IsSuccess()) return status;
            if (measure.Position > maxBytes || count == maxRecords || measure.Position > maxBytes - used - RecordHeaderBytes - ChecksumBytes * 2) return DeterministicStatus.CapacityExceeded;
            // Only unused tail bytes are written until the record is complete. A failed codec cannot replace live data.
            int payloadStart = used + RecordHeaderBytes;
            // Keep the codec's record-relative cursor identical to the former staging writer.
            var writer = new DeterministicWriter(data + payloadStart, maxBytes - payloadStart - ChecksumBytes * 2);
            status = WriteInputs(ref writer, tick, inputs, stateHashAfter); if (!status.IsSuccess()) return status;
            int bytes = writer.Position;
            var header = new DeterministicWriter(data, maxBytes, used); header.TryUInt((byte)Kind.Input, 1); header.TryUInt((uint)bytes, 4);
            writer = new DeterministicWriter(data, maxBytes, payloadStart + bytes);
            writer.TryUInt(DeterministicHash.Xxh64(data + used, RecordHeaderBytes + bytes), ChecksumBytes);
            records[count] = new Record { Kind = Kind.Input, Tick = tick, Offset = payloadStart, Bytes = bytes, Checkpoint = records[count - 1].Checkpoint };
            count++; used = writer.Position; nextTick++; return DeterministicStatus.None;
        }
        public DeterministicStatus TryWriteCanonical(ref DeterministicWriter writer)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (!initialized) return DeterministicStatus.InvalidConfiguration;
            if (!writer.Has(used + ChecksumBytes)) return DeterministicStatus.BufferTooSmall;
            var header = new DeterministicWriter(data, HeaderBytes); WriteHeader(ref header, count, initialTick);
            var trial = writer; trial.TryBytes(data, used); trial.TryUInt(DeterministicHash.Xxh64(data, used), ChecksumBytes);
            writer = trial; return DeterministicStatus.None;
        }
        private DeterministicStatus ReadInputs(ref DeterministicReader reader, out long tick, out int players, out ulong hash)
        {
            tick = 0; players = 0; hash = 0; var trial = reader;
            var status = trial.TryUInt(8, out ulong raw); if (!status.IsSuccess()) return status;
            if (raw >= long.MaxValue) return DeterministicStatus.InvalidEncoding;
            tick = (long)raw; status = trial.TryUInt(1, out ulong count); if (!status.IsSuccess()) return status;
            if (count > (uint)maxPlayers) return DeterministicStatus.InvalidEncoding;
            players = (int)count; ulong previous = 0;
            for (int i = 0; i < players; i++)
            {
                status = trial.TryUInt(8, out ulong player); if (!status.IsSuccess()) return status;
                if (player == 0 || player <= previous) return DeterministicStatus.InvalidEncoding;
                status = codec.TryRead(ref trial, out TInput input); if (!status.IsSuccess()) return status;
                decoded[i] = new DeterministicPlayerInput<TInput>(new PlayerId(player), input); previous = player;
            }
            status = trial.TryUInt(8, out hash); if (!status.IsSuccess()) return status;
            reader = trial; return DeterministicStatus.None;
        }
        public DeterministicStatus TryReadCanonical(ref DeterministicReader reader, DeterministicStateRegistry checkpointValidator)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (checkpointValidator == null) return DeterministicStatus.InvalidConfiguration;
            var trial = reader; int start = trial.Position; if (!trial.Has(HeaderBytes)) return DeterministicStatus.BufferTooSmall;
            trial.TryUInt(4, out ulong magic); trial.TryUInt(2, out ulong version); trial.TryUInt(8, out ulong profile);
            trial.TryUInt(4, out ulong schema); trial.TryUInt(2, out ulong codecVersion); trial.TryUInt(8, out ulong firstTick); trial.TryUInt(4, out ulong recordCount);
            if (magic != Magic || version != 1 || firstTick >= long.MaxValue || recordCount == 0 || recordCount > (uint)maxRecords) return DeterministicStatus.InvalidEncoding;
            if (profile != fingerprint || schema != codec.SchemaId || codecVersion != codec.Version) return DeterministicStatus.ProfileMismatch;
            long at = (long)firstTick; int checkpoint = -1;
            for (int i = 0; i < (int)recordCount; i++)
            {
                if (!trial.Has(RecordHeaderBytes)) return DeterministicStatus.BufferTooSmall;
                int recordStart = trial.Position; trial.TryUInt(1, out ulong kind); trial.TryUInt(4, out ulong size);
                if (kind < 1 || kind > 2 || size > (uint)maxBytes || (i == 0 && kind != (byte)Kind.Checkpoint)) return DeterministicStatus.InvalidEncoding;
                if (!trial.Has((int)size)) return DeterministicStatus.BufferTooSmall;
                var element = new DeterministicReader(trial.Buffer + trial.Position, (int)size); long tick; DeterministicStatus status;
                if (kind == (byte)Kind.Checkpoint) status = checkpointValidator.TryValidateCanonical(ref element, out tick);
                else status = ReadInputs(ref element, out tick, out _, out _);
                if (!status.IsSuccess()) return status;
                if (tick != at || element.Position != element.Length) return DeterministicStatus.InvalidEncoding;
                if (kind == (byte)Kind.Checkpoint) checkpoint = i;
                stagedRecords[i] = new Record { Kind = (Kind)kind, Tick = tick, Offset = trial.Position - start, Bytes = (int)size, Checkpoint = checkpoint };
                trial.Position += (int)size; status = trial.TryUInt(ChecksumBytes, out ulong checksum); if (!status.IsSuccess()) return status;
                if (DeterministicHash.Xxh64(trial.Buffer + recordStart, RecordHeaderBytes + (int)size) != checksum) return DeterministicStatus.InvalidEncoding;
                if (kind == (byte)Kind.Input) at++;
                if (trial.Position - start > maxBytes - ChecksumBytes) return DeterministicStatus.CapacityExceeded;
            }
            int body = trial.Position - start; var footer = trial.TryUInt(ChecksumBytes, out ulong archiveHash); if (!footer.IsSuccess()) return footer;
            if (DeterministicHash.Xxh64(trial.Buffer + start, body) != archiveHash) return DeterministicStatus.InvalidEncoding;
            // Validation has completed; memmove also supports a packet borrowed from this owner.
            RuntimeBytes.Copy(trial.Buffer + start, data, body);
            for (int i = 0; i < (int)recordCount; i++) records[i] = stagedRecords[i];
            used = body; count = (int)recordCount; initialTick = (long)firstTick; nextTick = at; initialized = true; reader = trial; return DeterministicStatus.None;
        }
        public DeterministicStatus TryFindCheckpoint(long targetBoundary, out int recordIndex, out long boundary)
        {
            recordIndex = -1; boundary = -1; if (disposed) return DeterministicStatus.Disposed;
            if (!initialized || targetBoundary < initialTick || targetBoundary > nextTick) return DeterministicStatus.OutOfRange;
            int at = UpperBound(targetBoundary) - 1;
            if (at >= 0) { recordIndex = records[at].Checkpoint; boundary = records[recordIndex].Tick; }
            return recordIndex < 0 ? DeterministicStatus.NotFound : DeterministicStatus.None;
        }
        private int UpperBound(long tick)
        {
            int first = 0, last = count;
            while (first < last)
            { int middle = first + (last - first) / 2; if (records[middle].Tick <= tick) first = middle + 1; else last = middle; }
            return first;
        }
        public DeterministicStatus TryRestoreCheckpoint(DeterministicStateRegistry registry, int recordIndex, out long boundary)
        {
            boundary = -1; if (disposed) return DeterministicStatus.Disposed;
            if (registry == null || (uint)recordIndex >= (uint)count || records[recordIndex].Kind != Kind.Checkpoint) return DeterministicStatus.OutOfRange;
            var record = records[recordIndex]; var reader = new DeterministicReader(data + record.Offset, record.Bytes);
            return registry.TryReadCanonical(ref reader, out boundary);
        }
        public DeterministicStatus TryReadInputs(long tick, out ReadOnlySpan<DeterministicPlayerInput<TInput>> inputs, out ulong stateHashAfter)
        {
            inputs = default; stateHashAfter = 0; if (disposed) return DeterministicStatus.Disposed;
            int index = UpperBound(tick) - 1;
            if (index >= 0 && records[index].Kind == Kind.Input && records[index].Tick == tick)
            {
                var record = records[index]; var reader = new DeterministicReader(data + record.Offset, record.Bytes);
                var status = ReadInputs(ref reader, out _, out int players, out stateHashAfter);
                if (status.IsSuccess()) inputs = new ReadOnlySpan<DeterministicPlayerInput<TInput>>(decoded, players); return status;
            }
            return DeterministicStatus.NotFound;
        }
        /// <summary>Use a fresh runner at a restored checkpoint boundary when seeking backwards; confirmed effects are irreversible.</summary>
        public DeterministicStatus TryReplayTo<TEquality, TEvent>(DeterministicSimulationRunner<TInput, TEquality, TEvent> runner, long targetBoundary)
            where TEquality : unmanaged, IDeterministicEquality<TInput> where TEvent : unmanaged
            => TryReplayToBounded(runner, targetBoundary, int.MaxValue, out _);
        /// <summary>Resume replay on a fresh checkpoint runner, spending at most maxTicks in this call.</summary>
        public DeterministicStatus TryReplayToBounded<TEquality, TEvent>(DeterministicSimulationRunner<TInput, TEquality, TEvent> runner,
            long targetBoundary, int maxTicks, out int executedTicks)
            where TEquality : unmanaged, IDeterministicEquality<TInput> where TEvent : unmanaged
        {
            executedTicks = 0;
            if (disposed) return DeterministicStatus.Disposed;
            if (maxTicks < 0) return DeterministicStatus.OutOfRange;
            if (runner == null || runner.Fingerprint != fingerprint) return DeterministicStatus.ProfileMismatch;
            if (targetBoundary < runner.NextTick || targetBoundary > nextTick) return DeterministicStatus.OutOfRange;
            PlayerId* ids = stackalloc PlayerId[64];
            while (runner.NextTick < targetBoundary && executedTicks < maxTicks)
            {
                long tick = runner.NextTick; var status = TryReadInputs(tick, out var inputs, out ulong expectedHash); if (!status.IsSuccess()) return status;
                for (int i = 0; i < inputs.Length; i++) ids[i] = inputs[i].Player;
                status = runner.Inputs.TryApplyActiveRoster(new ReadOnlySpan<PlayerId>(ids, inputs.Length), tick); if (!status.IsSuccess()) return status;
                for (int i = 0; i < inputs.Length; i++)
                {
                    status = runner.TrySubmitInput(inputs[i].Player, tick, inputs[i].Value); if (!status.IsSuccess()) return status;
                }
                if (runner.Confirmation == DeterministicConfirmationMode.ExternalCommit)
                { status = runner.TryCommitInputsThrough(tick); if (!status.IsSuccess()) return status; }
                status = runner.TryAdvanceBounded(1, out int executed, out int replayed); if (!status.IsSuccess()) return status;
                if (executed != 1 || replayed != 0) return DeterministicStatus.ProtocolConflict;
                executedTicks++;
                status = runner.History.TryGetHash(tick + 1, out ulong actual); if (!status.IsSuccess()) return status;
                if (actual != expectedHash) return DeterministicStatus.ProtocolConflict;
            }
            return runner.NextTick < targetBoundary ? DeterministicStatus.Pending : DeterministicStatus.None;
        }
        public void Dispose()
        { if (disposed) return; disposed = true; memory.Dispose(); data = null; records = stagedRecords = null; decoded = null; }
    }
}
