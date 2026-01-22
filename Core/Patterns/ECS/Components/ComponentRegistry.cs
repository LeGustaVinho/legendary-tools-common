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
        private readonly Dictionary<int, Type> _idToType;

        private int _manifestCount;
        private ulong _manifestXor64;
        private ulong _manifestSum64;

        private readonly bool _strictDeterminism;

        /// <summary>
        /// Initializes a new instance of the <see cref="ComponentRegistry"/> class.
        /// </summary>
        /// <param name="strictDeterminism">If true, hash collisions raise an exception instead of probing.</param>
        public ComponentRegistry(bool strictDeterminism)
        {
            _strictDeterminism = strictDeterminism;

            _typeToId = new Dictionary<Type, int>(128);
            _idToType = new Dictionary<int, Type>(128);

            _manifestCount = 0;
            _manifestXor64 = 0UL;
            _manifestSum64 = 0UL;
        }

        /// <summary>
        /// Gets the current manifest of registered components, including checksums.
        /// </summary>
        public ComponentManifest Manifest => new ComponentManifest(_manifestCount, _manifestXor64, _manifestSum64);

        /// <summary>
        /// Registers a component type or returns its existing ID.
        /// </summary>
        /// <typeparam name="T">The component type.</typeparam>
        /// <returns>The stable component type ID.</returns>
        public ComponentTypeId RegisterOrGet<T>() where T : struct
        {
            Type type = typeof(T);

            if (_typeToId.TryGetValue(type, out int existing))
                return new ComponentTypeId(existing);

            int id = ComputeStableTypeId(type);
            if (id == 0) id = 1;

            if (_idToType.TryGetValue(id, out Type usedBy))
            {
                if (usedBy != type)
                {
                    // In deterministic mode we must never resolve collisions by probing,
                    // because it becomes order-dependent across peers.
                    if (_strictDeterminism)
                    {
                        throw new InvalidOperationException(
                            "ComponentTypeId hash collision detected in determinism mode.\n" +
                            $"Type A: {usedBy.FullName}\n" +
                            $"Type B: {type.FullName}\n" +
                            $"Colliding Id: {id}\n" +
                            "Fix: rename one of the types (or change namespace/assembly) and ensure a fixed bootstrap registration list.");
                    }

                    // Non-deterministic mode: resolve collision by probing.
                    int probe = id;
                    do
                    {
                        probe = unchecked(probe + 1);
                        if (probe == 0) probe = 1;
                    } while (_idToType.ContainsKey(probe));

                    id = probe;
                }
            }

            _typeToId.Add(type, id);
            if (!_idToType.ContainsKey(id))
                _idToType.Add(id, type);

            unchecked
            {
                ulong h = ComputeStableTypeHash64(type);
                _manifestCount++;
                _manifestXor64 ^= h;
                _manifestSum64 += h;
            }

            return new ComponentTypeId(id);
        }

        /// <summary>
        /// Tries to get the ID of an already registered component type.
        /// </summary>
        /// <typeparam name="T">The component type.</typeparam>
        /// <param name="id">The component type ID if found.</param>
        /// <returns>True if the type is registered.</returns>
        public bool TryGetExisting<T>(out ComponentTypeId id) where T : struct
        {
            if (_typeToId.TryGetValue(typeof(T), out int v))
            {
                id = new ComponentTypeId(v);
                return true;
            }

            id = default;
            return false;
        }

        private static int ComputeStableTypeId(Type type)
        {
            string asm = type.Assembly.GetName().Name ?? string.Empty;
            string name = type.FullName ?? type.Name;

            const uint OffsetBasis = 2166136261u;
            const uint Prime = 16777619u;

            uint hash = OffsetBasis;

            HashStringOrdinal(ref hash, asm, Prime);
            hash ^= (byte)':';
            hash *= Prime;
            HashStringOrdinal(ref hash, name, Prime);

            int id = (int)(hash & 0x7FFFFFFFu);
            if (id == 0) id = 1;

            return id;
        }

        private static ulong ComputeStableTypeHash64(Type type)
        {
            string asm = type.Assembly.GetName().Name ?? string.Empty;
            string name = type.FullName ?? type.Name;

            const ulong OffsetBasis = 14695981039346656037UL;
            const ulong Prime = 1099511628211UL;

            ulong hash = OffsetBasis;

            HashStringOrdinal64(ref hash, asm, Prime);
            hash ^= (byte)':';
            hash *= Prime;
            HashStringOrdinal64(ref hash, name, Prime);

            return hash;
        }

        private static void HashStringOrdinal(ref uint hash, string s, uint prime)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];

                hash ^= (byte)(ch & 0xFF);
                hash *= prime;

                hash ^= (byte)((ch >> 8) & 0xFF);
                hash *= prime;
            }
        }

        private static void HashStringOrdinal64(ref ulong hash, string s, ulong prime)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];

                hash ^= (byte)(ch & 0xFF);
                hash *= prime;

                hash ^= (byte)((ch >> 8) & 0xFF);
                hash *= prime;
            }
        }
    }
}
