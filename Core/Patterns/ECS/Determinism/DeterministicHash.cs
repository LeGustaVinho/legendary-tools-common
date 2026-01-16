#nullable enable

using System;
using System.Runtime.CompilerServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Deterministic hashing utilities (platform-stable).
    /// </summary>
    public static class DeterministicHash
    {
        /// <summary>
        /// FNV-1a 32-bit offset basis.
        /// </summary>
        public const uint Fnv1A32Init = 2166136261u;

        /// <summary>
        /// FNV-1a 32-bit prime.
        /// </summary>
        public const uint Fnv1A32Prime = 16777619u;

        /// <summary>
        /// FNV-1a 64-bit offset basis.
        /// </summary>
        public const ulong Fnv1A64Init = 14695981039346656037ul;

        /// <summary>
        /// FNV-1a 64-bit prime.
        /// </summary>
        public const ulong Fnv1A64Prime = 1099511628211ul;

        /// <summary>
        /// Mixes a 32-bit word into an ongoing FNV-1a 32-bit hash.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Fnv1A32(uint hash, uint data)
        {
            unchecked
            {
                // Mix as 4 bytes to avoid endianness dependence.
                hash = (hash ^ (data & 0xFFu)) * Fnv1A32Prime;
                hash = (hash ^ ((data >> 8) & 0xFFu)) * Fnv1A32Prime;
                hash = (hash ^ ((data >> 16) & 0xFFu)) * Fnv1A32Prime;
                hash = (hash ^ ((data >> 24) & 0xFFu)) * Fnv1A32Prime;
                return hash;
            }
        }

        /// <summary>
        /// Mixes a 32-bit word into an ongoing FNV-1a 64-bit hash (as 4 bytes).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Fnv1A64(ulong hash, uint data)
        {
            unchecked
            {
                hash = (hash ^ (data & 0xFFu)) * Fnv1A64Prime;
                hash = (hash ^ ((data >> 8) & 0xFFu)) * Fnv1A64Prime;
                hash = (hash ^ ((data >> 16) & 0xFFu)) * Fnv1A64Prime;
                hash = (hash ^ ((data >> 24) & 0xFFu)) * Fnv1A64Prime;
                return hash;
            }
        }

        /// <summary>
        /// Hashes a sequence of integers deterministically (order-sensitive) using FNV-1a 32-bit.
        /// </summary>
        public static uint HashInts(ReadOnlySpan<int> values)
        {
            unchecked
            {
                uint h = Fnv1A32Init;
                for (int i = 0; i < values.Length; i++)
                {
                    h = Fnv1A32(h, (uint)values[i]);
                }

                return h;
            }
        }

        /// <summary>
        /// Hashes a sequence of component ids deterministically (order-sensitive) using FNV-1a 32-bit.
        /// </summary>
        public static uint HashComponentTypeIds(ReadOnlySpan<ComponentTypeId> values)
        {
            unchecked
            {
                uint h = Fnv1A32Init;
                for (int i = 0; i < values.Length; i++)
                {
                    h = Fnv1A32(h, (uint)values[i].Value);
                }

                return h;
            }
        }

        /// <summary>
        /// Hashes a sequence of component ids deterministically (order-sensitive) using FNV-1a 64-bit.
        /// </summary>
        public static ulong HashComponentTypeIds64(ReadOnlySpan<ComponentTypeId> values)
        {
            unchecked
            {
                ulong h = Fnv1A64Init;
                for (int i = 0; i < values.Length; i++)
                {
                    h = Fnv1A64(h, (uint)values[i].Value);
                }

                return h;
            }
        }

        /// <summary>
        /// Combines two 32-bit hashes deterministically.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Combine(uint a, uint b)
        {
            unchecked
            {
                uint h = a;
                h = Fnv1A32(h, b);
                return h;
            }
        }

        /// <summary>
        /// Combines two 64-bit hashes deterministically.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Combine(ulong a, uint b)
        {
            unchecked
            {
                ulong h = a;
                h = Fnv1A64(h, b);
                return h;
            }
        }
    }
}