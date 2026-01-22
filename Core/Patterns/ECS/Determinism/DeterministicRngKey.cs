using System;
using System.Runtime.CompilerServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Deterministic, network-ready RNG key (stateless RNG).
    /// The same key always produces the same random value on any machine.
    /// </summary>
    public readonly struct DeterministicRngKey : IEquatable<DeterministicRngKey>
    {
        /// <summary>Match/session seed (replicated).</summary>
        public readonly ulong BaseSeed;

        /// <summary>Fixed simulation tick.</summary>
        public readonly int Tick;

        /// <summary>
        /// Stream identifier (feature/system).
        /// This must be stable across machines and builds for network determinism.
        /// </summary>
        public readonly int StreamId;

        /// <summary>
        /// Stable scope identifier (e.g., NetworkEntityId, PlayerId, AbilityId).
        /// Use replicated IDs, not local entity indices.
        /// </summary>
        public readonly ulong ScopeId;

        /// <summary>
        /// Deterministic event/sample index within the scope (e.g., shotSequence, deathSequence, sampleIndex).
        /// Must be stable across machines.
        /// </summary>
        public readonly uint SampleIndex;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DeterministicRngKey(ulong baseSeed, int tick, int streamId, ulong scopeId, uint sampleIndex)
        {
            BaseSeed = baseSeed;
            Tick = tick;
            StreamId = streamId;
            ScopeId = scopeId;
            SampleIndex = sampleIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(DeterministicRngKey other)
        {
            return BaseSeed == other.BaseSeed
                && Tick == other.Tick
                && StreamId == other.StreamId
                && ScopeId == other.ScopeId
                && SampleIndex == other.SampleIndex;
        }

        public override bool Equals(object obj) => obj is DeterministicRngKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)(BaseSeed ^ (BaseSeed >> 32));
                h = (h * 397) ^ Tick;
                h = (h * 397) ^ StreamId;
                h = (h * 397) ^ (int)(ScopeId ^ (ScopeId >> 32));
                h = (h * 397) ^ (int)SampleIndex;
                return h;
            }
        }

        public static bool operator ==(DeterministicRngKey a, DeterministicRngKey b) => a.Equals(b);
        public static bool operator !=(DeterministicRngKey a, DeterministicRngKey b) => !a.Equals(b);

        /// <summary>
        /// Convenience helper that mirrors the constructor.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DeterministicRngKey Create(ulong baseSeed, int tick, int streamId, ulong scopeId, uint sampleIndex)
        {
            return new DeterministicRngKey(baseSeed, tick, streamId, scopeId, sampleIndex);
        }

        /// <summary>
        /// Creates a derived key by changing the sample index.
        /// Useful when you need multiple randoms for the same event deterministically.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DeterministicRngKey WithSample(uint sampleIndex)
        {
            return new DeterministicRngKey(BaseSeed, Tick, StreamId, ScopeId, sampleIndex);
        }
    }
}
