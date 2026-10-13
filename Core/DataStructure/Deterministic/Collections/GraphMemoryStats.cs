namespace LegendaryTools.Deterministic
{
    /// <summary>Payload bytes, excluding allocator/GC object headers and bookkeeping tables.</summary>
    public readonly struct GraphMemoryStats
    {
        public readonly long ReservedPayloadBytes, LivePayloadBytes;
        public readonly int LivePages, SharedLivePages, BufferSnapshotLeases;
        public GraphMemoryStats(long reserved, long live, int pages, int shared, int leases)
        { ReservedPayloadBytes = reserved; LivePayloadBytes = live; LivePages = pages; SharedLivePages = shared; BufferSnapshotLeases = leases; }
        public static GraphMemoryStats operator +(GraphMemoryStats a, GraphMemoryStats b) => new GraphMemoryStats(
            a.ReservedPayloadBytes + b.ReservedPayloadBytes, a.LivePayloadBytes + b.LivePayloadBytes,
            a.LivePages + b.LivePages, a.SharedLivePages + b.SharedLivePages, a.BufferSnapshotLeases + b.BufferSnapshotLeases);
    }
}
