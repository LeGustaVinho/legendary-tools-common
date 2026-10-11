using System;

namespace LegendaryTools.Deterministic
{
    /// <summary>Bounded checkpoint history. Captures are consecutive tick boundaries; returned bytes are borrowed.</summary>
    public sealed unsafe class DeterministicWorldHistory : IDisposable
    {
        private enum Format : byte { Empty, Full, Delta, Pages }
        private struct Frame
        {
            internal long Tick, BaseTick;
            internal ulong Hash;
            internal int Length, Stored;
            internal Format Format;
        }
        private readonly IDeterministicMemory memory;
        private readonly SnapshotPolicy policy;
        private readonly int retention, slots, maxBytes, interval, pageBytes, pagesPerFrame, pageCount;
        private Frame* frames;
        private byte* data, work, anchor;
        private int* pageRefs, freePages, framePages, stagedPages;
        private int freeCount;
        private long first = -1, newest = -1, anchorTick = -1;
        private int anchorLength;
        private bool disposed;
        public DeterministicBackend Backend { get; }
        public SnapshotPolicy Policy => policy;
        public int Capacity => retention;
        public int MaxCheckpointBytes => maxBytes;
        public long NewestTick => newest;
        public long OldestTick => newest < 0 ? -1 : Math.Max(first, newest - retention + 1);
        public int LivePages => policy == SnapshotPolicy.CopyOnWrite ? pageCount - freeCount : 0;
        /// <summary>Logical bytes reserved by the bounded arena, excluding allocator/object overhead.</summary>
        public int ReservedBytes => disposed ? 0 : memory.Length;

