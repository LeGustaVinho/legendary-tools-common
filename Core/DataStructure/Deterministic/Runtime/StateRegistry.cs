using System;

namespace LegendaryTools.Deterministic
{
    public unsafe interface ICanonicalStateAdapter
    {
        DeterministicBackend Backend { get; }
        DeterministicStatus TryWrite(ref DeterministicWriter writer);
        DeterministicStatus TryValidate(ref DeterministicReader reader, ref CanonicalScratch scratch);
        DeterministicStatus TryRead(ref DeterministicReader reader, ref CanonicalScratch scratch);
    }

    /// <summary>Registered once before Freeze. Signature includes the codec and all semantic policy/configuration values.</summary>
    public abstract unsafe class DeterministicStateBinding
    {
        public uint StateId { get; }
        public ulong Signature { get; }
        public int MaxCanonicalBytes { get; }
        public int ScratchBytes { get; }
        public abstract DeterministicBackend Backend { get; }
        protected DeterministicStateBinding(uint stateId, ulong signature, int maxCanonicalBytes, int scratchBytes)
        {
            if (stateId == 0 || signature == 0 || maxCanonicalBytes <= 0 || scratchBytes < 0)
                DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            StateId = stateId; Signature = signature; MaxCanonicalBytes = maxCanonicalBytes; ScratchBytes = scratchBytes;
        }
        public abstract DeterministicStatus TryWrite(ref DeterministicWriter writer);
        public abstract DeterministicStatus TryValidate(ref DeterministicReader reader, ref CanonicalScratch scratch);
        public abstract DeterministicStatus TryRead(ref DeterministicReader reader, ref CanonicalScratch scratch);
    }

    public sealed unsafe class DeterministicStateBinding<TAdapter> : DeterministicStateBinding
        where TAdapter : struct, ICanonicalStateAdapter
    {
        private TAdapter adapter;
        public override DeterministicBackend Backend => adapter.Backend;
        public DeterministicStateBinding(uint stateId, ulong signature, int maxCanonicalBytes, int scratchBytes, TAdapter adapter)
            : base(stateId, signature, maxCanonicalBytes, scratchBytes) { this.adapter = adapter; }
        public override DeterministicStatus TryWrite(ref DeterministicWriter writer) => adapter.TryWrite(ref writer);
        public override DeterministicStatus TryValidate(ref DeterministicReader reader, ref CanonicalScratch scratch) => adapter.TryValidate(ref reader, ref scratch);
        public override DeterministicStatus TryRead(ref DeterministicReader reader, ref CanonicalScratch scratch) => adapter.TryRead(ref reader, ref scratch);
    }

    /// <summary>Host orchestration shared by Managed and Burst worlds. Native simulation jobs must complete before access.</summary>
    public sealed unsafe class DeterministicStateRegistry : IDisposable
    {
        private const uint Magic = 0x3157444cu;
        private const int HeaderBytes = 26, EntryHeaderBytes = 16, FooterBytes = 8;
        private readonly DeterministicRuntimeContext context;
        private readonly DeterministicCompatibilityProfile profile;
        private readonly DeterministicStateBinding[] states;
        private readonly Record[] records;
        private int count, maxScratch;
        private bool disposed, faulted;
        private IDeterministicMemory memory;
        private byte* encoded, backup, incoming, workspace;
        private struct Record { internal int Offset, Length; }
        public bool IsFrozen { get; private set; }
        public bool IsFaulted => faulted;
        public int Count => count;
        public int MaxCanonicalBytes { get; private set; }
        public ulong Fingerprint { get; private set; }
        public DeterministicBackend Backend => context.Backend;

