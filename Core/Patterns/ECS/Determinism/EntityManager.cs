#nullable enable

using System;
using System.Runtime.CompilerServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Manages deterministic entity lifetime (create/destroy) with stale-reference detection (Index + Version).
    /// </summary>
    /// <remarks>
    /// - Index allocation is deterministic (FIFO free list).
    /// - Index 0 is reserved for <see cref="Entity.Null"/>. Valid entities start at index 1.
    /// - For "no GC in hot path", preallocate enough capacity up-front.
    /// </remarks>
    public sealed class EntityManager
    {
        private int[] _versions; // versions[index]
        private byte[] _alive; // alive[index] = 0/1

        private FreeListQueue _freeList;
        private int _nextIndex; // next never-used index (monotonic)

        /// <summary>
        /// Initializes the manager with a given capacity (number of entities).
        /// </summary>
        /// <param name="initialEntityCapacity">Initial maximum entity count (excluding index 0).</param>
        /// <param name="initialFreeListCapacity">Initial capacity for the FIFO free list queue.</param>
        public EntityManager(int initialEntityCapacity = 1024, int initialFreeListCapacity = 1024)
        {
            if (initialEntityCapacity < 1)
                initialEntityCapacity = 1;

            // +1 to reserve index 0 for Entity.Null.
            int arraySize = initialEntityCapacity + 1;

            _versions = new int[arraySize];
            _alive = new byte[arraySize];

            // Version 0 is reserved for Entity.Null; start versions at 1 for deterministic non-null entities.
            // (We still store version in array; default 0 means "never used".)
            _nextIndex = 1;

            _freeList = new FreeListQueue(Math.Max(1, initialFreeListCapacity));
        }

        /// <summary>
        /// Gets the current internal capacity (excluding index 0).
        /// </summary>
        public int Capacity => _versions.Length - 1;

        /// <summary>
        /// Creates a new entity deterministically (FIFO reuse of destroyed indices).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity CreateEntity()
        {
            int index;

            if (_freeList.TryDequeue(out index))
            {
                // Reuse index; version already incremented on DestroyEntity.
            }
            else
            {
                index = _nextIndex;
                EnsureEntityCapacity(index);
                _nextIndex++;
            }

            // First-time use: version is 0, bump to 1 to avoid colliding with Entity.Null (0,0).
            int version = _versions[index];
            if (version == 0)
            {
                version = 1;
                _versions[index] = 1;
            }

            _alive[index] = 1;
            return new Entity(index, version);
        }

        /// <summary>
        /// Destroys an entity if it is alive. Stale entities are ignored (detected by Version).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DestroyEntity(Entity e)
        {
            if (!IsAlive(e))
                return;

            int index = e.Index;

            _alive[index] = 0;

            // Increment version so any outstanding references become stale.
            // Skip 0 to keep Entity.Null unique and avoid accidental "null" collisions.
            int v = _versions[index] + 1;
            if (v == 0)
                v = 1;

            _versions[index] = v;

            _freeList.Enqueue(index);
        }

        /// <summary>
        /// Returns true if the entity is alive and its Version matches the current Version stored for its Index.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsAlive(Entity e)
        {
            int index = e.Index;

            // Fast bounds check (also excludes index 0 unless Version matches and alive is set).
            if ((uint)index >= (uint)_versions.Length)
                return false;

            // Alive bit must be set and version must match.
            return _alive[index] != 0 && _versions[index] == e.Version;
        }

        /// <summary>
        /// Gets the current version for an index (primarily for debugging/tools).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetVersionForIndex(int index)
        {
            if ((uint)index >= (uint)_versions.Length)
                return 0;

            return _versions[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureEntityCapacity(int requiredIndex)
        {
            // requiredIndex is a direct index, so required length is requiredIndex + 1.
            if (requiredIndex < _versions.Length)
                return;

            int newLen = _versions.Length * 2;
            int minLen = requiredIndex + 1;

            if (newLen < minLen)
                newLen = minLen;

            Array.Resize(ref _versions, newLen);
            Array.Resize(ref _alive, newLen);
        }

        /// <summary>
        /// A minimal, allocation-free FIFO queue for indices (except when resizing).
        /// </summary>
        private struct FreeListQueue
        {
            private int[] _buffer;
            private int _head;
            private int _tail;
            private int _count;

            public FreeListQueue(int capacity)
            {
                _buffer = new int[Math.Max(1, capacity)];
                _head = 0;
                _tail = 0;
                _count = 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Enqueue(int value)
            {
                EnsureCapacity(_count + 1);

                _buffer[_tail] = value;
                _tail++;

                if (_tail == _buffer.Length)
                    _tail = 0;

                _count++;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool TryDequeue(out int value)
            {
                if (_count == 0)
                {
                    value = 0;
                    return false;
                }

                value = _buffer[_head];
                _head++;

                if (_head == _buffer.Length)
                    _head = 0;

                _count--;
                return true;
            }

            private void EnsureCapacity(int required)
            {
                if (required <= _buffer.Length)
                    return;

                // Grow deterministically. (Allocation happens only when capacity is exceeded.)
                int newCapacity = _buffer.Length * 2;
                if (newCapacity < required)
                    newCapacity = required;

                int[] newBuffer = new int[newCapacity];

                // Copy in FIFO order.
                if (_count > 0)
                {
                    if (_head < _tail)
                    {
                        Array.Copy(_buffer, _head, newBuffer, 0, _count);
                    }
                    else
                    {
                        int firstPart = _buffer.Length - _head;
                        Array.Copy(_buffer, _head, newBuffer, 0, firstPart);
                        Array.Copy(_buffer, 0, newBuffer, firstPart, _tail);
                    }
                }

                _buffer = newBuffer;
                _head = 0;
                _tail = _count;
            }
        }
    }
}