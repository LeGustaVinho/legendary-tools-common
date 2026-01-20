using System;
using System.Collections.Generic;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal
{
    /// <summary>
    /// Holds all mutable world state in one place so services can stay small and focused.
    /// </summary>
    internal sealed class WorldState
    {
        public const int DefaultChunkCapacity = 128;

        public int[] Versions;
        public bool[] Alive;

        public int[] FreeList;
        public int FreeCount;

        public int NextIndex;

        public EntityLocation[] Locations;

        public int IterationDepth;

        /// <summary>
        /// Gets or sets a value indicating whether the world is currently inside a tick update scope.
        /// When true, structural changes must be routed through an ECB.
        /// </summary>
        public bool IsUpdating;

        /// <summary>
        /// Gets or sets the archetype version. Increment whenever archetype set changes.
        /// Used to invalidate query caches.
        /// </summary>
        public int ArchetypeVersion;

        public readonly SortedDictionary<ulong, List<Archetype>> ArchetypesByHash;

        public Archetype EmptyArchetype;

        public WorldState(int initialCapacity)
        {
            if (initialCapacity < 1) initialCapacity = 1;

            Versions = new int[initialCapacity];
            Alive = new bool[initialCapacity];

            FreeList = new int[Math.Max(16, initialCapacity / 4)];
            FreeCount = 0;

            NextIndex = 0;

            Locations = new EntityLocation[initialCapacity];
            for (int i = 0; i < Locations.Length; i++)
            {
                Locations[i] = EntityLocation.Invalid;
            }

            IterationDepth = 0;
            IsUpdating = false;
            ArchetypeVersion = 0;

            ArchetypesByHash = new SortedDictionary<ulong, List<Archetype>>();
            EmptyArchetype = null;
        }

        public void EnsureEntityCapacity(int required)
        {
            if (required <= Alive.Length) return;

            int newSize = Alive.Length;
            while (newSize < required)
            {
                newSize = newSize < 1024 ? newSize * 2 : newSize + newSize / 2;
            }

            Array.Resize(ref Versions, newSize);
            Array.Resize(ref Alive, newSize);

            int oldLocLen = Locations.Length;
            Array.Resize(ref Locations, newSize);

            for (int i = oldLocLen; i < Locations.Length; i++)
            {
                Locations[i] = EntityLocation.Invalid;
            }
        }

        public void PushFreeIndex(int index)
        {
            if (FreeCount >= FreeList.Length)
            {
                int newSize = FreeList.Length * 2;
                if (newSize < 16) newSize = 16;

                Array.Resize(ref FreeList, newSize);
            }

            FreeList[FreeCount++] = index;
        }

        public bool TryPopFreeIndex(out int index)
        {
            if (FreeCount > 0)
            {
                index = FreeList[--FreeCount];
                return true;
            }

            index = -1;
            return false;
        }
    }
}