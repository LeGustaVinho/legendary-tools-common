namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.Profiling
{
    /// <summary>
    /// Per-tick counters for the demo (spawn/destroy requests).
    /// This stays in Demo; core ECS does not depend on it.
    /// </summary>
    public sealed class EcsDemoTickCounters
    {
        /// <summary>
        /// Gets the number of spawn commands issued in the last tick.
        /// </summary>
        public int SpawnedLastTick { get; private set; }

        /// <summary>
        /// Gets the number of destroy commands issued in the last tick.
        /// </summary>
        public int DestroyedLastTick { get; private set; }

        private int _spawnedThisTick;
        private int _destroyedThisTick;

        /// <summary>
        /// Resets "this tick" counters. Call once right before running a tick.
        /// </summary>
        public void BeginTick()
        {
            _spawnedThisTick = 0;
            _destroyedThisTick = 0;
        }

        /// <summary>
        /// Commits "this tick" counters to "last tick". Call once right after the tick finished.
        /// </summary>
        public void EndTick()
        {
            SpawnedLastTick = _spawnedThisTick;
            DestroyedLastTick = _destroyedThisTick;
        }

        public void AddSpawn(int count)
        {
            if (count > 0) _spawnedThisTick += count;
        }

        public void AddDestroy(int count)
        {
            if (count > 0) _destroyedThisTick += count;
        }
    }
}