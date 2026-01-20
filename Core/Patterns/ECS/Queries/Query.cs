using System;

using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal;

namespace LegendaryTools.Common.Core.Patterns.ECS.Queries
{
    /// <summary>
    /// A query matches archetypes by component signature.
    /// MVP: All + None only.
    /// </summary>
    public sealed class Query
    {
        internal readonly int[] All;
        internal readonly int[] None;

        private Archetype[] _cachedArchetypes;
        private int _cachedArchetypeVersion;

        /// <summary>
        /// Initializes a new instance of the <see cref="Query"/> class.
        /// Arrays are copied, sorted, and de-duplicated.
        /// </summary>
        /// <param name="all">All-of component ids.</param>
        /// <param name="none">None-of component ids.</param>
        public Query(ReadOnlySpan<ComponentTypeId> all, ReadOnlySpan<ComponentTypeId> none)
        {
            All = Normalize(all);
            None = Normalize(none);

            _cachedArchetypes = Array.Empty<Archetype>();
            _cachedArchetypeVersion = int.MinValue;
        }

        internal Archetype[] GetOrBuildCache(StorageService storage)
        {
            int version = storage.ArchetypeVersion;

            if (_cachedArchetypeVersion == version)
            {
                return _cachedArchetypes;
            }

            // Two-pass rebuild to avoid temporary lists.
            int count = 0;
            foreach (Archetype a in storage.EnumerateArchetypesStable())
            {
                if (Matches(a))
                {
                    count++;
                }
            }

            if (count == 0)
            {
                _cachedArchetypes = Array.Empty<Archetype>();
                _cachedArchetypeVersion = version;
                return _cachedArchetypes;
            }

            Archetype[] cache = new Archetype[count];
            int write = 0;

            foreach (Archetype a in storage.EnumerateArchetypesStable())
            {
                if (Matches(a))
                {
                    cache[write++] = a;
                }
            }

            _cachedArchetypes = cache;
            _cachedArchetypeVersion = version;

            return _cachedArchetypes;
        }

        internal bool Matches(Archetype archetype)
        {
            // All: must contain all.
            for (int i = 0; i < All.Length; i++)
            {
                if (Array.BinarySearch(archetype.Signature.TypeIds, All[i]) < 0)
                {
                    return false;
                }
            }

            // None: must contain none.
            for (int i = 0; i < None.Length; i++)
            {
                if (Array.BinarySearch(archetype.Signature.TypeIds, None[i]) >= 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static int[] Normalize(ReadOnlySpan<ComponentTypeId> ids)
        {
            if (ids.Length == 0)
            {
                return Array.Empty<int>();
            }

            int[] arr = new int[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                arr[i] = ids[i].Value;
            }

            Array.Sort(arr);

            // De-duplicate.
            int unique = 1;
            for (int i = 1; i < arr.Length; i++)
            {
                if (arr[i] != arr[unique - 1])
                {
                    arr[unique++] = arr[i];
                }
            }

            if (unique == arr.Length)
            {
                return arr;
            }

            int[] trimmed = new int[unique];
            Array.Copy(arr, trimmed, unique);
            return trimmed;
        }
    }
}
