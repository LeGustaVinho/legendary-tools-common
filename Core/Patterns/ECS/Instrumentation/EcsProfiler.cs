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

        public void BeginSample()
        {
            _watch.Restart();
        }

        public long EndSampleTicks()
        {
            _watch.Stop();
            return _watch.ElapsedTicks;
        }
    }
}
#endif