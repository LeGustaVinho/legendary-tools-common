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

        /// <summary>
        /// Initializes a new instance of the <see cref="Scheduler"/> class.
        /// </summary>
        /// <param name="world">Target world.</param>
        public Scheduler(World world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));

            _groups = new Dictionary<SystemPhase, SystemGroup>(4);

            // Fixed order for determinism.
            _fixedOrder = new[]
            {
                SystemPhase.BeginSimulation,
                SystemPhase.Simulation,
                SystemPhase.EndSimulation,
                SystemPhase.Presentation
            };

            // Create groups upfront so order is always stable.
            for (int i = 0; i < _fixedOrder.Length; i++)
            {
                SystemPhase phase = _fixedOrder[i];
                _groups.Add(phase, new SystemGroup(phase));
            }

            _created = false;
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

        /// <summary>
        /// Creates all systems (OnCreate) in fixed order. Safe to call once.
        /// </summary>
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
        /// Runs one fixed simulation tick in fixed phase order.
        /// </summary>
        /// <param name="tick">Current tick.</param>
        public void Tick(int tick)
        {
            if (!_created) Create();

            // Simulation scope: structural changes must go through ECB.
            _world.BeginTick(tick);

            // BeginSimulation (optional, but kept as a stable phase)
            _groups[SystemPhase.BeginSimulation].UpdateAll(_world, tick);

            // Simulation
            _groups[SystemPhase.Simulation].UpdateAll(_world, tick);

            // EndSimulation: systems may still record commands to ECB.
            _groups[SystemPhase.EndSimulation].UpdateAll(_world, tick);

            // Playback at end of tick (defined sync point).
            _world.EndTick();

            // Presentation: read-only by convention (not enforced in MVP).
            _groups[SystemPhase.Presentation].UpdateAll(_world, tick);
        }
    }
}