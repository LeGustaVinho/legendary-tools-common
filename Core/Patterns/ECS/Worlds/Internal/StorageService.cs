using System;
using System.Collections.Generic;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Memory;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal
{
    internal sealed class StorageService
    {
        private readonly WorldState _state;

        private readonly ComponentRegistry _componentRegistry;

        private readonly Dictionary<int, Func<int, IChunkColumn>> _typedColumnFactories;

        public StorageService(WorldState state)
        {
            _state = state;
            _componentRegistry = new ComponentRegistry();
            _typedColumnFactories = new Dictionary<int, Func<int, IChunkColumn>>(128);
        }

        public int ArchetypeVersion => _state.ArchetypeVersion;

        public void InitializeEmptyArchetype()
        {
            ArchetypeSignature emptySig = new(ReadOnlySpan<int>.Empty);
            _state.EmptyArchetype = GetOrCreateArchetype(emptySig);
        }

        public void RegisterComponent<T>() where T : struct
        {
            ComponentTypeId id = _componentRegistry.GetOrCreate<T>();
            if (_typedColumnFactories.ContainsKey(id.Value)) return;

            _typedColumnFactories.Add(id.Value, cap => new ChunkColumn<T>(cap));
        }

        public ComponentTypeId GetComponentTypeId<T>() where T : struct
        {
            return _componentRegistry.GetOrCreate<T>();
        }

        public bool Has<T>(Entity entity) where T : struct
        {
            int index = entity.Index;
            if ((uint)index >= (uint)_state.Locations.Length) return false;

            EntityLocation loc = _state.Locations[index];
            if (!loc.IsValid) return false;

            ComponentTypeId typeId = _componentRegistry.GetOrCreate<T>();
            Archetype archetype = GetArchetypeById(loc.ArchetypeId);
            return archetype.Contains(typeId);
        }

        public ref readonly T GetRO<T>(Entity entity) where T : struct
        {
            ValidateHasLocation(entity);

            ComponentTypeId typeId = _componentRegistry.GetOrCreate<T>();
            EntityLocation loc = _state.Locations[entity.Index];

            Archetype archetype = GetArchetypeById(loc.ArchetypeId);
            if (!archetype.TryGetColumnIndex(typeId, out int columnIndex))
                throw new InvalidOperationException($"Entity {entity} does not have component {typeof(T).Name}.");

            Chunk chunk = archetype.GetChunkById(loc.ChunkId);
            ChunkColumn<T> col = (ChunkColumn<T>)chunk.Columns[columnIndex];
            return ref col.Data[loc.Row];
        }

        public ref T GetRW<T>(Entity entity) where T : struct
        {
            ValidateHasLocation(entity);

            ComponentTypeId typeId = _componentRegistry.GetOrCreate<T>();
            EntityLocation loc = _state.Locations[entity.Index];

            Archetype archetype = GetArchetypeById(loc.ArchetypeId);
            if (!archetype.TryGetColumnIndex(typeId, out int columnIndex))
                throw new InvalidOperationException($"Entity {entity} does not have component {typeof(T).Name}.");

            Chunk chunk = archetype.GetChunkById(loc.ChunkId);
            ChunkColumn<T> col = (ChunkColumn<T>)chunk.Columns[columnIndex];
            return ref col.Data[loc.Row];
        }

        public EntityLocation GetLocation(Entity entity)
        {
            int index = entity.Index;
            if ((uint)index >= (uint)_state.Locations.Length) return EntityLocation.Invalid;

            return _state.Locations[index];
        }

        public void SetLocation(int entityIndex, EntityLocation loc)
        {
            _state.Locations[entityIndex] = loc;
        }

        public Archetype GetEmptyArchetype()
        {
            return _state.EmptyArchetype;
        }

        public Archetype GetOrCreateArchetype(ArchetypeSignature signature)
        {
            ulong hash = signature.ComputeStableHash64();

            if (_state.ArchetypesByHash.TryGetValue(hash, out List<Archetype> existing))
            {
                // Fast path: exact signature already present.
                for (int i = 0; i < existing.Count; i++)
                {
                    if (existing[i].Signature.Equals(signature)) return existing[i];
                }

                // Create a unique archetype id inside this hash bucket.
                ArchetypeId id = CreateUniqueArchetypeIdWithinBucket(signature, hash, existing);

                Archetype created = new(signature, id);
                existing.Add(created);

                // Deterministic iteration requirement:
                // Archetypes must be enumerated in ArchetypeId ascending order.
                existing.Sort(CompareArchetypeByIdThenSignature);

                unchecked
                {
                    _state.ArchetypeVersion++;
                }

                return created;
            }

            // New bucket.
            uint disambiguator = signature.ComputeStableHash32();
            ArchetypeId freshId = new(hash, disambiguator);

            Archetype fresh = new(signature, freshId);
            _state.ArchetypesByHash.Add(hash, new List<Archetype>(1) { fresh });

            unchecked
            {
                _state.ArchetypeVersion++;
            }

            return fresh;
        }

        public Archetype GetArchetypeById(ArchetypeId id)
        {
            if (_state.ArchetypesByHash.TryGetValue(id.Value, out List<Archetype> list))
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].ArchetypeId == id) return list[i];
                }

            throw new InvalidOperationException($"Archetype {id} was not found.");
        }

        /// <summary>
        /// Zero-allocation archetype enumeration in deterministic order:
        /// 1) primary hash ascending (SortedDictionary key),
        /// 2) ArchetypeId ascending within the bucket (list already sorted).
        /// </summary>
        public ArchetypeEnumerable EnumerateArchetypesStable()
        {
            return new ArchetypeEnumerable(_state.ArchetypesByHash);
        }

        public readonly struct ArchetypeEnumerable
        {
            private readonly SortedDictionary<ulong, List<Archetype>> _dict;

            public ArchetypeEnumerable(SortedDictionary<ulong, List<Archetype>> dict)
            {
                _dict = dict;
            }

            public Enumerator GetEnumerator()
            {
                return new Enumerator(_dict);
            }

            public struct Enumerator
            {
                private SortedDictionary<ulong, List<Archetype>>.Enumerator _dictEnumerator;
                private List<Archetype> _currentList;
                private int _listIndex;

                public Archetype Current { get; private set; }

                public Enumerator(SortedDictionary<ulong, List<Archetype>> dict)
                {
                    _dictEnumerator = dict.GetEnumerator();
                    _currentList = null;
                    _listIndex = -1;
                    Current = null;
                }

                public bool MoveNext()
                {
                    // Continue within the current list.
                    if (_currentList != null)
                    {
                        _listIndex++;
                        if (_listIndex < _currentList.Count)
                        {
                            Current = _currentList[_listIndex];
                            return true;
                        }

                        _currentList = null;
                        _listIndex = -1;
                    }

                    // Advance dictionary until a non-empty list is found.
                    while (_dictEnumerator.MoveNext())
                    {
                        List<Archetype> list = _dictEnumerator.Current.Value;
                        if (list == null || list.Count == 0) continue;

                        _currentList = list;
                        _listIndex = 0;
                        Current = _currentList[0];
                        return true;
                    }

                    Current = null;
                    return false;
                }
            }
        }

        public void PlaceInEmptyArchetype(Entity entity)
        {
            Archetype empty = _state.EmptyArchetype;

            Chunk chunk = empty.GetOrCreateChunkWithSpace(
                WorldState.DefaultChunkCapacity,
                cap => CreateColumnsForSignature(cap, empty.Signature));

            int row = chunk.AddEntity(entity);

            _state.Locations[entity.Index] = new EntityLocation
            {
                ArchetypeId = empty.ArchetypeId,
                ChunkId = chunk.ChunkId,
                Row = row
            };
        }

        public void RemoveFromStorage(Entity entity)
        {
            int index = entity.Index;

            EntityLocation loc = _state.Locations[index];
            if (!loc.IsValid) return;

            Archetype archetype = GetArchetypeById(loc.ArchetypeId);
            Chunk chunk = archetype.GetChunkById(loc.ChunkId);

            chunk.RemoveAtSwapBack(loc.Row, out Entity swappedEntity, out bool didSwap);

            if (didSwap)
                _state.Locations[swappedEntity.Index] = new EntityLocation
                {
                    ArchetypeId = loc.ArchetypeId,
                    ChunkId = loc.ChunkId,
                    Row = loc.Row
                };

            _state.Locations[index] = EntityLocation.Invalid;
        }

        public Chunk AllocateDestinationSlot(Archetype dstArchetype, Entity entity, out int dstRow)
        {
            Chunk dstChunk = dstArchetype.GetOrCreateChunkWithSpace(
                WorldState.DefaultChunkCapacity,
                cap => CreateColumnsForSignature(cap, dstArchetype.Signature));

            dstRow = dstChunk.AddEntity(entity);
            return dstChunk;
        }

        public void CopyOverlappingComponents(
            Archetype srcArchetype,
            Chunk srcChunk,
            int srcRow,
            Archetype dstArchetype,
            Chunk dstChunk,
            int dstRow)
        {
            int[] srcTypes = srcArchetype.Signature.TypeIds;

            for (int i = 0; i < srcTypes.Length; i++)
            {
                ComponentTypeId typeId = new(srcTypes[i]);

                if (dstArchetype.TryGetColumnIndex(typeId, out int dstColumnIndex))
                {
                    IChunkColumn srcCol = srcChunk.Columns[i];
                    IChunkColumn dstCol = dstChunk.Columns[dstColumnIndex];
                    srcCol.CopyElementTo(srcRow, dstCol, dstRow);
                }
            }
        }

        public void RemoveFromSourceAndFixSwap(
            Archetype srcArchetype,
            EntityLocation srcLoc,
            int removedRow)
        {
            Chunk srcChunk = srcArchetype.GetChunkById(srcLoc.ChunkId);

            srcChunk.RemoveAtSwapBack(removedRow, out Entity swappedEntity, out bool didSwap);
            if (didSwap)
                _state.Locations[swappedEntity.Index] = new EntityLocation
                {
                    ArchetypeId = srcArchetype.ArchetypeId,
                    ChunkId = srcLoc.ChunkId,
                    Row = removedRow
                };
        }

        public IChunkColumn[] CreateColumnsForSignature(int capacity, ArchetypeSignature signature)
        {
            int[] typeIds = signature.TypeIds;

            IChunkColumn[] cols = EcsArrayPool<IChunkColumn>.Rent(typeIds.Length);
            for (int i = 0; i < typeIds.Length; i++)
            {
                cols[i] = CreateTypedColumn(typeIds[i], capacity);
            }

            return cols;
        }

        private IChunkColumn CreateTypedColumn(int typeId, int capacity)
        {
            if (!_typedColumnFactories.TryGetValue(typeId, out Func<int, IChunkColumn> factory))
                throw new InvalidOperationException(
                    $"No column factory registered for ComponentTypeId {typeId}. " +
                    $"Call World.RegisterComponent<T>() for each component type used in chunks.");

            return factory(capacity);
        }

        private void ValidateHasLocation(Entity entity)
        {
            int index = entity.Index;

            if ((uint)index >= (uint)_state.Locations.Length)
                throw new InvalidOperationException($"Entity {entity} index is out of range.");

            if (!_state.Locations[index].IsValid)
                throw new InvalidOperationException($"Entity {entity} does not have a valid storage location.");
        }

        private static ArchetypeId CreateUniqueArchetypeIdWithinBucket(
            ArchetypeSignature signature,
            ulong bucketHash,
            List<Archetype> bucket)
        {
            uint dis = signature.ComputeStableHash32();

            if (!BucketContainsId(bucket, bucketHash, dis))
                return new ArchetypeId(bucketHash, dis);

            for (int attempt = 1; attempt <= 32; attempt++)
            {
                uint seed = unchecked(2166136261u ^ (uint)(attempt * 0x9E3779B9u));
                dis = signature.ComputeStableHash32(seed);

                if (!BucketContainsId(bucket, bucketHash, dis))
                    return new ArchetypeId(bucketHash, dis);
            }

            dis = signature.ComputeStableHash32();
            uint probe = dis;
            for (uint step = 1; step != 0; step++)
            {
                probe = unchecked(dis + step);
                if (!BucketContainsId(bucket, bucketHash, probe))
                    return new ArchetypeId(bucketHash, probe);
            }

            throw new InvalidOperationException("Failed to create a unique ArchetypeId within the hash bucket.");
        }

        private static bool BucketContainsId(List<Archetype> bucket, ulong bucketHash, uint disambiguator)
        {
            ArchetypeId candidate = new(bucketHash, disambiguator);

            for (int i = 0; i < bucket.Count; i++)
            {
                if (bucket[i].ArchetypeId == candidate) return true;
            }

            return false;
        }

        private static int CompareArchetypeByIdThenSignature(Archetype a, Archetype b)
        {
            int cmp = a.ArchetypeId.Value.CompareTo(b.ArchetypeId.Value);
            if (cmp != 0) return cmp;

            cmp = a.ArchetypeId.Disambiguator.CompareTo(b.ArchetypeId.Disambiguator);
            if (cmp != 0) return cmp;

            return ArchetypeSignature.CompareLexicographic(a.Signature, b.Signature);
        }
    }
}