        public DeterministicWorldHistory(DeterministicRuntimeContext context, int maxCheckpointBytes,
            int capacity, SnapshotPolicy policy = SnapshotPolicy.KeyframesAndDeltas,
            int keyframeInterval = 16, int pageBytes = 4096)
        {
            if (context == null || maxCheckpointBytes <= 0 || capacity < 2 || policy > SnapshotPolicy.CopyOnWrite ||
                keyframeInterval <= 0 || keyframeInterval > capacity || pageBytes < 64 || (pageBytes & (pageBytes - 1)) != 0)
                DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            Backend = context.Backend; maxBytes = maxCheckpointBytes; retention = capacity;
            this.policy = policy; interval = keyframeInterval; this.pageBytes = pageBytes;
            slots = checked(capacity + (policy == SnapshotPolicy.KeyframesAndDeltas ? interval : 0));
            pagesPerFrame = checked((int)(((long)maxBytes + pageBytes - 1) / pageBytes));
            pageCount = policy == SnapshotPolicy.CopyOnWrite ? checked(pagesPerFrame * (slots + 1)) : 0;
            long metadata = Align16((long)sizeof(Frame) * slots);
            long bytes = metadata + Align16(maxBytes) * 2;
            if (policy == SnapshotPolicy.CopyOnWrite)
                bytes += (long)pageCount * pageBytes + (long)sizeof(int) * (pageCount * 2L + pagesPerFrame * (slots + 1L));
            else bytes += (long)slots * maxBytes;
            if (bytes > int.MaxValue - 15) DeterministicStatus.CapacityExceeded.ThrowIfFailure();
            memory = context.Memory.Allocate((int)bytes);
            frames = (Frame*)memory.Pointer; work = memory.Pointer + metadata; anchor = work + Align16(maxBytes);
            data = anchor + Align16(maxBytes);
            for (int i = 0; i < slots; i++) frames[i].Tick = -1;
            if (policy == SnapshotPolicy.CopyOnWrite)
            {
                pageRefs = (int*)(data + (long)pageCount * pageBytes); freePages = pageRefs + pageCount;
                framePages = freePages + pageCount; stagedPages = framePages + (long)slots * pagesPerFrame;
                for (int i = 0; i < pageCount; i++) freePages[i] = i;
                for (int i = 0; i < slots * pagesPerFrame; i++) framePages[i] = -1;
                freeCount = pageCount;
            }
        }
        private static long Align16(long value) => (value + 15) & ~15L;
        private int Slot(long tick) => (int)(tick % slots);
        private bool Available(long tick) => !disposed && newest >= 0 && tick >= OldestTick && tick <= newest && frames[Slot(tick)].Tick == tick;
        public DeterministicStatus TryGetHash(long tick, out ulong hash)
        {
            hash = 0; if (disposed) return DeterministicStatus.Disposed;
            if (!Available(tick)) return DeterministicStatus.HistoryExhausted;
            hash = frames[Slot(tick)].Hash; return DeterministicStatus.None;
        }
        public DeterministicStatus TryGetStoredBytes(long tick, out int bytes)
        {
            bytes = 0; if (disposed) return DeterministicStatus.Disposed;
            if (!Available(tick)) return DeterministicStatus.HistoryExhausted;
            bytes = frames[Slot(tick)].Stored; return DeterministicStatus.None;
        }
        public DeterministicStatus TryCapture(DeterministicStateRegistry registry, long tick)
        {
            if (registry == null || registry.Backend != Backend) return DeterministicStatus.ProfileMismatch;
            var status = registry.TryEncode(tick, out byte* packet, out int length); if (!status.IsSuccess()) return status;
            return TryCapture(tick, packet, length);
        }
        public DeterministicStatus TryCapture(long tick, byte* packet, int length)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (tick < 0 || (newest >= 0 && (newest == long.MaxValue || tick != newest + 1))) return DeterministicStatus.OutOfRange;
            if (length < 0 || length > maxBytes || (packet == null && length != 0)) return DeterministicStatus.BufferTooSmall;
            int slot = Slot(tick); var frame = new Frame { Tick = tick, BaseTick = -1, Length = length, Hash = DeterministicHash.Xxh64(packet, length) };
            if (policy == SnapshotPolicy.CopyOnWrite)
            {
                var status = CapturePages(slot, packet, length, ref frame); if (!status.IsSuccess()) return status;
            }
            else
            {
                byte* target = data + (long)slot * maxBytes;
                bool keyframe = policy == SnapshotPolicy.FullPerTick || newest < 0 || tick % interval == 0 || anchorTick < 0;
                int delta = keyframe ? maxBytes : DeltaBytes(packet, length);
                if (keyframe || delta >= length)
                {
                    RuntimeBytes.Copy(packet, target, length); frame.Format = Format.Full; frame.Stored = length;
                    RuntimeBytes.Copy(packet, anchor, length); anchorLength = length; anchorTick = tick;
                }
                else
                {
                    WriteDelta(packet, length, target); frame.Format = Format.Delta; frame.Stored = delta; frame.BaseTick = anchorTick;
                }
            }
            frames[slot] = frame; if (first < 0) first = tick; newest = tick; return DeterministicStatus.None;
        }
        private int DeltaBytes(byte* packet, int length)
        {
            long bytes = 0; int cursor = 0;
            while (cursor < length)
            {
                if (cursor < anchorLength && packet[cursor] == anchor[cursor]) { cursor++; continue; }
                int start = cursor++;
                while (cursor < length && (cursor >= anchorLength || packet[cursor] != anchor[cursor])) cursor++;
                bytes += CanonicalBinary.VarUIntBytes((uint)start) + CanonicalBinary.VarUIntBytes((uint)(cursor - start)) + cursor - start;
                if (bytes >= length) return length;
            }
            return (int)bytes;
        }
        private void WriteDelta(byte* packet, int length, byte* target)
        {
            var writer = new DeterministicWriter(target, maxBytes); int cursor = 0;
            while (cursor < length)
            {
                if (cursor < anchorLength && packet[cursor] == anchor[cursor]) { cursor++; continue; }
                int start = cursor++;
                while (cursor < length && (cursor >= anchorLength || packet[cursor] != anchor[cursor])) cursor++;
                writer.TryVarUInt((uint)start); writer.TryVarUInt((uint)(cursor - start)); writer.TryBytes(packet + start, cursor - start);
            }
        }
        private void ReleasePages(int slot)
        {
            if (frames[slot].Format != Format.Pages) return;
            int count = (frames[slot].Length + pageBytes - 1) / pageBytes;
            for (int i = 0; i < count; i++)
            {
                int page = framePages[slot * pagesPerFrame + i];
                if (--pageRefs[page] == 0) freePages[freeCount++] = page;
                framePages[slot * pagesPerFrame + i] = -1;
            }
        }
        private bool CanShare(int previous, int index, byte* packet, int length, out int page)
        {
            page = -1;
            if (previous < 0 || frames[previous].Format != Format.Pages) return false;
            int offset = index * pageBytes, bytes = Math.Min(pageBytes, length - offset);
            if (offset >= frames[previous].Length || Math.Min(pageBytes, frames[previous].Length - offset) != bytes) return false;
            page = framePages[previous * pagesPerFrame + index];
            return RuntimeBytes.Equal(packet + offset, data + (long)page * pageBytes, bytes);
        }
        private DeterministicStatus CapturePages(int slot, byte* packet, int length, ref Frame frame)
        {
            int count = (int)(((long)length + pageBytes - 1) / pageBytes), previous = newest < 0 ? -1 : Slot(newest), needed = 0;
            for (int i = 0; i < count; i++) if (!CanShare(previous, i, packet, length, out _)) needed++;
            if (needed > freeCount) return DeterministicStatus.SnapshotCapacityExceeded;
            for (int i = 0; i < count; i++)
            {
                if (!CanShare(previous, i, packet, length, out int page))
                {
                    page = freePages[--freeCount]; byte* target = data + (long)page * pageBytes;
                    RuntimeBytes.Clear(target, pageBytes); RuntimeBytes.Copy(packet + i * pageBytes, target, Math.Min(pageBytes, length - i * pageBytes));
                }
                pageRefs[page]++; stagedPages[i] = page;
            }
            ReleasePages(slot);
            for (int i = 0; i < count; i++) framePages[slot * pagesPerFrame + i] = stagedPages[i];
            frame.Format = Format.Pages; frame.Stored = checked(needed * pageBytes); return DeterministicStatus.None;
        }
        private DeterministicStatus Decode(long tick, byte* target, out int length)
        {
            length = 0; int slot = Slot(tick); var frame = frames[slot];
            if (frame.Tick != tick || frame.Format == Format.Empty) return DeterministicStatus.HistoryExhausted;
            length = frame.Length;
            if (frame.Format == Format.Full) RuntimeBytes.Copy(data + (long)slot * maxBytes, target, length);
            else if (frame.Format == Format.Pages)
            {
                int count = (int)(((long)length + pageBytes - 1) / pageBytes);
                for (int i = 0; i < count; i++)
                    RuntimeBytes.Copy(data + (long)framePages[slot * pagesPerFrame + i] * pageBytes,
                        target + i * pageBytes, Math.Min(pageBytes, length - i * pageBytes));
            }
            else
            {
                var basis = frames[Slot(frame.BaseTick)];
                if (basis.Tick != frame.BaseTick || basis.Format != Format.Full) return DeterministicStatus.HistoryExhausted;
                RuntimeBytes.Clear(target, length); RuntimeBytes.Copy(data + (long)Slot(frame.BaseTick) * maxBytes, target, Math.Min(length, basis.Length));
                var reader = new DeterministicReader(data + (long)slot * maxBytes, frame.Stored); int previousEnd = 0;
                while (reader.Position < reader.Length)
                {
                    var status = reader.TryVarUInt(out ulong offset); if (!status.IsSuccess()) return status;
                    status = reader.TryVarUInt(out ulong size); if (!status.IsSuccess()) return status;
                    if (offset < (uint)previousEnd || size == 0 || offset > (uint)length || size > (uint)length - offset || !reader.Has((int)size))
                        return DeterministicStatus.InvalidEncoding;
                    RuntimeBytes.Copy(reader.Buffer + reader.Position, target + (int)offset, (int)size);
                    reader.Position += (int)size; previousEnd = (int)(offset + size);
                }
            }
            return DeterministicHash.Xxh64(target, length) == frame.Hash ? DeterministicStatus.None : DeterministicStatus.InvalidEncoding;
        }
        public DeterministicStatus TryRestore(long tick, out byte* packet, out int length)
        {
            packet = null; length = 0; if (disposed) return DeterministicStatus.Disposed;
            if (!Available(tick)) return DeterministicStatus.HistoryExhausted;
            var status = Decode(tick, work, out length); if (status.IsSuccess()) packet = work; return status;
        }
        public DeterministicStatus TryRestore(DeterministicStateRegistry registry, long tick)
        {
            if (registry == null || registry.Backend != Backend) return DeterministicStatus.ProfileMismatch;
            var status = TryRestore(tick, out byte* packet, out int length); if (!status.IsSuccess()) return status;
            var reader = new DeterministicReader(packet, length);
            status = registry.TryReadCanonical(ref reader, out long decodedTick);
            return status.IsSuccess() && (decodedTick != tick || reader.Position != length) ? DeterministicStatus.ProtocolConflict : status;
        }
        public DeterministicStatus TryTruncate(long tick)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (!Available(tick)) return DeterministicStatus.HistoryExhausted;
            if (policy == SnapshotPolicy.KeyframesAndDeltas)
            {
                var frame = frames[Slot(tick)]; long basis = frame.Format == Format.Full ? tick : frame.BaseTick;
                var status = Decode(basis, anchor, out anchorLength); if (!status.IsSuccess()) return status;
                anchorTick = basis;
            }
            for (int i = 0; i < slots; i++) if (frames[i].Tick > tick)
            { ReleasePages(i); frames[i] = default; frames[i].Tick = -1; }
            first = OldestTick; newest = tick; return DeterministicStatus.None;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; memory.Dispose(); frames = null; data = work = anchor = null;
            pageRefs = freePages = framePages = stagedPages = null;
        }
        public DeterministicStatus TryReset()
        {
            if (disposed) return DeterministicStatus.Disposed;
            for (int i = 0; i < slots; i++) { ReleasePages(i); frames[i] = default; frames[i].Tick = -1; }
            first = newest = anchorTick = -1; anchorLength = 0; return DeterministicStatus.None;
        }
    }
}
