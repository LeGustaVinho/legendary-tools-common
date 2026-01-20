using System;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Memory;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal;

namespace LegendaryTools.Common.Core.Patterns.ECS.Queries
{
    /// <summary>
    /// Query (MVP): All (required) + None (optional). No Any for now.
    /// Designed for hot paths: matching uses bitsets and caches matching archetypes.
    /// </summary>
    public sealed class Query
    {
        // Kept only for debugging/introspection; matching uses masks.
        internal readonly int[] All;
        internal readonly int[] None;

        // Hot path masks.
        internal readonly ulong[] AllMask;
        internal readonly ulong[] NoneMask;

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
        private int _cachedArchetypeVersion;

        public Query(ReadOnlySpan<ComponentTypeId> all, ReadOnlySpan<ComponentTypeId> none)
        {
            if (all.Length == 0)
                throw new ArgumentException("Query.All is required (must not be empty).", nameof(all));

            All = Normalize(all);
            None = Normalize(none);

            AllMask = BuildMask(All);
            NoneMask = BuildMask(None);

            _cachedArchetypes = Array.Empty<Archetype>();
            _cachedArchetypesCount = 0;
            _cachedArchetypeVersion = int.MinValue;
        }

        internal ArchetypeCache GetOrBuildCache(StorageService storage)
        {
            int version = storage.ArchetypeVersion;

            // No allocations per tick: cache rebuild happens only on structural changes (version change).
            if (_cachedArchetypeVersion == version)
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
                _cachedArchetypeVersion = version;
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
            _cachedArchetypeVersion = version;

            return new ArchetypeCache(_cachedArchetypes, _cachedArchetypesCount);
        }

        internal bool Matches(Archetype archetype)
        {
            // Fast matching by bitset:
            // - AllMask bits must be present in signature mask.
            // - NoneMask bits must be absent.
            ulong[] sig = archetype.Signature.MaskWords;

            // All
            for (int i = 0; i < AllMask.Length; i++)
            {
                ulong sigWord = (uint)i < (uint)sig.Length ? sig[i] : 0UL;
                ulong need = AllMask[i];
                if ((sigWord & need) != need) return false;
            }

            // None
            for (int i = 0; i < NoneMask.Length; i++)
            {
                ulong sigWord = (uint)i < (uint)sig.Length ? sig[i] : 0UL;
                if ((sigWord & NoneMask[i]) != 0UL) return false;
            }

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