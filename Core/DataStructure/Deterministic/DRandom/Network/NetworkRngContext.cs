using System;

namespace LegendaryTools.Common.Core.Patterns.ECS.Random.Network
{
    /// <summary>
    /// Network-ready deterministic RNG context.
    /// All peers must use the same values to produce identical results.
    /// </summary>
    [Serializable]
    public readonly struct NetworkRngContext : IEquatable<NetworkRngContext>
    {
        /// <summary>
        /// Global match seed agreed by all peers (e.g., server authoritative seed).
        /// </summary>
        public readonly ulong MatchSeed;

        /// <summary>
        /// Deterministic simulation tick (lockstep/rollback).
        /// </summary>
        public readonly uint Tick;

        /// <summary>
        /// Optional sub-tick (e.g., phases inside a tick). Keep 0 if unused.
        /// </summary>
        public readonly ushort Phase;

        /// <summary>
        /// Optional salt for protocol/versioning. Keep constant across peers.
        /// Use it to intentionally invalidate replays when RNG logic changes.
        /// </summary>
        public readonly ushort ProtocolSalt;

        public NetworkRngContext(ulong matchSeed, uint tick, ushort phase = 0, ushort protocolSalt = 0)
        {
            MatchSeed = matchSeed;
            Tick = tick;
            Phase = phase;
            ProtocolSalt = protocolSalt;
        }

        public bool Equals(NetworkRngContext other)
        {
            return MatchSeed == other.MatchSeed
                   && Tick == other.Tick
                   && Phase == other.Phase
                   && ProtocolSalt == other.ProtocolSalt;
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkRngContext other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = (h * 31) ^ MatchSeed.GetHashCode();
                h = (h * 31) ^ Tick.GetHashCode();
                h = (h * 31) ^ Phase.GetHashCode();
                h = (h * 31) ^ ProtocolSalt.GetHashCode();
                return h;
            }
        }

        public static bool operator ==(NetworkRngContext a, NetworkRngContext b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(NetworkRngContext a, NetworkRngContext b)
        {
            return !a.Equals(b);
        }

        public override string ToString()
        {
            return
                $"NetworkRngContext(MatchSeed=0x{MatchSeed:X16}, Tick={Tick}, Phase={Phase}, ProtocolSalt={ProtocolSalt})";
        }
    }
}