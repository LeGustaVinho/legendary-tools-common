using System.Runtime.CompilerServices;

namespace LegendaryTools.Deterministic
{
    /// <summary>Integer-only bit operations with identical results in Managed and Burst code.</summary>
    public static class DeterministicBits
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Count(ulong bits)
        {
            unchecked
            {
                bits -= (bits >> 1) & 0x5555555555555555UL;
                bits = (bits & 0x3333333333333333UL) + ((bits >> 2) & 0x3333333333333333UL);
                bits = (bits + (bits >> 4)) & 0x0f0f0f0f0f0f0f0fUL;
                return (int)((bits * 0x0101010101010101UL) >> 56);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TrailingZeroCount(ulong bits)
            => bits == 0 ? 64 : Count(unchecked((bits & (0UL - bits)) - 1UL));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int LeadingZeroCount(ulong bits)
        {
            if (bits == 0) return 64;
            int count = 0;
            if ((bits >> 32) == 0) { count += 32; bits <<= 32; }
            if ((bits >> 48) == 0) { count += 16; bits <<= 16; }
            if ((bits >> 56) == 0) { count += 8; bits <<= 8; }
            if ((bits >> 60) == 0) { count += 4; bits <<= 4; }
            if ((bits >> 62) == 0) { count += 2; bits <<= 2; }
            return count + ((bits >> 63) == 0 ? 1 : 0);
        }
    }
}
