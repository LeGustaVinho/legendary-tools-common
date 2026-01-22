using System.Runtime.CompilerServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Lightweight rolling hash accumulator for determinism checks.
    /// Use in debug/dev builds to compare cross-machine outputs.
    /// </summary>
    public struct DeterminismHashAccumulator
    {
        private ulong _hash;

        /// <summary>Gets the current rolling hash value.</summary>
        public ulong Value => _hash;

        /// <summary>Resets the rolling hash to zero.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset() => _hash = 0UL;

        /// <summary>
        /// Adds a 64-bit value to the rolling hash.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(ulong value)
        {
            // Simple and fast mixing suitable for debug checks.
            _hash ^= value + 0x9E3779B97F4A7C15UL + (_hash << 6) + (_hash >> 2);
        }

        /// <summary>
        /// Adds a 32-bit value to the rolling hash.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(uint value) => Add((ulong)value);

        /// <summary>
        /// Adds an integer to the rolling hash.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(int value) => Add((ulong)(uint)value);
    }
}
