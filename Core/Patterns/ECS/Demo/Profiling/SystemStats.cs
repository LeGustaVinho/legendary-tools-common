using System.Diagnostics;

namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.Profiling
{
    /// <summary>
    /// Per-system performance statistics (Stopwatch ticks based).
    /// </summary>
    public sealed class SystemStats
    {
        private long _calls;
        private long _sumTicks;

        public SystemStats(string name)
        {
            Name = name;
            MinTicks = long.MaxValue;
            MaxTicks = 0;
            LastTicks = 0;
        }

        public string Name { get; }

        public long Calls => _calls;

        public long LastTicks { get; private set; }

        public long MinTicks { get; private set; }

        public long MaxTicks { get; private set; }

        public double AvgTicks => _calls == 0 ? 0.0 : (double)_sumTicks / _calls;

        public double LastMs => TicksToMs(LastTicks);

        public double MinMs => MinTicks == long.MaxValue ? 0.0 : TicksToMs(MinTicks);

        public double MaxMs => TicksToMs(MaxTicks);

        public double AvgMs => TicksToMs((long)AvgTicks);

        public void Record(long elapsedTicks)
        {
            LastTicks = elapsedTicks;

            if (elapsedTicks < MinTicks)
            {
                MinTicks = elapsedTicks;
            }

            if (elapsedTicks > MaxTicks)
            {
                MaxTicks = elapsedTicks;
            }

            _calls++;
            _sumTicks += elapsedTicks;
        }

        private static double TicksToMs(long ticks)
        {
            // Stopwatch ticks -> seconds -> ms.
            return (ticks * 1000.0) / Stopwatch.Frequency;
        }
    }
}
