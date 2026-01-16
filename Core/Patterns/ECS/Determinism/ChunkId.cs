#nullable enable

using System;
using System.Runtime.CompilerServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Deterministic identifier for a chunk (commonly an incrementing counter per archetype).
    /// </summary>
    public readonly struct ChunkId : IEquatable<ChunkId>, IComparable<ChunkId>
    {
        /// <summary>
        /// Underlying integer value.
        /// </summary>
        public readonly int Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ChunkId(int value)
        {
            Value = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(ChunkId other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object? obj)
        {
            return obj is ChunkId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(ChunkId other)
        {
            return Value.CompareTo(other.Value);
        }

        public static bool operator ==(ChunkId a, ChunkId b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(ChunkId a, ChunkId b)
        {
            return !a.Equals(b);
        }

        public static bool operator <(ChunkId a, ChunkId b)
        {
            return a.Value < b.Value;
        }

        public static bool operator >(ChunkId a, ChunkId b)
        {
            return a.Value > b.Value;
        }

        public static bool operator <=(ChunkId a, ChunkId b)
        {
            return a.Value <= b.Value;
        }

        public static bool operator >=(ChunkId a, ChunkId b)
        {
            return a.Value >= b.Value;
        }

        public override string ToString()
        {
            return $"ChunkId({Value})";
        }
    }
}