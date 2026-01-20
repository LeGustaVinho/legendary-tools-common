using System;
using UnityEngine;
using LegendaryTools.Common.Core.Patterns.ECS.Demo.Profiling;
using LegendaryTools.Common.Core.Patterns.ECS.Demo.Simulation;
using LegendaryTools.Common.Core.Patterns.ECS.Demo.Simulation.Systems;
using LegendaryTools.Common.Core.Patterns.ECS.Demo.UI;
using LegendaryTools.Common.Core.Patterns.ECS.Systems;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;

namespace LegendaryTools.Common.Core.Patterns.ECS.Demo
{
    /// <summary>
    /// Demo entry point. Attach to a GameObject in a scene.
    /// </summary>
    public sealed class EcsDemoBootstrap : MonoBehaviour
    {
        [SerializeField] private EcsDemoConfig _config;

        private World _world;
        private Scheduler _scheduler;

        private int _tick;
        private float _accumulator;

        private EcsDemoProfiler _profiler;
        private EcsDemoTickCounters _counters;
        private EcsDemoHud _hud;

        private float TickDelta => 1.0f / Mathf.Max(1, _config != null ? _config.TickRate : 60);

        private void Awake()
        {
            if (_config == null)
                // Safe fallback if no asset is assigned.
                _config = ScriptableObject.CreateInstance<EcsDemoConfig>();

            _world = new World(Mathf.Max(1024, _config.InitialEntityCount + 256));
            _scheduler = _world.CreateScheduler();

            _profiler = new EcsDemoProfiler();
            _counters = new EcsDemoTickCounters();

            // Build systems (Simulation phase).
            AddProfiled(SystemPhase.Simulation, new MovementSystem());
            AddProfiled(SystemPhase.Simulation, new DamageSystem());
            AddProfiled(SystemPhase.Simulation, new LifetimeSystem(_counters));

            // Optional spawner.
            if (_config.SpawnPerTick > 0)
                AddProfiled(
                    SystemPhase.Simulation,
                    new SpawnSystem(_counters, _config.SpawnPerTick, _config.LifetimeMinTicks,
                        _config.LifetimeMaxTicks));

            // Initialize systems.
            _scheduler.Create();

            // Create initial entities (outside tick, immediate changes allowed).
            EcsDemoEntityFactory.CreateInitialEntities(_world, _config);

            // HUD.
            if (_config.EnableHud)
            {
                _hud = gameObject.GetComponent<EcsDemoHud>();
                if (_hud == null) _hud = gameObject.AddComponent<EcsDemoHud>();

                _hud.Initialize(_world, _profiler, _counters, _config, Mathf.Max(1, _config.TickRate));
            }

            _tick = 0;
            _accumulator = 0.0f;
        }

        private void OnDestroy()
        {
            try
            {
                _scheduler?.Destroy();
            }
            catch
            {
                // Ignore in teardown.
            }
        }

        private void Update()
        {
            // Run a fixed-rate simulation regardless of Unity's frame rate.
            float dt = Time.deltaTime;
            _accumulator += dt;

            float tickDelta = TickDelta;

            // Limit catch-up to avoid spiraling in editor hiccups.
            int maxTicksPerFrame = 8;
            int ran = 0;

            while (_accumulator >= tickDelta && ran < maxTicksPerFrame)
            {
                _counters.BeginTick();

                try
                {
                    _scheduler.Tick(_tick);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    if (_config.StopOnException)
                    {
                        enabled = false;
                        break;
                    }
                }

                _counters.EndTick();

                _tick++;
                ran++;
                _accumulator -= tickDelta;

                if (_hud != null) _hud.SetTick(_tick);
            }
        }

        private void AddProfiled(SystemPhase phase, ISystem system)
        {
            string name = system.GetType().Name;
            SystemStats stats = new(name);
            _profiler.Add(stats);

            ProfiledSystemWrapper wrapped = new(system, stats);
            _scheduler.AddSystem(phase, wrapped);
        }
    }
}