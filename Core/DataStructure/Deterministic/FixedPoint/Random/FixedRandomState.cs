using System;
using System.Runtime.InteropServices;

namespace LegendaryTools.FixedPoint
{
    /// <summary>Complete reproducible state. Cache data is derived, never persisted. Max position means exhausted.</summary>
    [Serializable, StructLayout(LayoutKind.Sequential)]
    public readonly struct FixedRandomState : IEquatable<FixedRandomState>
    {
        public const int Bytes = 24;
        public readonly ulong Key, SequenceId, WordPosition;
        public FixedRandomState(ulong key, ulong sequenceId, ulong wordPosition)
        { Key = key; SequenceId = sequenceId; WordPosition = wordPosition; }
        public bool Equals(FixedRandomState other) => Key == other.Key && SequenceId == other.SequenceId && WordPosition == other.WordPosition;
        public override bool Equals(object obj) => obj is FixedRandomState other && Equals(other);
        public override int GetHashCode()
        {
            ulong h = FixedRandomKernel.Mix64(Key ^ FixedRandomKernel.Mix64(SequenceId) ^ FixedRandomKernel.Mix64(WordPosition));
            return unchecked((int)(h ^ (h >> 32)));
        }
        public static bool operator ==(FixedRandomState a, FixedRandomState b) => a.Equals(b);
        public static bool operator !=(FixedRandomState a, FixedRandomState b) => !a.Equals(b);
    }
}
