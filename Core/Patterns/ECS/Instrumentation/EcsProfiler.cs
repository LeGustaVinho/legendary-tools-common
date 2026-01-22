#if ECS_PROFILING
using System.Diagnostics;

namespace LegendaryTools.Common.Core.Patterns.ECS.Instrumentation
{
    /// <summary>
    /// Optional instrumentation helper compiled only when ECS_PROFILING is defined.
    /// Must never influence simulation decisions (read-only, for presentation/debug tooling).
    /// </summary>
    internal sealed class EcsProfiler
    {
        private readonly Stopwatch _watch = new Stopwatch();

        /// <summary>
        /// Starts or restarts the profiling sample timer.
        /// </summary>
        public void BeginSample()
        {
            _watch.Restart();
        }

        /// <summary>
        /// Stops the timer and returns the elapsed ticks since the last sample started.
        /// </summary>
        /// <returns>Elapsed ticks.</returns>
        public long EndSampleTicks()
        {
            _watch.Stop();
            return _watch.ElapsedTicks;
        }
    }
}
#endif