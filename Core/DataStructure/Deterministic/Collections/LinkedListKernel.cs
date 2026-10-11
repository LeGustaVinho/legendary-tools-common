namespace LegendaryTools.Deterministic
{
    public struct LinkedListValue<T> where T : unmanaged
    {
        public T Value;
        public StableHandle Previous, Next;
    }
    public struct LinkedListState { public StableHandle First, Last; }

    public static class LinkedListKernel<T, TBuffer> where T : unmanaged where TBuffer : struct, IDeterministicBuffer<SlotMapNode<LinkedListValue<T>>>
    {
        public static DeterministicStatus TryGet(ref TBuffer buffer, in SlotMapState slots, StableHandle handle, out T value)
        { var status = SlotMapKernel<LinkedListValue<T>, TBuffer>.TryGet(ref buffer, slots, handle, out var node); value = node.Value; return status; }
        public static DeterministicStatus TrySet(ref TBuffer buffer, in SlotMapState slots, StableHandle handle, in T value)
        {
            var status = SlotMapKernel<LinkedListValue<T>, TBuffer>.TryGet(ref buffer, slots, handle, out var node); if (!status.IsSuccess()) return status;
            node.Value = value; return SlotMapKernel<LinkedListValue<T>, TBuffer>.TrySet(ref buffer, slots, handle, node);
        }
        private static DeterministicStatus Reserve(ref TBuffer buffer, int required, StableHandle handle, bool prepare)
        { return !handle.IsValid ? DeterministicStatus.None : prepare ? buffer.PrepareWrite(required, (int)handle.Index, 1) : buffer.CanPrepareWrite(required, (int)handle.Index, 1); }
        private static void Link(ref TBuffer buffer, StableHandle at, StableHandle target, bool next)
        {
            if (!at.IsValid) return;
            var node = buffer.Read((int)at.Index); if (next) node.Value.Next = target; else node.Value.Previous = target; buffer.Write((int)at.Index, node);
        }
        /// <param name="position">0 first; 1 last; 2 before anchor; 3 after anchor.</param>
        public static DeterministicStatus TryAdd(ref TBuffer buffer, ref SlotMapState slots, ref LinkedListState links,
            in T value, int position, StableHandle anchor, out StableHandle handle)
        {
            handle = default; if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            StableHandle previous, next;
            if (position == 0) { previous = default; next = links.First; }
            else if (position == 1) { previous = links.Last; next = default; }
            else if (position == 2 || position == 3)
            {
                var status = SlotMapKernel<LinkedListValue<T>, TBuffer>.TryGet(ref buffer, slots, anchor, out var node); if (!status.IsSuccess()) return status;
                previous = position == 2 ? node.Previous : anchor; next = position == 2 ? anchor : node.Next;
            }
            else return DeterministicStatus.InvalidConfiguration;
            if (slots.FreeHead == 0 && slots.Used == int.MaxValue) return DeterministicStatus.CapacityExceeded;
            int index = slots.FreeHead == 0 ? slots.Used : slots.FreeHead - 1;
            int required = slots.FreeHead == 0 ? slots.Used + 1 : buffer.Capacity;
            for (int phase = 0; phase < 2; phase++)
            {
                var status = phase == 0 ? buffer.CanPrepareWrite(required, index, 1) : buffer.PrepareWrite(required, index, 1); if (!status.IsSuccess()) return status;
                status = Reserve(ref buffer, required, previous, phase != 0); if (!status.IsSuccess()) return status;
                status = Reserve(ref buffer, required, next, phase != 0); if (!status.IsSuccess()) return status;
            }
            var item = new LinkedListValue<T> { Value = value, Previous = previous, Next = next };
            var added = SlotMapKernel<LinkedListValue<T>, TBuffer>.TryAdd(ref buffer, ref slots, item, out handle); if (!added.IsSuccess()) return added;
            Link(ref buffer, previous, handle, true); Link(ref buffer, next, handle, false);
            if (!previous.IsValid) links.First = handle; if (!next.IsValid) links.Last = handle;
            buffer.CommitWrite(); return DeterministicStatus.None;
        }
        public static DeterministicStatus TryRemove(ref TBuffer buffer, ref SlotMapState slots, ref LinkedListState links, StableHandle handle)
        {
            var status = SlotMapKernel<LinkedListValue<T>, TBuffer>.TryGet(ref buffer, slots, handle, out var node); if (!status.IsSuccess()) return status;
            var slot = buffer.Read((int)handle.Index);
            for (int phase = 0; phase < 2; phase++)
            {
                status = Reserve(ref buffer, buffer.Capacity, handle, phase != 0); if (!status.IsSuccess()) return status;
                status = Reserve(ref buffer, buffer.Capacity, node.Previous, phase != 0); if (!status.IsSuccess()) return status;
                status = Reserve(ref buffer, buffer.Capacity, node.Next, phase != 0); if (!status.IsSuccess()) return status;
                if (slot.Generation != uint.MaxValue && slots.FreeTail != 0)
                { status = phase == 0 ? buffer.CanPrepareWrite(buffer.Capacity, slots.FreeTail - 1, 1) : buffer.PrepareWrite(buffer.Capacity, slots.FreeTail - 1, 1); if (!status.IsSuccess()) return status; }
            }
            status = SlotMapKernel<LinkedListValue<T>, TBuffer>.TryRemove(ref buffer, ref slots, handle); if (!status.IsSuccess()) return status;
            Link(ref buffer, node.Previous, node.Next, true); Link(ref buffer, node.Next, node.Previous, false);
            if (!node.Previous.IsValid) links.First = node.Next; if (!node.Next.IsValid) links.Last = node.Previous;
            buffer.CommitWrite(); return DeterministicStatus.None;
        }
        public static DeterministicStatus TryClear(ref TBuffer buffer, ref SlotMapState slots, ref LinkedListState links)
        { var status = SlotMapKernel<LinkedListValue<T>, TBuffer>.TryClear(ref buffer, ref slots); if (status.IsSuccess()) links = default; return status; }
    }
}
