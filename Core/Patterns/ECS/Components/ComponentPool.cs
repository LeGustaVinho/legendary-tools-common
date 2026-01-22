using System;

namespace LegendaryTools.Common.Core.Patterns.ECS.Components
{
    /// <summary>
    /// Simple per-entity component storage for a single component type.
    /// MVP implementation: array indexed by Entity.Index + presence bitset (bool array).
    /// </summary>
    /// <typeparam name="T">Component type.</typeparam>
    internal sealed class ComponentPool<T> : IComponentPool where T : struct
    {
        private T[] _data;
        private bool[] _present;

        /// <summary>
        /// Initializes a new instance of the <see cref="ComponentPool{T}"/> class.
        /// </summary>
        /// <param name="initialCapacity">Initial entity capacity.</param>
        public ComponentPool(int initialCapacity)
        {
            if (initialCapacity < 1) initialCapacity = 1;

            _data = new T[initialCapacity];
            _present = new bool[initialCapacity];
        }

        /// <summary>
        /// Checks whether a component exists for the given entity index.
        /// </summary>
        /// <param name="entityIndex">Entity index.</param>
        /// <returns>True if present.</returns>
        public bool Has(int entityIndex)
        {
            if ((uint)entityIndex >= (uint)_present.Length) return false;

            return _present[entityIndex];
        }

        /// <summary>
        /// Adds (or replaces) a component for the given entity index.
        /// </summary>
        /// <param name="entityIndex">Entity index.</param>
        /// <param name="value">Component value.</param>
        public void Add(int entityIndex, in T value)
        {
            EnsureCapacity(entityIndex + 1);
            _data[entityIndex] = value;
            _present[entityIndex] = true;
        }

        /// <summary>
        /// Removes a component for the given entity index.
        /// </summary>
        /// <param name="entityIndex">Entity index.</param>
        public void Remove(int entityIndex)
        {
            if ((uint)entityIndex >= (uint)_present.Length) return;

            _present[entityIndex] = false;
            _data[entityIndex] = default;
        }


        /// <summary>
        /// Gets a readonly reference to the component for the given entity index.
        /// </summary>
        /// <param name="entityIndex">Entity index.</param>
        /// <param name="value">The read-only component value.</param>
        /// <returns>True if present.</returns>
        public bool TryGetRO(int entityIndex, out T value)
        {
            if ((uint)entityIndex >= (uint)_present.Length || !_present[entityIndex])
            {
                value = default;
                return false;
            }

            value = _data[entityIndex];
            return true;
        }

        /// <summary>
        /// Tries to get a writable reference wrapper for the given entity index.
        /// </summary>
        /// <param name="entityIndex">Entity index.</param>
        /// <param name="value">The writable reference wrapper.</param>
        /// <returns>True if present.</returns>
        public bool TryGetRW(int entityIndex, out RefValue<T> value)
        {
            if ((uint)entityIndex >= (uint)_present.Length || !_present[entityIndex])
            {
                value = default;
                return false;
            }

            value = new RefValue<T>(this, entityIndex);
            return true;
        }

        /// <summary>
        /// Gets a strict readonly reference to the component. Throws if missing.
        /// </summary>
        /// <param name="entityIndex">Entity index.</param>
        /// <returns>Readonly reference.</returns>
        public ref readonly T GetRO(int entityIndex)
        {
            ValidateIndexAndPresence(entityIndex);
            return ref _data[entityIndex];
        }

        /// <summary>
        /// Gets a writable reference to the component for the given entity index.
        /// </summary>
        /// <param name="entityIndex">Entity index.</param>
        /// <returns>Writable reference to component.</returns>
        public ref T GetRW(int entityIndex)
        {
            ValidateIndexAndPresence(entityIndex);
            return ref _data[entityIndex];
        }

        /// <summary>
        /// Ensures internal arrays have at least the specified capacity.
        /// </summary>
        /// <param name="requiredCapacity">Required number of elements.</param>
        public void EnsureCapacity(int requiredCapacity)
        {
            if (requiredCapacity <= _data.Length) return;

            int newSize = _data.Length;
            while (newSize < requiredCapacity)
            {
                newSize = newSize < 1024 ? newSize * 2 : newSize + newSize / 2;
            }

            Array.Resize(ref _data, newSize);
            Array.Resize(ref _present, newSize);
        }

        private void ValidateIndexAndPresence(int entityIndex)
        {
            if ((uint)entityIndex >= (uint)_present.Length)
                throw new IndexOutOfRangeException(
                    $"Entity index {entityIndex} is out of range (pool size {_present.Length}).");

            if (!_present[entityIndex])
                throw new InvalidOperationException(
                    $"Component {typeof(T).Name} is not present on entity index {entityIndex}.");
        }

        /// <summary>
        /// Helper struct to return a writable reference (ref return) via an out parameter pattern,
        /// by deferring the actual ref return access.
        /// </summary>
        internal readonly struct RefValue<TValue> where TValue : struct
        {
            private readonly ComponentPool<TValue> _pool;
            private readonly int _index;

            public RefValue(ComponentPool<TValue> pool, int index)
            {
                _pool = pool;
                _index = index;
            }

            /// <summary>
            /// Gets the writable reference to the component.
            /// </summary>
            public ref TValue Value => ref _pool.GetRW(_index);
        }
    }
}