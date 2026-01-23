using System;

namespace LegendaryTools.Common.Core.Patterns.ECS.Random
{
    /// <summary>
    /// Deterministic RNG based on PCG32 (32-bit output, 64-bit state).
    /// Stable across platforms when using integer operations only.
    /// </summary>
    [Serializable]
    public struct DeterministicRng : IRng
    {
        private const ulong Multiplier = 6364136223846793005UL;

        private ulong _state;
        private ulong _inc;

        /// <summary>
        /// Creates a RNG with a given seed and stream id.
        /// Using different stream ids yields independent sequences.
        /// </summary>
        public DeterministicRng(ulong seed, ulong streamId = 1)
        {
            _state = 0UL;
            _inc = (streamId << 1) | 1UL; // must be odd
            Seed(seed);
        }

        /// <inheritdoc />
        public RngState State => new(_state, _inc);

        /// <inheritdoc />
        public void SetState(RngState state)
        {
            _state = state.State;
            _inc = state.Inc | 1UL; // enforce odd increment for PCG safety
        }

        /// <summary>
        /// Reseeds the generator while keeping the current stream increment.
        /// </summary>
        public void Seed(ulong seed)
        {
            _state = 0UL;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        /// <inheritdoc />
        public uint NextUInt()
        {
            ulong oldState = _state;
            _state = unchecked(oldState * Multiplier + _inc);

            uint xorshifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
            int rot = (int)(oldState >> 59);
            return (xorshifted >> rot) | (xorshifted << (-rot & 31));
        }

        /// <inheritdoc />
        public ulong NextULong()
        {
            // Compose 64-bit value from two 32-bit outputs.
            ulong hi = NextUInt();
            ulong lo = NextUInt();
            return (hi << 32) | lo;
        }

        /// <inheritdoc />
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be > 0.");

            return (int)NextUIntBounded((uint)maxExclusive);
        }

        /// <inheritdoc />
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be > minInclusive.");

            uint range = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)NextUIntBounded(range);
        }

        /// <inheritdoc />
        public int NextIntInclusive(int minInclusive, int maxInclusive)
        {
            if (maxInclusive < minInclusive)
                throw new ArgumentOutOfRangeException(nameof(maxInclusive), "maxInclusive must be >= minInclusive.");

            // inclusive range size = (max - min + 1)
            ulong range = (ulong)(maxInclusive - minInclusive) + 1UL;
            if (range <= uint.MaxValue) return minInclusive + (int)NextUIntBounded((uint)range);

            // Very large ranges: fallback to 64-bit bounded.
            return (int)(minInclusive + (long)NextULongBounded(range));
        }

        /// <inheritdoc />
        public float NextFloat01()
        {
            // Use 24 random bits for float mantissa => deterministic and uniform-ish.
            // Produces value in [0,1).
            uint v = NextUInt();
            uint mantissa = v >> 8; // keep top 24 bits
            return mantissa * (1.0f / 16777216.0f); // 2^24
        }

        /// <inheritdoc />
        public float NextFloat(float minInclusive, float maxExclusive)
        {
            if (!(maxExclusive > minInclusive))
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be > minInclusive.");

            return minInclusive + (maxExclusive - minInclusive) * NextFloat01();
        }

        /// <inheritdoc />
        public double NextDouble01()
        {
            // 53 bits for double mantissa.
            ulong v = NextULong();
            ulong mantissa = v >> 11;
            return mantissa * (1.0 / 9007199254740992.0); // 2^53
        }

        /// <inheritdoc />
        public bool Chance(float p)
        {
            if (p <= 0f) return false;
            if (p >= 1f) return true;
            return NextFloat01() < p;
        }

        /// <inheritdoc />
        public bool Chance(double p)
        {
            if (p <= 0d) return false;
            if (p >= 1d) return true;
            return NextDouble01() < p;
        }

        /// <inheritdoc />
        public bool NextBool()
        {
            return (NextUInt() & 1U) != 0U;
        }

        /// <inheritdoc />
        public void NextBytes(byte[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            int i = 0;
            while (i < buffer.Length)
            {
                uint r = NextUInt();
                buffer[i++] = (byte)r;
                if (i < buffer.Length) buffer[i++] = (byte)(r >> 8);
                if (i < buffer.Length) buffer[i++] = (byte)(r >> 16);
                if (i < buffer.Length) buffer[i++] = (byte)(r >> 24);
            }
        }

        /// <inheritdoc />
        public void Advance(ulong delta)
        {
            // PCG "advance" using exponentiation by squaring.
            // State transition: state = state * Multiplier + inc (mod 2^64)
            ulong curMult = Multiplier;
            ulong curPlus = _inc;
            ulong accMult = 1UL;
            ulong accPlus = 0UL;

            ulong d = delta;
            while (d > 0UL)
            {
                if ((d & 1UL) != 0UL)
                {
                    accMult = unchecked(accMult * curMult);
                    accPlus = unchecked(accPlus * curMult + curPlus);
                }

                curPlus = unchecked((curMult + 1UL) * curPlus);
                curMult = unchecked(curMult * curMult);
                d >>= 1;
            }

            _state = unchecked(accMult * _state + accPlus);
        }

        /// <inheritdoc />
        public DeterministicRng Split(ulong streamId)
        {
            // Derive a seed from current RNG + stream id, then initialize a new RNG.
            // This makes stream creation deterministic and order-dependent (by design).
            ulong mixed = SplitMix64.Mix(NextULong() ^ SplitMix64.Mix(streamId));
            return new DeterministicRng(mixed, streamId);
        }

        private uint NextUIntBounded(uint bound)
        {
            // Rejection sampling to avoid modulo bias.
            // threshold = (-bound) % bound (for uint arithmetic)
            uint threshold = unchecked((uint)(0U - bound)) % bound;

            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold) return r % bound;
            }
        }

        private ulong NextULongBounded(ulong bound)
        {
            // 64-bit rejection sampling.
            ulong threshold = unchecked(0UL - bound) % bound;

            while (true)
            {
                ulong r = NextULong();
                if (r >= threshold) return r % bound;
            }
        }
    }
}