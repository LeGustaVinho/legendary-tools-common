using System;

namespace LegendaryTools.Deterministic
{
    /// <summary>Host-owned boundary history. Individual portable packets remain bounded; aggregate storage may be paged.</summary>
    public unsafe interface IDeterministicWorldHistory : IDisposable
    {
        DeterministicBackend Backend { get; }
        uint HashAlgorithmId { get; }
        ulong HashConfigurationFingerprint { get; }
        SnapshotPolicy Policy { get; }
        int Capacity { get; }
        int MaxCheckpointBytes { get; }
        long NewestTick { get; }
        long OldestTick { get; }
        int LivePages { get; }
        long ReservedBytes { get; }
        long LocalHistoryReservedBytes { get; }
        DeterministicStatus TryGetHash(long tick, out ulong hash);
        DeterministicStatus TryGetStoredBytes(long tick, out int bytes);
        DeterministicStatus TryCapture(DeterministicStateRegistry registry, long tick);
        DeterministicStatus TryCapture(long tick, byte* packet, int length);
        DeterministicStatus TryRestore(long tick, out byte* packet, out int length);
        DeterministicStatus TryRestore(DeterministicStateRegistry registry, long tick);
        DeterministicStatus TryTruncate(long tick);
        DeterministicStatus TryReset();
    }
}
