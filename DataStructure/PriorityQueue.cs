using System;

namespace LegendaryTools
{
    /// <summary>
    /// A priority queue implementation using QuickSort for ordering elements.
    /// </summary>
    /// <typeparam name="T">The type of elements stored in the queue.</typeparam>
    public class PriorityQueue<T> where T : IComparable<T>
    {
        private T[] data;          // Internal array to store elements
        private int count;         // Current number of elements in the queue
        private int capacity;      // Current capacity of the internal array

        /// <summary>
        /// Initializes a new instance of the PriorityQueue class with an initial capacity.
        /// </summary>
        /// <param name="initialCapacity">The initial capacity of the queue.</param>
        public PriorityQueue(int initialCapacity = 16)
        {
            if (initialCapacity <= 0)
                throw new ArgumentException("Initial capacity must be greater than zero.");

            capacity = initialCapacity;
            data = new T[capacity];
            count = 0;
        }

        /// <summary>
        /// Adds a new element to the priority queue.
        /// </summary>
        /// <param name="item">The element to add.</param>
        public void Enqueue(T item)
        {
            if (count == capacity)
            {
                Resize();
            }

            data[count] = item;
            count++;
            QuickSort(0, count - 1);
        }

        /// <summary>
        /// Removes and returns the element with the highest priority.
        /// </summary>
        /// <returns>The element with the highest priority.</returns>
        /// <exception cref="InvalidOperationException">Thrown when attempting to dequeue from an empty queue.</exception>
        public T Dequeue()
        {
            if (IsEmpty)
                throw new InvalidOperationException("Cannot dequeue from an empty priority queue.");

            T highestPriorityItem = data[0];
            // Move the last element to the front and reduce the count
            data[0] = data[count - 1];
            count--;
            // Sort the array again to maintain order
            QuickSort(0, count - 1);
            return highestPriorityItem;
        }

        /// <summary>
        /// Returns the element with the highest priority without removing it.
        /// </summary>
        /// <returns>The element with the highest priority.</returns>
        /// <exception cref="InvalidOperationException">Thrown when attempting to peek into an empty queue.</exception>
        public T Peek()
        {
            if (IsEmpty)
                throw new InvalidOperationException("Cannot peek into an empty priority queue.");

            return data[0];
        }

        /// <summary>
        /// Gets the number of elements in the priority queue.
        /// </summary>
        public int Count => count;

        /// <summary>
        /// Determines whether the priority queue is empty.
        /// </summary>
        public bool IsEmpty => count == 0;

        /// <summary>
        /// Resizes the internal array by doubling its capacity.
        /// </summary>
        private void Resize()
        {
            capacity *= 2;
            Array.Resize(ref data, capacity);
        }

        /// <summary>
        /// Implements the QuickSort algorithm to sort the array based on element priority.
        /// </summary>
        /// <param name="low">The starting index.</param>
        /// <param name="high">The ending index.</param>
        private void QuickSort(int low, int high)
        {
            if (low < high)
            {
                int pivotIndex = Partition(low, high);
                QuickSort(low, pivotIndex - 1);
                QuickSort(pivotIndex + 1, high);
            }
        }

        /// <summary>
        /// Partitions the array for QuickSort.
        /// </summary>
        /// <param name="low">The starting index.</param>
        /// <param name="high">The ending index.</param>
        /// <returns>The pivot index after partitioning.</returns>
        private int Partition(int low, int high)
        {
            T pivot = data[high];
            int i = low - 1;

            for (int j = low; j < high; j++)
            {
                // For a max-priority queue, use CompareTo > 0
                if (data[j].CompareTo(pivot) > 0)
                {
                    i++;
                    Swap(i, j);
                }
            }

            Swap(i + 1, high);
            return i + 1;
        }

        /// <summary>
        /// Swaps two elements in the internal array.
        /// </summary>
        /// <param name="i">The index of the first element.</param>
        /// <param name="j">The index of the second element.</param>
        private void Swap(int i, int j)
        {
            if (i == j) return;

            T temp = data[i];
            data[i] = data[j];
            data[j] = temp;
        }

        /// <summary>
        /// Clears all elements from the priority queue.
        /// </summary>
        public void Clear()
        {
            Array.Clear(data, 0, count);
            count = 0;
        }

        /// <summary>
        /// Returns an enumerator that iterates through the priority queue.
        /// </summary>
        /// <returns>An enumerator for the priority queue.</returns>
        public System.Collections.Generic.IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < count; i++)
            {
                yield return data[i];
            }
        }
    }
}
