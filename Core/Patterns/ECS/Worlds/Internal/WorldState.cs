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
        /// <summary>
        /// Default capacity used when the user does not specify a chunk capacity.
        /// </summary>
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

        public int ArchetypeVersion;

        /// <summary>
        /// Incremented whenever a structural change happens that can affect queries:
        /// - entity added/removed from chunks
        /// - entity moved between archetypes
        /// - chunk creation due to capacity
        /// - archetype creation
        /// </summary>
        public int StructuralVersion;

        // Determinism runtime toggle.
        public readonly bool Deterministic;

        public readonly int SimulationHz;
        public readonly float TickDelta;
        public int CurrentTick;
        public float PresentationDeltaTime;

        // Deterministic system order hook (used for ECB sorting).
        public int CurrentSystemOrder;

        // Bucketed storage for signature lookup (fast).
        public readonly SortedDictionary<ulong, List<Archetype>> ArchetypesByHash;

        // Stable deterministic order for queries: ArchetypeId ascending.
        public readonly SortedDictionary<ArchetypeId, Archetype> ArchetypesById;

        public Archetype EmptyArchetype;

        /// <summary>
        /// Storage behavior policies for this world.
        /// </summary>
        public readonly StoragePolicies StoragePolicies;

        /// <summary>
        /// Configured chunk capacity for the world.
        /// </summary>
        public int ChunkCapacity => StoragePolicies.ChunkCapacity;

        /// <summary>
        /// Backward-compatible constructor using default storage policies.
        /// </summary>
        public WorldState(int initialCapacity)
            : this(initialCapacity, StoragePolicies.Default, 60, deterministic: false)
        {
        }

        public WorldState(int initialCapacity, StoragePolicies storagePolicies, int simulationHz, bool deterministic)
        {
            if (initialCapacity < 1) initialCapacity = 1;
            if (simulationHz < 1) simulationHz = 1;

            StoragePolicies = storagePolicies;
            Deterministic = deterministic;

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
            StructuralVersion = 0;

            SimulationHz = simulationHz;
            TickDelta = 1.0f / simulationHz;
            CurrentTick = 0;
            PresentationDeltaTime = 0f;

            CurrentSystemOrder = 0;

            ArchetypesByHash = new SortedDictionary<ulong, List<Archetype>>();
            ArchetypesById = new SortedDictionary<ArchetypeId, Archetype>(ArchetypeIdComparer.Instance);

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

        public void IncrementStructuralVersion()
        {
            unchecked
            {
                StructuralVersion++;
            }
        }
    }
}
