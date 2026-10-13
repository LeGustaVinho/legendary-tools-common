namespace LegendaryTools.Deterministic
{
    public static class BitSetKernel<TBuffer> where TBuffer : struct, IDeterministicBuffer<ulong>
    {
        public static DeterministicStatus TryGet(ref TBuffer buffer, int length, int index, out bool value)
        {
            value = false;
            if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            if ((uint)index >= (uint)length) return DeterministicStatus.OutOfRange;
            value = (buffer.Read(index >> 6) & (1UL << (index & 63))) != 0; return DeterministicStatus.None;
        }
        public static DeterministicStatus TrySet(ref TBuffer buffer, int length, int index, bool value)
        {
            if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            if ((uint)index >= (uint)length) return DeterministicStatus.OutOfRange;
            int word = index >> 6; var status = buffer.PrepareWrite(buffer.Capacity, word, 1);
            if (!status.IsSuccess()) return status;
            ulong bits = buffer.Read(word), mask = 1UL << (index & 63);
            bits = value ? bits | mask : bits & ~mask; buffer.Write(word, bits); buffer.CommitWrite(); return DeterministicStatus.None;
        }
        public static int Count(ref TBuffer buffer)
        {
            int total = 0;
            int capacity = buffer.Capacity;
            for (int i = 0; i < capacity; i++) total += DeterministicBits.Count(buffer.Read(i));
            return total;
        }
        public static int NextSetBit(ref TBuffer buffer, int length, int start)
        {
            if (start >= length) return -1;
            int word = start >> 6; ulong bits = buffer.Read(word) & (ulong.MaxValue << (start & 63));
            while (true)
            {
                if (bits != 0)
                {
                    int bit = DeterministicBits.TrailingZeroCount(bits);
                    int found = word * 64 + bit; return found < length ? found : -1;
                }
                word++; if (word >= buffer.Capacity) return -1; bits = buffer.Read(word);
            }
        }
    }
}
