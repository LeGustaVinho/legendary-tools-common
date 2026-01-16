#nullable enable

using System;
using System.Runtime.CompilerServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Compact deterministic identifier for a component type.
    /// </summary>
    public readonly struct ComponentTypeId : IEquatable<ComponentTypeId>, IComparable<ComponentTypeId>
    {
        /// <summary>
        /// Underlying integer value. Must be stable within a given <c>World</c> runtime.
        /// </summary>
        public readonly int Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ComponentTypeId(int value)
        {
            Value = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(ComponentTypeId other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object? obj)
        {
            return obj is ComponentTypeId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(ComponentTypeId other)
        {
            return Value.CompareTo(other.Value);
        }

        public static bool operator ==(ComponentTypeId a, ComponentTypeId b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(ComponentTypeId a, ComponentTypeId b)
        {
            return !a.Equals(b);
        }

        public static bool operator <(ComponentTypeId a, ComponentTypeId b)
        {
            return a.Value < b.Value;
        }

        public static bool operator >(ComponentTypeId a, ComponentTypeId b)
        {
            return a.Value > b.Value;
        }

        public static bool operator <=(ComponentTypeId a, ComponentTypeId b)
        {
            return a.Value <= b.Value;
        }

        public static bool operator >=(ComponentTypeId a, ComponentTypeId b)
        {
            return a.Value >= b.Value;
        }

        public override string ToString()
        {
            return $"ComponentTypeId({Value})";
        }
    }
}