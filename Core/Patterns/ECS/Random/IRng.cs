using System;

namespace LegendaryTools.Common.Core.Patterns.ECS.Random
{
    /// <summary>
    /// Deterministic random number generator interface for gameplay.
    /// Implementations must be platform-stable and reproducible.
    /// </summary>
    public interface IRng
    {
        /// <summary>Gets the current RNG state snapshot.</summary>
        RngState State { get; }

        /// <summary>Sets the RNG state snapshot.</summary>
        void SetState(RngState state);

        /// <summary>Returns the next uniformly distributed 32-bit unsigned integer.</summary>
        uint NextUInt();

        /// <summary>Returns the next uniformly distributed 64-bit unsigned integer.</summary>
        ulong NextULong();

        /// <summary>Returns an integer in [0, maxExclusive).</summary>
        int NextInt(int maxExclusive);

        /// <summary>Returns an integer in [minInclusive, maxExclusive).</summary>
        int NextInt(int minInclusive, int maxExclusive);

        /// <summary>Returns an integer in [minInclusive, maxInclusive].</summary>
        int NextIntInclusive(int minInclusive, int maxInclusive);

        /// <summary>Returns a float in [0, 1).</summary>
        float NextFloat01();

        /// <summary>Returns a float in [minInclusive, maxExclusive).</summary>
        float NextFloat(float minInclusive, float maxExclusive);

        /// <summary>Returns a double in [0, 1).</summary>
        double NextDouble01();

        /// <summary>Returns true with probability p in [0,1].</summary>
        bool Chance(float p);

        /// <summary>Returns true with probability p in [0,1].</summary>
        bool Chance(double p);

        /// <summary>Returns a random boolean with 50% probability.</summary>
        bool NextBool();

        /// <summary>Fills the buffer with random bytes.</summary>
        void NextBytes(byte[] buffer);

        /// <summary>Advances the RNG by delta steps (deterministic skip-ahead).</summary>
        void Advance(ulong delta);

        /// <summary>Creates a new independent RNG stream derived from this RNG.</summary>
        DeterministicRng Split(ulong streamId);
    }
}