#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Registry that assigns stable <see cref="ComponentTypeId"/>s within a World by explicit boot-time registration.
    /// </summary>
    /// <remarks>
    /// Determinism guarantee:
    /// - IDs are assigned incrementally in the exact order that components are registered.
    /// - Do not rely on reflection-based discovery to register components.
    /// - Call <see cref="Seal"/> after boot to prevent accidental late registration.
    /// </remarks>
    public sealed class ComponentTypeRegistry
    {
        private readonly Dictionary<Type, int> _typeToId;
        private ComponentTypeInfo[] _infosById; // index == id, 0 unused
        private int _nextId;
        private bool _sealed;

        /// <summary>
        /// Initializes a registry with an initial capacity for component types.
        /// </summary>
        public ComponentTypeRegistry(int initialTypeCapacity = 64)
        {
            if (initialTypeCapacity < 1)
                initialTypeCapacity = 1;

            _typeToId = new Dictionary<Type, int>(initialTypeCapacity);
            _infosById = new ComponentTypeInfo[Math.Max(8, initialTypeCapacity + 1)];
            _nextId = 1; // 0 reserved/unused
            _sealed = false;
        }

        /// <summary>
        /// Gets the number of registered component types.
        /// </summary>
        public int Count => _nextId - 1;

        /// <summary>
        /// Prevents further registration. Call at the end of world boot for safety.
        /// </summary>
        public void Seal()
        {
            _sealed = true;
        }

        /// <summary>
        /// Returns true if the registry is sealed.
        /// </summary>
        public bool IsSealed => _sealed;

        /// <summary>
        /// Registers a component type explicitly and returns its stable id.
        /// Re-registering returns the existing id.
        /// </summary>
        /// <typeparam name="T">Component type (struct).</typeparam>
        /// <exception cref="InvalidOperationException">Thrown if registry is sealed.</exception>
        public ComponentTypeId Register<T>() where T : struct
        {
            return RegisterInternal(typeof(T), InferFlags<T>());
        }

        /// <summary>
        /// Registers a component type explicitly and returns its stable id, using the provided flags.
        /// Re-registering returns the existing id.
        /// </summary>
        /// <typeparam name="T">Component type (struct).</typeparam>
        /// <param name="flags">Flags describing storage handling.</param>
        /// <exception cref="InvalidOperationException">Thrown if registry is sealed.</exception>
        public ComponentTypeId Register<T>(ComponentTypeFlags flags) where T : struct
        {
            flags = NormalizeFlags<T>(flags);
            return RegisterInternal(typeof(T), flags);
        }

        /// <summary>
        /// Tries to get an id for a component type. Returns false if not registered.
        /// </summary>
        public bool TryGetId(Type componentType, out ComponentTypeId id)
        {
            if (_typeToId.TryGetValue(componentType, out int raw))
            {
                id = new ComponentTypeId(raw);
                return true;
            }

            id = default;
            return false;
        }

        /// <summary>
        /// Gets an id for a component type. Throws if not registered.
        /// </summary>
        public ComponentTypeId GetId(Type componentType)
        {
            if (!_typeToId.TryGetValue(componentType, out int raw))
                throw new KeyNotFoundException($"Component type not registered: {componentType.FullName}");

            return new ComponentTypeId(raw);
        }

        /// <summary>
        /// Gets metadata for a component by id. Throws if id is invalid.
        /// </summary>
        public ref readonly ComponentTypeInfo GetInfo(ComponentTypeId id)
        {
            int raw = id.Value;
            if ((uint)raw >= (uint)_infosById.Length || raw <= 0)
                throw new ArgumentOutOfRangeException(nameof(id), $"Invalid ComponentTypeId: {raw}");

            return ref _infosById[raw];
        }

        /// <summary>
        /// Gets metadata for a component by type. Throws if not registered.
        /// </summary>
        public ref readonly ComponentTypeInfo GetInfo(Type componentType)
        {
            return ref GetInfo(GetId(componentType));
        }

        private ComponentTypeId RegisterInternal(Type type, ComponentTypeFlags flags)
        {
            if (_sealed)
                throw new InvalidOperationException(
                    "ComponentTypeRegistry is sealed. Register all components during world boot.");

            if (_typeToId.TryGetValue(type, out int existing))
                return new ComponentTypeId(existing);

            int id = _nextId++;
            EnsureInfoCapacity(id);

            int sizeBytes;
            int strideBytes;

            if ((flags & ComponentTypeFlags.Tag) != 0)
            {
                sizeBytes = 0;
                strideBytes = 0;
                flags &= ~ComponentTypeFlags.Data;
            }
            else
            {
                bool blittable = (flags & ComponentTypeFlags.Blittable) != 0;
                if (blittable)
                {
                    sizeBytes = Marshal.SizeOf(type);
                    strideBytes = sizeBytes;
                }
                else
                {
                    sizeBytes = 0;
                    strideBytes = 0;
                }

                flags |= ComponentTypeFlags.Data;
                flags &= ~ComponentTypeFlags.Tag;
            }

            ComponentTypeInfo info = new(
                new ComponentTypeId(id),
                flags,
                sizeBytes,
                strideBytes,
                type,
                type.FullName ?? type.Name);

            _typeToId.Add(type, id);
            _infosById[id] = info;

            return new ComponentTypeId(id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ComponentTypeFlags InferFlags<T>() where T : struct
        {
            bool isTag = typeof(IComponentTag).IsAssignableFrom(typeof(T));

            bool containsRefs = RuntimeHelpers.IsReferenceOrContainsReferences<T>();
            bool blittable = !containsRefs;

            ComponentTypeFlags flags = isTag ? ComponentTypeFlags.Tag : ComponentTypeFlags.Data;

            if (blittable)
                flags |= ComponentTypeFlags.Blittable;

            return flags;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ComponentTypeFlags NormalizeFlags<T>(ComponentTypeFlags flags) where T : struct
        {
            bool tag = (flags & ComponentTypeFlags.Tag) != 0;
            bool data = (flags & ComponentTypeFlags.Data) != 0;

            if (tag && data)
            {
                flags &= ~ComponentTypeFlags.Data;
            }
            else if (!tag && !data)
            {
                ComponentTypeFlags inferred = InferFlags<T>();
                flags |= inferred & (ComponentTypeFlags.Tag | ComponentTypeFlags.Data);
            }

            bool containsRefs = RuntimeHelpers.IsReferenceOrContainsReferences<T>();
            if (!containsRefs)
                flags |= ComponentTypeFlags.Blittable;
            else
                flags &= ~ComponentTypeFlags.Blittable;

            return flags;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureInfoCapacity(int id)
        {
            if (id < _infosById.Length)
                return;

            int newLen = _infosById.Length * 2;
            int minLen = id + 1;
            if (newLen < minLen)
                newLen = minLen;

            Array.Resize(ref _infosById, newLen);
        }
    }
}