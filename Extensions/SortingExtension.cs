using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LegendaryTools
{
    public static class SortingExtension
    {
        /// <summary>
        ///     Asynchronously sorts the provided list in place using the Merge Sort algorithm.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The list to sort.</param>
        /// <param name="comparer">The comparer to determine the order of elements.</param>
        /// <returns>A task that represents the asynchronous sort operation.</returns>
        public static async Task MergeSortAsync<T>(this IList<T> list, IComparer<T> comparer = null)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));

            comparer ??= Comparer<T>.Default;

            // Create a temporary array for merging
            T[] tempArray = new T[list.Count];

            await SortAsync(list, tempArray, 0, list.Count - 1, comparer);
        }

        /// <summary>
        ///     Recursively sorts the list using the Merge Sort algorithm.
        /// </summary>
        private static async Task SortAsync<T>(IList<T> list, T[] tempArray, int left, int right, IComparer<T> comparer)
        {
            if (left >= right) return;
            int middle = left + (right - left) / 2;

            // Define tasks for sorting the left and right halves
            Task leftTask = SortAsync(list, tempArray, left, middle, comparer);
            Task rightTask = SortAsync(list, tempArray, middle + 1, right, comparer);

            // Await both tasks to complete
            await Task.WhenAll(leftTask, rightTask);

            // Merge the sorted halves
            await Task.Run(() => Merge(list, tempArray, left, middle, right, comparer));
        }

        /// <summary>
        ///     Merges two sorted sublists into a single sorted sublist.
        /// </summary>
        private static void Merge<T>(IList<T> list, T[] tempArray, int left, int middle, int right,
            IComparer<T> comparer)
        {
            int i = left;
            int j = middle + 1;
            int k = left;

            // Copy both halves into the temporary array
            for (int index = left; index <= right; index++) tempArray[index] = list[index];

            // Merge back to the original list
            while (i <= middle && j <= right)
            {
                if (comparer.Compare(tempArray[i], tempArray[j]) <= 0)
                    list[k++] = tempArray[i++];
                else
                    list[k++] = tempArray[j++];
            }

            // Copy any remaining elements from the left half
            while (i <= middle)
            {
                list[k++] = tempArray[i++];
            }

            // No need to copy the right half since it's already in place
        }

        /// <summary>
        ///     Sorts the list in-place using the Heapsort algorithm and the provided comparer.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The list to sort.</param>
        /// <param name="comparer">The comparer to determine the order of elements.</param>
        public static void HeapSort<T>(this IList<T> list, IComparer<T> comparer = null)
        {
            if (list == null)
                throw new ArgumentNullException(nameof(list));
            comparer ??= Comparer<T>.Default;

            int count = list.Count;

            // Build the max heap
            for (int i = count / 2 - 1; i >= 0; i--) Heapify(list, count, i, comparer);

            // One by one extract elements from the heap
            for (int i = count - 1; i > 0; i--)
            {
                // Move current root to end
                Swap(list, 0, i);

                // Call heapify on the reduced heap
                Heapify(list, i, 0, comparer);
            }
        }

        /// <summary>
        ///     Maintains the heap property for a subtree rooted at index i.
        /// </summary>
        private static void Heapify<T>(IList<T> list, int heapSize, int rootIndex, IComparer<T> comparer)
        {
            int largest = rootIndex; // Initialize largest as root
            int leftChild = 2 * rootIndex + 1; // Left child index
            int rightChild = 2 * rootIndex + 2; // Right child index

            // If left child exists and is greater than root
            if (leftChild < heapSize && comparer.Compare(list[leftChild], list[largest]) > 0) largest = leftChild;

            // If right child exists and is greater than largest so far
            if (rightChild < heapSize && comparer.Compare(list[rightChild], list[largest]) > 0) largest = rightChild;

            // If largest is not root
            if (largest != rootIndex)
            {
                Swap(list, rootIndex, largest);

                // Recursively heapify the affected subtree
                Heapify(list, heapSize, largest, comparer);
            }
        }

        /// <summary>
        ///     Swaps two elements in the list.
        /// </summary>
        private static void Swap<T>(IList<T> list, int indexA, int indexB)
        {
            if (indexA == indexB)
                return;

            T temp = list[indexA];
            list[indexA] = list[indexB];
            list[indexB] = temp;
        }
        
        private const int TIMSORT_MIN_MERGE = 32;

        public static void Timsort<T>(this IList<T> list, IComparer<T> comparer = null)
        {
            if (list == null)
                throw new ArgumentNullException(nameof(list));
            comparer ??= Comparer<T>.Default;
            
            int n = list.Count;
            if (n < 2)
                return;

            // Calculate minimum run size
            int minRun = MinRunLength(TIMSORT_MIN_MERGE);

            // Stack to keep track of runs
            Stack<Run> runStack = new Stack<Run>();

            int i = 0;
            while (i < n)
            {
                int runStart = i;
                int runEnd = i + 1;

                // Identify if the run is ascending or descending
                if (runEnd < n)
                {
                    if (comparer.Compare(list[runStart], list[runEnd]) <= 0)
                    {
                        // Ascending
                        while (runEnd < n - 1 && comparer.Compare(list[runEnd], list[runEnd + 1]) <= 0)
                            runEnd++;
                    }
                    else
                    {
                        // Descending
                        while (runEnd < n - 1 && comparer.Compare(list[runEnd], list[runEnd + 1]) > 0)
                            runEnd++;
                        // Reverse to make it ascending
                        ReverseRun(list, runStart, runEnd);
                    }
                }

                int runLength = runEnd - runStart + 1;

                // If run is smaller than minRun, extend it with insertion sort
                if (runLength < minRun)
                {
                    int force = Math.Min(minRun, n) - runLength;
                    runEnd = Math.Min(runStart + minRun - 1, n - 1);
                    InsertionSort(list, runStart, runEnd, comparer);
                    runLength = runEnd - runStart + 1;
                }

                // Push the run onto the stack
                runStack.Push(new Run(runStart, runEnd));

                // Check invariants and merge runs if necessary
                MergeCollapse(runStack, list, comparer);

                i = runEnd + 1;
            }

            // Merge all remaining runs
            MergeForceCollapse(runStack, list, comparer);
        }

        private static int MinRunLength(int n)
        {
            int r = 0;
            while (n >= TIMSORT_MIN_MERGE)
            {
                r |= n & 1;
                n >>= 1;
            }

            return n + r;
        }

        private struct Run
        {
            public readonly int Start;
            public readonly int End;

            public Run(int start, int end)
            {
                Start = start;
                End = end;
            }

            public int Length => End - Start + 1;
        }

        private static void ReverseRun<T>(IList<T> list, int start, int end)
        {
            while (start < end)
            {
                T temp = list[start];
                list[start] = list[end];
                list[end] = temp;
                start++;
                end--;
            }
        }

        private static void InsertionSort<T>(IList<T> list, int left, int right, IComparer<T> comparer)
        {
            for (int i = left + 1; i <= right; i++)
            {
                T key = list[i];
                int j = i - 1;
                while (j >= left && comparer.Compare(list[j], key) > 0)
                {
                    list[j + 1] = list[j];
                    j--;
                }

                list[j + 1] = key;
            }
        }

        private static void MergeCollapse<T>(Stack<Run> runStack, IList<T> list, IComparer<T> comparer)
        {
            while (runStack.Count > 1)
            {
                Run X = runStack.Pop();
                Run Y = runStack.Pop();

                if (runStack.Count > 0)
                {
                    Run Z = runStack.Peek();
                    if (Y.Length <= Z.Length + X.Length)
                    {
                        if (Z.Length < X.Length)
                        {
                            runStack.Pop();
                            Merge(list, Z, Y, comparer);
                            runStack.Push(new Run(Z.Start, Y.End));
                        }
                        else
                        {
                            Merge(list, Y, X, comparer);
                            runStack.Push(new Run(Y.Start, X.End));
                        }

                        continue;
                    }
                }

                if (Y.Length <= X.Length)
                {
                    Merge(list, Y, X, comparer);
                    runStack.Push(new Run(Y.Start, X.End));
                }
                else
                {
                    runStack.Push(X);
                    break;
                }
            }
        }

        private static void MergeForceCollapse<T>(Stack<Run> runStack, IList<T> list, IComparer<T> comparer)
        {
            while (runStack.Count > 1)
            {
                Run X = runStack.Pop();
                Run Y = runStack.Pop();
                Merge(list, Y, X, comparer);
                runStack.Push(new Run(Y.Start, X.End));
            }
        }

        private static void Merge<T>(IList<T> list, Run run1, Run run2, IComparer<T> comparer)
        {
            int len1 = run1.Length;
            int len2 = run2.Length;

            // Create copies of runs to merge
            T[] left = new T[len1];
            T[] right = new T[len2];
            for (int i = 0; i < len1; i++)
                left[i] = list[run1.Start + i];
            for (int i = 0; i < len2; i++)
                right[i] = list[run2.Start + i];

            int iLeft = 0, iRight = 0, iMerge = run1.Start;

            while (iLeft < len1 && iRight < len2)
                if (comparer.Compare(left[iLeft], right[iRight]) <= 0)
                    list[iMerge++] = left[iLeft++];
                else
                    list[iMerge++] = right[iRight++];

            while (iLeft < len1) list[iMerge++] = left[iLeft++];

            while (iRight < len2) list[iMerge++] = right[iRight++];
        }
    }
}