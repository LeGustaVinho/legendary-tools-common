using System;
using System.Runtime.CompilerServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Deterministic, stateless RNG based on a key.
    /// Order of calls does not matter as long as keys are stable.
    /// </summary>
    public static class DeterministicRng
    {
        /// <summary>
        /// Generates a deterministic 64-bit random number for the given key.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong NextU64(in DeterministicRngKey key)
        {
            // Build a 64-bit input from the key fields then apply a strong finalizer mix.
            // This is a "counter-based" RNG: same input -> same output.
            ulong x = key.BaseSeed;

            x ^= (ulong)(uint)key.Tick;
            x = Mix64(x);

            x ^= (ulong)(uint)key.StreamId;
            x = Mix64(x);

            x ^= key.ScopeId;
            x = Mix64(x);

            x ^= key.SampleIndex;
            x = Mix64(x);

#if ECS_DETERMINISM_CHECKS
            // No global accumulator here by design; callers can accumulate if desired.
#endif
            return x;
        }

        /// <summary>
        /// Generates a deterministic 32-bit random number for the given key.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint NextU32(in DeterministicRngKey key)
        {
            return (uint)(NextU64(in key) >> 32);
        }

        /// <summary>
        /// Returns a deterministic float in [0, 1).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float NextFloat01(in DeterministicRngKey key)
        {
            // Use top 24 bits for a stable float mantissa-like distribution.
            // 24 bits fits exactly in float's mantissa precision.
            uint v = NextU32(in key);
            uint top24 = v >> 8; // 24 bits
            return top24 * (1.0f / 16777216.0f); // 2^24
        }

        /// <summary>
        /// Returns a deterministic float in [minInclusive, maxExclusive).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float RangeFloat(in DeterministicRngKey key, float minInclusive, float maxExclusive)
        {
            float t = NextFloat01(in key);
            return minInclusive + (maxExclusive - minInclusive) * t;
        }

        /// <summary>
        /// Returns a deterministic int in [minInclusive, maxExclusive) with unbiased distribution.
        /// </summary>
        public static int RangeInt(in DeterministicRngKey key, int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be greater than minInclusive.");

            uint range = (uint)(maxExclusive - minInclusive);

            // Rejection sampling to remove modulo bias.
            // threshold = (2^32 % range) -> reject values below threshold.
            uint threshold = (uint)(unchecked((0u - range) % range));

            // Deterministically probe additional samples by incrementing sampleIndex.
            // This stays stateless and stable.
            uint sample = key.SampleIndex;
            while (true)
            {
                uint r = NextU32(key.WithSample(sample));
                if (r >= threshold)
                {
                    uint scaled = r % range;
                    return (int)(scaled + (uint)minInclusive);
                }

                sample++;
            }
        }

        /// <summary>
        /// Splits a single key into a deterministic "subkey" for additional samples.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DeterministicRngKey SubKey(in DeterministicRngKey key, uint subSampleIndex)
        {
            // Derive by combining sampleIndex in a stable way.
            // Keeping the same base fields ensures identical cross-machine behavior.
            return new DeterministicRngKey(key.BaseSeed, key.Tick, key.StreamId, key.ScopeId, subSampleIndex);
        }

        /// <summary>
        /// 64-bit mix finalizer (SplitMix64-style finalizer).
        /// Good diffusion for counter-based RNG.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Mix64(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
