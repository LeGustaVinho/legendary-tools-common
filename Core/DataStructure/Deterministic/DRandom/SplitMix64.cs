using System;
using System.Text;

namespace LegendaryTools.Common.Core.Patterns.ECS.Random
{
    /// <summary>
    /// SplitMix64 utilities for seed expansion and hashing.
    /// </summary>
    public static class SplitMix64
    {
        /// <summary>
        /// Mixes the input bits (one-shot). Useful for deriving sub-seeds.
        /// </summary>
        public static ulong Mix(ulong x)
        {
            x = unchecked(x + 0x9E3779B97F4A7C15UL);
            x = unchecked((x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL);
            x = unchecked((x ^ (x >> 27)) * 0x94D049BB133111EBUL);
            return x ^ (x >> 31);
        }

        /// <summary>
        /// Creates a stable 64-bit seed from a string (UTF8 bytes).
        /// </summary>
        public static ulong SeedFromString(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            byte[] bytes = Encoding.UTF8.GetBytes(text);
            ulong h = 14695981039346656037UL; // FNV offset basis
            for (int i = 0; i < bytes.Length; i++)
            {
                h ^= bytes[i];
                h *= 1099511628211UL; // FNV prime
            }

            return Mix(h);
        }

        /// <summary>
        /// Combines two seeds into one, in a stable way.
        /// </summary>
        public static ulong Combine(ulong a, ulong b)
        {
            return Mix(a ^ Mix(b));
        }

        /// <summary>
        /// Combines multiple inputs into a seed.
        /// </summary>
        public static ulong Combine(ulong a, ulong b, ulong c)
        {
            return Mix(Combine(a, b) ^ Mix(c));
        }
    }
}