namespace LegendaryTools.Deterministic
{
    public struct SlotMapNode<T> where T : unmanaged
    {
        public T Value;
        public uint Generation;
        public int NextFree;
        public byte Active, Retired;
    }
    public struct SlotMapState
    {
        public int Count, Used, FreeHead, FreeTail;
    }
    public readonly struct SlotMapItem<T> where T : unmanaged
    {
        public readonly StableHandle Handle;
        public readonly T Value;
        public SlotMapItem(StableHandle handle, T value) { Handle = handle; Value = value; }
    }

    /// <summary>FIFO reuse. Exhausted generations retire their slots instead of wrapping.</summary>
    public static class SlotMapKernel<T, TBuffer> where T : unmanaged where TBuffer : struct, IDeterministicBuffer<SlotMapNode<T>>
    {
        public static DeterministicStatus TryGet(ref TBuffer buffer, in SlotMapState state, StableHandle handle, out T value)
        {
            value = default;
            if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            if (!handle.IsValid || handle.Index >= (uint)state.Used) return DeterministicStatus.NotFound;
            var node = buffer.Read((int)handle.Index);
            if (node.Active == 0 || node.Generation != handle.Generation) return DeterministicStatus.NotFound;
            value = node.Value; return DeterministicStatus.None;
        }
        public static DeterministicStatus TryAdd(ref TBuffer buffer, ref SlotMapState state, in T value, out StableHandle handle)
        {
            handle = default;
            if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            bool reused = state.FreeHead != 0;
            if (!reused && state.Used == int.MaxValue) return DeterministicStatus.CapacityExceeded;
            int index = reused ? state.FreeHead - 1 : state.Used;
            int required = reused ? buffer.Capacity : state.Used + 1;
            var status = buffer.PrepareWrite(required, index, 1); if (!status.IsSuccess()) return status;
            AddReserved(ref buffer, ref state, value, out handle);
            buffer.CommitWrite(); return DeterministicStatus.None;
        }
        // Compound collections reserve every participating buffer before calling these helpers.
        internal static void AddReserved(ref TBuffer buffer, ref SlotMapState state, in T value, out StableHandle handle)
        {
            bool reused = state.FreeHead != 0;
            int index = reused ? state.FreeHead - 1 : state.Used;
            var node = reused ? buffer.Read(index) : new SlotMapNode<T> { Generation = 1 };
            if (reused) { state.FreeHead = node.NextFree; if (state.FreeHead == 0) state.FreeTail = 0; }
            else state.Used++;
            node.NextFree = 0; node.Active = 1; node.Value = value; buffer.Write(index, node);
            state.Count++; handle = new StableHandle((uint)index, node.Generation);
        }
        public static DeterministicStatus TrySet(ref TBuffer buffer, in SlotMapState state, StableHandle handle, in T value)
        {
            var status = TryGet(ref buffer, state, handle, out _); if (!status.IsSuccess()) return status;
            int index = (int)handle.Index; status = buffer.PrepareWrite(buffer.Capacity, index, 1); if (!status.IsSuccess()) return status;
            var node = buffer.Read(index); node.Value = value; buffer.Write(index, node); buffer.CommitWrite(); return DeterministicStatus.None;
        }
        public static DeterministicStatus TryRemove(ref TBuffer buffer, ref SlotMapState state, StableHandle handle)
        {
            var status = TryGet(ref buffer, state, handle, out _); if (!status.IsSuccess()) return status;
            int index = (int)handle.Index; var node = buffer.Read(index); bool retire = node.Generation == uint.MaxValue;
            for (int phase = 0; phase < 2; phase++)
            {
                status = phase == 0 ? buffer.CanPrepareWrite(buffer.Capacity, index, 1) : buffer.PrepareWrite(buffer.Capacity, index, 1);
                if (!status.IsSuccess()) return status;
                if (!retire && state.FreeTail != 0)
                {
                    status = phase == 0 ? buffer.CanPrepareWrite(buffer.Capacity, state.FreeTail - 1, 1) : buffer.PrepareWrite(buffer.Capacity, state.FreeTail - 1, 1);
                    if (!status.IsSuccess()) return status;
                }
            }
            RemoveReserved(ref buffer, ref state, index, node); buffer.CommitWrite(); return DeterministicStatus.None;
        }
        internal static void RemoveReserved(ref TBuffer buffer, ref SlotMapState state, int index, SlotMapNode<T> node)
        {
            node.Value = default; node.Active = 0; node.NextFree = 0;
            if (node.Generation == uint.MaxValue) node.Retired = 1;
            else
            {
                node.Generation++;
                if (state.FreeTail != 0)
                { var tail = buffer.Read(state.FreeTail - 1); tail.NextFree = index + 1; buffer.Write(state.FreeTail - 1, tail); }
                else state.FreeHead = index + 1;
                state.FreeTail = index + 1;
            }
            buffer.Write(index, node); state.Count--;
        }
        public static unsafe DeterministicStatus TryClear(ref TBuffer buffer, ref SlotMapState state)
        {
            if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            if (state.Count == 0) return DeterministicStatus.None;
            // Keep dense/small clears contiguous. Sparse high-watermark clears only detach written pages.
            if (state.Used >= 4096 && state.Count <= 256 && state.Count <= state.Used / 16)
            {
                int* indices = stackalloc int[256]; int count = 0;
                for (int i = 0; i < state.Used && count < state.Count; i++) if (buffer.Read(i).Active != 0) indices[count++] = i;
                if (count != state.Count) return DeterministicStatus.ProtocolConflict;
                for (int phase = 0; phase < 2; phase++)
                {
                    for (int i = 0; i < count; i++)
                    {
                        var s = phase == 0 ? buffer.CanPrepareWrite(buffer.Capacity, indices[i], 1) : buffer.PrepareWrite(buffer.Capacity, indices[i], 1);
                        if (!s.IsSuccess()) return s;
                    }
                    if (state.FreeTail != 0)
                    {
                        var s = phase == 0 ? buffer.CanPrepareWrite(buffer.Capacity, state.FreeTail - 1, 1) : buffer.PrepareWrite(buffer.Capacity, state.FreeTail - 1, 1);
                        if (!s.IsSuccess()) return s;
                    }
                }
                for (int i = 0; i < count; i++) RemoveReserved(ref buffer, ref state, indices[i], buffer.Read(indices[i]));
                buffer.CommitWrite(); return DeterministicStatus.None;
            }
            var status = buffer.PrepareWrite(buffer.Capacity, 0, state.Used); if (!status.IsSuccess()) return status;
            for (int i = 0; i < state.Used; i++) { var node = buffer.Read(i); if (node.Active != 0) RemoveReserved(ref buffer, ref state, i, node); }
            buffer.CommitWrite(); return DeterministicStatus.None;
        }
    }
}
