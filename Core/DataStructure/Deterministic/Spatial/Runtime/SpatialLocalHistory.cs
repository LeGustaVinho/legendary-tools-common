using System;

namespace LegendaryTools.Deterministic
{
    /// <summary>A bounded host-only lease table. Eviction changes restore cost, never the canonical world.</summary>
    internal sealed class SpatialLocalHistory<TSnapshot> where TSnapshot : struct, IDisposable
    {
        private readonly TSnapshot[] snapshots;
        private readonly object[] owners;
        private readonly long[] ticks;
        private readonly ulong[] leases;
        private ulong serial;
        private int cursor;
        internal int Capacity => snapshots.Length;
        internal int Count { get; private set; }
        internal SpatialLocalHistory(int capacity)
        { snapshots = new TSnapshot[capacity]; owners = new object[capacity]; ticks = new long[capacity]; leases = new ulong[capacity]; }
        internal int BeginCapture()
        {
            if (Capacity == 0) return -1;
            int slot = cursor; cursor = (cursor + 1) % Capacity;
            ReleaseSlot(slot); return slot;
        }
        internal ulong Publish(int slot, object owner, long tick, TSnapshot snapshot)
        {
            if (++serial == 0) ++serial;
            snapshots[slot] = snapshot; owners[slot] = owner; ticks[slot] = tick; Count++; return leases[slot] = serial;
        }
        internal bool TryFind(object owner, long tick, ulong lease, out TSnapshot snapshot)
        {
            snapshot = default; if (owner == null || lease == 0) return false;
            for (int i = 0; i < Capacity; i++)
                if (leases[i] == lease && ticks[i] == tick && ReferenceEquals(owners[i], owner)) { snapshot = snapshots[i]; return true; }
            return false;
        }
        internal void Release(object owner, long tick, ulong lease)
        {
            if (lease == 0) return;
            for (int i = 0; i < Capacity; i++)
                if (leases[i] == lease && ticks[i] == tick && ReferenceEquals(owners[i], owner)) { ReleaseSlot(i); return; }
        }
        private void ReleaseSlot(int slot)
        {
            if (leases[slot] == 0) return;
            // A destroyed Native owner already released all its page roots. Other failures (e.g. pending jobs) remain visible.
            try { snapshots[slot].Dispose(); } catch (ObjectDisposedException) { }
            snapshots[slot] = default; owners[slot] = null; leases[slot] = 0; Count--;
        }
    }
    internal static unsafe class SpatialSourceDescriptor
    {
        internal const uint Magic = 0x3144534c;
        internal static uint[] Sources(uint stateId, ReadOnlySpan<uint> sourceStateIds)
        {
            if (sourceStateIds.Length == 0) DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            var ids = sourceStateIds.ToArray(); Array.Sort(ids);
            for (int i = 0; i < ids.Length; i++)
                if (ids[i] == 0 || ids[i] == stateId || i > 0 && ids[i] == ids[i - 1]) DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            return ids;
        }
        internal static ulong Signature(int dimensions, SpatialIndexOptions options, uint[] sources, ulong rules, ulong keyPolicy)
        {
            if (rules == 0 || keyPolicy == 0 || !FixedBoundsDeterministicProfile.TryFingerprint(out ulong numeric).IsSuccess()) return 0;
            ulong hash = DeterministicHash.Mix(0x4c44535044525631UL ^ (uint)dimensions ^ numeric ^ options.SemanticFingerprint);
            hash = DeterministicHash.Mix(hash ^ rules); hash = DeterministicHash.Mix(hash ^ keyPolicy);
            hash = DeterministicHash.Mix(hash ^ (uint)sources.Length);
            for (int i = 0; i < sources.Length; i++) hash = DeterministicHash.Mix(hash ^ sources[i]);
            return hash;
        }
        internal static DeterministicStatus Read(ref DeterministicReader reader)
        {
            var trial = reader; var status = trial.TryUInt(4, out ulong magic);
            if (!status.IsSuccess()) return status;
            if (magic != Magic) return DeterministicStatus.InvalidEncoding;
            reader = trial; return DeterministicStatus.None;
        }
    }
}
