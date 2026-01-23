using System;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Queries;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Tests.ECS
{
    public static class EcsWorldSnapshotHasher
    {
        /// <summary>
        /// Computes a deterministic 64-bit hash for all entities that have TestPosition and TestVelocity.
        /// Iteration order is stable: query archetypes stable + chunks in archetype order + rows in chunk order.
        /// </summary>
        public static ulong ComputePositionVelocitySnapshot(World world)
        {
            Query query = world.QueryAll<TestPosition, TestVelocity>();
            WorldQueryResult result = world.BeginQuery(query);

            try
            {
                ulong h = 14695981039346656037UL; // FNV-1a 64 offset basis
                const ulong prime = 1099511628211UL;

                foreach (Archetype archetype in result.Archetypes)
                {
                    // Include archetype id for extra safety.
                    h = FnvMixU64(h, archetype.ArchetypeId.Value, prime);
                    h = FnvMixU32(h, archetype.ArchetypeId.Disambiguator, prime);

                    if (!archetype.TryGetColumnIndex(world.GetComponentTypeId<TestPosition>(), out int posCol))
                        continue;
                    if (!archetype.TryGetColumnIndex(world.GetComponentTypeId<TestVelocity>(), out int velCol))
                        continue;

                    Chunk[] chunks = archetype.ChunksBuffer;
                    int chunkCount = archetype.ChunkCount;

                    for (int c = 0; c < chunkCount; c++)
                    {
                        Chunk chunk = chunks[c];
                        if (chunk == null || chunk.Count == 0) continue;

                        h = FnvMixI32(h, chunk.ChunkId, prime);
                        h = FnvMixI32(h, chunk.Count, prime);

                        ReadOnlySpan<TestPosition> pos = chunk.GetSpanRO<TestPosition>(posCol);
                        ReadOnlySpan<TestVelocity> vel = chunk.GetSpanRO<TestVelocity>(velCol);

                        for (int i = 0; i < chunk.Count; i++)
                        {
                            Entity e = chunk.Entities[i];

                            h = FnvMixI32(h, e.Index, prime);
                            h = FnvMixI32(h, e.Version, prime);

                            h = FnvMixI32(h, pos[i].X, prime);
                            h = FnvMixI32(h, pos[i].Y, prime);

                            h = FnvMixI32(h, vel[i].X, prime);
                            h = FnvMixI32(h, vel[i].Y, prime);
                        }
                    }
                }

                return h;
            }
            finally
            {
                result.Dispose();
            }
        }

        private static ulong FnvMixI32(ulong h, int v, ulong prime)
        {
            unchecked
            {
                uint u = (uint)v;
                h ^= (byte)(u & 0xFF);
                h *= prime;
                h ^= (byte)((u >> 8) & 0xFF);
                h *= prime;
                h ^= (byte)((u >> 16) & 0xFF);
                h *= prime;
                h ^= (byte)((u >> 24) & 0xFF);
                h *= prime;
                return h;
            }
        }

        private static ulong FnvMixU32(ulong h, uint u, ulong prime)
        {
            unchecked
            {
                h ^= (byte)(u & 0xFF);
                h *= prime;
                h ^= (byte)((u >> 8) & 0xFF);
                h *= prime;
                h ^= (byte)((u >> 16) & 0xFF);
                h *= prime;
                h ^= (byte)((u >> 24) & 0xFF);
                h *= prime;
                return h;
            }
        }

        private static ulong FnvMixU64(ulong h, ulong u, ulong prime)
        {
            unchecked
            {
                h ^= (byte)(u & 0xFF);
                h *= prime;
                h ^= (byte)((u >> 8) & 0xFF);
                h *= prime;
                h ^= (byte)((u >> 16) & 0xFF);
                h *= prime;
                h ^= (byte)((u >> 24) & 0xFF);
                h *= prime;
                h ^= (byte)((u >> 32) & 0xFF);
                h *= prime;
                h ^= (byte)((u >> 40) & 0xFF);
                h *= prime;
                h ^= (byte)((u >> 48) & 0xFF);
                h *= prime;
                h ^= (byte)((u >> 56) & 0xFF);
                h *= prime;
                return h;
            }
        }
    }
}