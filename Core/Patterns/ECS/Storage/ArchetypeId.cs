using System;

namespace LegendaryTools.Common.Core.Patterns.ECS.Storage
{
    /// <summary>
    /// Unique identifier for an archetype.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Value"/> is the primary 64-bit hash of the signature and is used for bucketing.
    /// </para>
    /// <para>
    /// <see cref="Disambiguator"/> is a secondary hash used to avoid collisions where different
    /// signatures share the same primary hash.
    /// </para>
    /// </remarks>
    public readonly struct ArchetypeId : IEquatable<ArchetypeId>
    {
        /// <summary>
        /// Primary 64-bit hash used for bucketing (not sufficient alone for uniqueness).
        /// </summary>
        public readonly ulong Value;

        /// <summary>
        /// Secondary hash used to disambiguate collisions in <see cref="Value"/>.
        /// </summary>
        public readonly uint Disambiguator;

        /// <summary>
        /// Creates an archetype id with a primary hash and optional disambiguator.
        /// </summary>
        /// <param name="value">Primary 64-bit hash of the signature.</param>
        /// <param name="disambiguator">Secondary disambiguator hash.</param>
        public ArchetypeId(ulong value, uint disambiguator = 0)
        {
            Value = value;
            Disambiguator = disambiguator;
        }

        public bool Equals(ArchetypeId other)
        {
            return Value == other.Value && Disambiguator == other.Disambiguator;
        }

        public override bool Equals(object obj)
        {
            return obj is ArchetypeId other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)(Value ^ (Value >> 32));
                h = (h * 397) ^ (int)Disambiguator;
                return h;
            }
        }

        public static bool operator ==(ArchetypeId a, ArchetypeId b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(ArchetypeId a, ArchetypeId b)
        {
            return !a.Equals(b);
        }

        public override string ToString()
        {
            return $"ArchetypeId(Hash=0x{Value:X16}, Dis=0x{Disambiguator:X8})";
        }
    }
}