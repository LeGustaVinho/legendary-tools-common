using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Memory
{
    /// <summary>
    /// Simple deterministic-friendly managed array pool.
    /// Avoids System.Buffers dependency variations across Unity profiles.
    /// </summary>
    internal static class EcsArrayPool<T>
    {
        private static readonly object s_lock = new();
        private static readonly Dictionary<int, Stack<T[]>> s_buckets = new(16);

        /// <summary>
        /// Rents an array with at least <paramref name="minLength"/> elements.
        /// Returned array length can be larger than requested.
        /// </summary>
        private static readonly bool s_typeContainsReferences =
            RuntimeHelpers.IsReferenceOrContainsReferences<T>();

        public static T[] Rent(int minLength)
        {
            if (minLength < 1) minLength = 1;

            int size = NextPowerOfTwo(minLength);

            lock (s_lock)
            {
                if (s_buckets.TryGetValue(size, out Stack<T[]> stack) && stack.Count > 0)
                    return stack.Pop();
            }

            return new T[size];
        }

        /// <summary>
        /// Returns an array to the pool.
        /// </summary>
        public static void Return(T[] array, bool clear)
        {
            if (array == null) return;

            // Always clear if T can contain references to avoid retaining objects via the pool.
            if (clear || s_typeContainsReferences)
                Array.Clear(array, 0, array.Length);

            int size = array.Length;
            lock (s_lock)
            {
                if (!s_buckets.TryGetValue(size, out Stack<T[]> stack))
                {
                    stack = new Stack<T[]>(8);
                    s_buckets.Add(size, stack);
                }

                stack.Push(array);
            }
        }

        private static int NextPowerOfTwo(int v)
        {
            v--;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            v++;
            return v < 1 ? 1 : v;
        }
    }
}