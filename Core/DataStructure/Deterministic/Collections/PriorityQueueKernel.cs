namespace LegendaryTools.Deterministic
{
    public struct PriorityQueueNode<T, TPriority> where T : unmanaged where TPriority : unmanaged
    {
        public T Element;
        public TPriority Priority;
        public ulong Sequence;
    }
    public struct PriorityQueueState
    {
        public int Count;
        public ulong NextSequence;
    }

    /// <summary>Stable minimum heap. Only the sift path and the retired tail cross the write barrier.</summary>
    public static class PriorityQueueKernel<T, TPriority, TComparer, TBuffer>
        where T : unmanaged where TPriority : unmanaged
        where TComparer : unmanaged, IDeterministicComparer<TPriority>
        where TBuffer : struct, IDeterministicBuffer<PriorityQueueNode<T, TPriority>>
    {
        private static int Compare(in PriorityQueueNode<T, TPriority> a, in PriorityQueueNode<T, TPriority> b, TComparer comparer)
        { int c = comparer.Compare(a.Priority, b.Priority); return c != 0 ? c : a.Sequence.CompareTo(b.Sequence); }
        private static DeterministicStatus Reserve(ref TBuffer buffer, int required, int index, bool prepare) =>
            prepare ? buffer.PrepareWrite(required, index, 1) : buffer.CanPrepareWrite(required, index, 1);
        public static DeterministicStatus TryEnqueue(ref TBuffer buffer, ref PriorityQueueState state, in T element, in TPriority priority, TComparer comparer)
        {
            if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            if (!comparer.IsValid) return DeterministicStatus.UnsupportedType;
            if (state.Count == int.MaxValue) return DeterministicStatus.CapacityExceeded;
            if (state.NextSequence == ulong.MaxValue) return DeterministicStatus.GenerationExhausted;
            var candidate = new PriorityQueueNode<T, TPriority> { Element = element, Priority = priority, Sequence = state.NextSequence };
            int required = state.Count + 1;
            for (int phase = 0; phase < 2; phase++)
            {
                int cursor = state.Count;
                while (true)
                {
                    var status = Reserve(ref buffer, required, cursor, phase != 0); if (!status.IsSuccess()) return status;
                    if (cursor == 0) break;
                    int parent = (cursor - 1) / 2;
                    if (Compare(candidate, buffer.Read(parent), comparer) >= 0) break;
                    cursor = parent;
                }
            }
            int destination = state.Count;
            while (destination != 0)
            {
                int parent = (destination - 1) / 2; var node = buffer.Read(parent);
                if (Compare(candidate, node, comparer) >= 0) break;
                buffer.Write(destination, node); destination = parent;
            }
            buffer.Write(destination, candidate); state.Count++; state.NextSequence++; buffer.CommitWrite(); return DeterministicStatus.None;
        }
        public static DeterministicStatus TryPeek(ref TBuffer buffer, in PriorityQueueState state, out T element, out TPriority priority)
        {
            element = default; priority = default;
            if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            if (state.Count == 0) return DeterministicStatus.Empty;
            var node = buffer.Read(0); element = node.Element; priority = node.Priority; return DeterministicStatus.None;
        }
        private static int MinChild(ref TBuffer buffer, int cursor, int count, TComparer comparer)
        {
            long first = 2L * cursor + 1; if (first >= count) return -1;
            int left = (int)first, right = left + 1;
            return right < count && Compare(buffer.Read(right), buffer.Read(left), comparer) < 0 ? right : left;
        }
        public static DeterministicStatus TryDequeue(ref TBuffer buffer, ref PriorityQueueState state, TComparer comparer, out T element, out TPriority priority)
        {
            element = default; priority = default;
            if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            if (!comparer.IsValid) return DeterministicStatus.UnsupportedType;
            if (state.Count == 0) return DeterministicStatus.Empty;
            var root = buffer.Read(0); var candidate = buffer.Read(state.Count - 1); int count = state.Count - 1;
            for (int phase = 0; phase < 2; phase++)
            {
                var status = Reserve(ref buffer, buffer.Capacity, state.Count - 1, phase != 0); if (!status.IsSuccess()) return status;
                int cursor = 0;
                while (true)
                {
                    status = Reserve(ref buffer, buffer.Capacity, cursor, phase != 0); if (!status.IsSuccess()) return status;
                    int child = MinChild(ref buffer, cursor, count, comparer);
                    if (child < 0 || Compare(candidate, buffer.Read(child), comparer) <= 0) break;
                    cursor = child;
                }
            }
            int destination = 0;
            while (true)
            {
                int child = MinChild(ref buffer, destination, count, comparer);
                if (child < 0 || Compare(candidate, buffer.Read(child), comparer) <= 0) break;
                var node = buffer.Read(child); buffer.Write(destination, node); destination = child;
            }
            if (count != 0) buffer.Write(destination, candidate);
            PriorityQueueNode<T, TPriority> zero = default; buffer.Write(state.Count - 1, zero);
            state.Count = count; buffer.CommitWrite(); element = root.Element; priority = root.Priority; return DeterministicStatus.None;
        }
        public static DeterministicStatus TryClear(ref TBuffer buffer, ref PriorityQueueState state)
        {
            if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            var status = buffer.PrepareWrite(buffer.Capacity, 0, state.Count); if (!status.IsSuccess()) return status;
            PriorityQueueNode<T, TPriority> zero = default; for (int i = 0; i < state.Count; i++) buffer.Write(i, zero);
            state.Count = 0; buffer.CommitWrite(); return DeterministicStatus.None;
        }
    }
}
