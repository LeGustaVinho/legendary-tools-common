using System;
using LegendaryTools.Common.Core.Patterns.ECS.Memory;
using LegendaryTools.Common.Core.Patterns.ECS.Queries;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal;

namespace LegendaryTools.Common.Core.Patterns.ECS.Parallel
{
    /// <summary>
    /// Builds a stable chunk work list from a query:
    /// - Archetypes are enumerated in stable order.
    /// - Chunks are iterated by their stable ordinal index.
    /// - Empty chunks are skipped.
    /// </summary>
    internal static class ChunkWorkList
    {
        public static void Build(StorageService storage, Query query, PooledList<ChunkWorkItem> outWork)
        {
            if (storage == null) throw new ArgumentNullException(nameof(storage));
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (outWork == null) throw new ArgumentNullException(nameof(outWork));

            outWork.Clear();

            Query.ArchetypeCache cache = query.GetOrBuildCache(storage);

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
                    if (chunk == null) continue;
                    if (chunk.Count == 0) continue;

                    outWork.Add(new ChunkWorkItem(archetype, chunk, a, c));
                }
            }
        }
    }
}