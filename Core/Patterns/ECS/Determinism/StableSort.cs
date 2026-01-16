#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Deterministic stable sorting helpers (no GC allocations; uses <see cref="ArrayPool{T}"/> when needed).
    /// </summary>
    public static class StableSort
    {
        /// <summary>
        /// Sorts the span using a stable deterministic algorithm.
        /// </summary>
        public static void Sort<T>(Span<T> span, IComparer<T> comparer)
        {
            if (span.Length <= 1)
                return;

            // Stable insertion sort is fast for small inputs and always stable.
            if (span.Length <= 32)
            {
                InsertionSort(span, comparer);
                return;
            }

            // Stable merge sort for larger inputs.
            T[] rented = ArrayPool<T>.Shared.Rent(span.Length);
            try
            {
                Span<T> temp = rented.AsSpan(0, span.Length);
                MergeSort(span, temp, comparer);
            }
            finally
            {
                ArrayPool<T>.Shared.Return(rented, false);
            }
        }

        /// <summary>
        /// Sorts the span using a stable deterministic algorithm and <see cref="IComparable{T}"/>.
        /// </summary>
        public static void Sort<T>(Span<T> span) where T : IComparable<T>
        {
            Sort(span, Comparer<T>.Default);
        }

        private static void InsertionSort<T>(Span<T> span, IComparer<T> comparer)
        {
            for (int i = 1; i < span.Length; i++)
            {
                T key = span[i];
                int j = i - 1;

                // Move items greater than key one position ahead.
                // Using ">" preserves stability (equal items keep their relative order).
                while (j >= 0 && comparer.Compare(span[j], key) > 0)
                {
                    span[j + 1] = span[j];
                    j--;
                }

                span[j + 1] = key;
            }
        }

        private static void MergeSort<T>(Span<T> span, Span<T> temp, IComparer<T> comparer)
        {
            // Bottom-up merge sort to avoid recursion.
            int n = span.Length;

            // Copy initial data into temp buffer.
            span.CopyTo(temp);

            Span<T> src = temp;
            Span<T> dst = span;

            // Tracks where "src" currently points (Span cannot be ReferenceEquals'ed).
            bool srcIsSpan = false; // src starts as temp

            for (int width = 1; width < n; width <<= 1)
            {
                for (int i = 0; i < n; i += width << 1)
                {
                    int left = i;
                    int mid = Math.Min(i + width, n);
                    int right = Math.Min(i + (width << 1), n);

                    Merge(src, dst, left, mid, right, comparer);
                }

                // Swap roles.
                Span<T> swap = src;
                src = dst;
                dst = swap;

                srcIsSpan = !srcIsSpan;
            }

            // If the final result ended in temp, copy back to span.
            if (!srcIsSpan) src.CopyTo(span);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Merge<T>(Span<T> src, Span<T> dst, int left, int mid, int right, IComparer<T> comparer)
        {
            int i = left;
            int j = mid;
            int k = left;

            while (i < mid && j < right)
            {
                // Use "<=" to preserve stability (left item wins ties).
                if (comparer.Compare(src[i], src[j]) <= 0)
                    dst[k++] = src[i++];
                else
                    dst[k++] = src[j++];
            }

            while (i < mid)
            {
                dst[k++] = src[i++];
            }

            while (j < right)
            {
                dst[k++] = src[j++];
            }
        }
    }
}