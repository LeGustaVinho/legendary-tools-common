namespace LegendaryTools.Deterministic
{
    internal static class SpatialMutationKernel<TKey, TData, TBounds, TBoundsAccess, TPolicy, TStorage>
        where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
        where TBoundsAccess : unmanaged, ISpatialBoundsAccess<TBounds>
        where TPolicy : unmanaged, IDeterministicEquality<TKey>, IDeterministicComparer<TKey>
        where TStorage : struct, ISpatialStorage<TKey, TData, TBounds>
    {
        internal static DeterministicStatus Apply(ref TStorage store, in SpatialMutation<TKey, TData, TBounds> command,
            TBoundsAccess boundsAccess, TPolicy policy, bool synchronizing = false)
        {
            if (!store.IsCreated) return DeterministicStatus.Disposed;
            if (!synchronizing && store.Options.Ownership == SpatialStateOwnership.Derived) return DeterministicStatus.InvalidConfiguration;
            if (command.Kind > SpatialMutationKind.Clear) return DeterministicStatus.OutOfRange;
            int dimensions = boundsAccess.Dimensions;
            if (command.Kind == SpatialMutationKind.Clear)
            {
                var status = store.ClearRecords(); if (!status.IsSuccess()) return status;
                store.State.Root = -1; store.State.NodeUsed = 0; store.State.FreeNode = -1;
                if (store.Options.Strategy == SpatialIndexStrategy.SparseGrid) store.ClearCells();
                store.State.LinkUsed = 0; store.State.FreeLink = -1; store.State.OverflowCount = 0; store.State.OverflowHead = -1; store.State.Dirty = false;
                store.State.Revision++; return DeterministicStatus.None;
            }
            int oldSlot = store.Find(command.Key);
            if (command.Kind == SpatialMutationKind.Add && oldSlot >= 0) return DeterministicStatus.DuplicateKey;
            if (command.Kind != SpatialMutationKind.Add && oldSlot < 0) return DeterministicStatus.NotFound;
            var strategy = store.Options.Strategy;
            if (command.Kind == SpatialMutationKind.Remove)
            {
                // Remove first can fail only before changing the table. Acceleration removals are bounded writes.
                var status = store.Remove(command.Key); if (!status.IsSuccess()) return status;
                if (strategy == SpatialIndexStrategy.DynamicBvh) SpatialBvhKernel<TKey, TData, TBounds, TPolicy, TStorage>.Remove(ref store, oldSlot, dimensions, policy);
                else if (strategy == SpatialIndexStrategy.SparseGrid)
                {
                    SpatialGridKernel<TKey, TData, TBounds, TStorage>.Remove(ref store, oldSlot);
                    SpatialBvhKernel<TKey, TData, TBounds, TPolicy, TStorage>.Remove(ref store, oldSlot, dimensions, policy);
                }
                else store.State.Dirty = true;
                store.State.Revision++; return DeterministicStatus.None;
            }
            bool geometric = command.Kind == SpatialMutationKind.Add || command.Kind == SpatialMutationKind.UpdateBounds;
            SpatialRawBounds envelope = default;
            if (geometric) { var s = boundsAccess.TryRead(command.Bounds, out envelope); if (!s.IsSuccess()) return s; }
            if ((command.Kind == SpatialMutationKind.Add || command.Kind == SpatialMutationKind.SetMetadata) && !command.Metadata.IsValid)
                return DeterministicStatus.OutOfRange;
            var record = oldSlot >= 0 ? store.ReadEntry(oldSlot).Value : new SpatialRecord<TKey, TData, TBounds>(command.Key, command.Bounds, command.Data, command.Metadata);
            switch (command.Kind)
            {
                case SpatialMutationKind.UpdateBounds: record.Bounds = command.Bounds; break;
                case SpatialMutationKind.SetData: record.Data = command.Data; break;
                case SpatialMutationKind.SetMetadata: record.Metadata = command.Metadata; break;
            }
            var written = store.Put(record, command.Kind == SpatialMutationKind.Add, out int slot); if (!written.IsSuccess()) return written;
            if (geometric)
            {
                bool add = oldSlot < 0; var placement = add ? new SpatialPlacement { Leaf = -1, GridHead = -1 } : store.ReadPlacement(slot);
                bool contained = !add && strategy == SpatialIndexStrategy.DynamicBvh && store.ReadNode(placement.Leaf).Bounds.Contains(envelope, dimensions);
                if (!add && !contained)
                {
                    if (strategy == SpatialIndexStrategy.DynamicBvh) SpatialBvhKernel<TKey, TData, TBounds, TPolicy, TStorage>.Remove(ref store, slot, dimensions, policy);
                    else if (strategy == SpatialIndexStrategy.SparseGrid)
                    {
                        SpatialGridKernel<TKey, TData, TBounds, TStorage>.Remove(ref store, slot);
                        SpatialBvhKernel<TKey, TData, TBounds, TPolicy, TStorage>.Remove(ref store, slot, dimensions, policy);
                    }
                    placement = store.ReadPlacement(slot);
                }
                placement.Bounds = envelope; store.WritePlacement(slot, placement);
                if (!contained)
                {
                    if (strategy == SpatialIndexStrategy.DynamicBvh)
                        SpatialBvhKernel<TKey, TData, TBounds, TPolicy, TStorage>.Insert(ref store, slot, dimensions,
                            SpatialGeometry.Normalize(unchecked((ulong)store.Options.FatMargin.RawValue), LegendaryTools.FixedPoint.FixedRuntime.State), policy);
                    else if (strategy == SpatialIndexStrategy.SparseGrid)
                    {
                        SpatialGridKernel<TKey, TData, TBounds, TStorage>.Add(ref store, slot, dimensions);
                        SpatialBvhKernel<TKey, TData, TBounds, TPolicy, TStorage>.Insert(ref store, slot, dimensions, 0, policy);
                    }
                    else store.State.Dirty = true;
                }
            }
            store.State.Revision++; return DeterministicStatus.None;
        }
        internal static DeterministicStatus Refresh(ref TStorage store, TBoundsAccess boundsAccess, TPolicy policy)
        {
            if (!store.IsCreated) return DeterministicStatus.Disposed;
            for (int i = 0; i < store.State.Records.Used; i++)
            {
                var entry = store.ReadEntry(i); if (entry.Active == 0) continue;
                var status = boundsAccess.TryRead(entry.Value.Bounds, out var envelope); if (!status.IsSuccess()) return status;
                store.WritePlacement(i, new SpatialPlacement { Bounds = envelope, Leaf = -1, GridHead = -1 });
            }
            return Rebuild(ref store, boundsAccess, policy);
        }
        internal static DeterministicStatus Rebuild(ref TStorage store, TBoundsAccess boundsAccess, TPolicy policy)
        {
            if (!store.IsCreated) return DeterministicStatus.Disposed;
            if (store.Options.Strategy == SpatialIndexStrategy.SparseGrid)
            {
                SpatialGridKernel<TKey, TData, TBounds, TStorage>.Rebuild(ref store, boundsAccess.Dimensions);
                SpatialBvhKernel<TKey, TData, TBounds, TPolicy, TStorage>.Rebuild(ref store, boundsAccess.Dimensions, 0, policy, false);
            }
            else SpatialBvhKernel<TKey, TData, TBounds, TPolicy, TStorage>.Rebuild(ref store, boundsAccess.Dimensions,
                SpatialGeometry.Normalize(unchecked((ulong)store.Options.FatMargin.RawValue), LegendaryTools.FixedPoint.FixedRuntime.State), policy,
                store.Options.Strategy == SpatialIndexStrategy.StaticBvh);
            store.State.Revision++; return DeterministicStatus.None;
        }
    }
}
