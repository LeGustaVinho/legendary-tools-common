using System;

namespace LegendaryTools.Common.Core.Patterns.ECS.Random
{
    /// <summary>
    /// A convenience wrapper to enforce the concept of named streams.
    /// Useful for "loot", "combat", "ai", "worldgen", etc.
    /// </summary>
    [Serializable]
    public sealed class RngStream
    {
        private DeterministicRng _rng;

        /// <summary>
        /// Creates a stream from a base seed and a stream name.
        /// </summary>
        public RngStream(ulong baseSeed, string streamName)
        {
            if (streamName == null) throw new ArgumentNullException(nameof(streamName));
            ulong streamId = SplitMix64.SeedFromString(streamName);
            _rng = new DeterministicRng(SplitMix64.Combine(baseSeed, streamId), streamId);
        }

        /// <summary>Gets or sets the internal state snapshot.</summary>
        public RngState State
        {
            get => _rng.State;
            set => _rng.SetState(value);
        }

        /// <summary>Gets access to the deterministic RNG.</summary>
        public ref DeterministicRng Rng => ref _rng;
    }
}