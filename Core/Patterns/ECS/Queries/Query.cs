using System;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Memory;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal;

namespace LegendaryTools.Common.Core.Patterns.ECS.Queries
{
    /// <summary>
    /// Query: All (required) + None (optional) + Any (optional).
    /// Designed for hot paths: matching uses bitsets and caches matching archetypes.
    /// Cache invalidation uses World structural version (changes on archetype/chunk structural updates).
    /// </summary>
    public sealed class Query
    {
        // Kept only for debugging/introspection; matching uses masks.
        internal readonly int[] All;
        internal readonly int[] None;
        internal readonly int[] Any;

        // Hot path masks.
        internal readonly ulong[] AllMask;
        internal readonly ulong[] NoneMask;
        internal readonly ulong[] AnyMask;

        /// <summary>
        /// Prepared filter container for future extensions (e.g., enabled bits).
        /// </summary>
        internal readonly struct PreparedFilters
        {
            public readonly ulong[] All;
            public readonly ulong[] None;
            public readonly ulong[] Any;

            // Reserved for future filters (kept here to avoid API breaking later).
            public readonly ulong[] Enabled;
            public readonly ulong[] Disabled;

            public PreparedFilters(ulong[] all, ulong[] none, ulong[] any)
            {
                All = all;
                None = none;
                Any = any;

                Enabled = Array.Empty<ulong>();
                Disabled = Array.Empty<ulong>();
            }
        }

        internal readonly PreparedFilters Filters;

        internal readonly struct ArchetypeCache
        {
            public readonly Archetype[] Buffer;
            public readonly int Count;

            public ArchetypeCache(Archetype[] buffer, int count)
            {
                Buffer = buffer;
                Count = count;
            }
        }

        private Archetype[] _cachedArchetypes;
        private int _cachedArchetypesCount;
        private int _cachedStructuralVersion;

        /// <summary>
        /// Creates a query with All + None (Any is empty).
        /// </summary>
        public Query(ReadOnlySpan<ComponentTypeId> all, ReadOnlySpan<ComponentTypeId> none)
            : this(all, none, ReadOnlySpan<ComponentTypeId>.Empty)
        {
        }

        /// <summary>
        /// Creates a query with All + None + Any.
        /// </summary>
        public Query(ReadOnlySpan<ComponentTypeId> all, ReadOnlySpan<ComponentTypeId> none,
            ReadOnlySpan<ComponentTypeId> any)
        {
            if (all.Length == 0)
                throw new ArgumentException("Query.All is required (must not be empty).", nameof(all));

            All = Normalize(all);
            None = Normalize(none);
            Any = Normalize(any);

            AllMask = BuildMask(All);
            NoneMask = BuildMask(None);
            AnyMask = BuildMask(Any);

            Filters = new PreparedFilters(AllMask, NoneMask, AnyMask);

            _cachedArchetypes = Array.Empty<Archetype>();
            _cachedArchetypesCount = 0;
            _cachedStructuralVersion = int.MinValue;
        }

        internal ArchetypeCache GetOrBuildCache(StorageService storage)
        {
            int version = storage.StructuralVersion;

            // No allocations per tick: cache rebuild happens only on structural changes (version change).
            if (_cachedStructuralVersion == version)
                return new ArchetypeCache(_cachedArchetypes, _cachedArchetypesCount);

            int count = 0;
            foreach (Archetype a in storage.EnumerateArchetypesStable())
            {
                if (Matches(a)) count++;
            }

            if (count == 0)
            {
                ReturnCached();
                _cachedArchetypes = Array.Empty<Archetype>();
                _cachedArchetypesCount = 0;
                _cachedStructuralVersion = version;
                return new ArchetypeCache(_cachedArchetypes, 0);
            }

            Archetype[] newBuf = EcsArrayPool<Archetype>.Rent(count);
            int write = 0;

            foreach (Archetype a in storage.EnumerateArchetypesStable())
            {
                if (Matches(a)) newBuf[write++] = a;
            }

            ReturnCached();

            _cachedArchetypes = newBuf;
            _cachedArchetypesCount = write;
            _cachedStructuralVersion = version;

            return new ArchetypeCache(_cachedArchetypes, _cachedArchetypesCount);
        }

        internal bool Matches(Archetype archetype)
        {
            // Fast matching by bitset:
            // - AllMask bits must be present in signature mask.
            // - NoneMask bits must be absent.
            // - If AnyMask is not empty: at least one Any bit must be present.
            ulong[] sig = archetype.Signature.MaskWords;

            // All
            ulong[] all = Filters.All;
            for (int i = 0; i < all.Length; i++)
            {
                ulong sigWord = (uint)i < (uint)sig.Length ? sig[i] : 0UL;
                ulong need = all[i];
                if ((sigWord & need) != need) return false;
            }

            // None
            ulong[] none = Filters.None;
            for (int i = 0; i < none.Length; i++)
            {
                ulong sigWord = (uint)i < (uint)sig.Length ? sig[i] : 0UL;
                if ((sigWord & none[i]) != 0UL) return false;
            }

            // Any (optional)
            ulong[] any = Filters.Any;
            if (any.Length > 0)
            {
                bool anyHit = false;

                for (int i = 0; i < any.Length; i++)
                {
                    ulong sigWord = (uint)i < (uint)sig.Length ? sig[i] : 0UL;
                    if ((sigWord & any[i]) != 0UL)
                    {
                        anyHit = true;
                        break;
                    }
                }

                if (!anyHit) return false;
            }

            // Future filters (Enabled/Disabled) intentionally not applied yet.

            return true;
        }

        private void ReturnCached()
        {
            if (_cachedArchetypes != null && _cachedArchetypes.Length > 0)
                EcsArrayPool<Archetype>.Return(_cachedArchetypes, true);
        }

        private static int[] Normalize(ReadOnlySpan<ComponentTypeId> ids)
        {
            if (ids.Length == 0) return Array.Empty<int>();

            int[] arr = new int[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                arr[i] = ids[i].Value;
            }

            Array.Sort(arr);

            int unique = 1;
            for (int i = 1; i < arr.Length; i++)
            {
                if (arr[i] != arr[unique - 1]) arr[unique++] = arr[i];
            }

            if (unique == arr.Length) return arr;

            int[] trimmed = new int[unique];
            Array.Copy(arr, trimmed, unique);
            return trimmed;
        }

        private static ulong[] BuildMask(int[] sortedUniqueTypeIds)
        {
            if (sortedUniqueTypeIds.Length == 0) return Array.Empty<ulong>();

            int maxId = sortedUniqueTypeIds[sortedUniqueTypeIds.Length - 1];
            if (maxId < 0) return Array.Empty<ulong>();

            int words = (maxId >> 6) + 1;
            ulong[] mask = new ulong[words];

            for (int i = 0; i < sortedUniqueTypeIds.Length; i++)
            {
                int id = sortedUniqueTypeIds[i];
                if (id < 0) continue;

                int w = id >> 6;
                int b = id & 63;
                mask[w] |= 1UL << b;
            }

            return mask;
        }
    }
}