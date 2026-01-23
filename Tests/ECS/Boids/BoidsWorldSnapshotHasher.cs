using System;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Queries;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Tests.ECS.Boids
{
    public static class BoidsWorldSnapshotHasher
    {
        public static ulong ComputeSnapshot(World world)
        {
            // NOTE: World.QueryAll supports up to 3 generic args in this project.
            // Hash only current position/velocity (next buffers are derived and not needed for snapshot equality).
            Query query = world.QueryAll<BoidPosition, BoidVelocity>();
            WorldQueryResult result = world.BeginQuery(query);

            try
            {
                ulong h = 14695981039346656037UL; // FNV-1a 64 offset basis
                const ulong prime = 1099511628211UL;

                foreach (Archetype archetype in result.Archetypes)
                {
                    h = MixU64(h, archetype.ArchetypeId.Value, prime);
                    h = MixU32(h, archetype.ArchetypeId.Disambiguator, prime);

                    if (!archetype.TryGetColumnIndex(world.GetComponentTypeId<BoidPosition>(), out int posCol))
                        continue;
                    if (!archetype.TryGetColumnIndex(world.GetComponentTypeId<BoidVelocity>(), out int velCol))
                        continue;

                    Chunk[] chunks = archetype.ChunksBuffer;
                    int chunkCount = archetype.ChunkCount;

                    for (int c = 0; c < chunkCount; c++)
                    {
                        Chunk chunk = chunks[c];
                        if (chunk == null || chunk.Count == 0) continue;

                        h = MixI32(h, chunk.ChunkId, prime);
                        h = MixI32(h, chunk.Count, prime);

                        ReadOnlySpan<BoidPosition> pos = chunk.GetSpanRO<BoidPosition>(posCol);
                        ReadOnlySpan<BoidVelocity> vel = chunk.GetSpanRO<BoidVelocity>(velCol);

                        for (int i = 0; i < chunk.Count; i++)
                        {
                            Entity e = chunk.Entities[i];

                            h = MixI32(h, e.Index, prime);
                            h = MixI32(h, e.Version, prime);

                            h = MixI32(h, pos[i].X, prime);
                            h = MixI32(h, pos[i].Y, prime);

                            h = MixI32(h, vel[i].X, prime);
                            h = MixI32(h, vel[i].Y, prime);
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

        private static ulong MixI32(ulong h, int v, ulong prime)
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

        private static ulong MixU32(ulong h, uint u, ulong prime)
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

        private static ulong MixU64(ulong h, ulong u, ulong prime)
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