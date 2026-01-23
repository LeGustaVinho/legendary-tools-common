using System;

namespace LegendaryTools.Common.Core.Patterns.ECS.Random.Network
{
    /// <summary>
    /// Factory for network-ready deterministic RNG streams.
    /// Designed for ECS usage: order-independent and replay/rollback friendly.
    /// </summary>
    public static class NetworkRngFactory
    {
        /// <summary>
        /// Creates a deterministic RNG for a given context and key.
        /// This is order-independent: it does not depend on call order, only on inputs.
        /// </summary>
        public static DeterministicRng Create(NetworkRngContext context, NetworkRngKey key)
        {
            // Build a base seed from stable inputs.
            // Important: ONLY use stable ids here. Never use runtime entity indices.
            ulong s0 = SplitMix64.Mix(context.MatchSeed);
            ulong s1 = SplitMix64.Mix(((ulong)context.Tick << 32) | ((ulong)context.Phase << 16) |
                                      context.ProtocolSalt);
            ulong s2 = SplitMix64.Mix(key.StreamId);
            ulong s3 = SplitMix64.Mix(key.EntityStableId);
            ulong s4 = SplitMix64.Mix(((ulong)key.EventId << 32) | key.RollIndex);

            ulong combined = SplitMix64.Combine(SplitMix64.Combine(s0, s1),
                SplitMix64.Combine(SplitMix64.Combine(s2, s3), s4));

            // Use stream id as the PCG stream selector to keep sequences separated,
            // but still deterministic and stable.
            // PCG requires odd increment; DeterministicRng enforces that.
            ulong pcgStream = key.StreamId != 0 ? key.StreamId : 1UL;

            return new DeterministicRng(combined, pcgStream);
        }

        /// <summary>
        /// Creates a deterministic RNG stream id from a stable string name (e.g., "combat", "loot").
        /// This must be identical on all peers (same spelling/case).
        /// </summary>
        public static ulong StreamIdFromName(string streamName)
        {
            return SplitMix64.SeedFromString(streamName);
        }

        /// <summary>
        /// Convenience: creates an entity-scoped RNG.
        /// </summary>
        public static DeterministicRng CreateForEntity(
            NetworkRngContext context,
            string streamName,
            ulong entityStableId,
            uint eventId = 0,
            uint rollIndex = 0)
        {
            ulong streamId = StreamIdFromName(streamName);
            return Create(context, new NetworkRngKey(streamId, entityStableId, eventId, rollIndex));
        }

        /// <summary>
        /// Convenience: creates a system/global RNG stream (no entity).
        /// </summary>
        public static DeterministicRng CreateGlobal(
            NetworkRngContext context,
            string streamName,
            uint eventId = 0,
            uint rollIndex = 0)
        {
            ulong streamId = StreamIdFromName(streamName);
            return Create(context, new NetworkRngKey(streamId, 0UL, eventId, rollIndex));
        }
    }
}