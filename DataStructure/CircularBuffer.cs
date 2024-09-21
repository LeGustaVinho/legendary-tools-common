using System;

namespace LegendaryTools
{
    /// <summary>
    /// A fixed-size circular buffer (ring buffer) implementation.
    /// </summary>
    /// <typeparam name="T">The type of elements stored in the buffer.</typeparam>
    public class CircularBuffer<T>
    {
        private readonly T[] buffer;    // Fixed-size array to store buffer elements
        private int head;               // Points to the next position to write
        private int tail;               // Points to the next position to read
        private int count;              // Number of elements currently in the buffer
        private readonly int capacity;  // Maximum number of elements the buffer can hold

        /// <summary>
        /// Initializes a new instance of the CircularBuffer class with the specified capacity.
        /// </summary>
        /// <param name="capacity">The maximum number of elements the buffer can hold.</param>
        /// <exception cref="ArgumentException">Thrown when capacity is less than or equal to zero.</exception>
        public CircularBuffer(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentException("Capacity must be greater than zero.", nameof(capacity));

            this.capacity = capacity;
            buffer = new T[capacity];
            head = 0;
            tail = 0;
            count = 0;
        }

        /// <summary>
        /// Adds an element to the buffer. If the buffer is full, the oldest element is overwritten.
        /// </summary>
        /// <param name="item">The element to add.</param>
        public void Enqueue(T item)
        {
            buffer[head] = item;
            head = (head + 1) % capacity;

            if (count == capacity)
            {
                // Buffer is full, overwrite the oldest element
                tail = (tail + 1) % capacity;
            }
            else
            {
                count++;
            }
        }

        /// <summary>
        /// Removes and returns the oldest element from the buffer.
        /// </summary>
        /// <returns>The oldest element in the buffer.</returns>
        /// <exception cref="InvalidOperationException">Thrown when attempting to dequeue from an empty buffer.</exception>
        public T Dequeue()
        {
            if (IsEmpty)
                throw new InvalidOperationException("Cannot dequeue from an empty buffer.");

            T item = buffer[tail];
            buffer[tail] = default(T); // Clear the slot (optional)
            tail = (tail + 1) % capacity;
            count--;
            return item;
        }

        /// <summary>
        /// Gets the oldest element without removing it from the buffer.
        /// </summary>
        /// <returns>The oldest element in the buffer.</returns>
        /// <exception cref="InvalidOperationException">Thrown when attempting to peek into an empty buffer.</exception>
        public T Peek()
        {
            if (IsEmpty)
                throw new InvalidOperationException("Cannot peek into an empty buffer.");

            return buffer[tail];
        }

        /// <summary>
        /// Gets the current number of elements in the buffer.
        /// </summary>
        public int Count => count;

        /// <summary>
        /// Gets the maximum number of elements the buffer can hold.
        /// </summary>
        public int Capacity => capacity;

        /// <summary>
        /// Determines whether the buffer is empty.
        /// </summary>
        public bool IsEmpty => count == 0;

        /// <summary>
        /// Determines whether the buffer is full.
        /// </summary>
        public bool IsFull => count == capacity;

        /// <summary>
        /// Clears all elements from the buffer.
        /// </summary>
        public void Clear()
        {
            Array.Clear(buffer, 0, buffer.Length);
            head = 0;
            tail = 0;
            count = 0;
        }

        /// <summary>
        /// Returns an enumerator that iterates through the buffer from oldest to newest.
        /// </summary>
        /// <returns>An enumerator for the buffer.</returns>
        public System.Collections.Generic.IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < count; i++)
            {
                yield return buffer[(tail + i) % capacity];
            }
        }
    }
}
