using System.Diagnostics;
using LegendaryTools.Common.Core.Patterns.ECS.Systems;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;

namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.Profiling
{
    /// <summary>
    /// Wraps a system and measures its OnUpdate time.
    /// </summary>
    public sealed class ProfiledSystemWrapper : ISystem
    {
        private readonly ISystem _inner;
        private readonly SystemStats _stats;

        public ProfiledSystemWrapper(ISystem inner, SystemStats stats)
        {
            _inner = inner;
            _stats = stats;
        }

        public void OnCreate(World world)
        {
            _inner.OnCreate(world);
        }

        public void OnUpdate(World world, int tick)
        {
            long start = Stopwatch.GetTimestamp();
            _inner.OnUpdate(world, tick);
            long end = Stopwatch.GetTimestamp();

            _stats.Record(end - start);
        }

        public void OnDestroy(World world)
        {
            _inner.OnDestroy(world);
        }
    }
}