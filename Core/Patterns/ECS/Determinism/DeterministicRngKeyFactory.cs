using System;
using System.Runtime.CompilerServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Canonical factory for building deterministic RNG keys.
    /// Centralizing key creation prevents accidental misuse (e.g., using unstable IDs).
    ///
    /// NOTE:
    /// This factory is intentionally generic and does not depend on enums.
    /// Stream ids should be stable constants defined by the caller (e.g., per feature/system).
    /// </summary>
    public static class DeterministicRngKeyFactory
    {
        /// <summary>
        /// Creates a deterministic RNG key.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DeterministicRngKey Create(
            in MatchSeed matchSeed,
            int tick,
            int streamId,
            ulong scopeId,
            uint sampleIndex)
        {
            Validate(in matchSeed, tick, streamId, scopeId, sampleIndex);
            return new DeterministicRngKey(matchSeed.Value, tick, streamId, scopeId, sampleIndex);
        }

        /// <summary>
        /// Creates a deterministic key for an entity-scoped event.
        /// Recommended usage is to pass a stable, replicated <paramref name="networkEntityId"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DeterministicRngKey ForEntityEvent(
            in MatchSeed matchSeed,
            int tick,
            int streamId,
            uint networkEntityId,
            uint eventSequence,
            uint sampleOffset = 0)
        {
            ulong scopeId = networkEntityId;
            uint sampleIndex = unchecked(eventSequence + sampleOffset);
            Validate(in matchSeed, tick, streamId, scopeId, sampleIndex);
            return new DeterministicRngKey(matchSeed.Value, tick, streamId, scopeId, sampleIndex);
        }

        /// <summary>
        /// Creates a deterministic key for a player-scoped event.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DeterministicRngKey ForPlayerEvent(
            in MatchSeed matchSeed,
            int tick,
            int streamId,
            uint playerId,
            uint eventSequence,
            uint sampleOffset = 0)
        {
            ulong scopeId = playerId;
            uint sampleIndex = unchecked(eventSequence + sampleOffset);
            Validate(in matchSeed, tick, streamId, scopeId, sampleIndex);
            return new DeterministicRngKey(matchSeed.Value, tick, streamId, scopeId, sampleIndex);
        }

        /// <summary>
        /// Creates a deterministic key for a global (non-entity) event.
        /// Useful for match-wide events, map rules, etc.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DeterministicRngKey ForGlobalEvent(
            in MatchSeed matchSeed,
            int tick,
            int streamId,
            uint eventId,
            uint sampleOffset = 0)
        {
            // Reserve scopeId=0 for "global".
            ulong scopeId = 0UL;
            uint sampleIndex = unchecked(eventId + sampleOffset);
            Validate(in matchSeed, tick, streamId, scopeId, sampleIndex);
            return new DeterministicRngKey(matchSeed.Value, tick, streamId, scopeId, sampleIndex);
        }

        /// <summary>
        /// Derives a deterministic 32-bit event id from stable inputs.
        /// Use this when you need an event id but only have multiple stable identifiers.
        /// Inputs must be stable across machines (e.g., replicated IDs, replicated counters, tick).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint DeriveEventId(
            ulong a,
            ulong b,
            uint c,
            uint d = 0)
        {
            ulong x = 0xA0761D6478BD642FUL;

            x ^= a;
            x = Mix64(x);

            x ^= b;
            x = Mix64(x);

            x ^= c;
            x = Mix64(x);

            x ^= d;
            x = Mix64(x);

            return (uint)(x >> 32);
        }

        /// <summary>
        /// Derives a deterministic event id from (tick, ownerId, localSequence).
        /// This is a common pattern for action events such as shots/casts.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint DeriveEventIdFromTickOwnerSequence(int tick, uint ownerNetworkId, uint localSequence)
        {
            return DeriveEventId((ulong)(uint)tick, ownerNetworkId, localSequence);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Validate(
            in MatchSeed matchSeed,
            int tick,
            int streamId,
            ulong scopeId,
            uint sampleIndex)
        {
#if ECS_DETERMINISM_CHECKS
            if (tick < 0)
                throw new ArgumentOutOfRangeException(nameof(tick), "Tick must be non-negative.");

            if (streamId < 0)
                throw new ArgumentOutOfRangeException(nameof(streamId), "StreamId must be non-negative.");

            // BaseSeed == 0 is not inherently wrong, but it is a common mistake in network simulations.
            if (matchSeed.Value == 0UL)
                throw new InvalidOperationException("MatchSeed.Value is 0. Ensure the match seed is initialized and replicated before simulation.");

            // scopeId == 0 is allowed for global events only (see ForGlobalEvent).
            _ = scopeId;
            _ = sampleIndex;
#endif
        }

        /// <summary>
        /// 64-bit mix finalizer (SplitMix64-style finalizer).
        /// Kept local to avoid coupling and to remain usable without ECS integration.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Mix64(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
