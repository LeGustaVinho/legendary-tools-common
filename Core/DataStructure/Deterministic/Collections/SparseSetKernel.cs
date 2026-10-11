namespace LegendaryTools.Deterministic
{
    public struct SparseSetEntry<T> where T : unmanaged
    {
        public StableHandle Handle;
        public T Value;
        public SparseSetEntry(StableHandle handle, T value) { Handle = handle; Value = value; }
    }
    public struct SparseSetIndex
    {
        public uint Generation;
        public int DenseIndex;
    }

    /// <summary>Dense iteration follows insertion and swap removal. Sorted iteration is a separate sparse-index scan.</summary>
    public static class SparseSetKernel<T, TDense, TSparse> where T : unmanaged
        where TDense : struct, IDeterministicBuffer<SparseSetEntry<T>>
        where TSparse : struct, IDeterministicBuffer<SparseSetIndex>
    {
        public static DeterministicStatus TryGet(ref TDense dense, ref TSparse sparse, StableHandle handle, out T value)
        {
            value = default;
            if (!dense.IsCreated || !sparse.IsCreated) return DeterministicStatus.Disposed;
            if (!handle.IsValid || handle.Index >= (uint)sparse.Capacity) return DeterministicStatus.NotFound;
            var cell = sparse.Read((int)handle.Index);
            if (cell.DenseIndex == 0 || cell.Generation != handle.Generation) return DeterministicStatus.NotFound;
            value = dense.Read(cell.DenseIndex - 1).Value; return DeterministicStatus.None;
        }
        public static DeterministicStatus TryPut(ref TDense dense, ref TSparse sparse, ref SequenceState state,
            int maxEntitySlots, StableHandle handle, in T value, bool addOnly)
        {
            if (!dense.IsCreated || !sparse.IsCreated) return DeterministicStatus.Disposed;
            if (!handle.IsValid) return DeterministicStatus.InvalidConfiguration;
            if (handle.Index >= (uint)maxEntitySlots) return DeterministicStatus.OutOfRange;
            int slot = (int)handle.Index;
            var cell = slot < sparse.Capacity ? sparse.Read(slot) : default;
            if (cell.DenseIndex != 0)
            {
                if (cell.Generation != handle.Generation) return DeterministicStatus.ProtocolConflict;
                if (addOnly) return DeterministicStatus.DuplicateKey;
                var status = dense.PrepareWrite(dense.Capacity, cell.DenseIndex - 1, 1);
                if (!status.IsSuccess()) return status;
                dense.Write(cell.DenseIndex - 1, new SparseSetEntry<T>(handle, value)); dense.CommitWrite(); return DeterministicStatus.None;
            }
            if (state.Count == int.MaxValue) return DeterministicStatus.CapacityExceeded;
            for (int phase = 0; phase < 2; phase++)
            {
                var status = phase == 0 ? dense.CanPrepareWrite(state.Count + 1, state.Count, 1) : dense.PrepareWrite(state.Count + 1, state.Count, 1);
                if (!status.IsSuccess()) return status;
                status = phase == 0 ? sparse.CanPrepareWrite(slot + 1, slot, 1) : sparse.PrepareWrite(slot + 1, slot, 1);
                if (!status.IsSuccess()) return status;
            }
            dense.Write(state.Count, new SparseSetEntry<T>(handle, value));
            sparse.Write(slot, new SparseSetIndex { Generation = handle.Generation, DenseIndex = state.Count + 1 });
            state.Count++; dense.CommitWrite(); sparse.CommitWrite(); return DeterministicStatus.None;
        }
        public static DeterministicStatus TryRemove(ref TDense dense, ref TSparse sparse, ref SequenceState state, StableHandle handle)
        {
            var status = TryGet(ref dense, ref sparse, handle, out _); if (!status.IsSuccess()) return status;
            int index = sparse.Read((int)handle.Index).DenseIndex - 1, last = state.Count - 1;
            var moved = dense.Read(last);
            for (int phase = 0; phase < 2; phase++)
            {
                status = phase == 0 ? dense.CanPrepareWrite(dense.Capacity, index, 1) : dense.PrepareWrite(dense.Capacity, index, 1);
                if (!status.IsSuccess()) return status;
                status = phase == 0 ? dense.CanPrepareWrite(dense.Capacity, last, 1) : dense.PrepareWrite(dense.Capacity, last, 1);
                if (!status.IsSuccess()) return status;
                status = phase == 0 ? sparse.CanPrepareWrite(sparse.Capacity, (int)handle.Index, 1) : sparse.PrepareWrite(sparse.Capacity, (int)handle.Index, 1);
                if (!status.IsSuccess()) return status;
                if (index != last)
                {
                    status = phase == 0 ? sparse.CanPrepareWrite(sparse.Capacity, (int)moved.Handle.Index, 1) : sparse.PrepareWrite(sparse.Capacity, (int)moved.Handle.Index, 1);
                    if (!status.IsSuccess()) return status;
                }
            }
            if (index != last)
            {
                dense.Write(index, moved);
                sparse.Write((int)moved.Handle.Index, new SparseSetIndex { Generation = moved.Handle.Generation, DenseIndex = index + 1 });
            }
            dense.Write(last, default); sparse.Write((int)handle.Index, default); state.Count--;
            dense.CommitWrite(); sparse.CommitWrite(); return DeterministicStatus.None;
        }
        public static DeterministicStatus TryClear(ref TDense dense, ref TSparse sparse, ref SequenceState state)
        {
            if (!dense.IsCreated || !sparse.IsCreated) return DeterministicStatus.Disposed;
            if (state.Count == 0) return DeterministicStatus.None;
            for (int phase = 0; phase < 2; phase++)
            {
                var status = phase == 0 ? dense.CanPrepareWrite(dense.Capacity, 0, state.Count) : dense.PrepareWrite(dense.Capacity, 0, state.Count);
                if (!status.IsSuccess()) return status;
                for (int i = 0; i < state.Count; i++)
                {
                    int slot = (int)dense.Read(i).Handle.Index;
                    status = phase == 0 ? sparse.CanPrepareWrite(sparse.Capacity, slot, 1) : sparse.PrepareWrite(sparse.Capacity, slot, 1);
                    if (!status.IsSuccess()) return status;
                }
            }
            for (int i = 0; i < state.Count; i++) { sparse.Write((int)dense.Read(i).Handle.Index, default); dense.Write(i, default); }
            state = default; dense.CommitWrite(); sparse.CommitWrite(); return DeterministicStatus.None;
        }
    }
}
