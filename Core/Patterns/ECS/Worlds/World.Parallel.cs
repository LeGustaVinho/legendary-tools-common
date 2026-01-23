using LegendaryTools.Common.Core.Patterns.ECS.Commands;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds
{
    public sealed partial class World
    {
        /// <summary>
        /// Preallocates deterministic ECB capacity per worker for commands and temp entities.
        /// Must be called during bootstrap (before simulation) if you will emit ECB commands in parallel.
        /// </summary>
        public void WarmupEcbParallel(int workerCount, int expectedCommandsPerWorker, int expectedTempEntitiesPerWorker)
        {
            EnsureEcbInitialized();
            StateEcb.WarmupParallel(workerCount, expectedCommandsPerWorker, expectedTempEntitiesPerWorker);
        }

        /// <summary>
        /// Preallocates deterministic ECB value capacity per worker for a given component type.
        /// Must be called during bootstrap (before simulation) if you will ECB.Add&lt;T&gt;(...) in parallel.
        /// </summary>
        public void WarmupEcbValuesParallel<T>(int workerCount, int expectedAddsPerWorker) where T : struct
        {
            EnsureEcbInitialized();
            StateEcb.WarmupValuesParallel<T>(workerCount, expectedAddsPerWorker);
        }

        /// <summary>
        /// Returns a per-worker ICommandBuffer view. Each worker writes into its own buffer, later merged deterministically.
        /// </summary>
        internal ICommandBuffer GetEcbWorker(int workerIndex)
        {
            EnsureEcbInitialized();
            return StateEcb.GetWorkerBuffer(workerIndex);
        }
    }
}