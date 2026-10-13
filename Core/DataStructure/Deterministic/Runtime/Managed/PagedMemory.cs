using System;

namespace LegendaryTools.Deterministic
{
    /// <summary>Host page pool backed by bounded slabs. No individual allocation or byte offset needs to exceed Int32.</summary>
    public sealed unsafe class DeterministicPagedMemory : IDisposable
    {
        private struct Page { internal int References, NextFree, Length; internal ulong Hash; }
        private readonly DeterministicRuntimeContext context;
        private readonly IDeterministicMemory[] slabs;
        private readonly DeterministicArray<Page> pages;
        private readonly int pagesPerSlab, maxPages;
        private int allocatedPages, freeHead = -1, livePages;
        private long reservedBytes;
        private bool disposed;
        public int PageBytes { get; }
        public int SlabBytes { get; }
        public long CapacityBytes { get; }
        public long ReservedBytes => disposed ? 0 : reservedBytes;
        public int LivePages => disposed ? 0 : livePages;
        public long MetadataBytes => disposed ? 0 : (long)maxPages * sizeof(Page) + (long)slabs.Length * IntPtr.Size;
        public DeterministicPagedMemory(DeterministicRuntimeContext context, long capacityBytes, int pageBytes = 4096,
            int slabBytes = 16 * 1024 * 1024)
        {
            if (context == null || pageBytes < 64 || (pageBytes & (pageBytes - 1)) != 0 || slabBytes < pageBytes ||
                slabBytes % pageBytes != 0 || capacityBytes < pageBytes || capacityBytes / pageBytes > int.MaxValue)
                throw new ArgumentException("Paged storage requires a bounded page/slab budget.");
            this.context = context; PageBytes = pageBytes; SlabBytes = slabBytes;
            maxPages = (int)(capacityBytes / pageBytes); CapacityBytes = (long)maxPages * pageBytes;
            pagesPerSlab = slabBytes / pageBytes;
            slabs = new IDeterministicMemory[(int)(((long)maxPages + pagesPerSlab - 1) / pagesPerSlab)];
            pages = new DeterministicArray<Page>(new BufferOptions(maxPages, snapshotSlots: 0));
        }
        private byte* Pointer(int page) => slabs[page / pagesPerSlab].Pointer + (long)(page % pagesPerSlab) * PageBytes;
        public DeterministicStatus TryAcquire(byte* source, int length, ulong hash, out int page)
        {
            page = -1; if (disposed) return DeterministicStatus.Disposed;
            if (length < 0 || length > PageBytes || (source == null && length != 0)) return DeterministicStatus.OutOfRange;
            int next = freeHead >= 0 ? freeHead : allocatedPages;
            if (next >= maxPages) return DeterministicStatus.SnapshotCapacityExceeded;
            int slab = next / pagesPerSlab;
            if (slabs[slab] == null)
            {
                int count = Math.Min(pagesPerSlab, maxPages - slab * pagesPerSlab);
                var memory = context.Memory.Allocate(checked(count * PageBytes));
                slabs[slab] = memory; reservedBytes += memory.Length;
            }
            if (freeHead >= 0) freeHead = pages[next].NextFree; else allocatedPages++;
            if (length != 0) RuntimeBytes.Copy(source, Pointer(next), length);
            pages[next] = new Page { References = 1, NextFree = -1, Length = length, Hash = hash };
            livePages++; page = next; return DeterministicStatus.None;
        }
        public DeterministicStatus TryAddReference(int page)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if ((uint)page >= (uint)allocatedPages) return DeterministicStatus.InvalidOwner;
            var value = pages[page];
            if (value.References <= 0) return DeterministicStatus.Invalidated;
            if (value.References == int.MaxValue) return DeterministicStatus.CapacityExceeded;
            value.References++; pages[page] = value; return DeterministicStatus.None;
        }
        public DeterministicStatus TryRelease(int page)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if ((uint)page >= (uint)allocatedPages) return DeterministicStatus.InvalidOwner;
            var value = pages[page]; if (value.References <= 0) return DeterministicStatus.Invalidated;
            value.References--;
            if (value.References == 0) { value.NextFree = freeHead; freeHead = page; livePages--; }
            pages[page] = value; return DeterministicStatus.None;
        }
        public DeterministicStatus TryRead(int page, out byte* bytes, out int length, out ulong hash)
        {
            bytes = null; length = 0; hash = 0;
            if (disposed) return DeterministicStatus.Disposed;
            if ((uint)page >= (uint)allocatedPages) return DeterministicStatus.InvalidOwner;
            var value = pages[page]; if (value.References <= 0) return DeterministicStatus.Invalidated;
            bytes = Pointer(page); length = value.Length; hash = value.Hash; return DeterministicStatus.None;
        }
        public bool EqualsBytes(int page, byte* bytes, int length, ulong hash)
        {
            var status = TryRead(page, out var previous, out int previousLength, out ulong previousHash);
            if (!status.IsSuccess() || length != previousLength || hash != previousHash) return false;
            for (int i = 0; i < length; i++) if (previous[i] != bytes[i]) return false;
            return true;
        }
        public void Dispose()
        {
            if (disposed) return;
            for (int i = 0; i < slabs.Length; i++) slabs[i]?.Dispose();
            pages.Dispose(); disposed = true; reservedBytes = 0; livePages = 0;
        }
    }
}
