using UnityEngine;

using LegendaryTools.Common.Core.Patterns.ECS.Demo.Profiling;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;

namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.UI
{
    /// <summary>
    /// Simple OnGUI HUD for demo performance stats.
    /// </summary>
    public sealed class EcsDemoHud : MonoBehaviour
    {
        private EcsDemoProfiler _profiler;
        private EcsDemoTickCounters _counters;
        private EcsDemoConfig _config;
        private World _world;

        private int _tick;
        private int _tickRate;

        private float _nextRefreshTime;
        private string _cachedText;

        public void Initialize(World world, EcsDemoProfiler profiler, EcsDemoTickCounters counters, EcsDemoConfig config, int tickRate)
        {
            _world = world;
            _profiler = profiler;
            _counters = counters;
            _config = config;
            _tickRate = tickRate;

            _cachedText = string.Empty;
            _nextRefreshTime = 0.0f;
        }

        public void SetTick(int tick)
        {
            _tick = tick;
        }

        private void OnGUI()
        {
            if (_profiler == null || _config == null || !_config.EnableHud)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now >= _nextRefreshTime)
            {
                int hz = Mathf.Max(1, _config.HudRefreshHz);
                _nextRefreshTime = now + (1.0f / hz);

                int alive = _world != null ? _world.GetAliveEntityCount() : 0;
                int spawned = _counters != null ? _counters.SpawnedLastTick : 0;
                int destroyed = _counters != null ? _counters.DestroyedLastTick : 0;

                _cachedText = _profiler.BuildTableText(_tick, _tickRate, alive, spawned, destroyed);
            }

            GUI.Label(new Rect(10, 10, 780, 820), _cachedText);
        }
    }
}
