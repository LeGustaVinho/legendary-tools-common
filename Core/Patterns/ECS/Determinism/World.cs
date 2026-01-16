#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Minimal connected ECS world (deterministic building blocks wired together).
    /// </summary>
    /// <remarks>
    /// This world supports:
    /// - Entity lifetime (EntityManager)
    /// - Stable component ids (ComponentTypeRegistry)
    /// - Archetype lookup/creation (ArchetypeManager)
    /// - SoA chunk storage (Chunk) and O(1) location mapping (EntityLocationMap)
    /// - Deterministic queries and iteration (Query)
    ///
    /// Structural changes policy:
    /// - During simulation, structural changes must be recorded via ECB and played back at fixed sync points.
    /// - Direct calls to Create/Destroy/Add/Remove are guarded while in simulation.
    /// </remarks>
    public sealed class World
    {
        private readonly int _chunkCapacity;

        private bool _inSimulation;
        private bool _inEcbPlayback;
        private int _currentTick;

        public EntityManager Entities { get; }
        public ComponentTypeRegistry Components { get; }
        public ArchetypeManager Archetypes { get; }
        public EntityLocationMap Locations { get; }

        public int CurrentTick => _currentTick;
        public bool InSimulation => _inSimulation;

        public World(
            int initialEntityCapacity = 1024,
            int initialComponentCapacity = 64,
            int initialArchetypeCapacity = 64,
            int chunkCapacity = 16_384)
        {
            if (chunkCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(chunkCapacity));

            _chunkCapacity = chunkCapacity;

            Entities = new EntityManager(initialEntityCapacity, initialEntityCapacity);
            Components = new ComponentTypeRegistry(initialComponentCapacity);
            Archetypes = new ArchetypeManager(initialArchetypeCapacity);
            Locations = new EntityLocationMap(initialEntityCapacity);

            _inSimulation = false;
            _inEcbPlayback = false;
            _currentTick = 0;

            Archetype empty = Archetypes.GetOrCreateArchetype(ComponentTypeSet.Empty);
            EnsureWritableChunk(empty);
        }

        public void BeginTick(int tick, EntityCommandBuffer? beginEcb = null)
        {
            _currentTick = tick;
            _inSimulation = true;

            beginEcb?.Playback(this);
        }

        public void EndTick(EntityCommandBuffer? endEcb = null)
        {
            endEcb?.Playback(this);
            _inSimulation = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureStructuralAllowed()
        {
            if (_inEcbPlayback)
                return;

            if (_inSimulation)
                throw new InvalidOperationException(
                    "Direct structural changes are prohibited during simulation. Record them via EntityCommandBuffer and playback at sync points.");
        }

        public Entity CreateEntity()
        {
            EnsureStructuralAllowed();

            Entity e = Entities.CreateEntity();
            Locations.EnsureCapacity(e.Index);

            Archetype empty = Archetypes.GetOrCreateArchetype(ComponentTypeSet.Empty);
            Chunk chunk = EnsureWritableChunk(empty);

            int row = chunk.AddRow(e);
            Locations.Set(e, empty.Id, chunk, row);

            return e;
        }

        public void DestroyEntity(Entity e)
        {
            EnsureStructuralAllowed();
            InternalDestroyEntityForEcb(e);
        }

        public bool Has<T>(Entity e) where T : struct
        {
            if (!Entities.IsAlive(e))
                return false;

            ComponentTypeId id = Components.GetId(typeof(T));

            return Locations.TryGetChunkAndRow(e, out Chunk chunk, out _)
                   && chunk.Signature.Contains(id);
        }

        public void Add<T>(Entity e) where T : struct
        {
            EnsureStructuralAllowed();
            ComponentTypeId id = Components.GetId(typeof(T));
            InternalAddComponentForEcb(e, id);
        }

        public void Remove<T>(Entity e) where T : struct
        {
            EnsureStructuralAllowed();
            ComponentTypeId id = Components.GetId(typeof(T));
            InternalRemoveComponentForEcb(e, id);
        }

        public ref readonly T GetRO<T>(Entity e) where T : struct
        {
            if (!Entities.IsAlive(e))
                throw new InvalidOperationException("Entity is not alive.");

            if (!Locations.TryGetChunkAndRow(e, out Chunk chunk, out int row))
                throw new InvalidOperationException("Entity has no location.");

            T[] arr = chunk.GetComponentArray<T>();
            return ref arr[row];
        }

        public ref T GetRW<T>(Entity e) where T : struct
        {
            if (!Entities.IsAlive(e))
                throw new InvalidOperationException("Entity is not alive.");

            if (!Locations.TryGetChunkAndRow(e, out Chunk chunk, out int row))
                throw new InvalidOperationException("Entity has no location.");

            T[] arr = chunk.GetComponentArray<T>();
            return ref arr[row];
        }

        public void ForEachChunk(Query query, Action<Chunk> action)
        {
            if (query is null) throw new ArgumentNullException(nameof(query));
            if (action is null) throw new ArgumentNullException(nameof(action));

            IReadOnlyList<Archetype> arches = query.GetMatchingArchetypes(this);

            for (int i = 0; i < arches.Count; i++)
            {
                Archetype a = arches[i];
                IReadOnlyList<Chunk> chunks = a.ChunkInstances;

                for (int c = 0; c < chunks.Count; c++)
                {
                    action(chunks[c]);
                }
            }
        }

        public void ForEachChunk<TProcessor>(Query query, ref TProcessor processor)
            where TProcessor : struct, IChunkProcessor
        {
            if (query is null) throw new ArgumentNullException(nameof(query));

            IReadOnlyList<Archetype> arches = query.GetMatchingArchetypes(this);

            for (int i = 0; i < arches.Count; i++)
            {
                Archetype a = arches[i];
                IReadOnlyList<Chunk> chunks = a.ChunkInstances;

                for (int c = 0; c < chunks.Count; c++)
                {
                    processor.Execute(chunks[c]);
                }
            }
        }

        public void ForEachEntity(Query query, Action<Chunk, int> actionPerRow)
        {
            if (query is null) throw new ArgumentNullException(nameof(query));
            if (actionPerRow is null) throw new ArgumentNullException(nameof(actionPerRow));

            IReadOnlyList<Archetype> arches = query.GetMatchingArchetypes(this);

            for (int i = 0; i < arches.Count; i++)
            {
                Archetype a = arches[i];
                IReadOnlyList<Chunk> chunks = a.ChunkInstances;

                for (int c = 0; c < chunks.Count; c++)
                {
                    Chunk chunk = chunks[c];
                    int count = chunk.Count;

                    for (int row = 0; row < count; row++)
                    {
                        actionPerRow(chunk, row);
                    }
                }
            }
        }

        public void ForEachEntity<TRowProcessor>(Query query, ref TRowProcessor processor)
            where TRowProcessor : struct, IRowProcessor
        {
            if (query is null) throw new ArgumentNullException(nameof(query));

            IReadOnlyList<Archetype> arches = query.GetMatchingArchetypes(this);

            for (int i = 0; i < arches.Count; i++)
            {
                Archetype a = arches[i];
                IReadOnlyList<Chunk> chunks = a.ChunkInstances;

                for (int c = 0; c < chunks.Count; c++)
                {
                    Chunk chunk = chunks[c];
                    int count = chunk.Count;

                    for (int row = 0; row < count; row++)
                    {
                        processor.Execute(chunk, row);
                    }
                }
            }
        }

        // =========================
        // ECB playback integration
        // =========================

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void BeginEcbPlayback()
        {
            _inEcbPlayback = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void EndEcbPlayback()
        {
            _inEcbPlayback = false;
        }

        internal void InternalCreateEntityForEcb(Entity requested, long sortKey)
        {
            _ = sortKey;

            // Minimal deterministic behavior:
            // - Create in playback order.
            // - 'requested' is reserved for future entity reservation.
            if (!requested.IsNull && Entities.IsAlive(requested))
                return;

            CreateEntity();
        }

        internal void InternalDestroyEntityForEcb(Entity e)
        {
            if (!Entities.IsAlive(e))
                return;

            if (Locations.TryGetChunkAndRow(e, out Chunk chunk, out int row))
            {
                Entity moved = chunk.RemoveRowSwapBack(row);
                if (moved != Entity.Null) Locations.OnSwapBackMoved(chunk, moved, row);

                Locations.Clear(e);
            }

            Entities.DestroyEntity(e);
        }

        internal void InternalAddComponentForEcb(Entity e, ComponentTypeId id)
        {
            if (!Entities.IsAlive(e))
                return;

            if (!Locations.TryGetChunkAndRow(e, out Chunk oldChunk, out int oldRow))
                throw new InvalidOperationException("Entity has no location (it may not be placed into storage).");

            if (oldChunk.Signature.Contains(id))
                return;

            ComponentTypeSet newSig = oldChunk.Signature.Add(id);
            Migrate(e, oldChunk, oldRow, newSig);
        }

        internal void InternalRemoveComponentForEcb(Entity e, ComponentTypeId id)
        {
            if (!Entities.IsAlive(e))
                return;

            if (!Locations.TryGetChunkAndRow(e, out Chunk oldChunk, out int oldRow))
                throw new InvalidOperationException("Entity has no location (it may not be placed into storage).");

            if (!oldChunk.Signature.Contains(id))
                return;

            ComponentTypeSet newSig = oldChunk.Signature.Remove(id);
            Migrate(e, oldChunk, oldRow, newSig);
        }

        // =========================
        // Migration internals
        // =========================

        private void Migrate(Entity e, Chunk oldChunk, int oldRow, in ComponentTypeSet newSignature)
        {
            Archetype newArch = Archetypes.GetOrCreateArchetype(newSignature);
            Chunk newChunk = EnsureWritableChunk(newArch);

            int newRow = newChunk.AddRow(e);

            CopyIntersectionData(oldChunk, oldRow, newChunk, newRow);

            Locations.OnMigrated(e, newChunk, newRow);

            Entity moved = oldChunk.RemoveRowSwapBack(oldRow);
            if (moved != Entity.Null) Locations.OnSwapBackMoved(oldChunk, moved, oldRow);
        }

        private void CopyIntersectionData(Chunk srcChunk, int srcRow, Chunk dstChunk, int dstRow)
        {
            ReadOnlySpan<ComponentTypeId> a = srcChunk.Signature.AsSpan();
            ReadOnlySpan<ComponentTypeId> b = dstChunk.Signature.AsSpan();

            int i = 0;
            int j = 0;

            while (i < a.Length && j < b.Length)
            {
                int av = a[i].Value;
                int bv = b[j].Value;

                if (av == bv)
                {
                    ComponentTypeId id = a[i];

                    ref readonly ComponentTypeInfo info = ref Components.GetInfo(id);
                    if ((info.Flags & ComponentTypeFlags.Data) != 0)
                        if (srcChunk.TryGetComponentArray(id, out Array srcArray) &&
                            dstChunk.TryGetComponentArray(id, out Array dstArray))
                            Array.Copy(srcArray, srcRow, dstArray, dstRow, 1);

                    i++;
                    j++;
                }
                else if (av < bv)
                {
                    i++;
                }
                else
                {
                    j++;
                }
            }
        }

        private Chunk EnsureWritableChunk(Archetype archetype)
        {
            int count = archetype.ChunkInstances.Count;
            if (count > 0)
            {
                Chunk last = archetype.ChunkInstances[count - 1];
                if (last.Count < last.Capacity)
                    return last;
            }

            return archetype.CreateChunk(Components, _chunkCapacity);
        }
    }
}