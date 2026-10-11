namespace LegendaryTools.Deterministic
{
    /// <summary>Shared by GC and native storage. Validation and reservation precede every write.</summary>
    public static class SequenceKernel<T, TBuffer> where T : unmanaged where TBuffer : struct, IDeterministicBuffer<T>
    {
        public static DeterministicStatus TryGet(ref TBuffer buffer, in SequenceState state, int index, out T value)
        {
            value = default;
            if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            if ((uint)index >= (uint)state.Count) return DeterministicStatus.OutOfRange;
            value = buffer.Read(index); return DeterministicStatus.None;
        }

        public static DeterministicStatus TrySet(ref TBuffer buffer, in SequenceState state, int index, in T value)
        {
            if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            if ((uint)index >= (uint)state.Count) return DeterministicStatus.OutOfRange;
            var status = buffer.PrepareWrite(state.Count, index, 1);
            if (!status.IsSuccess()) return status;
            buffer.Write(index, value); buffer.CommitWrite(); return DeterministicStatus.None;
        }

        public static DeterministicStatus TryInsert(ref TBuffer buffer, ref SequenceState state, int index, in T value)
        {
            if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            if ((uint)index > (uint)state.Count) return DeterministicStatus.OutOfRange;
            if (state.Count == int.MaxValue) return DeterministicStatus.CapacityExceeded;
            var status = buffer.PrepareWrite(state.Count + 1, index, state.Count - index + 1);
            if (!status.IsSuccess()) return status;
            for (int i = state.Count; i > index; i--) { var previous = buffer.Read(i - 1); buffer.Write(i, previous); }
            buffer.Write(index, value); state.Count++; buffer.CommitWrite(); return DeterministicStatus.None;
        }

        public static DeterministicStatus TryRemoveAt(ref TBuffer buffer, ref SequenceState state, int index, bool swapBack = false)
        {
            if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            if ((uint)index >= (uint)state.Count) return DeterministicStatus.OutOfRange;
            var status = buffer.PrepareWrite(state.Count, index, state.Count - index);
            if (!status.IsSuccess()) return status;
            if (swapBack) { var last = buffer.Read(state.Count - 1); buffer.Write(index, last); }
            else for (int i = index; i + 1 < state.Count; i++) { var next = buffer.Read(i + 1); buffer.Write(i, next); }
            T zero = default; buffer.Write(state.Count - 1, zero); state.Count--;
            buffer.CommitWrite(); return DeterministicStatus.None;
        }

        public static DeterministicStatus TryClear(ref TBuffer buffer, ref SequenceState state)
        {
            if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            if (state.Count == 0) return DeterministicStatus.None;
            var status = buffer.PrepareWrite(state.Count, 0, state.Count);
            if (!status.IsSuccess()) return status;
            T zero = default;
            for (int i = 0; i < state.Count; i++) buffer.Write(i, zero);
            state = default; buffer.CommitWrite(); return DeterministicStatus.None;
        }

        public static int IndexOf<TEquality>(ref TBuffer buffer, in SequenceState state, in T item, TEquality equality)
            where TEquality : struct, IDeterministicEquality<T>
        {
            for (int i = 0; i < state.Count; i++) { var value = buffer.Read(i); if (equality.Equals(value, item)) return i; }
            return -1;
        }
        public static DeterministicStatus TryIndexOf<TEquality>(ref TBuffer buffer, in SequenceState state, in T item, TEquality equality, out int index)
            where TEquality : struct, IDeterministicEquality<T>
        {
            index = -1; if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            if (!equality.IsValid) return DeterministicStatus.UnsupportedType;
            index = IndexOf(ref buffer, state, item, equality); return DeterministicStatus.None;
        }
    }

    public static class RingKernel<T, TBuffer> where T : unmanaged where TBuffer : struct, IDeterministicBuffer<T>
    {
        public static int Physical(in SequenceState state, int capacity, int index) => (int)(((long)state.Head + index) % capacity);

        public static DeterministicStatus TryGet(ref TBuffer buffer, in SequenceState state, int index, out T value)
        {
            value = default;
            if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            if ((uint)index >= (uint)state.Count) return DeterministicStatus.OutOfRange;
            value = buffer.Read(Physical(state, buffer.Capacity, index)); return DeterministicStatus.None;
        }

        public static DeterministicStatus TryPush(ref TBuffer buffer, ref SequenceState state, in T value, bool front)
        {
            if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            // Growth moves wrapped values. Reserve all destination slots before publishing the new capacity.
            int oldCapacity = buffer.Capacity;
            if (state.Count == int.MaxValue) return DeterministicStatus.CapacityExceeded;
            int writeStart = state.Count == oldCapacity ? 0 : front ? (state.Head + oldCapacity - 1) % oldCapacity : Physical(state, oldCapacity, state.Count);
            if (state.Count == 0) writeStart = 0;
            var status = buffer.PrepareWrite(state.Count + 1, writeStart, state.Count == oldCapacity ? state.Count + 1 : 1);
            if (!status.IsSuccess()) return status;
            int capacity = buffer.Capacity;
            if (capacity != oldCapacity && state.Head != 0)
            {
                // Rotate the full old ring in place. MaxCapacity need not be a power of two.
                Reverse(ref buffer, 0, state.Head - 1);
                Reverse(ref buffer, state.Head, oldCapacity - 1);
                Reverse(ref buffer, 0, oldCapacity - 1);
                state.Head = 0;
            }
            int position;
            if (front) { state.Head = state.Count == 0 ? 0 : (state.Head + capacity - 1) % capacity; position = state.Head; }
            else position = Physical(state, capacity, state.Count);
            buffer.Write(position, value); state.Count++; buffer.CommitWrite(); return DeterministicStatus.None;
        }

        private static void Reverse(ref TBuffer buffer, int first, int last)
        {
            while (first < last)
            {
                var a = buffer.Read(first); var b = buffer.Read(last);
                buffer.Write(first++, b); buffer.Write(last--, a);
            }
        }

        public static DeterministicStatus TryPop(ref TBuffer buffer, ref SequenceState state, bool front, out T value)
        {
            value = default;
            if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            if (state.Count == 0) return DeterministicStatus.Empty;
            int position = Physical(state, buffer.Capacity, front ? 0 : state.Count - 1);
            var status = buffer.PrepareWrite(buffer.Capacity, position, 1);
            if (!status.IsSuccess()) return status;
            value = buffer.Read(position); T zero = default; buffer.Write(position, zero);
            state.Count--;
            state.Head = state.Count == 0 ? 0 : front ? (state.Head + 1) % buffer.Capacity : state.Head;
            buffer.CommitWrite(); return DeterministicStatus.None;
        }
    }
}
