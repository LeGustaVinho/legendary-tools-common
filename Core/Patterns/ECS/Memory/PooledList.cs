using System;
using System.Collections.Generic;

namespace LegendaryTools.Common.Core.Patterns.ECS.Memory
{
    /// <summary>
    /// Minimal pooled growable buffer to avoid List allocations in hot paths.
    /// </summary>
    internal sealed class PooledList<T>
    {
        private T[] _buffer;

        public PooledList(int initialCapacity = 16)
        {
            if (initialCapacity < 1) initialCapacity = 1;

            _buffer = EcsArrayPool<T>.Rent(initialCapacity);
            Count = 0;
        }

        public int Count { get; private set; }

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
            Count = 0;
        }

        public void Add(in T item)
        {
            int next = Count + 1;
            if (next > _buffer.Length) Grow(next);

            _buffer[Count] = item;
            Count = next;
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
            EcsArrayPool<T>.Return(tmp, clear);
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

            EcsArrayPool<T>.Return(_buffer, false);
            _buffer = newBuf;
        }
    }
}