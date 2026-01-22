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
        private readonly List<SystemEntry> _systems;
        private bool _created;
        private bool _sorted;

        private int _autoOrder;

        /// <summary>
        /// Initializes a new instance of the <see cref="SystemGroup"/> class.
        /// </summary>
        /// <param name="phase">The phase this group belongs to.</param>
        /// <param name="layoutVersion">Layout version for determinism.</param>
        public SystemGroup(SystemPhase phase, int layoutVersion = 1)
        {
            Phase = phase;
            LayoutVersion = layoutVersion;

            _systems = new List<SystemEntry>(32);
            _created = false;
            _sorted = false;

            _autoOrder = 0;
        }

        /// <summary>
        /// Gets the phase associated with this group.
        /// </summary>
        public SystemPhase Phase { get; }

        /// <summary>
        /// Version tag for deterministic scheduling layouts.
        /// </summary>
        public int LayoutVersion { get; }

        /// <summary>
        /// Adds a system using an auto-assigned deterministic order (based on add order).
        /// </summary>
        public void Add(ISystem system)
        {
            Add(system, _autoOrder++);
        }

        /// <summary>
        /// Adds a system with an explicit deterministic order.
        /// Systems are sorted by (Order, TypeName) once at Create() time.
        /// </summary>
        public void Add(ISystem system, int order)
        {
            if (system == null) throw new ArgumentNullException(nameof(system));

            _systems.Add(new SystemEntry(system, order));
            _sorted = false;
        }

        internal void CreateAll(World world)
        {
            if (_created) return;

            SortIfNeeded();

            for (int i = 0; i < _systems.Count; i++)
            {
                _systems[i].System.OnCreate(world);
            }

            _created = true;
        }

        internal void UpdateAll(World world, int tick)
        {
            SortIfNeeded();

            for (int i = 0; i < _systems.Count; i++)
            {
                // List<T> indexer returns by value (copy), so ref readonly is not allowed here.
                SystemEntry entry = _systems[i];

                world.SetCurrentSystemOrder(entry.Order);
                entry.System.OnUpdate(world, tick);
            }

            world.SetCurrentSystemOrder(0);
        }

        internal void DestroyAll(World world)
        {
            SortIfNeeded();

            for (int i = _systems.Count - 1; i >= 0; i--)
            {
                _systems[i].System.OnDestroy(world);
            }

            _created = false;
        }

        /// <summary>
        /// Gets the number of systems in this group.
        /// </summary>
        public int Count => _systems.Count;

        private void SortIfNeeded()
        {
            if (_sorted) return;

            _systems.Sort(SystemEntryComparer.Instance);
            _sorted = true;
        }

        private readonly struct SystemEntry
        {
            public readonly ISystem System;
            public readonly int Order;
            public readonly string TypeName;

            public SystemEntry(ISystem system, int order)
            {
                System = system;
                Order = order;
                TypeName = system.GetType().FullName ?? system.GetType().Name;
            }
        }

        private sealed class SystemEntryComparer : IComparer<SystemEntry>
        {
            public static readonly SystemEntryComparer Instance = new();

            public int Compare(SystemEntry x, SystemEntry y)
            {
                int cmp = x.Order.CompareTo(y.Order);
                if (cmp != 0) return cmp;

                return string.CompareOrdinal(x.TypeName, y.TypeName);
            }
        }
    }
}