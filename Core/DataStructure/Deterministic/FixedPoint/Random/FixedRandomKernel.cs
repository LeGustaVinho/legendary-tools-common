namespace LegendaryTools.FixedPoint
{
    internal static class FixedRandomKernel
    {
        // SplitMix64 finalizer by Sebastiano Vigna, public domain (prng.di.unimi.it/splitmix64.c).
        internal static ulong Mix64(ulong value)
        {
            value = unchecked((value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL);
            value = unchecked((value ^ (value >> 27)) * 0x94d049bb133111ebUL);
            return value ^ (value >> 31);
        }

        // Philox4x32-10, Random123 by D. E. Shaw Research. See Random123-LICENSE.txt.
        // Counter order: block low/high, sequence low/high. Key order: low/high.
        internal static void Block(ulong key, ulong sequence, ulong block, out uint a, out uint b, out uint c, out uint d)
        {
            unchecked
            {
                a = (uint)block; b = (uint)(block >> 32); c = (uint)sequence; d = (uint)(sequence >> 32);
                uint k0 = (uint)key, k1 = (uint)(key >> 32);
                for (int round = 0; round < 10; round++)
                {
                    ulong p0 = (ulong)a * 0xD2511F53U, p1 = (ulong)c * 0xCD9E8D57U;
                    uint nextA = (uint)(p1 >> 32) ^ b ^ k0, nextC = (uint)(p0 >> 32) ^ d ^ k1;
                    a = nextA; b = (uint)p1; c = nextC; d = (uint)p0;
                    k0 += 0x9E3779B9U; k1 += 0xBB67AE85U;
                }
            }
        }

        internal static bool Bounded(ref FixedRandom random, ulong bound, out ulong value, out FixedStatus status)
        {
            value = 0;
            if (bound == 0) { status = FixedStatus.InvalidDomain; return false; }
            if (bound == 1) { status = FixedStatus.None; return true; }
            // Discard the incomplete residue classes at the bottom. The accepted range is a multiple of bound.
            if (bound <= uint.MaxValue)
            {
                uint n = (uint)bound, threshold = unchecked(0U - n) % n;
                do
                {
                    if (!random.TryNextUInt32(out uint word, out status)) return false;
                    if (word >= threshold) { value = word % n; return true; }
                } while (true);
            }
            ulong limit = unchecked(0UL - bound) % bound;
            do
            {
                if (!random.TryNextUInt64(out ulong word, out status)) return false;
                if (word >= limit) { value = word % bound; return true; }
            } while (true);
        }

        internal static bool Range(ref FixedRandom random, ulong min, ulong max, FixedKind kind, FixedRuntimeState state,
            bool inclusive, out ulong result, out FixedStatus status)
        {
            var f = new FixedFormat(kind, state); result = f.NaN;
            status = !state.IsInitialized ? FixedStatus.Uninitialized : FixedStatus.None;
            if (!status.IsSuccess()) return false;
            FixedDecoded a = f.Decode(min), b = f.Decode(max);
            if (!a.IsFinite || !b.IsFinite) { status = FixedStatus.InvalidDomain; return false; }
            min = a.IsZero ? 0 : min & f.Mask; max = b.IsZero ? 0 : max & f.Mask;
            if (FixedKernel.Compare(min, max, kind, state) > 0) { status = FixedStatus.InvalidDomain; return false; }
            if (min == max) { result = min; return true; }
            ulong width = unchecked((max - min) & f.Mask);
            if (inclusive) width++;
            if (!Bounded(ref random, width, out ulong offset, out status)) return false;
            result = unchecked(min + offset) & f.Mask; return true;
        }

        internal static bool Unit(ref FixedRandom random, FixedKind kind, FixedRuntimeState state, out ulong raw, out FixedStatus status)
        {
            var f = new FixedFormat(kind, state); raw = f.NaN;
            status = state.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized;
            if (!status.IsSuccess() || !Bounded(ref random, f.Scale, out ulong sample, out status)) return false;
            raw = sample; return true;
        }

        internal static bool Chance(ref FixedRandom random, ulong raw, FixedKind kind, FixedRuntimeState state,
            out bool result, out FixedStatus status)
        {
            result = false; var f = new FixedFormat(kind, state);
            status = !state.IsInitialized ? FixedStatus.Uninitialized : FixedStatus.None;
            if (!status.IsSuccess()) return false;
            var p = f.Decode(raw);
            if (!p.IsFinite || p.Negative && !p.IsZero || p.Magnitude > f.Scale)
            { status = FixedStatus.InvalidDomain; return false; }
            return random.TryChanceRatio(p.Magnitude, f.Scale, out result, out status);
        }
    }
}
