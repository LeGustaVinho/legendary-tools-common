using System;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Memory;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal;

namespace LegendaryTools.Common.Core.Patterns.ECS.Queries
{
    public sealed class Query
    {
        internal readonly int[] All;
        internal readonly int[] None;

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
            All = Normalize(all);
            None = Normalize(none);

            _cachedArchetypes = Array.Empty<Archetype>();
            _cachedArchetypesCount = 0;
            _cachedArchetypeVersion = int.MinValue;
        }

        internal ArchetypeCache GetOrBuildCache(StorageService storage)
        {
            int version = storage.ArchetypeVersion;

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
            for (int i = 0; i < All.Length; i++)
            {
                if (Array.BinarySearch(archetype.Signature.TypeIds, All[i]) < 0) return false;
            }

            for (int i = 0; i < None.Length; i++)
            {
                if (Array.BinarySearch(archetype.Signature.TypeIds, None[i]) >= 0) return false;
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
    }
}