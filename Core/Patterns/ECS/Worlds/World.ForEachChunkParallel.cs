using System;
using System.Threading.Tasks;
using LegendaryTools.Common.Core.Patterns.ECS.Commands;
using LegendaryTools.Common.Core.Patterns.ECS.Memory;
using LegendaryTools.Common.Core.Patterns.ECS.Parallel;
using LegendaryTools.Common.Core.Patterns.ECS.Queries;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds
{
    public sealed partial class World
    {
        /// <summary>
        /// Executes a chunk query in parallel with deterministic worker assignment.
        /// Worker index is derived from the stable work-item index, not from threads/scheduling.
        /// </summary>
        /// <remarks>
        /// Determinism notes:
        /// - Work list is built in stable order (archetypes stable + chunk ordinal).
        /// - Work item i is always processed by worker (i % workerCount), regardless of runtime scheduling.
        /// - Each worker writes into its own ECB buffer (GetEcbWorker(worker)).
        ///
        /// Thread-safety note:
        /// - The processor is copied per worker. Do not rely on mutations made inside Execute to be visible outside.
        ///   If you need to aggregate results, use a deterministic per-worker reduction buffer (e.g. DeterministicReduction&lt;T&gt;).
        /// </remarks>
        public void ForEachChunkParallel<TProcessor>(Query query, int workerCount, ref TProcessor processor)
            where TProcessor : struct, IParallelChunkProcessor
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (workerCount < 1) workerCount = 1;

            EnsureEcbInitialized();

            // Ensure worker APIs/buffers exist. In deterministic mode, warmup should be used to preallocate capacities.
            StateEcb.EnsureWorkers(workerCount);

            EnterIteration();
            try
            {
                PooledList<ChunkWorkItem> work = new(Math.Max(16, workerCount * 8));
                try
                {
                    ChunkWorkList.Build(Storage, query, work);

                    int workCount = work.Count;
                    if (workCount <= 0) return;

                    // Striped assignment: item i always maps to worker (i % workerCount) deterministically.
                    Task[] tasks = new Task[workerCount];

                    for (int w = 0; w < workerCount; w++)
                    {
                        int worker = w;
                        ICommandBuffer ecb = GetEcbWorker(worker);

                        // IMPORTANT: Copy the ref processor into a local value per worker (cannot capture ref in lambdas).
                        TProcessor localProcessor = processor;

                        tasks[w] = Task.Run(() =>
                        {
                            for (int i = worker; i < workCount; i += workerCount)
                            {
                                ChunkWorkItem item = work[i];
                                localProcessor.Execute(item.Archetype, item.Chunk, worker, ecb);
                            }
                        });
                    }

                    Task.WaitAll(tasks);
                }
                finally
                {
                    work.Dispose(true);
                }
            }
            finally
            {
                ExitIteration();
            }
        }
    }
}