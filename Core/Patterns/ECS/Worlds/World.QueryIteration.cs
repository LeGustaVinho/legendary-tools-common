using LegendaryTools.Common.Core.Patterns.ECS.Queries;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds
{
    public sealed partial class World
    {
        /// <summary>
        /// Starts a query iteration scope and returns the matching archetypes as a span via <see cref="WorldQueryResult"/>.
        /// This is the recommended API for high-performance code:
        /// iterate archetypes/chunks and use <see cref="Chunk.GetSpanRO{T}(int)"/> / <see cref="Chunk.GetSpanRW{T}(int)"/>
        /// with plain for loops.
        /// </summary>
        /// <remarks>
        /// While the result is alive, structural changes are disallowed (same rule as other iteration APIs).
        /// </remarks>
        public WorldQueryResult BeginQuery(Query query)
        {
            EnterIteration();

            Query.ArchetypeCache cache = query.GetOrBuildCache(Storage);
            return new WorldQueryResult(this, cache.Buffer, cache.Count);
        }

        /// <summary>
        /// Iterates over all chunks matching the query.
        /// High performance iteration (alloc-free, cache-friendly).
        /// </summary>
        /// <typeparam name="TProcessor">Struct implementing IChunkProcessor.</typeparam>
        /// <param name="query">The query to match.</param>
        /// <param name="processor">The processor struct (passed by ref).</param>
        public void ForEachChunk<TProcessor>(Query query, ref TProcessor processor)
            where TProcessor : struct, IChunkProcessor
        {
            EnterIteration();
            try
            {
                Query.ArchetypeCache cache = query.GetOrBuildCache(Storage);

                Archetype[] matching = cache.Buffer;
                int matchingCount = cache.Count;

                for (int a = 0; a < matchingCount; a++)
                {
                    Archetype archetype = matching[a];

                    // Zero-alloc chunk iteration over the archetype internal buffer.
                    Chunk[] chunks = archetype.ChunksBuffer;
                    int chunkCount = archetype.ChunkCount;

                    // Deterministic by design:
                    // - Archetypes are cached in deterministic order (StorageService enumeration),
                    // - Chunks are stored in ChunkId order (sequential creation).
                    for (int c = 0; c < chunkCount; c++)
                    {
                        Chunk chunk = chunks[c];
                        if (chunk.Count == 0) continue;

                        processor.Execute(archetype, chunk);
                    }
                }
            }
            finally
            {
                ExitIteration();
            }
        }

        /// <summary>
        /// Iterates over all entities matching the query.
        /// Convenience API; slightly slower than chunk iteration due to callback overhead.
        /// </summary>
        /// <typeparam name="TProcessor">Struct implementing IEntityProcessor.</typeparam>
        /// <param name="query">The query to match.</param>
        /// <param name="processor">The processor struct (passed by ref).</param>
        public void ForEachEntity<TProcessor>(Query query, ref TProcessor processor)
            where TProcessor : struct, IEntityProcessor
        {
            EnterIteration();
            try
            {
                Query.ArchetypeCache cache = query.GetOrBuildCache(Storage);

                Archetype[] matching = cache.Buffer;
                int matchingCount = cache.Count;

                for (int a = 0; a < matchingCount; a++)
                {
                    Archetype archetype = matching[a];

                    Chunk[] chunks = archetype.ChunksBuffer;
                    int chunkCount = archetype.ChunkCount;

                    for (int c = 0; c < chunkCount; c++)
                    {
                        Chunk chunk = chunks[c];
                        int count = chunk.Count;
                        if (count == 0) continue;

                        for (int i = 0; i < count; i++)
                        {
                            processor.Execute(chunk.Entities[i]);
                        }
                    }
                }
            }
            finally
            {
                ExitIteration();
            }
        }
    }
}