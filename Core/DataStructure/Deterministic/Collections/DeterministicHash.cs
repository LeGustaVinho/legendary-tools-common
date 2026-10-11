namespace LegendaryTools.Deterministic
{
    /// <summary>XXH64, algorithm version 1. Input is canonical bytes, never a struct memory image.</summary>
    public static unsafe class DeterministicHash
    {
        public const ushort Version = 1;
        private const ulong P1 = 11400714785074694791UL, P2 = 14029467366897019727UL,
            P3 = 1609587929392839161UL, P4 = 9650029242287828579UL, P5 = 2870177450012600261UL;
        private static ulong Rotate(ulong x, int n) => (x << n) | (x >> (64 - n));
        private static ulong Read64(byte* p)
        { ulong v = 0; for (int i = 0; i < 8; i++) v |= (ulong)p[i] << (8 * i); return v; }
        private static uint Read32(byte* p)
        { uint v = 0; for (int i = 0; i < 4; i++) v |= (uint)p[i] << (8 * i); return v; }
        private static ulong Round(ulong accumulator, ulong input) => unchecked(Rotate(accumulator + input * P2, 31) * P1);
        private static ulong Merge(ulong accumulator, ulong lane) => unchecked((accumulator ^ Round(0, lane)) * P1 + P4);
        public static ulong Mix(ulong value)
        { unchecked { value ^= value >> 33; value *= P2; value ^= value >> 29; value *= P3; return value ^ (value >> 32); } }

        public static ulong Xxh64(byte* bytes, int length, ulong seed = 0)
        {
            // Caller validates the borrowed region. No pointer participates in the digest.
            unchecked
            {
                int cursor = 0; ulong hash;
                if (length >= 32)
                {
                    ulong a = seed + P1 + P2, b = seed + P2, c = seed, d = seed - P1;
                    while (cursor <= length - 32)
                    {
                        a = Round(a, Read64(bytes + cursor)); b = Round(b, Read64(bytes + cursor + 8));
                        c = Round(c, Read64(bytes + cursor + 16)); d = Round(d, Read64(bytes + cursor + 24)); cursor += 32;
                    }
                    hash = Rotate(a, 1) + Rotate(b, 7) + Rotate(c, 12) + Rotate(d, 18);
                    hash = Merge(Merge(Merge(Merge(hash, a), b), c), d);
                }
                else hash = seed + P5;
                hash += (ulong)length;
                while (cursor <= length - 8) { hash ^= Round(0, Read64(bytes + cursor)); hash = Rotate(hash, 27) * P1 + P4; cursor += 8; }
                if (cursor <= length - 4) { hash ^= Read32(bytes + cursor) * P1; hash = Rotate(hash, 23) * P2 + P3; cursor += 4; }
                while (cursor < length) { hash ^= bytes[cursor++] * P5; hash = Rotate(hash, 11) * P1; }
                return Mix(hash);
            }
        }
    }
}
