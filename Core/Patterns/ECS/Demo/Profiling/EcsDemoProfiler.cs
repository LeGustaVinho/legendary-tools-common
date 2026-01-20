using System.Collections.Generic;
using System.Text;

namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.Profiling
{
    /// <summary>
    /// Aggregates profiled systems and renders a stable text table for HUD/log.
    /// </summary>
    public sealed class EcsDemoProfiler
    {
        private readonly List<SystemStats> _stats;
        private readonly StringBuilder _sb;

        public EcsDemoProfiler(int initialCapacity = 32)
        {
            _stats = new List<SystemStats>(initialCapacity);
            _sb = new StringBuilder(2048);
        }

        public IReadOnlyList<SystemStats> Stats => _stats;

        public void Add(SystemStats stats)
        {
            _stats.Add(stats);
        }

        public string BuildTableText(int tick, int tickRate, int aliveCount, int spawnedLastTick, int destroyedLastTick)
        {
            _sb.Clear();

            _sb.Append("ECS Demo Profiler\n");
            _sb.Append("Tick: ").Append(tick).Append(" | TickRate: ").Append(tickRate).Append('\n');
            _sb.Append("Alive: ").Append(aliveCount)
               .Append(" | Spawned(last): ").Append(spawnedLastTick)
               .Append(" | Destroyed(last): ").Append(destroyedLastTick)
               .Append('\n');

            _sb.Append("------------------------------------------------------------\n");
            _sb.Append("System                          | Last | Avg  | Min  | Max\n");
            _sb.Append("------------------------------------------------------------\n");

            for (int i = 0; i < _stats.Count; i++)
            {
                SystemStats s = _stats[i];

                // Fixed width name column for easy visual comparison.
                string name = s.Name ?? "Unnamed";
                if (name.Length > 30)
                {
                    name = name.Substring(0, 30);
                }

                _sb.Append(name.PadRight(30));
                _sb.Append(" | ");
                _sb.Append(FormatMs(s.LastMs)).Append(" | ");
                _sb.Append(FormatMs(s.AvgMs)).Append(" | ");
                _sb.Append(FormatMs(s.MinMs)).Append(" | ");
                _sb.Append(FormatMs(s.MaxMs)).Append('\n');
            }

            return _sb.ToString();
        }

        private static string FormatMs(double ms)
        {
            // Keep it compact and stable.
            if (ms < 10.0) return ms.ToString("0.000");
            if (ms < 100.0) return ms.ToString("0.00");
            return ms.ToString("0.0");
        }
    }
}
