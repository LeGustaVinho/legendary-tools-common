using System;

namespace LegendaryTools.Common.Core.Patterns.ECS.Random
{
    /// <summary>
    /// Portable RNG state snapshot.
    /// </summary>
    [Serializable]
    public readonly struct RngState : IEquatable<RngState>
    {
        /// <summary>Internal state (algorithm specific).</summary>
        public readonly ulong State;

        /// <summary>Stream/sequence selector (must be odd for PCG).</summary>
        public readonly ulong Inc;

        /// <summary>
        /// Creates a state snapshot.
        /// </summary>
        public RngState(ulong state, ulong inc)
        {
            State = state;
            Inc = inc;
        }

        public bool Equals(RngState other)
        {
            return State == other.State && Inc == other.Inc;
        }

        public override bool Equals(object obj)
        {
            return obj is RngState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = (h * 31) ^ State.GetHashCode();
                h = (h * 31) ^ Inc.GetHashCode();
                return h;
            }
        }

        public static bool operator ==(RngState a, RngState b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(RngState a, RngState b)
        {
            return !a.Equals(b);
        }

        public override string ToString()
        {
            return $"RngState(State=0x{State:X16}, Inc=0x{Inc:X16})";
        }
    }
}