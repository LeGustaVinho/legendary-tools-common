namespace LegendaryTools.Deterministic
{
    /// <summary>Integer primitive bits have no padding. Application structs require an explicit policy.</summary>
    public unsafe struct PrimitiveEquality<T> : IDeterministicEquality<T> where T : unmanaged
    {
        public bool IsValid => IsSupported;
        public bool IsSupported => typeof(T) == typeof(byte) || typeof(T) == typeof(sbyte) || typeof(T) == typeof(short) ||
            typeof(T) == typeof(ushort) || typeof(T) == typeof(int) || typeof(T) == typeof(uint) ||
            typeof(T) == typeof(long) || typeof(T) == typeof(ulong) || typeof(T) == typeof(bool) || typeof(T) == typeof(char);
        internal static ulong Bits(in T value)
        {
            T copy = value; byte* p = (byte*)&copy; ulong bits = 0;
            for (int i = 0; i < sizeof(T) && i < 8; i++) bits |= (ulong)p[i] << (8 * i);
            return bits;
        }
        public bool Equals(in T a, in T b) => Bits(a) == Bits(b);
        public ulong Hash(in T value) => DeterministicHash.Mix(Bits(value));
    }

    public unsafe struct PrimitiveComparer<T> : IDeterministicComparer<T> where T : unmanaged
    {
        public bool IsValid => default(PrimitiveEquality<T>).IsValid;
        public int Compare(in T a, in T b)
        {
            ulong left = PrimitiveEquality<T>.Bits(a), right = PrimitiveEquality<T>.Bits(b);
            bool signed = typeof(T) == typeof(sbyte) || typeof(T) == typeof(short) || typeof(T) == typeof(int) || typeof(T) == typeof(long);
            if (signed) { ulong mask = 1UL << (sizeof(T) * 8 - 1); left ^= mask; right ^= mask; }
            return left < right ? -1 : left > right ? 1 : 0;
        }
    }
}
