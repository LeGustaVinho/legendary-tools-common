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
        /// <returns>Readonly reference to component.</returns>
        public ref readonly T GetRO(int entityIndex)
        {
            return ref _data[entityIndex];
        }

        /// <summary>
        /// Gets a writable reference to the component for the given entity index.
        /// </summary>
        /// <param name="entityIndex">Entity index.</param>
        /// <returns>Writable reference to component.</returns>
        public ref T GetRW(int entityIndex)
        {
            return ref _data[entityIndex];
        }

        /// <inheritdoc/>
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
    }
}