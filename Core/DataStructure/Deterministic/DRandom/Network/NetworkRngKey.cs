using System;

namespace LegendaryTools.Common.Core.Patterns.ECS.Random.Network
{
    /// <summary>
    /// Identifies a deterministic RNG stream for networked ECS gameplay.
    /// This key is designed to be stable and order-independent.
    /// </summary>
    [Serializable]
    public readonly struct NetworkRngKey : IEquatable<NetworkRngKey>
    {
        /// <summary>
        /// A stable stream identifier (e.g., hash of "combat", "loot", "ai").
        /// </summary>
        public readonly ulong StreamId;

        /// <summary>
        /// Stable entity identifier (0 if not entity-scoped).
        /// Use a network-stable id (not a transient runtime index).
        /// </summary>
        public readonly ulong EntityStableId;

        /// <summary>
        /// Optional stable "event id" or "action id" within an entity/system.
        /// Use 0 if not needed.
        /// </summary>
        public readonly uint EventId;

        /// <summary>
        /// Optional stable "roll index" within an event (0..N).
        /// Must be deterministic and consistent across peers.
        /// </summary>
        public readonly uint RollIndex;

        public NetworkRngKey(ulong streamId, ulong entityStableId = 0, uint eventId = 0, uint rollIndex = 0)
        {
            StreamId = streamId;
            EntityStableId = entityStableId;
            EventId = eventId;
            RollIndex = rollIndex;
        }

        public bool Equals(NetworkRngKey other)
        {
            return StreamId == other.StreamId
                   && EntityStableId == other.EntityStableId
                   && EventId == other.EventId
                   && RollIndex == other.RollIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkRngKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = (h * 31) ^ StreamId.GetHashCode();
                h = (h * 31) ^ EntityStableId.GetHashCode();
                h = (h * 31) ^ EventId.GetHashCode();
                h = (h * 31) ^ RollIndex.GetHashCode();
                return h;
            }
        }

        public static bool operator ==(NetworkRngKey a, NetworkRngKey b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(NetworkRngKey a, NetworkRngKey b)
        {
            return !a.Equals(b);
        }

        public override string ToString()
        {
            return
                $"NetworkRngKey(StreamId=0x{StreamId:X16}, EntityStableId={EntityStableId}, EventId={EventId}, RollIndex={RollIndex})";
        }
    }
}