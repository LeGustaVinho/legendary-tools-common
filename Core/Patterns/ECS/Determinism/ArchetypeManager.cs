#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Creates and looks up archetypes by deterministic signatures.
    /// </summary>
    public sealed class ArchetypeManager
    {
        private readonly Dictionary<ComponentTypeSet, Archetype> _map;
        private readonly Dictionary<ArchetypeId, Archetype> _byId;

        private readonly List<Archetype> _allByCreation;
        private readonly List<Archetype> _allSortedById;

        private int _version;

        public ArchetypeManager(int initialArchetypeCapacity = 64)
        {
            if (initialArchetypeCapacity < 1)
                initialArchetypeCapacity = 1;

            _map = new Dictionary<ComponentTypeSet, Archetype>(initialArchetypeCapacity);
            _byId = new Dictionary<ArchetypeId, Archetype>(initialArchetypeCapacity);

            _allByCreation = new List<Archetype>(initialArchetypeCapacity);
            _allSortedById = new List<Archetype>(initialArchetypeCapacity);

            _version = 0;
        }

        /// <summary>
        /// Gets a monotonically increasing version that changes whenever a new archetype is created.
        /// Useful for query cache invalidation.
        /// </summary>
        public int Version => _version;

        public int Count => _allByCreation.Count;

        /// <summary>
        /// Archetypes in creation order (not guaranteed deterministic across different creation patterns).
        /// Prefer <see cref="AllSortedById"/> for deterministic iteration.
        /// </summary>
        public IReadOnlyList<Archetype> All => _allByCreation;

        /// <summary>
        /// Archetypes sorted by <see cref="ArchetypeId"/> ascending (deterministic iteration order).
        /// </summary>
        public IReadOnlyList<Archetype> AllSortedById => _allSortedById;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Archetype GetOrCreateArchetype(in ComponentTypeSet set)
        {
            if (_map.TryGetValue(set, out Archetype archetype))
                return archetype;

            archetype = new Archetype(set);

            _map.Add(set, archetype);
            _byId.Add(archetype.Id, archetype);

            _allByCreation.Add(archetype);
            InsertSortedById(archetype);

            _version++; // Structural change: new archetype exists.

            return archetype;
        }

        public Archetype GetOrCreateArchetype(ReadOnlySpan<ComponentTypeId> unorderedIds)
        {
            ComponentTypeSet set = new(unorderedIds);
            return GetOrCreateArchetype(in set);
        }

        public bool TryGetArchetype(in ComponentTypeSet set, out Archetype archetype)
        {
            return _map.TryGetValue(set, out archetype!);
        }

        public bool TryGetArchetypeById(ArchetypeId id, out Archetype archetype)
        {
            return _byId.TryGetValue(id, out archetype!);
        }

        public Archetype GetArchetypeById(ArchetypeId id)
        {
            if (!_byId.TryGetValue(id, out Archetype archetype))
                throw new KeyNotFoundException($"Archetype not found for id: {id}");

            return archetype;
        }

        private void InsertSortedById(Archetype archetype)
        {
            // Binary insert into _allSortedById to keep it sorted without relying on dictionary ordering.
            int lo = 0;
            int hi = _allSortedById.Count - 1;

            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                ArchetypeId midId = _allSortedById[mid].Id;

                if (midId.Value < archetype.Id.Value)
                    lo = mid + 1;
                else
                    hi = mid - 1;
            }

            _allSortedById.Insert(lo, archetype);
        }
    }
}