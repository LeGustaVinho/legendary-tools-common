#nullable enable

using System;
using System.Runtime.CompilerServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Deterministic identifier for an archetype (derived from a stable signature).
    /// </summary>
    /// <remarks>
    /// Uses a 64-bit stable hash to drastically reduce collision probability.
    /// </remarks>
    public readonly struct ArchetypeId : IEquatable<ArchetypeId>, IComparable<ArchetypeId>
    {
        /// <summary>
        /// Underlying 64-bit value (stable hash of the ordered signature).
        /// </summary>
        public readonly ulong Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArchetypeId(ulong value)
        {
            Value = value;
        }

        /// <summary>
        /// Back-compat constructor. Interprets the 32-bit value as an unsigned 32-bit payload.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArchetypeId(int value)
        {
            Value = unchecked((uint)value);
        }

        /// <summary>
        /// Folded 32-bit value (useful for tables/debug). Deterministic.
        /// </summary>
        public int Value32
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => unchecked((int)(Value ^ (Value >> 32)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(ArchetypeId other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object? obj)
        {
            return obj is ArchetypeId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value32;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(ArchetypeId other)
        {
            if (Value < other.Value) return -1;
            if (Value > other.Value) return 1;
            return 0;
        }

        public static bool operator ==(ArchetypeId a, ArchetypeId b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(ArchetypeId a, ArchetypeId b)
        {
            return !a.Equals(b);
        }

        public static bool operator <(ArchetypeId a, ArchetypeId b)
        {
            return a.Value < b.Value;
        }

        public static bool operator >(ArchetypeId a, ArchetypeId b)
        {
            return a.Value > b.Value;
        }

        public static bool operator <=(ArchetypeId a, ArchetypeId b)
        {
            return a.Value <= b.Value;
        }

        public static bool operator >=(ArchetypeId a, ArchetypeId b)
        {
            return a.Value >= b.Value;
        }

        public override string ToString()
        {
            return $"ArchetypeId(0x{Value:X16})";
        }
    }
}