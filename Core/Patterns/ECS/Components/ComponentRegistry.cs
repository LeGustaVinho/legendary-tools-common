using System;
using System.Collections.Generic;

namespace LegendaryTools.Common.Core.Patterns.ECS.Components
{
    /// <summary>
    /// Assigns stable (within a World instance) ids to component types.
    /// </summary>
    internal sealed class ComponentRegistry
    {
        private readonly Dictionary<Type, int> _typeToId;
        private int _nextId;

        /// <summary>
        /// Initializes a new instance of the <see cref="ComponentRegistry"/> class.
        /// </summary>
        public ComponentRegistry()
        {
            _typeToId = new Dictionary<Type, int>(128);
            _nextId = 1;
        }

        /// <summary>
        /// Gets (or creates) the stable component type id for <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Component type.</typeparam>
        /// <returns>Stable id within this registry.</returns>
        public ComponentTypeId GetOrCreate<T>() where T : struct
        {
            Type type = typeof(T);

            if (_typeToId.TryGetValue(type, out int existing))
            {
                return new ComponentTypeId(existing);
            }

            int id = _nextId++;
            _typeToId.Add(type, id);
            return new ComponentTypeId(id);
        }
    }
}
