using System;
using System.Runtime.CompilerServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Deterministic RNG context with local state.
    /// Use only when the call order within the context is guaranteed deterministic.
    /// For network readiness, prefer <see cref="DeterministicRng"/> with keys.
    /// </summary>
    public struct RngContext
    {
        private ulong _state;

        /// <summary>Gets the current internal state (serialize for rollback if needed).</summary>
        public ulong State => _state;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RngContext(ulong seed)
        {
            _state = seed;
        }

        /// <summary>
        /// Creates a context seed derived from a deterministic key.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RngContext FromKey(in DeterministicRngKey key)
        {
            return new RngContext(DeterministicRng.NextU64(in key));
        }

        /// <summary>
        /// Advances and returns a 64-bit random value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong NextU64()
        {
            // Use the same finalizer as stateless RNG, but evolve state each call.
            _state = Mix64(_state);
            return _state;
        }

        /// <summary>
        /// Advances and returns a 32-bit random value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint NextU32() => (uint)(NextU64() >> 32);

        /// <summary>
        /// Advances and returns a float in [0, 1).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float NextFloat01()
        {
            uint v = NextU32();
            uint top24 = v >> 8;
            return top24 * (1.0f / 16777216.0f);
        }

        /// <summary>
        /// Advances and returns an int in [minInclusive, maxExclusive) with unbiased distribution.
        /// </summary>
        public int RangeInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be greater than minInclusive.");

            uint range = (uint)(maxExclusive - minInclusive);
            uint threshold = (uint)(unchecked((0u - range) % range));

            while (true)
            {
                uint r = NextU32();
                if (r >= threshold)
                {
                    uint scaled = r % range;
                    return (int)(scaled + (uint)minInclusive);
                }
            }
        }

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
