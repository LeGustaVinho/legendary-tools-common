using System;
using System.Collections.Generic;

using LegendaryTools.Common.Core.Patterns.ECS.Worlds;

namespace LegendaryTools.Common.Core.Patterns.ECS.Systems
{
    /// <summary>
    /// A fixed-order list of systems executed sequentially.
    /// </summary>
    public sealed class SystemGroup
    {
        private readonly List<ISystem> _systems;
        private bool _created;

        /// <summary>
        /// Initializes a new instance of the <see cref="SystemGroup"/> class.
        /// </summary>
        /// <param name="phase">Group phase.</param>
        public SystemGroup(SystemPhase phase)
        {
            Phase = phase;
            _systems = new List<ISystem>(32);
            _created = false;
        }

        /// <summary>
        /// Gets the phase this group belongs to.
        /// </summary>
        public SystemPhase Phase { get; }

        /// <summary>
        /// Adds a system to the end of the group (fixed order).
        /// </summary>
        /// <param name="system">System instance.</param>
        public void Add(ISystem system)
        {
            if (system == null)
            {
                throw new ArgumentNullException(nameof(system));
            }

            _systems.Add(system);
        }

        internal void CreateAll(World world)
        {
            if (_created)
            {
                return;
            }

            for (int i = 0; i < _systems.Count; i++)
            {
                _systems[i].OnCreate(world);
            }

            _created = true;
        }

        internal void UpdateAll(World world, int tick)
        {
            for (int i = 0; i < _systems.Count; i++)
            {
                _systems[i].OnUpdate(world, tick);
            }
        }

        internal void DestroyAll(World world)
        {
            for (int i = _systems.Count - 1; i >= 0; i--)
            {
                _systems[i].OnDestroy(world);
            }
        }

        /// <summary>
        /// Gets the number of systems in this group.
        /// </summary>
        public int Count => _systems.Count;
    }
}
