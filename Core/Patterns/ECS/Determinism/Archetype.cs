#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Represents an archetype: a unique ordered component signature and its associated chunk instances.
    /// </summary>
    /// <remarks>
    /// Important invariants (determinism + correctness):
    /// - Chunk ids are allocated monotonically per archetype (deterministic within that archetype).
    /// - There is a single source of truth for chunks: <see cref="_chunkInstances"/>.
    /// - A chunk id is never created without a corresponding chunk instance (prevents divergence).
    /// </remarks>
    public sealed class Archetype
    {
        private readonly List<Chunk> _chunkInstances;
        private int _nextChunkId;

        // Eagerly created to avoid first-use allocations during add/remove loops.
        private readonly Dictionary<int, Archetype> _addTransitions; // componentId -> archetype
        private readonly Dictionary<int, Archetype> _removeTransitions; // componentId -> archetype

        // Query/matching helpers.
        private ComponentBitSet? _mask;
        private int _maskMaxComponentId;

        public ArchetypeId Id { get; }

        public ComponentTypeSet Signature { get; }

        /// <summary>
        /// Chunk instances in deterministic creation order (append-only).
        /// </summary>
        public IReadOnlyList<Chunk> ChunkInstances => _chunkInstances;

        /// <summary>
        /// Gets the number of chunk instances.
        /// </summary>
        public int ChunkCount => _chunkInstances.Count;

        internal Archetype(in ComponentTypeSet signature)
        {
            Signature = signature;
            Id = ArchetypeIdFactory.FromSignature(signature);

            _chunkInstances = new List<Chunk>(4);
            _nextChunkId = 1;

            _addTransitions = new Dictionary<int, Archetype>(4);
            _removeTransitions = new Dictionary<int, Archetype>(4);

            _mask = null;
            _maskMaxComponentId = 0;
        }

        /// <summary>
        /// Creates a new chunk instance for this archetype and returns it.
        /// </summary>
        /// <remarks>
        /// This is the only supported way to create chunks to guarantee that chunk ids never diverge
        /// from the actual chunk instances.
        /// </remarks>
        public Chunk CreateChunk(ComponentTypeRegistry registry, int capacity)
        {
            if (registry is null) throw new ArgumentNullException(nameof(registry));

            ChunkId id = AllocateChunkId();

            Chunk chunk = new(id, this, registry, capacity);
            _chunkInstances.Add(chunk);

            return chunk;
        }

        /// <summary>
        /// Returns the chunk id at a given index without maintaining a separate id list.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ChunkId GetChunkIdAt(int index)
        {
            return _chunkInstances[index].Id;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ChunkId AllocateChunkId()
        {
            int id = _nextChunkId++;
            return new ChunkId(id);
        }

        /// <summary>
        /// Gets (or creates and caches) the destination archetype for adding a component to this archetype.
        /// </summary>
        internal Archetype GetOrCreateAddTransition(ComponentTypeId componentId, ArchetypeManager manager)
        {
            int key = componentId.Value;
            if (_addTransitions.TryGetValue(key, out Archetype dst))
                return dst;

            ComponentTypeSet newSig = Signature.Add(componentId);
            dst = manager.GetOrCreateArchetype(in newSig);

            _addTransitions.Add(key, dst);
            return dst;
        }

        /// <summary>
        /// Gets (or creates and caches) the destination archetype for removing a component from this archetype.
        /// </summary>
        internal Archetype GetOrCreateRemoveTransition(ComponentTypeId componentId, ArchetypeManager manager)
        {
            int key = componentId.Value;
            if (_removeTransitions.TryGetValue(key, out Archetype dst))
                return dst;

            ComponentTypeSet newSig = Signature.Remove(componentId);
            dst = manager.GetOrCreateArchetype(in newSig);

            _removeTransitions.Add(key, dst);
            return dst;
        }

        /// <summary>
        /// Gets a deterministic component mask (bitset) for fast query matching.
        /// </summary>
        /// <remarks>
        /// The mask is cached per archetype. If new component types are registered later (max id increases),
        /// the mask is rebuilt to match the new size.
        /// </remarks>
        internal ComponentBitSet GetOrBuildMask(int maxComponentId)
        {
            if (_mask is not null && _maskMaxComponentId >= maxComponentId)
                return _mask;

            int wordCount = ComponentBitSet.GetWordCountForMaxId(maxComponentId);
            ComponentBitSet mask = new(wordCount);

            ReadOnlySpan<ComponentTypeId> ids = Signature.AsSpan();
            for (int i = 0; i < ids.Length; i++)
            {
                mask.Set(ids[i].Value);
            }

            _mask = mask;
            _maskMaxComponentId = maxComponentId;
            return mask;
        }

        public override string ToString()
        {
            return $"Archetype(Id={Id}, Count={Signature.Count}, Chunks={_chunkInstances.Count})";
        }
    }
}