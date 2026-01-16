#nullable enable

using System;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// O(1) entity location inside chunked storage.
    /// </summary>
    /// <remarks>
    /// This is runtime indexing data (not intended to be part of deterministic state serialization).
    /// </remarks>
    public readonly struct EntityLocation : IEquatable<EntityLocation>
    {
        /// <summary>
        /// Archetype id that owns the chunk.
        /// </summary>
        public readonly ArchetypeId ArchetypeId;

        /// <summary>
        /// Chunk id inside the archetype.
        /// </summary>
        public readonly ChunkId ChunkId;

        /// <summary>
        /// Row index within the chunk.
        /// </summary>
        public readonly int Row;

        public EntityLocation(ArchetypeId archetypeId, ChunkId chunkId, int row)
        {
            ArchetypeId = archetypeId;
            ChunkId = chunkId;
            Row = row;
        }

        public bool Equals(EntityLocation other)
        {
            return ArchetypeId.Equals(other.ArchetypeId)
                   && ChunkId.Equals(other.ChunkId)
                   && Row == other.Row;
        }

        public override bool Equals(object? obj)
        {
            return obj is EntityLocation other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = ArchetypeId.GetHashCode();
                h = (h * 397) ^ ChunkId.GetHashCode();
                h = (h * 397) ^ Row;
                return h;
            }
        }

        public static bool operator ==(EntityLocation a, EntityLocation b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(EntityLocation a, EntityLocation b)
        {
            return !a.Equals(b);
        }

        public override string ToString()
        {
            return $"Loc(Arch={ArchetypeId}, Chunk={ChunkId.Value}, Row={Row})";
        }
    }
}