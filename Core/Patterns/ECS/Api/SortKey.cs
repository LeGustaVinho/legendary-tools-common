using System;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;

namespace LegendaryTools.Common.Core.Patterns.ECS.Api
{
    /// <summary>
    /// Helpers to generate stable sort keys for deterministic ECB.
    /// Keep it fast and predictable.
    /// </summary>
    public static class SortKey
    {
        /// <summary>
        /// Returns a stable non-zero sortKey based on an owner entity index.
        /// </summary>
        public static int FromOwner(Entity owner)
        {
            // Entity.Index can be 0. Deterministic ECB forbids 0.
            int v = owner.Index;
            return v == 0 ? 1 : v;
        }

        /// <summary>
        /// Combines two integers into a stable non-zero key.
        /// </summary>
        public static int Combine(int a, int b)
        {
            unchecked
            {
                // FNV-1a style mix (cheap, stable, deterministic).
                uint hash = 2166136261u;
                hash ^= (uint)a;
                hash *= 16777619u;
                hash ^= (uint)b;
                hash *= 16777619u;

                int key = (int)(hash & 0x7FFFFFFFu);
                return key == 0 ? 1 : key;
            }
        }

        /// <summary>
        /// Common pattern: (chunkId,row) for entities emitted from chunk iteration.
        /// </summary>
        public static int FromChunkRow(int chunkId, int row)
        {
            return Combine(chunkId, row);
        }

        /// <summary>
        /// Common pattern: owner + local index (e.g., bullets spawned by an entity).
        /// </summary>
        public static int FromOwnerAndLocal(Entity owner, int localIndex)
        {
            return Combine(FromOwner(owner), localIndex);
        }

        /// <summary>
        /// Ensures a key is non-zero.
        /// </summary>
        public static int NonZero(int key)
        {
            return key == 0 ? 1 : key;
        }
    }
}