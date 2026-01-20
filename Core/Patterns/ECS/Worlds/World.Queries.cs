using LegendaryTools.Common.Core.Patterns.ECS.Queries;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds
{
    public sealed partial class World
    {
        /// <summary>
        /// Creates a query with All + None constraints.
        /// Intended to be created once and reused (cache-friendly).
        /// </summary>
        /// <param name="query">Query instance.</param>
        /// <returns>The same query for fluent usage.</returns>
        public Query CreateQuery(Query query)
        {
            // KISS: the Query object already owns its cache; this method exists for API discoverability.
            return query;
        }

        /// <summary>
        /// Iterates matching chunks in stable order: ArchetypeId ascending, then ChunkId ascending.
        /// Allocation-free if the query cache is already built and no new archetypes are created.
        /// </summary>
        /// <typeparam name="TProcessor">Chunk processor type.</typeparam>
        /// <param name="query">Query.</param>
        /// <param name="processor">Processor (struct) passed by reference.</param>
        public void ForEachChunk<TProcessor>(Query query, ref TProcessor processor)
            where TProcessor : struct, IChunkProcessor
        {
            EnterIteration();
            try
            {
                Archetype[] matching = query.GetOrBuildCache(Storage);

                for (int a = 0; a < matching.Length; a++)
                {
                    Archetype archetype = matching[a];
                    var chunks = archetype.Chunks;

                    // Chunks are stored in ChunkId order (index == ChunkId in MVP).
                    for (int c = 0; c < chunks.Count; c++)
                    {
                        Chunk chunk = chunks[c];
                        if (chunk.Count == 0)
                        {
                            continue;
                        }

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
        /// Iterates matching entities in stable order derived from chunk iteration.
        /// Allocation-free if the query cache is already built and no new archetypes are created.
        /// </summary>
        /// <typeparam name="TProcessor">Entity processor type.</typeparam>
        /// <param name="query">Query.</param>
        /// <param name="processor">Processor (struct) passed by reference.</param>
        public void ForEachEntity<TProcessor>(Query query, ref TProcessor processor)
            where TProcessor : struct, IEntityProcessor
        {
            EnterIteration();
            try
            {
                Archetype[] matching = query.GetOrBuildCache(Storage);

                for (int a = 0; a < matching.Length; a++)
                {
                    Archetype archetype = matching[a];
                    var chunks = archetype.Chunks;

                    for (int c = 0; c < chunks.Count; c++)
                    {
                        Chunk chunk = chunks[c];
                        int count = chunk.Count;
                        if (count == 0)
                        {
                            continue;
                        }

                        // Entity order is stable within the current storage state.
                        // Swap-back removals change per-tick ordering but remain deterministic.
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
