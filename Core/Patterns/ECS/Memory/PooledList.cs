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

        public PooledList(int initialCapacity = 16)
        {
            if (initialCapacity < 1) initialCapacity = 1;

            _buffer = EcsArrayPool<T>.Rent(initialCapacity);
            Count = 0;
        }

        public int Count { get; private set; }

        /// <summary>
        /// Current internal buffer capacity. Used for no-growth checks.
        /// </summary>
        public int Capacity => _buffer.Length;

        public T this[int index]
        {
            get => _buffer[index];
            set => _buffer[index] = value;
        }

        public Span<T> AsSpan()
        {
            return new Span<T>(_buffer, 0, Count);
        }

        public void Clear()
        {
            Clear(false);
        }

        public void Clear(bool clearReferences)
        {
            if (clearReferences && s_typeContainsReferences && Count > 0)
                // Clear only the active range to release references.
                Array.Clear(_buffer, 0, Count);

            Count = 0;
        }

        public void ClearReferences()
        {
            Clear(true);
        }

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

        public void EnsureCapacity(int capacity)
        {
            if (capacity <= _buffer.Length) return;

            Grow(capacity);
        }

        public void Sort(IComparer<T> comparer)
        {
            Array.Sort(_buffer, 0, Count, comparer);
        }

        public T[] DangerousGetBuffer()
        {
            return _buffer;
        }

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
