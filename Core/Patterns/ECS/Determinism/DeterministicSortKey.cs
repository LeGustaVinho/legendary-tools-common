#nullable enable

using System.Runtime.CompilerServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Helpers for building deterministic 64-bit sort keys for ECB/events.
    /// </summary>
    public static class DeterministicSortKey
    {
        /// <summary>
        /// Packs two 32-bit values into a single 64-bit key (a as high, b as low).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Pack(int a, int b)
        {
            unchecked
            {
                ulong ua = (uint)a;
                ulong ub = (uint)b;
                return (long)((ua << 32) | ub);
            }
        }

        /// <summary>
        /// Deterministic key derived from an entity index/version.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long FromEntity(Entity e)
        {
            return Pack(e.Index, e.Version);
        }

        /// <summary>
        /// Deterministic key derived from a (chunkId,row) pair.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long FromChunkRow(ChunkId chunkId, int row)
        {
            return Pack(chunkId.Value, row);
        }

        /// <summary>
        /// Deterministic key derived from (chunkId,row,entityIndex) to reduce ties.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long FromChunkRowEntity(ChunkId chunkId, int row, int entityIndex)
        {
            unchecked
            {
                int low = (row * 73856093) ^ entityIndex;
                return Pack(chunkId.Value, low);
            }
        }
    }
}