        public DeterministicStateRegistry(DeterministicRuntimeContext context,
            DeterministicCompatibilityProfile profile, int stateCapacity)
        {
            if (context == null || !profile.IsValid || stateCapacity <= 0)
                DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            this.context = context; this.profile = profile;
            states = new DeterministicStateBinding[stateCapacity]; records = new Record[stateCapacity];
        }
        public DeterministicStatus TryRegister(DeterministicStateBinding binding)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (IsFrozen) return DeterministicStatus.Invalidated;
            if (binding == null) return DeterministicStatus.InvalidConfiguration;
            if (binding.Backend != Backend) return DeterministicStatus.ProfileMismatch;
            for (int i = 0; i < count; i++) if (states[i].StateId == binding.StateId) return DeterministicStatus.DuplicateKey;
            if (count == states.Length) return DeterministicStatus.CapacityExceeded;
            states[count++] = binding; return DeterministicStatus.None;
        }
        public DeterministicStatus TryFreeze()
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (IsFrozen) return DeterministicStatus.None;
            if (count == 0) return DeterministicStatus.InvalidConfiguration;
            long total = HeaderBytes + FooterBytes + (long)count * EntryHeaderBytes;
            ulong hash = profile.Fingerprint;
            for (int i = 1; i < count; i++)
            {
                var value = states[i]; int j = i - 1;
                while (j >= 0 && states[j].StateId > value.StateId) { states[j + 1] = states[j]; j--; }
                states[j + 1] = value;
            }
            for (int i = 0; i < count; i++)
            {
                var state = states[i]; total += state.MaxCanonicalBytes;
                maxScratch = Math.Max(maxScratch, state.ScratchBytes);
                hash = DeterministicHash.Mix(hash ^ state.StateId); hash = DeterministicHash.Mix(hash ^ state.Signature);
            }
            if (total * 3 + maxScratch + 15 > int.MaxValue - 15) return DeterministicStatus.CapacityExceeded;
            MaxCanonicalBytes = (int)total;
            memory = context.Memory.Allocate(checked(MaxCanonicalBytes * 3 + maxScratch + 15));
            encoded = memory.Pointer; backup = encoded + MaxCanonicalBytes; incoming = backup + MaxCanonicalBytes;
            workspace = (byte*)(((ulong)(incoming + MaxCanonicalBytes) + 15) & ~15UL);
            Fingerprint = hash; IsFrozen = true; return DeterministicStatus.None;
        }
        private DeterministicStatus Ready()
            => disposed ? DeterministicStatus.Disposed : faulted ? DeterministicStatus.ProtocolConflict :
                !IsFrozen ? DeterministicStatus.InvalidConfiguration : DeterministicStatus.None;

        private DeterministicStatus WriteBody(ref DeterministicWriter writer, long tick)
        {
            writer.TryUInt(Magic, 4); writer.TryUInt(1, 2); writer.TryUInt(Fingerprint, 8);
            writer.TryUInt((ulong)tick, 8); writer.TryUInt((uint)count, 4);
            for (int i = 0; i < count; i++)
            {
                var state = states[i]; var measure = DeterministicWriter.CreateMeasure();
                var status = state.TryWrite(ref measure); if (!status.IsSuccess()) return status;
                if (measure.Position > state.MaxCanonicalBytes) return DeterministicStatus.CapacityExceeded;
                writer.TryUInt(state.StateId, 4); writer.TryUInt(state.Signature, 8); writer.TryUInt((uint)measure.Position, 4);
                int start = writer.Position; status = state.TryWrite(ref writer); if (!status.IsSuccess()) return status;
                if (writer.Position - start != measure.Position) return DeterministicStatus.ProtocolConflict;
            }
            return DeterministicStatus.None;
        }
        /// <summary>Bytes are borrowed until the next encode/import or disposal. Capture into a history/replay before advancing.</summary>
        public DeterministicStatus TryEncode(long tick, out byte* bytes, out int length)
        {
            bytes = null; length = 0; var status = Ready(); if (!status.IsSuccess()) return status;
            if (tick < 0) return DeterministicStatus.OutOfRange;
            var measure = DeterministicWriter.CreateMeasure(); status = WriteBody(ref measure, tick); if (!status.IsSuccess()) return status;
            if (measure.Position > MaxCanonicalBytes - FooterBytes) return DeterministicStatus.CapacityExceeded;
            var writer = new DeterministicWriter(encoded, MaxCanonicalBytes);
            status = WriteBody(ref writer, tick); if (!status.IsSuccess()) return status;
            writer.TryUInt(DeterministicHash.Xxh64(encoded, writer.Position), FooterBytes);
            bytes = encoded; length = writer.Position; return DeterministicStatus.None;
        }
        public DeterministicStatus TryWriteCanonical(ref DeterministicWriter writer, long tick)
        {
            var status = TryEncode(tick, out byte* bytes, out int length); if (!status.IsSuccess()) return status;
            return writer.TryBytes(bytes, length);
        }

        private DeterministicStatus Validate(ref DeterministicReader reader, out long tick)
        {
            tick = 0; var status = Ready(); if (!status.IsSuccess()) return status;
            var trial = reader; int start = trial.Position;
            if (!trial.Has(HeaderBytes)) return DeterministicStatus.BufferTooSmall;
            trial.TryUInt(4, out ulong magic); trial.TryUInt(2, out ulong version); trial.TryUInt(8, out ulong signature);
            trial.TryUInt(8, out ulong tickValue); trial.TryUInt(4, out ulong entries);
            if (magic != Magic || version != 1 || tickValue > long.MaxValue) return DeterministicStatus.InvalidEncoding;
            if (signature != Fingerprint || entries != (uint)count) return DeterministicStatus.ProfileMismatch;
            for (int i = 0; i < count; i++)
            {
                if (!trial.Has(EntryHeaderBytes)) return DeterministicStatus.BufferTooSmall;
                trial.TryUInt(4, out ulong id); trial.TryUInt(8, out ulong schema); trial.TryUInt(4, out ulong size);
                var state = states[i];
                if (id != state.StateId || schema != state.Signature) return DeterministicStatus.ProfileMismatch;
                if (size > (uint)state.MaxCanonicalBytes) return DeterministicStatus.InvalidEncoding;
                int length = (int)size;
                if (!trial.Has(length)) return DeterministicStatus.BufferTooSmall;
                var element = new DeterministicReader(trial.Buffer + trial.Position, length);
                var scratch = new CanonicalScratch(workspace, state.ScratchBytes);
                status = state.TryValidate(ref element, ref scratch); if (!status.IsSuccess()) return status;
                if (element.Position != length) return DeterministicStatus.InvalidEncoding;
                records[i] = new Record { Offset = trial.Position - start, Length = length }; trial.Position += length;
            }
            int body = trial.Position - start;
            if (body > MaxCanonicalBytes - FooterBytes) return DeterministicStatus.InvalidEncoding;
            status = trial.TryUInt(FooterBytes, out ulong checksum); if (!status.IsSuccess()) return status;
            if (DeterministicHash.Xxh64(trial.Buffer + start, body) != checksum) return DeterministicStatus.InvalidEncoding;
            reader = trial; tick = (long)tickValue; return DeterministicStatus.None;
        }
        public DeterministicStatus TryValidateCanonical(ref DeterministicReader reader, out long tick) => Validate(ref reader, out tick);

        private DeterministicStatus Apply(byte* packet, bool knownRecords)
        {
            int cursor = HeaderBytes;
            for (int i = 0; i < count; i++)
            {
                int offset, size;
                if (knownRecords) { offset = records[i].Offset; size = records[i].Length; }
                else
                {
                    var header = new DeterministicReader(packet + cursor, EntryHeaderBytes);
                    header.TryUInt(4, out _); header.TryUInt(8, out _); header.TryUInt(4, out ulong raw);
                    size = (int)raw; offset = cursor + EntryHeaderBytes; cursor = offset + size;
                }
                var element = new DeterministicReader(packet + offset, size);
                var scratch = new CanonicalScratch(workspace, states[i].ScratchBytes);
                var status = states[i].TryRead(ref element, ref scratch); if (!status.IsSuccess()) return status;
                if (element.Position != size) return DeterministicStatus.ProtocolConflict;
            }
            return DeterministicStatus.None;
        }
        /// <summary>Validate every state first; a failed publication restores the prior canonical world.</summary>
        public DeterministicStatus TryReadCanonical(ref DeterministicReader reader, out long tick)
        {
            tick = 0; var trial = reader;
            var status = Validate(ref trial, out long requestedTick); if (!status.IsSuccess()) return status;
            int size = trial.Position - reader.Position;
            RuntimeBytes.Copy(reader.Buffer + reader.Position, incoming, size);
            status = TryEncode(0, out byte* previous, out int previousBytes); if (!status.IsSuccess()) return status;
            RuntimeBytes.Copy(previous, backup, previousBytes);
            status = Apply(incoming, true);
            if (!status.IsSuccess())
            {
                if (!Apply(backup, false).IsSuccess()) { faulted = true; return DeterministicStatus.ProtocolConflict; }
                return status;
            }
            reader = trial; tick = requestedTick; return DeterministicStatus.None;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            memory?.Dispose(); encoded = backup = incoming = workspace = null;
        }
    }
}
