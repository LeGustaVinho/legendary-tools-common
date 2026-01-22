using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Memory
{
    /// <summary>
    /// Minimal pooled growable buffer to avoid List allocations in hot paths.
    /// </summary>
    internal sealed class PooledList<T>
    {
        private static readonly bool s_typeContainsReferences = RuntimeHelpers.IsReferenceOrContainsReferences<T>();

        private T[] _buffer;

        /// <summary>
        /// Initializes a new instance of the <see cref="PooledList{T}"/> class.
        /// </summary>
        /// <param name="initialCapacity">Initial capacity of the list.</param>
        public PooledList(int initialCapacity = 16)
        {
            if (initialCapacity < 1) initialCapacity = 1;

            _buffer = EcsArrayPool<T>.Rent(initialCapacity);
            Count = 0;
        }

        /// <summary>
        /// Gets the current number of elements in the list.
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        /// Current internal buffer capacity. Used for no-growth checks.
        /// </summary>
        public int Capacity => _buffer.Length;

        /// <summary>
        /// Gets or sets the element at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index of the element.</param>
        /// <returns>The element at the specified index.</returns>
        public T this[int index]
        {
            get => _buffer[index];
            set => _buffer[index] = value;
        }

        /// <summary>
        /// Returns a Span view of the active elements.
        /// </summary>
        /// <returns>A Span&lt;T&gt; of the active range.</returns>
        public Span<T> AsSpan()
        {
            return new Span<T>(_buffer, 0, Count);
        }

        /// <summary>
        /// Clears the list. Does not clear the underlying array unless T contains references.
        /// </summary>
        public void Clear()
        {
            Clear(false);
        }

        /// <summary>
        /// Clears the list, optionally forcing a clear of the underlying array.
        /// </summary>
        /// <param name="clearReferences">If true, clears the array range.</param>
        public void Clear(bool clearReferences)
        {
            if (clearReferences && s_typeContainsReferences && Count > 0)
                // Clear only the active range to release references.
                Array.Clear(_buffer, 0, Count);

            Count = 0;
        }

        /// <summary>
        /// Clears the list and forces a clear of the underlying array (useful for dropping references).
        /// </summary>
        public void ClearReferences()
        {
            Clear(true);
        }

        /// <summary>
        /// Adds an item to the list, growing if necessary.
        /// </summary>
        /// <param name="item">The item to add.</param>
        public void Add(in T item)
        {
            int next = Count + 1;
            if (next > _buffer.Length) Grow(next);

            _buffer[Count] = item;
            Count = next;
        }

        /// <summary>
        /// Adds an item without growing the internal buffer.
        /// Returns false if capacity would be exceeded.
        /// </summary>
        public bool TryAddNoGrow(in T item)
        {
            int next = Count + 1;
            if (next > _buffer.Length) return false;

            _buffer[Count] = item;
            Count = next;
            return true;
        }

        /// <summary>
        /// Ensures the list has at least the specified capacity.
        /// </summary>
        /// <param name="capacity">Minimum capacity required.</param>
        public void EnsureCapacity(int capacity)
        {
            if (capacity <= _buffer.Length) return;

            Grow(capacity);
        }

        /// <summary>
        /// Sorts the elements in the entire list using the specified comparer.
        /// </summary>
        /// <param name="comparer">The IComparer&lt;T&gt; implementation to use when comparing elements.</param>
        public void Sort(IComparer<T> comparer)
        {
            Array.Sort(_buffer, 0, Count, comparer);
        }

        /// <summary>
        /// Returns the internal array buffer. Warning: usage may exceed Count.
        /// </summary>
        /// <returns>The raw backing array.</returns>
        public T[] DangerousGetBuffer()
        {
            return _buffer;
        }

        /// <summary>
        /// Disposes the list, returning the buffer to the pool.
        /// </summary>
        /// <param name="clear">Whether to clear the buffer contents before returning.</param>
        public void Dispose(bool clear)
        {
            T[] tmp = _buffer;
            _buffer = Array.Empty<T>();
            Count = 0;

            // If T contains references, always clear before pooling to avoid retaining objects.
            bool shouldClear = clear || s_typeContainsReferences;
            EcsArrayPool<T>.Return(tmp, shouldClear);
        }

        private void Grow(int required)
        {
            int newSize = _buffer.Length;
            while (newSize < required)
            {
                newSize = newSize < 1024 ? newSize * 2 : newSize + newSize / 2;
            }

            T[] newBuf = EcsArrayPool<T>.Rent(newSize);
            Array.Copy(_buffer, 0, newBuf, 0, Count);

            // If T contains references, clear before pooling to avoid retaining objects.
            EcsArrayPool<T>.Return(_buffer, s_typeContainsReferences);
            _buffer = newBuf;
        }
    }
}