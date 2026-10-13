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

    /// <summary>Optional local invalidation token. Every successful mutation and restore must change the token.</summary>
    public interface IRevisionedCanonicalStateAdapter
    {
        ulong Revision { get; }
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
        /// <summary>Optional simulation clock. Every supplied clock must agree with the checkpoint boundary.</summary>
        public virtual bool TryGetAuthoritativeBoundary(out long boundary) { boundary = 0; return false; }
        /// <summary>Validate incoming bytes against their enclosing checkpoint boundary before publication.</summary>
        public virtual DeterministicStatus TryValidateAtBoundary(ref DeterministicReader reader, ref CanonicalScratch scratch, long boundary)
            => TryValidate(ref reader, ref scratch);
        public abstract DeterministicStatus TryRead(ref DeterministicReader reader, ref CanonicalScratch scratch);
        /// <summary>Optional derived-state dependencies; IDs must be unique, registered and acyclic.</summary>
        public virtual int DependencyCount => 0;
        public virtual uint DependencyStateId(int ordinal) => 0;
        public virtual DeterministicStatus TryPrepareCapture() => DeterministicStatus.None;
        /// <summary>Called after every logical state has been restored, also during recovery after a failed import.</summary>
        public virtual DeterministicStatus TryCompleteRestore(object historyOwner, long tick, ulong localLease) => DeterministicStatus.None;
        public virtual bool HasLocalHistory => false;
        public virtual int LocalHistoryCount => 0;
        public virtual long LocalRestoreCount => 0;
        /// <summary>Local leases are optional optimizations, excluded from canonical bytes and fingerprints.</summary>
        public virtual ulong CaptureLocal(object historyOwner, long tick) => 0;
        public virtual void ReleaseLocal(object historyOwner, long tick, ulong lease) { }
        /// <summary>Canonical body split into stable logical chunks. Concatenation must equal TryWrite exactly.</summary>
        public virtual int HistoryChunkCount => 1;
        public virtual ulong HistoryChunkLayoutFingerprint => 0x475043484e4b0001UL;
        public virtual int MaxHistoryChunkBytes(int ordinal) => ordinal == 0 ? MaxCanonicalBytes : 0;
        /// <summary>Local cache token only. Zero means untracked: serialize on every capture.</summary>
        public virtual ulong HistoryChunkRevision(int ordinal) => 0;
        public virtual DeterministicStatus TryWriteHistoryChunk(int ordinal, ref DeterministicWriter writer)
            => ordinal == 0 ? TryWrite(ref writer) : DeterministicStatus.OutOfRange;
    }

    public unsafe class DeterministicStateBinding<TAdapter> : DeterministicStateBinding
        where TAdapter : struct, ICanonicalStateAdapter
    {
        protected TAdapter adapter;
        public override DeterministicBackend Backend => adapter.Backend;
        public DeterministicStateBinding(uint stateId, ulong signature, int maxCanonicalBytes, int scratchBytes, TAdapter adapter)
            : base(stateId, signature, maxCanonicalBytes, scratchBytes)
        { this.adapter = adapter; }
        public override DeterministicStatus TryWrite(ref DeterministicWriter writer) => adapter.TryWrite(ref writer);
        public override DeterministicStatus TryValidate(ref DeterministicReader reader, ref CanonicalScratch scratch) => adapter.TryValidate(ref reader, ref scratch);
        public override DeterministicStatus TryRead(ref DeterministicReader reader, ref CanonicalScratch scratch) => adapter.TryRead(ref reader, ref scratch);
    }

    /// <summary>Statically constrained revision access: no boxing, reflection or allocation during capture.</summary>
    public sealed unsafe class RevisionedDeterministicStateBinding<TAdapter> : DeterministicStateBinding<TAdapter>
        where TAdapter : struct, ICanonicalStateAdapter, IRevisionedCanonicalStateAdapter
    {
        public RevisionedDeterministicStateBinding(uint stateId, ulong signature, int maxCanonicalBytes, int scratchBytes, TAdapter adapter)
            : base(stateId, signature, maxCanonicalBytes, scratchBytes, adapter) { }
        public override ulong HistoryChunkRevision(int ordinal) => ordinal == 0 ? adapter.Revision : 0;
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
        private readonly int[] completionOrder;
        private int count, maxScratch;
        private bool disposed, faulted;
        private IDeterministicMemory encodedMemory, backupMemory, incomingMemory, scratchMemory;
        private byte* encoded, backup, incoming, workspace;
        private struct Record { internal int Offset, Length; }
        public bool IsFrozen { get; private set; }
        public bool IsFaulted => faulted;
        public int Count => count;
        public int MaxCanonicalBytes { get; private set; }
        public ulong Fingerprint { get; private set; }
        public DeterministicBackend Backend => context.Backend;
        public long ReservedBytes => (long)(encodedMemory?.Length ?? 0) + (backupMemory?.Length ?? 0) +
            (incomingMemory?.Length ?? 0) + (scratchMemory?.Length ?? 0);

        public DeterministicStateRegistry(DeterministicRuntimeContext context,
            DeterministicCompatibilityProfile profile, int stateCapacity)
        {
            if (context == null || !profile.IsValid || stateCapacity <= 0)
                DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            this.context = context; this.profile = profile;
            states = new DeterministicStateBinding[stateCapacity]; records = new Record[stateCapacity];
            completionOrder = new int[stateCapacity];
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
            // Stable topological order. StateId sorting remains the canonical wire order.
            var completed = new bool[count];
            for (int position = 0; position < count; position++)
            {
                int candidate = -1;
                for (int i = 0; i < count; i++)
                {
                    if (completed[i]) continue;
                    bool ready = true; var state = states[i];
                    if (state.DependencyCount < 0) return DeterministicStatus.InvalidConfiguration;
                    for (int d = 0; d < state.DependencyCount; d++)
                    {
                        uint id = state.DependencyStateId(d); int dependency = -1;
                        for (int p = 0; p < d; p++) if (state.DependencyStateId(p) == id) return DeterministicStatus.InvalidConfiguration;
                        for (int j = 0; j < count; j++) if (states[j].StateId == id) { dependency = j; break; }
                        if (id == 0 || dependency < 0 || dependency == i) return DeterministicStatus.InvalidConfiguration;
                        if (!completed[dependency]) ready = false;
                    }
                    if (ready) { candidate = i; break; }
                }
                if (candidate < 0) return DeterministicStatus.InvalidConfiguration;
                completed[candidate] = true; completionOrder[position] = candidate;
            }
            // Each portable packet remains bounded; aggregate staging memory has no Int32 arena limit.
            if (total > int.MaxValue - 15 || maxScratch > int.MaxValue - 15) return DeterministicStatus.CapacityExceeded;
            MaxCanonicalBytes = (int)total;
            try
            {
                encodedMemory = context.Memory.Allocate(MaxCanonicalBytes);
                backupMemory = context.Memory.Allocate(MaxCanonicalBytes);
                incomingMemory = context.Memory.Allocate(MaxCanonicalBytes);
                scratchMemory = context.Memory.Allocate(maxScratch);
                encoded = encodedMemory.Pointer; backup = backupMemory.Pointer; incoming = incomingMemory.Pointer;
                workspace = scratchMemory.Pointer;
            }
            catch
            {
                encodedMemory?.Dispose(); backupMemory?.Dispose(); incomingMemory?.Dispose(); scratchMemory?.Dispose();
                encodedMemory = backupMemory = incomingMemory = scratchMemory = null;
                encoded = backup = incoming = workspace = null;
                throw;
            }
            Fingerprint = hash; IsFrozen = true; return DeterministicStatus.None;
        }
        private DeterministicStatus Ready()
            => disposed ? DeterministicStatus.Disposed : faulted ? DeterministicStatus.ProtocolConflict :
                !IsFrozen ? DeterministicStatus.InvalidConfiguration : DeterministicStatus.None;
        internal DeterministicStateBinding HistoryState(int ordinal) => states[ordinal];
        internal DeterministicStatus TryPrepareHistoryCapture(long tick)
        {
            var status = Ready(); if (!status.IsSuccess()) return status;
            if (tick < 0) return DeterministicStatus.OutOfRange;
            for (int i = 0; i < count; i++)
            { status = states[completionOrder[i]].TryPrepareCapture(); if (!status.IsSuccess()) return status; }
            status = ResolveAuthoritativeBoundary(out long actual, out bool known); if (!status.IsSuccess()) return status;
            return known && actual != tick ? DeterministicStatus.OutOfRange : DeterministicStatus.None;
        }

        private DeterministicStatus WriteBody(ref DeterministicWriter writer, long tick)
        {
            if (!writer.Has(HeaderBytes)) return DeterministicStatus.BufferTooSmall;
            writer.TryUInt(Magic, 4); writer.TryUInt(1, 2); writer.TryUInt(Fingerprint, 8);
            writer.TryUInt((ulong)tick, 8); writer.TryUInt((uint)count, 4);
            for (int i = 0; i < count; i++)
            {
                var state = states[i]; var measure = DeterministicWriter.CreateMeasure();
                var status = state.TryWrite(ref measure); if (!status.IsSuccess()) return status;
                if ((uint)measure.Position > (uint)state.MaxCanonicalBytes) return DeterministicStatus.CapacityExceeded;
                if (!writer.Has(EntryHeaderBytes) || writer.Capacity - writer.Position - EntryHeaderBytes < measure.Position)
                    return DeterministicStatus.BufferTooSmall;
                writer.TryUInt(state.StateId, 4); writer.TryUInt(state.Signature, 8); writer.TryUInt((uint)measure.Position, 4);
                int start = writer.Position;
                writer.PremeasuredEnd = start + measure.Position;
                status = state.TryWrite(ref writer); writer.PremeasuredEnd = 0; if (!status.IsSuccess()) return status;
                if (writer.Position - start != measure.Position) return DeterministicStatus.ProtocolConflict;
            }
            return DeterministicStatus.None;
        }
        /// <summary>Bytes are borrowed until the next encode/import or disposal. Capture into a history/replay before advancing.</summary>
        public DeterministicStatus TryEncode(long tick, out byte* bytes, out int length)
        {
            bytes = null; length = 0; var status = Ready(); if (!status.IsSuccess()) return status;
            if (tick < 0) return DeterministicStatus.OutOfRange;
            for (int i = 0; i < count; i++)
            {
                status = states[completionOrder[i]].TryPrepareCapture(); if (!status.IsSuccess()) return status;
            }
            status = ResolveAuthoritativeBoundary(out long actualBoundary, out bool knownBoundary);
            if (!status.IsSuccess()) return status;
            if (knownBoundary && actualBoundary != tick) return DeterministicStatus.OutOfRange;
            // The frozen arena is bounded by the registered maxima; only each state's atomic writer needs measurement.
            var writer = new DeterministicWriter(encoded, MaxCanonicalBytes - FooterBytes);
            status = WriteBody(ref writer, tick); if (!status.IsSuccess()) return status;
            var complete = new DeterministicWriter(encoded, MaxCanonicalBytes, writer.Position);
            status = complete.TryUInt(DeterministicHash.Xxh64(encoded, writer.Position), FooterBytes); if (!status.IsSuccess()) return status;
            bytes = encoded; length = complete.Position; return DeterministicStatus.None;
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
                status = state.TryValidateAtBoundary(ref element, ref scratch, (long)tickValue); if (!status.IsSuccess()) return status;
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

        private DeterministicStatus ResolveAuthoritativeBoundary(out long boundary, out bool known)
        {
            boundary = 0; known = false;
            for (int i = 0; i < count; i++)
            {
                if (!states[i].TryGetAuthoritativeBoundary(out long candidate)) continue;
                if (candidate < 0) return DeterministicStatus.InvalidEncoding;
                if (known && candidate != boundary) return DeterministicStatus.ProtocolConflict;
                boundary = candidate; known = true;
            }
            return DeterministicStatus.None;
        }

        private DeterministicStatus ValidateAuthoritativeBoundary(long expectedTick)
        {
            var status = ResolveAuthoritativeBoundary(out long actualTick, out bool known);
            if (!status.IsSuccess()) return status;
            return known && actualTick != expectedTick ? DeterministicStatus.ProtocolConflict : DeterministicStatus.None;
        }

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
            => ReadCanonical(ref reader, out tick, -1, -1);
        internal DeterministicStatus TryReadCanonicalBoundary(ref DeterministicReader reader, out long tick, long expectedTick, int expectedLength)
            => ReadCanonical(ref reader, out tick, expectedTick, expectedLength);
        internal DeterministicStatus TryReadCanonicalBoundary(ref DeterministicReader reader, out long tick, long expectedTick, int expectedLength,
            object historyOwner, ulong[] localLeases, int leaseOffset)
            => ReadCanonical(ref reader, out tick, expectedTick, expectedLength, historyOwner, localLeases, leaseOffset);
        private DeterministicStatus CompleteRestore(object historyOwner, long tick, ulong[] localLeases, int leaseOffset)
        {
            for (int i = 0; i < count; i++)
            {
                int state = completionOrder[i];
                var status = states[state].TryCompleteRestore(historyOwner, tick, localLeases == null ? 0 : localLeases[leaseOffset + state]);
                if (!status.IsSuccess()) return status;
            }
            return DeterministicStatus.None;
        }
        internal bool HasLocalHistory
        {
            get { for (int i = 0; i < count; i++) if (states[i].HasLocalHistory) return true; return false; }
        }
        internal void CaptureLocal(object owner, long tick, ulong[] leases, int offset)
        { for (int i = 0; i < count; i++) leases[offset + i] = states[i].CaptureLocal(owner, tick); }
        internal void ReleaseLocal(object owner, long tick, ulong[] leases, int offset)
        { for (int i = 0; i < count; i++) { states[i].ReleaseLocal(owner, tick, leases[offset + i]); leases[offset + i] = 0; } }
        private DeterministicStatus ReadCanonical(ref DeterministicReader reader, out long tick, long expectedTick, int expectedLength,
            object historyOwner = null, ulong[] localLeases = null, int leaseOffset = 0)
        {
            tick = 0; var trial = reader;
            var status = Validate(ref trial, out long requestedTick); if (!status.IsSuccess()) return status;
            int size = trial.Position - reader.Position;
            if (expectedTick >= 0 && (requestedTick != expectedTick || size != expectedLength)) return DeterministicStatus.ProtocolConflict;
            RuntimeBytes.Copy(reader.Buffer + reader.Position, incoming, size);
            status = ResolveAuthoritativeBoundary(out long recoveryTick, out _); if (!status.IsSuccess()) return status;
            status = TryEncode(recoveryTick, out byte* previous, out int previousBytes); if (!status.IsSuccess()) return status;
            RuntimeBytes.Copy(previous, backup, previousBytes);
            status = Apply(incoming, true);
            if (status.IsSuccess()) status = CompleteRestore(historyOwner, requestedTick, localLeases, leaseOffset);
            // Completion can change an earlier state's clock, so check the entire
            // published world only after every completion callback has succeeded.
            if (status.IsSuccess()) status = ValidateAuthoritativeBoundary(requestedTick);
            if (!status.IsSuccess())
            {
                var recovery = Apply(backup, false);
                if (recovery.IsSuccess()) recovery = CompleteRestore(null, recoveryTick, null, 0);
                if (recovery.IsSuccess()) recovery = ValidateAuthoritativeBoundary(recoveryTick);
                if (!recovery.IsSuccess()) { faulted = true; return DeterministicStatus.ProtocolConflict; }
                return status;
            }
            reader = trial; tick = requestedTick; return DeterministicStatus.None;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            encodedMemory?.Dispose(); backupMemory?.Dispose(); incomingMemory?.Dispose(); scratchMemory?.Dispose();
            encodedMemory = backupMemory = incomingMemory = scratchMemory = null;
            encoded = backup = incoming = workspace = null;
        }
    }
}
