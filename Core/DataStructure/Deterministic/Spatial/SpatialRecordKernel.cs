namespace LegendaryTools.Deterministic
{
    internal static class SpatialRecordKernel<TKey, TData, TBounds, TStorage>
        where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
        where TStorage : struct, ISpatialStorage<TKey, TData, TBounds>
    {
        // Canonical insertion order, traversing the nearer end without shared mutable cache.
        // Reading every ordinal is still quadratic; dense authoritative sources remain O(1) per row.
        internal static DeterministicStatus GetAt(ref TStorage storage, int ordinal, out SpatialRecord<TKey, TData, TBounds> record)
        {
            record = default;
            if (!storage.IsCreated) return DeterministicStatus.Disposed;
            if ((uint)ordinal >= (uint)storage.State.Records.Count) return DeterministicStatus.OutOfRange;
            int fromTail = storage.State.Records.Count - 1 - ordinal;
            bool backwards = fromTail < ordinal;
            int steps = backwards ? fromTail : ordinal;
            int encoded = backwards ? storage.State.Records.OrderTail : storage.State.Records.OrderHead;
            for (int i = 0; i <= steps; i++)
            {
                if (encoded == 0) return DeterministicStatus.ProtocolConflict;
                var entry = storage.ReadEntry(encoded - 1);
                if (i == steps) { record = entry.Value; return DeterministicStatus.None; }
                encoded = backwards ? entry.OrderPrevious : entry.OrderNext;
            }
            return DeterministicStatus.ProtocolConflict;
        }
    }
}
