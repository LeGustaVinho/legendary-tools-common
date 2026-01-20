using System.Collections.Generic;
using LegendaryTools.Common.Core.Patterns.ECS.Queries;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds
{
    public sealed partial class World
    {
        public Query CreateQuery(Query query)
        {
            return query;
        }

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
                    IReadOnlyList<Chunk> chunks = archetype.Chunks;

                    for (int c = 0; c < chunks.Count; c++)
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
                    IReadOnlyList<Chunk> chunks = archetype.Chunks;

                    for (int c = 0; c < chunks.Count; c++)
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