#nullable enable

using System;
using System.Runtime.CompilerServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Deterministic entity identifier (value type).
    /// </summary>
    /// <remarks>
    /// An <see cref="Entity"/> is valid only if its <see cref="Version"/> matches the world's current version for <see cref="Index"/>.
    /// </remarks>
    public readonly struct Entity : IEquatable<Entity>, IComparable<Entity>
    {
        /// <summary>
        /// Entity index into world tables.
        /// </summary>
        public readonly int Index;

        /// <summary>
        /// Entity version used to detect stale references.
        /// </summary>
        public readonly int Version;

        /// <summary>
        /// Represents an invalid entity reference.
        /// </summary>
        public static readonly Entity Null = default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity(int index, int version)
        {
            Index = index;
            Version = version;
        }

        /// <summary>
        /// Gets whether this entity is <see cref="Null"/>.
        /// </summary>
        public bool IsNull
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Index == 0 && Version == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Entity other)
        {
            return Index == other.Index && Version == other.Version;
        }

        public override bool Equals(object? obj)
        {
            return obj is Entity other && Equals(other);
        }

        /// <summary>
        /// Deterministic hash suitable for dictionaries and state hashing (when used consistently).
        /// </summary>
        public override int GetHashCode()
        {
            unchecked
            {
                uint h = DeterministicHash.Fnv1A32Init;
                h = DeterministicHash.Fnv1A32(h, (uint)Index);
                h = DeterministicHash.Fnv1A32(h, (uint)Version);
                return (int)h;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(Entity other)
        {
            int c = Index.CompareTo(other.Index);
            return c != 0 ? c : Version.CompareTo(other.Version);
        }

        public static bool operator ==(Entity a, Entity b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(Entity a, Entity b)
        {
            return !a.Equals(b);
        }

        public static bool operator <(Entity a, Entity b)
        {
            return a.CompareTo(b) < 0;
        }

        public static bool operator >(Entity a, Entity b)
        {
            return a.CompareTo(b) > 0;
        }

        public static bool operator <=(Entity a, Entity b)
        {
            return a.CompareTo(b) <= 0;
        }

        public static bool operator >=(Entity a, Entity b)
        {
            return a.CompareTo(b) >= 0;
        }

        public override string ToString()
        {
            return IsNull ? "Entity.Null" : $"Entity({Index}:{Version})";
        }
    }
}