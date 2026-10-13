using System;

namespace LegendaryTools.Deterministic
{
    /// <summary>Incremental canonical body chunks in a slab-backed COW page pool. Portable packets are assembled only for cold restore/export.</summary>
    public sealed unsafe class DeterministicPagedWorldHistory : IDeterministicWorldHistory
    {
        private struct Plan { internal int State, Ordinal, MaxBytes, PageOffset, Pages; }
        private struct Chunk { internal int Length, Pages; internal ulong Revision, Hash; }
        private struct Frame { internal long Tick; internal ulong Hash; internal int Stored; }
        private const uint Magic = 0x3157444cu;
        public const uint IncrementalHashAlgorithm = 2;
        private readonly DeterministicStateRegistry registry;
        private readonly Plan[] plans;
        private readonly DeterministicArray<Chunk> chunks;
        private readonly DeterministicArray<Frame> frames;
        private readonly DeterministicArray<int> pageIds;
        private readonly DeterministicPagedMemory pool;
        private readonly DeterministicRuntimeContext context;
        private readonly int slots, pagesPerFrame;
        private readonly IDeterministicMemory scratch;
        private IDeterministicMemory portable;
        private long first = -1, newest = -1;
        private bool disposed;
        public DeterministicBackend Backend => registry.Backend;
        public uint HashAlgorithmId => IncrementalHashAlgorithm;
        public ulong HashConfigurationFingerprint { get; }
        public SnapshotPolicy Policy => SnapshotPolicy.CopyOnWrite;
        public int Capacity { get; }
        public int MaxCheckpointBytes => registry.MaxCanonicalBytes;
        public long NewestTick => newest;
        public long OldestTick => newest < 0 ? -1 : Math.Max(first, newest - Capacity + 1);
        public int LivePages => pool.LivePages;
        public int LastSerializedChunks { get; private set; }
        public int LastReusedChunks { get; private set; }
        public long LastSerializedBytes { get; private set; }
        public long ReservedBytes => disposed ? 0 : pool.ReservedBytes + pool.MetadataBytes + scratch.Length +
            (portable == null ? 0 : portable.Length) + (long)slots * (sizeof(Frame) + plans.Length * sizeof(Chunk) + pagesPerFrame * sizeof(int));
        public long LocalHistoryReservedBytes => 0;
        public DeterministicPagedWorldHistory(DeterministicRuntimeContext context, DeterministicStateRegistry registry,
            int capacity, long pageBudgetBytes, int pageBytes = 4096, int slabBytes = 16 * 1024 * 1024)
        {
            if (context == null || registry == null || !registry.IsFrozen || registry.Backend != context.Backend || capacity < 2 || capacity == int.MaxValue)
                throw new ArgumentException("Incremental history needs a frozen compatible registry and retained boundaries.");
            if (pageBytes < 64 || (pageBytes & (pageBytes - 1)) != 0 || slabBytes < pageBytes || slabBytes % pageBytes != 0)
                throw new ArgumentException("Invalid page/slab geometry.");
            this.context = context; this.registry = registry; Capacity = capacity; slots = capacity + 1;
            int count = 0, maxScratch = 0; long totalPages = 0;
            ulong layout = DeterministicHash.Mix(0x4750484153480002UL);
            for (int state = 0; state < registry.Count; state++)
            {
                var binding = registry.HistoryState(state); if (binding.HistoryChunkCount <= 0) throw new ArgumentException("Invalid chunk declaration.");
                count = checked(count + binding.HistoryChunkCount);
                layout = DeterministicHash.Mix(layout ^ binding.StateId); layout = DeterministicHash.Mix(layout ^ binding.Signature);
                layout = DeterministicHash.Mix(layout ^ (uint)binding.HistoryChunkCount);
                layout = DeterministicHash.Mix(layout ^ binding.HistoryChunkLayoutFingerprint);
            }
            HashConfigurationFingerprint = layout;
            plans = new Plan[count]; int index = 0;
            for (int state = 0; state < registry.Count; state++)
            {
                var binding = registry.HistoryState(state);
                for (int ordinal = 0; ordinal < binding.HistoryChunkCount; ordinal++)
                {
                    int bytes = binding.MaxHistoryChunkBytes(ordinal);
                    if (bytes < 0 || bytes > binding.MaxCanonicalBytes) throw new ArgumentException("Invalid chunk byte budget.");
                    int pages = (int)(((long)bytes + pageBytes - 1) / pageBytes);
                    if (totalPages + pages > int.MaxValue) throw new ArgumentException("Chunk directory exceeds its bounded index space.");
                    plans[index++] = new Plan { State = state, Ordinal = ordinal, MaxBytes = bytes, PageOffset = (int)totalPages, Pages = pages };
                    totalPages += pages; maxScratch = Math.Max(maxScratch, bytes);
                }
            }
            if ((long)slots * count > int.MaxValue || totalPages * slots > int.MaxValue) throw new ArgumentException("Frame directory exceeds its bounded index space.");
            pagesPerFrame = (int)totalPages;
            try
            {
                pool = new DeterministicPagedMemory(context, pageBudgetBytes, pageBytes, slabBytes);
                frames = new DeterministicArray<Frame>(new BufferOptions(slots, snapshotSlots: 0));
                chunks = new DeterministicArray<Chunk>(new BufferOptions(checked(slots * count), snapshotSlots: 0));
                pageIds = new DeterministicArray<int>(new BufferOptions(checked(slots * pagesPerFrame), snapshotSlots: 0));
                scratch = context.Memory.Allocate(Math.Max(1, maxScratch));
                for (int i = 0; i < slots; i++) frames[i] = new Frame { Tick = -1 };
            }
            catch { Dispose(); throw; }
        }
        private int Slot(long tick) => (int)(tick % slots);
        private bool Available(long tick) => !disposed && newest >= 0 && tick >= OldestTick && tick <= newest && frames[Slot(tick)].Tick == tick;
        private void Release(int slot)
        {
            for (int index = 0; index < plans.Length; index++)
            {
                int at = slot * plans.Length + index; var chunk = chunks[at];
                for (int p = 0; p < chunk.Pages; p++) pool.TryRelease(pageIds[slot * pagesPerFrame + plans[index].PageOffset + p]).ThrowIfFailure();
                chunks[at] = default;
            }
            frames[slot] = new Frame { Tick = -1 };
        }
        public DeterministicStatus TryCapture(DeterministicStateRegistry state, long tick)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (!ReferenceEquals(state, registry)) return DeterministicStatus.InvalidOwner;
            if (tick < 0 || tick == long.MaxValue || newest >= 0 && tick != newest + 1) return DeterministicStatus.OutOfRange;
            var status = registry.TryPrepareHistoryCapture(tick); if (!status.IsSuccess()) return status;
            int slot = Slot(tick), previousSlot = newest < 0 ? -1 : Slot(newest);
            Release(slot);
            bool committed = false;
            try
            {
                status = CaptureBody(tick, slot, previousSlot); committed = status.IsSuccess(); return status;
            }
            finally { if (!committed && frames[slot].Tick != tick) Release(slot); }
        }
        private DeterministicStatus CaptureBody(long tick, int slot, int previousSlot)
        {
            var status = DeterministicStatus.None;
            ulong hash = DeterministicHash.Mix(0x4750484953540002UL);
            hash = DeterministicHash.Mix(hash ^ registry.Fingerprint); hash = DeterministicHash.Mix(hash ^ (ulong)tick);
            hash = DeterministicHash.Mix(hash ^ HashConfigurationFingerprint);
            int stored = 0, stateBytes = 0, previousState = -1, serialized = 0, reused = 0;
            long serializedBytes = 0;
            for (int index = 0; index < plans.Length; index++)
            {
                var plan = plans[index]; var binding = registry.HistoryState(plan.State);
                if (plan.State != previousState) { previousState = plan.State; stateBytes = 0; }
                ulong revision = binding.HistoryChunkRevision(plan.Ordinal);
                var old = previousSlot < 0 ? default : chunks[previousSlot * plans.Length + index];
                Chunk next;
                if (previousSlot >= 0 && revision != 0 && revision == old.Revision)
                {
                    reused++;
                    next = old;
                    for (int p = 0; p < next.Pages; p++)
                    {
                        int page = pageIds[previousSlot * pagesPerFrame + plan.PageOffset + p];
                        status = pool.TryAddReference(page); if (!status.IsSuccess()) break;
                        pageIds[slot * pagesPerFrame + plan.PageOffset + p] = page;
                        chunks[slot * plans.Length + index] = new Chunk { Pages = p + 1 };
                    }
                }
                else
                {
                    var writer = new DeterministicWriter(scratch.Pointer, plan.MaxBytes);
                    status = binding.TryWriteHistoryChunk(plan.Ordinal, ref writer);
                    if (!status.IsSuccess()) { Release(slot); return status; }
                    int length = writer.Position;
                    if (length < 0 || length > plan.MaxBytes) { Release(slot); return DeterministicStatus.CapacityExceeded; }
                    next = new Chunk { Length = length, Revision = revision, Hash = DeterministicHash.Xxh64(scratch.Pointer, length),
                        Pages = (int)(((long)length + pool.PageBytes - 1) / pool.PageBytes) };
                    serialized++; serializedBytes += length;
                    for (int p = 0; p < next.Pages; p++)
                    {
                        int lengthHere = Math.Min(pool.PageBytes, length - p * pool.PageBytes);
                        byte* source = scratch.Pointer + p * pool.PageBytes; ulong pageHash = DeterministicHash.Xxh64(source, lengthHere);
                        int page = p < old.Pages && previousSlot >= 0 ? pageIds[previousSlot * pagesPerFrame + plan.PageOffset + p] : -1;
                        if (page >= 0 && pool.EqualsBytes(page, source, lengthHere, pageHash)) status = pool.TryAddReference(page);
                        else status = pool.TryAcquire(source, lengthHere, pageHash, out page);
                        if (!status.IsSuccess()) break;
                        pageIds[slot * pagesPerFrame + plan.PageOffset + p] = page;
                        chunks[slot * plans.Length + index] = new Chunk { Pages = p + 1 };
                    }
                }
                if (!status.IsSuccess()) { Release(slot); return status; }
                chunks[slot * plans.Length + index] = next;
                if ((long)stateBytes + next.Length > binding.MaxCanonicalBytes || (long)stored + next.Length > MaxCheckpointBytes - 34L - 16L * registry.Count)
                { Release(slot); return DeterministicStatus.CapacityExceeded; }
                stateBytes += next.Length; stored += next.Length;
                hash = DeterministicHash.Mix(hash ^ binding.StateId); hash = DeterministicHash.Mix(hash ^ binding.Signature);
                hash = DeterministicHash.Mix(hash ^ (uint)plan.Ordinal); hash = DeterministicHash.Mix(hash ^ (uint)next.Length);
                hash = DeterministicHash.Mix(hash ^ next.Hash);
            }
            frames[slot] = new Frame { Tick = tick, Hash = hash, Stored = stored };
            LastSerializedChunks = serialized; LastReusedChunks = reused; LastSerializedBytes = serializedBytes;
            if (newest < 0) first = tick;
            long priorOldest = OldestTick; newest = tick;
            if (priorOldest >= 0 && priorOldest < OldestTick) { Release(Slot(priorOldest)); first = OldestTick; }
            return DeterministicStatus.None;
        }
        public DeterministicStatus TryGetHash(long tick, out ulong hash)
        { hash = 0; if (disposed) return DeterministicStatus.Disposed; if (!Available(tick)) return DeterministicStatus.HistoryExhausted; hash = frames[Slot(tick)].Hash; return DeterministicStatus.None; }
        public DeterministicStatus TryGetStoredBytes(long tick, out int bytes)
        { bytes = 0; if (disposed) return DeterministicStatus.Disposed; if (!Available(tick)) return DeterministicStatus.HistoryExhausted; bytes = frames[Slot(tick)].Stored; return DeterministicStatus.None; }
        public DeterministicStatus TryRestore(long tick, out byte* packet, out int length)
        {
            packet = null; length = 0; if (disposed) return DeterministicStatus.Disposed;
            if (!Available(tick)) return DeterministicStatus.HistoryExhausted;
            if (portable == null) portable = context.Memory.Allocate(MaxCheckpointBytes);
            var writer = new DeterministicWriter(portable.Pointer, MaxCheckpointBytes - 8);
            writer.TryUInt(Magic, 4); writer.TryUInt(1, 2); writer.TryUInt(registry.Fingerprint, 8);
            writer.TryUInt((ulong)tick, 8); writer.TryUInt((uint)registry.Count, 4);
            int slot = Slot(tick), index = 0;
            ulong verifiedHash = DeterministicHash.Mix(0x4750484953540002UL);
            verifiedHash = DeterministicHash.Mix(verifiedHash ^ registry.Fingerprint);
            verifiedHash = DeterministicHash.Mix(verifiedHash ^ (ulong)tick);
            verifiedHash = DeterministicHash.Mix(verifiedHash ^ HashConfigurationFingerprint);
            int verifiedBytes = 0;
            for (int state = 0; state < registry.Count; state++)
            {
                var binding = registry.HistoryState(state); int body = 0, start = index;
                while (index < plans.Length && plans[index].State == state) { body = checked(body + chunks[slot * plans.Length + index].Length); index++; }
                if (body > binding.MaxCanonicalBytes) return DeterministicStatus.ProtocolConflict;
                var status = writer.TryUInt(binding.StateId, 4); if (status.IsSuccess()) status = writer.TryUInt(binding.Signature, 8);
                if (status.IsSuccess()) status = writer.TryUInt((uint)body, 4); if (!status.IsSuccess()) return status;
                for (int c = start; c < index; c++)
                {
                    var chunk = chunks[slot * plans.Length + c];
                    if (chunk.Length < 0 || chunk.Length > plans[c].MaxBytes || chunk.Pages != ((long)chunk.Length + pool.PageBytes - 1) / pool.PageBytes)
                        return DeterministicStatus.InvalidEncoding;
                    int copied = 0;
                    for (int p = 0; p < chunk.Pages; p++)
                    {
                        status = pool.TryRead(pageIds[slot * pagesPerFrame + plans[c].PageOffset + p], out var bytes, out int size, out ulong pageHash);
                        if (!status.IsSuccess()) return status;
                        if (size < 0 || copied + (long)size > chunk.Length || DeterministicHash.Xxh64(bytes, size) != pageHash)
                            return DeterministicStatus.InvalidEncoding;
                        RuntimeBytes.Copy(bytes, scratch.Pointer + copied, size); copied += size;
                    }
                    if (copied != chunk.Length || DeterministicHash.Xxh64(scratch.Pointer, copied) != chunk.Hash) return DeterministicStatus.InvalidEncoding;
                    verifiedHash = DeterministicHash.Mix(verifiedHash ^ binding.StateId);
                    verifiedHash = DeterministicHash.Mix(verifiedHash ^ binding.Signature);
                    verifiedHash = DeterministicHash.Mix(verifiedHash ^ (uint)plans[c].Ordinal);
                    verifiedHash = DeterministicHash.Mix(verifiedHash ^ (uint)copied);
                    verifiedHash = DeterministicHash.Mix(verifiedHash ^ chunk.Hash);
                    verifiedBytes += copied;
                    status = writer.TryBytes(scratch.Pointer, copied); if (!status.IsSuccess()) return status;
                }
            }
            if (verifiedHash != frames[slot].Hash || verifiedBytes != frames[slot].Stored) return DeterministicStatus.InvalidEncoding;
            var complete = new DeterministicWriter(portable.Pointer, MaxCheckpointBytes, writer.Position);
            var footer = complete.TryUInt(DeterministicHash.Xxh64(portable.Pointer, writer.Position), 8); if (!footer.IsSuccess()) return footer;
            packet = portable.Pointer; length = complete.Position; return DeterministicStatus.None;
        }
        public DeterministicStatus TryRestore(DeterministicStateRegistry state, long tick)
        {
            if (state == null || state.Fingerprint != registry.Fingerprint || state.Backend != Backend) return DeterministicStatus.ProfileMismatch;
            var status = TryRestore(tick, out var packet, out int length); if (!status.IsSuccess()) return status;
            var reader = new DeterministicReader(packet, length); return state.TryReadCanonicalBoundary(ref reader, out _, tick, length);
        }
        // Raw packets lack chunk revision provenance. Import through the registered world, then capture its logical chunks.
        public DeterministicStatus TryCapture(long tick, byte* packet, int length) => disposed ? DeterministicStatus.Disposed : DeterministicStatus.InvalidConfiguration;
        public DeterministicStatus TryTruncate(long tick)
        {
            if (disposed) return DeterministicStatus.Disposed; if (!Available(tick)) return DeterministicStatus.HistoryExhausted;
            for (long remove = newest; remove > tick; remove--) Release(Slot(remove));
            newest = tick; return DeterministicStatus.None;
        }
        public DeterministicStatus TryReset()
        {
            if (disposed) return DeterministicStatus.Disposed;
            for (int i = 0; i < slots; i++) Release(i); newest = first = -1;
            LastSerializedChunks = LastReusedChunks = 0; LastSerializedBytes = 0; return DeterministicStatus.None;
        }
        public void Dispose()
        {
            if (disposed) return;
            if (frames != null && chunks != null && pageIds != null && pool != null) TryReset();
            pool?.Dispose(); frames?.Dispose(); chunks?.Dispose(); pageIds?.Dispose(); scratch?.Dispose(); portable?.Dispose(); disposed = true;
        }
    }
}
