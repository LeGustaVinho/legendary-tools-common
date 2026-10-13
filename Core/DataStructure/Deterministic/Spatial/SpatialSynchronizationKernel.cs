namespace LegendaryTools.Deterministic
{
    internal static unsafe class SpatialSynchronizationKernel<TKey, TData, TBounds, TAccess, TPolicy, TStorage>
        where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
        where TAccess : unmanaged, ISpatialBoundsAccess<TBounds>
        where TPolicy : unmanaged, IDeterministicEquality<TKey>, IDeterministicComparer<TKey>
        where TStorage : struct, ISpatialStorage<TKey, TData, TBounds>
    {
        // The caller owns a transaction root. An unstable, malformed or duplicate source never publishes partial state.
        internal static DeterministicStatus Replace<TSource>(ref TStorage storage, TSource source, TAccess access, TPolicy policy)
            where TSource : IDeterministicSpatialSource<TKey, TData, TBounds>
        {
            ulong identity = source.Identity, revision = source.Revision; int count = source.Count;
            if (identity == 0) return DeterministicStatus.InvalidConfiguration;
            if (count < 0 || count > storage.Options.Records.MaxCapacity) return DeterministicStatus.CapacityExceeded;
            // Order is an internal build workspace, never a snapshot root. Reconcile only changed records/memberships.
            for (int i = 0; i < storage.State.Records.Used; i++) storage.WriteOrder(i, 0);
            var status = DeterministicStatus.None;
            // Retire missing records before admitting additions, so full-capacity membership replacement succeeds.
            // A source must allow repeated stable ordinal reads within one revision.
            for (int i = 0; i < count; i++)
            {
                status = source.TryGetAt(i, out var record); if (!status.IsSuccess()) return status;
                status = access.TryRead(record.Bounds, out _); if (!status.IsSuccess()) return status;
                if (!record.Metadata.IsValid) return DeterministicStatus.OutOfRange;
                int slot = storage.Find(record.Key);
                if (slot >= 0)
                { if (storage.ReadOrder(slot) != 0) return DeterministicStatus.DuplicateKey; storage.WriteOrder(slot, 1); }
            }
            if (source.Identity != identity || source.Revision != revision || source.Count != count) return DeterministicStatus.Invalidated;
            for (int i = 0; i < storage.State.Records.Used; i++) if (storage.ReadEntry(i).Active != 0 && storage.ReadOrder(i) == 0)
            {
                status = Apply(ref storage, new SpatialMutation<TKey, TData, TBounds> { Kind = SpatialMutationKind.Remove, Key = storage.ReadEntry(i).Key }, access, policy);
                if (!status.IsSuccess()) return status;
            }
            // Reuse the first-pass marks: 1 means retained, 2 means published in this pass.
            // New slots are marked after admission, even when build workspace held stale data.
            for (int i = 0; i < count; i++)
            {
                status = source.TryGetAt(i, out var record); if (!status.IsSuccess()) return status;
                status = access.TryRead(record.Bounds, out _); if (!status.IsSuccess()) return status;
                if (!record.Metadata.IsValid) return DeterministicStatus.OutOfRange;
                int slot = storage.Find(record.Key);
                if (slot < 0)
                {
                    status = Apply(ref storage, new SpatialMutation<TKey, TData, TBounds> { Kind = SpatialMutationKind.Add,
                        Key = record.Key, Bounds = record.Bounds, Data = record.Data, Metadata = record.Metadata }, access, policy);
                    if (!status.IsSuccess()) return status;
                    slot = storage.Find(record.Key);
                }
                else
                {
                    if (storage.ReadOrder(slot) == 2) return DeterministicStatus.DuplicateKey;
                    var old = storage.ReadEntry(slot).Value;
                    if (!RawEquals(old.Bounds, record.Bounds)) status = Apply(ref storage,
                        new SpatialMutation<TKey, TData, TBounds> { Kind = SpatialMutationKind.UpdateBounds, Key = record.Key, Bounds = record.Bounds }, access, policy);
                    if (!status.IsSuccess()) return status;
                    if (!RawEquals(old.Data, record.Data)) status = Apply(ref storage,
                        new SpatialMutation<TKey, TData, TBounds> { Kind = SpatialMutationKind.SetData, Key = record.Key, Data = record.Data }, access, policy);
                    if (!status.IsSuccess()) return status;
                    if (old.Metadata.Layer != record.Metadata.Layer || old.Metadata.Flags != record.Metadata.Flags) status = Apply(ref storage,
                        new SpatialMutation<TKey, TData, TBounds> { Kind = SpatialMutationKind.SetMetadata, Key = record.Key, Metadata = record.Metadata }, access, policy);
                }
                if (!status.IsSuccess()) return status;
                storage.WriteOrder(slot, 2);
            }
            if (source.Identity != identity || source.Revision != revision || source.Count != count) return DeterministicStatus.Invalidated;
            for (int i = 0; i < storage.State.Records.Used; i++) if (storage.ReadEntry(i).Active != 0 && storage.ReadOrder(i) != 2)
            {
                status = Apply(ref storage, new SpatialMutation<TKey, TData, TBounds> { Kind = SpatialMutationKind.Remove, Key = storage.ReadEntry(i).Key }, access, policy);
                if (!status.IsSuccess()) return status;
            }
            if (storage.Options.Strategy == SpatialIndexStrategy.StaticBvh && storage.State.Dirty)
            {
                status = SpatialMutationKernel<TKey, TData, TBounds, TAccess, TPolicy, TStorage>.Rebuild(ref storage, access, policy);
                if (!status.IsSuccess()) return status;
            }
            storage.State.SourceIdentity = identity; storage.State.SourceRevision = revision; storage.State.SourceSynchronized = true;
            storage.State.Revision++;
            return DeterministicStatus.None;
        }
        private static DeterministicStatus Apply(ref TStorage storage, in SpatialMutation<TKey, TData, TBounds> mutation, TAccess access, TPolicy policy)
            => SpatialMutationKernel<TKey, TData, TBounds, TAccess, TPolicy, TStorage>.Apply(ref storage, mutation, access, policy, true);
        private static bool RawEquals<T>(T left, T right) where T : unmanaged
        {
            byte* a = (byte*)&left; byte* b = (byte*)&right;
            for (int i = 0; i < sizeof(T); i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
