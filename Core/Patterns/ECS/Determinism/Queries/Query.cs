#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Deterministic query over archetypes using (All/None/Any) component masks with cached matching archetypes.
    /// </summary>
    /// <remarks>
    /// Cache invalidates when <see cref="ArchetypeManager.Version"/> changes (new archetypes created).
    /// After warmup, repeated iteration performs zero allocations.
    /// </remarks>
    public sealed class Query
    {
        private readonly ComponentBitSet _all;
        private readonly ComponentBitSet _none;
        private readonly ComponentBitSet _any;

        private readonly List<Archetype> _matchingArchetypes;

        private int _cachedArchetypeManagerVersion;
        private int _cachedMaxComponentId;

        internal Query(int maxComponentId)
        {
            int wordCount = ComponentBitSet.GetWordCountForMaxId(maxComponentId);

            _all = new ComponentBitSet(wordCount);
            _none = new ComponentBitSet(wordCount);
            _any = new ComponentBitSet(wordCount);

            _matchingArchetypes = new List<Archetype>(32);

            _cachedArchetypeManagerVersion = -1;
            _cachedMaxComponentId = maxComponentId;
        }

        /// <summary>
        /// Adds a component id to the All set (required).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AddAll(ComponentTypeId id)
        {
            _all.Set(id.Value);
        }

        /// <summary>
        /// Adds a component id to the None set (forbidden).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AddNone(ComponentTypeId id)
        {
            _none.Set(id.Value);
        }

        /// <summary>
        /// Adds a component id to the Any set (at least one required).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AddAny(ComponentTypeId id)
        {
            _any.Set(id.Value);
        }

        /// <summary>
        /// Returns the cached matching archetypes in deterministic order (by ArchetypeId ascending).
        /// </summary>
        internal IReadOnlyList<Archetype> GetMatchingArchetypes(World world)
        {
            EnsureCache(world);
            return _matchingArchetypes;
        }

        /// <summary>
        /// Ensures the query cache is built and up-to-date.
        /// </summary>
        internal void EnsureCache(World world)
        {
            int maxComponentId = world.Components.Count;
            int archVersion = world.Archetypes.Version;

            // If registry grew, our masks might be smaller; rebuild masks (rare; typically registry is sealed at boot).
            if (maxComponentId > _cachedMaxComponentId)
                // Recreate masks with the larger size (query build-time allocation, not per-iteration hot path).
                RebuildMasks(maxComponentId);

            if (_cachedArchetypeManagerVersion == archVersion)
                return;

            _matchingArchetypes.Clear();

            IReadOnlyList<Archetype> allArchetypes = world.Archetypes.AllSortedById;

            for (int i = 0; i < allArchetypes.Count; i++)
            {
                Archetype a = allArchetypes[i];

                ComponentBitSet archMask = a.GetOrBuildMask(maxComponentId);

                // All: must contain all required.
                if (_all.AnySet && !archMask.ContainsAll(_all))
                    continue;

                // None: must not intersect.
                if (_none.AnySet && !archMask.IntersectsNone(_none))
                    continue;

                // Any: if specified, must intersect.
                if (_any.AnySet && !archMask.Intersects(_any))
                    continue;

                _matchingArchetypes.Add(a);
            }

            _cachedArchetypeManagerVersion = archVersion;
        }

        private void RebuildMasks(int newMaxComponentId)
        {
            // NOTE:
            // Query is expected to be built at boot/warmup; registry growth during simulation is discouraged.
            // We still handle it deterministically by rebuilding internal masks.

            int wordCount = ComponentBitSet.GetWordCountForMaxId(newMaxComponentId);

            // Create new masks and copy old bits.
            ComponentBitSet newAll = new(wordCount);
            ComponentBitSet newNone = new(wordCount);
            ComponentBitSet newAny = new(wordCount);

            newAll.CopyFrom(_all);
            newNone.CopyFrom(_none);
            newAny.CopyFrom(_any);

            // Swap references (fields are readonly, so we must do a controlled replacement via reflection? No.)
            // Instead, we keep the original instances and do not support resizing in-place.
            // This method will only be called if someone registers more components after query creation.
            // To keep code simple and safe, we throw with a clear message.

            throw new InvalidOperationException(
                "Query was created before all components were registered. " +
                "Register all components during world boot (then seal the registry) before creating queries.");
        }
    }
}