#nullable enable

using System;
using System.Runtime.CompilerServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Deterministic component bitset used for archetype/query matching.
    /// </summary>
    /// <remarks>
    /// - Bit index corresponds to component id value (0 is unused/reserved).
    /// - No allocations during matching operations.
    /// </remarks>
    public sealed class ComponentBitSet
    {
        private readonly ulong[] _words;

        /// <summary>
        /// Initializes a bitset with a fixed word count.
        /// </summary>
        public ComponentBitSet(int wordCount)
        {
            if (wordCount <= 0)
                wordCount = 1;

            _words = new ulong[wordCount];
        }

        /// <summary>
        /// Gets the underlying word count.
        /// </summary>
        public int WordCount => _words.Length;

        /// <summary>
        /// Clears all bits.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            Array.Clear(_words, 0, _words.Length);
        }

        /// <summary>
        /// Sets the given bit index.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int bitIndex)
        {
            if (bitIndex < 0)
                return;

            int word = bitIndex >> 6;
            if ((uint)word >= (uint)_words.Length)
                return;

            int shift = bitIndex & 63;
            _words[word] |= 1UL << shift;
        }

        /// <summary>
        /// Returns true if any bit is set.
        /// </summary>
        public bool AnySet
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                for (int i = 0; i < _words.Length; i++)
                {
                    if (_words[i] != 0UL)
                        return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Returns true if this bitset contains all bits in <paramref name="required"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsAll(ComponentBitSet required)
        {
            int n = required._words.Length;
            if (_words.Length < n)
                return false;

            for (int i = 0; i < n; i++)
            {
                ulong req = required._words[i];
                if ((_words[i] & req) != req)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Returns true if this bitset has any common bit with <paramref name="other"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(ComponentBitSet other)
        {
            int n = _words.Length < other._words.Length ? _words.Length : other._words.Length;
            for (int i = 0; i < n; i++)
            {
                if ((_words[i] & other._words[i]) != 0UL)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Returns true if this bitset has no common bits with <paramref name="other"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IntersectsNone(ComponentBitSet other)
        {
            return !Intersects(other);
        }

        /// <summary>
        /// Copies all bits from <paramref name="src"/> into this instance.
        /// </summary>
        public void CopyFrom(ComponentBitSet src)
        {
            int n = _words.Length < src._words.Length ? _words.Length : src._words.Length;
            Array.Copy(src._words, 0, _words, 0, n);

            // If this is larger than src, clear the remaining.
            if (_words.Length > n)
                Array.Clear(_words, n, _words.Length - n);
        }

        /// <summary>
        /// Computes the required ulong word count for the given maximum component id.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetWordCountForMaxId(int maxComponentId)
        {
            if (maxComponentId < 0)
                maxComponentId = 0;

            // Need to represent bit maxComponentId inclusive.
            int maxWord = maxComponentId >> 6;
            return maxWord + 1;
        }
    }
}