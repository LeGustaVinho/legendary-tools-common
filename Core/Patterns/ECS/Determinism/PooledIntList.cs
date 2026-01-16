#nullable enable

using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Pool-friendly growable int list that avoids GC allocations (rents from <see cref="ArrayPool{T}"/>).
    /// </summary>
    /// <remarks>
    /// Intended for tooling/hot paths that must avoid GC. Not part of the simulated state.
    /// </remarks>
    public struct PooledIntList : IDisposable
    {
        private int[]? _buffer;
        private int _count;

        /// <summary>
        /// Gets the number of valid items.
        /// </summary>
        public readonly int Count => _count;

        /// <summary>
        /// Gets the underlying span for the active range.
        /// </summary>
        public readonly Span<int> AsSpan()
        {
            return _buffer is null ? Span<int>.Empty : _buffer.AsSpan(0, _count);
        }

        /// <summary>
        /// Gets or sets an item in the active range.
        /// </summary>
        public int this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get
            {
                if ((uint)index >= (uint)_count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                return _buffer![index];
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if ((uint)index >= (uint)_count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                _buffer![index] = value;
            }
        }

        /// <summary>
        /// Initializes the list with an optional initial capacity.
        /// </summary>
        public PooledIntList(int initialCapacity)
        {
            _buffer = initialCapacity > 0 ? ArrayPool<int>.Shared.Rent(initialCapacity) : null;
            _count = 0;
        }

        /// <summary>
        /// Adds an item to the end of the list.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(int value)
        {
            EnsureCapacity(_count + 1);
            _buffer![_count++] = value;
        }

        /// <summary>
        /// Clears the list without returning the buffer to the pool.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            _count = 0;
        }

        /// <summary>
        /// Returns the internal buffer to the pool.
        /// </summary>
        public void Dispose()
        {
            if (_buffer is not null)
            {
                ArrayPool<int>.Shared.Return(_buffer, false);
                _buffer = null;
                _count = 0;
            }
        }

        private void EnsureCapacity(int required)
        {
            if (_buffer is null)
            {
                _buffer = ArrayPool<int>.Shared.Rent(Math.Max(required, 8));
                return;
            }

            if (required <= _buffer.Length)
                return;

            int newCapacity = _buffer.Length * 2;
            if (newCapacity < required)
                newCapacity = required;

            int[] newBuffer = ArrayPool<int>.Shared.Rent(newCapacity);
            Array.Copy(_buffer, 0, newBuffer, 0, _count);
            ArrayPool<int>.Shared.Return(_buffer, false);
            _buffer = newBuffer;
        }
    }
}