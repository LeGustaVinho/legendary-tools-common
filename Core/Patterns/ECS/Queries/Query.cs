using System;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Memory;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal;

namespace LegendaryTools.Common.Core.Patterns.ECS.Queries
{
    /// <summary>
    /// Describes a filter for entities based on their component types.
    /// Can include All (required), None (excluded), and Any (at least one) constraints.
    /// </summary>
    public sealed class Query
    {
        // Sorted unique stable type ids (hashed ids). Never use them as bit positions.
        internal readonly int[] All;
        internal readonly int[] None;
        internal readonly int[] Any;

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
        /// Initializes a new instance of the <see cref="Query"/> class.
        /// </summary>
        /// <param name="all">Components that MUST be present on the entity.</param>
        /// <param name="none">Components that MUST NOT be present on the entity.</param>
        public Query(ReadOnlySpan<ComponentTypeId> all, ReadOnlySpan<ComponentTypeId> none)
            : this(all, none, ReadOnlySpan<ComponentTypeId>.Empty)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Query"/> class.
        /// </summary>
        /// <param name="all">Components that MUST be present on the entity.</param>
        /// <param name="none">Components that MUST NOT be present on the entity.</param>
        /// <param name="any">Components where AT LEAST ONE must be present on the entity (if the list is not empty).</param>
        public Query(
            ReadOnlySpan<ComponentTypeId> all,
            ReadOnlySpan<ComponentTypeId> none,
            ReadOnlySpan<ComponentTypeId> any)
        {
            if (all.Length == 0)
                throw new ArgumentException("Query.All is required (must not be empty).", nameof(all));

            All = Normalize(all);
            None = Normalize(none);
            Any = Normalize(any);

            _cachedArchetypes = Array.Empty<Archetype>();
            _cachedArchetypesCount = 0;
            _cachedStructuralVersion = int.MinValue;
        }

        /// <summary>
        /// Gets or refreshes the cached list of matching archetypes.
        /// </summary>
        internal ArchetypeCache GetOrBuildCache(StorageService storage)
        {
            int version = storage.StructuralVersion;

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

        /// <summary>
        /// Checks if an archetype matches the query constraints.
        /// </summary>
        internal bool Matches(Archetype archetype)
        {
            // Match using sorted arrays (merge/binary search). This avoids any mask allocations
            // and is safe even when ComponentTypeId values are large hashes.
            int[] sig = archetype.Signature.TypeIds;

            if (!ContainsAll(sig, All)) return false;
            if (Intersects(sig, None)) return false;

            if (Any.Length > 0 && !Intersects(sig, Any)) return false;

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

        /// <summary>
        /// Returns true if <paramref name="signatureSorted"/> contains all elements in <paramref name="requiredSorted"/>.
        /// Both arrays must be sorted ascending and unique.
        /// </summary>
        private static bool ContainsAll(int[] signatureSorted, int[] requiredSorted)
        {
            if (requiredSorted.Length == 0) return true;
            if (signatureSorted.Length == 0) return false;

            int i = 0; // signature
            int j = 0; // required

            while (i < signatureSorted.Length && j < requiredSorted.Length)
            {
                int a = signatureSorted[i];
                int b = requiredSorted[j];

                if (a == b)
                {
                    i++;
                    j++;
                }
                else if (a < b)
                {
                    i++;
                }
                else
                {
                    // Required element not found.
                    return false;
                }
            }

            return j == requiredSorted.Length;
        }

        /// <summary>
        /// Returns true if <paramref name="signatureSorted"/> intersects <paramref name="probeSorted"/>.
        /// Both arrays must be sorted ascending and unique.
        /// </summary>
        private static bool Intersects(int[] signatureSorted, int[] probeSorted)
        {
            if (probeSorted.Length == 0) return false;
            if (signatureSorted.Length == 0) return false;

            int i = 0;
            int j = 0;

            while (i < signatureSorted.Length && j < probeSorted.Length)
            {
                int a = signatureSorted[i];
                int b = probeSorted[j];

                if (a == b) return true;
                if (a < b) i++;
                else j++;
            }

            return false;
        }
    }
}
