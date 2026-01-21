using System;
using System.Collections.Generic;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;

namespace LegendaryTools.Common.Core.Patterns.ECS.Systems
{
    /// <summary>
    /// MVP scheduler: single-thread, fixed order. Owns system groups and drives ticks.
    /// </summary>
    public sealed class Scheduler
    {
        private readonly World _world;
        private readonly Dictionary<SystemPhase, SystemGroup> _groups;
        private readonly SystemPhase[] _fixedOrder;

        private bool _created;

        // IMPORTANT: Must NOT be readonly (FixedTickClock is a mutable struct).
        private FixedTickClock _clock;

        /// <summary>
        /// Version tag for deterministic scheduling layouts.
        /// </summary>
        public int LayoutVersion { get; }

        public Scheduler(World world, int simulationHz = 60, int layoutVersion = 1)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));

            LayoutVersion = layoutVersion;

            _groups = new Dictionary<SystemPhase, SystemGroup>(4);

            _fixedOrder = new[]
            {
                SystemPhase.BeginSimulation,
                SystemPhase.Simulation,
                SystemPhase.EndSimulation,
                SystemPhase.Presentation
            };

            for (int i = 0; i < _fixedOrder.Length; i++)
            {
                SystemPhase phase = _fixedOrder[i];
                _groups.Add(phase, new SystemGroup(phase, layoutVersion));
            }

            _created = false;

            _clock = new FixedTickClock(simulationHz, 0);
        }

        /// <summary>
        /// Gets a group for the specified phase.
        /// </summary>
        /// <param name="phase">Phase.</param>
        /// <returns>System group.</returns>
        public SystemGroup GetGroup(SystemPhase phase)
        {
            return _groups[phase];
        }

        /// <summary>
        /// Adds a system to the end of the specified phase group (fixed order).
        /// </summary>
        /// <param name="phase">Target phase.</param>
        /// <param name="system">System.</param>
        public void AddSystem(SystemPhase phase, ISystem system)
        {
            _groups[phase].Add(system);
        }

        public void AddSystem(SystemPhase phase, ISystem system, int order)
        {
            _groups[phase].Add(system, order);
        }

        public void Create()
        {
            if (_created) return;

            for (int i = 0; i < _fixedOrder.Length; i++)
            {
                _groups[_fixedOrder[i]].CreateAll(_world);
            }

            _created = true;
        }

        /// <summary>
        /// Destroys all systems (OnDestroy) in reverse fixed order.
        /// </summary>
        public void Destroy()
        {
            if (!_created) return;

            for (int i = _fixedOrder.Length - 1; i >= 0; i--)
            {
                _groups[_fixedOrder[i]].DestroyAll(_world);
            }

            _created = false;
        }

        /// <summary>
        /// Runs exactly one deterministic simulation tick (fixed TickDelta),
        /// then runs presentation once with a provided variable deltaTime.
        /// </summary>
        public void Tick(int tick, float presentationDeltaTime)
        {
            if (!_created) Create();

            // Keep clock in sync for users that mix APIs.
            _clock.Reset(tick);

            _world.BeginTick(tick);

            // BeginSimulation (optional, but kept as a stable phase)
            _groups[SystemPhase.BeginSimulation].UpdateAll(_world, tick);

            // Simulation
            _groups[SystemPhase.Simulation].UpdateAll(_world, tick);

            // EndSimulation: systems may still record commands to ECB.
            _groups[SystemPhase.EndSimulation].UpdateAll(_world, tick);

            // Playback at end of tick (defined sync point).
            _world.EndTick();

            _world.SetPresentationDeltaTime(presentationDeltaTime);
            _groups[SystemPhase.Presentation].UpdateAll(_world, tick);
        }

        /// <summary>
        /// Backwards-compatible manual tick. Uses the last presentation delta stored in the world.
        /// </summary>
        public void Tick(int tick)
        {
            Tick(tick, _world.Time.PresentationDeltaTime);
        }

        /// <summary>
        /// Unity-style driving API:
        /// consumes 0..N fixed simulation ticks from a variable frame deltaTime,
        /// then runs presentation exactly once using that variable deltaTime.
        /// </summary>
        public void Advance(float deltaTime)
        {
            if (!_created) Create();

            _clock.Accumulate(deltaTime);

            int tickForPresentation = _clock.Tick;

            while (_clock.TryConsumeTick(out int tick))
            {
                tickForPresentation = tick;

                _world.BeginTick(tick);

                _groups[SystemPhase.BeginSimulation].UpdateAll(_world, tick);
                _groups[SystemPhase.Simulation].UpdateAll(_world, tick);
                _groups[SystemPhase.EndSimulation].UpdateAll(_world, tick);

                _world.EndTick();
            }

            _world.SetPresentationDeltaTime(deltaTime);
            _groups[SystemPhase.Presentation].UpdateAll(_world, tickForPresentation);
        }
    }
}