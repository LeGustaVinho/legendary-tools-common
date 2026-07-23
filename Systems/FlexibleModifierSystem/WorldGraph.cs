using System;
using System.Collections.Generic;
using System.Linq;

namespace LegendaryTools.ModifierSystem
{
    public sealed class TagDefinition<TEntity> where TEntity : WorldEntity
    {
        public StableId<TagIdKind> Id { get; }
        public TagDefinition(string id) => Id = new StableId<TagIdKind>(id);
    }

    public sealed class ComponentDefinition<TEntity, TValue> where TEntity : WorldEntity
    {
        public StableId<ComponentIdKind> Id { get; }
        public ComponentDefinition(string id) => Id = new StableId<ComponentIdKind>(id);
    }

    public abstract class WorldEntity
    {
        private List<IAttributeSlot> _attributeSlots;
        private List<IAttributeDefinition> _ownedAttributeDefinitions;
        private HashSet<object> _tags;
        private Dictionary<object, object> _components;

        public EntityId Id { get; private set; }
        public SimulationWorld World { get; private set; }
        public IReadOnlyCollection<IAttributeDefinition> AttributeDefinitions =>
            _ownedAttributeDefinitions == null
                ? (IReadOnlyCollection<IAttributeDefinition>)Array.Empty<IAttributeDefinition>()
                : _ownedAttributeDefinitions.AsReadOnly();

        internal void Bind(SimulationWorld world, EntityId id)
        {
            if (World != null) throw new InvalidOperationException("Entity is already attached to a world.");
            World = world ?? throw new ArgumentNullException(nameof(world));
            Id = id;
        }

        protected GameAttribute<TEntity, TValue> AddAttribute<TEntity, TValue>(
            AttributeDefinition<TEntity, TValue> definition, TValue baseValue) where TEntity : WorldEntity
        {
            if (!(this is TEntity typed))
                throw new InvalidOperationException($"{GetType().Name} cannot own {definition.Id}.");
            if (World == null) throw new InvalidOperationException("Attach the entity before defining attributes.");
            World.RegisterAttribute(definition);
            int slotIndex = World.GetAttributeSlotIndex(definition);
            if (_attributeSlots == null) _attributeSlots = new List<IAttributeSlot>();
            while (_attributeSlots.Count <= slotIndex) _attributeSlots.Add(null);
            if (_attributeSlots[slotIndex] != null)
                throw new InvalidOperationException($"{definition.Id} already exists on {Id}.");
            var attribute = new GameAttribute<TEntity, TValue>(typed, definition, baseValue);
            _attributeSlots[slotIndex] = attribute;
            if (_ownedAttributeDefinitions == null)
                _ownedAttributeDefinitions = new List<IAttributeDefinition>();
            _ownedAttributeDefinitions.Add(definition);
            World.NotifyStructureChanged();
            return attribute;
        }

        public GameAttribute<TEntity, TValue> GetAttribute<TEntity, TValue>(
            AttributeDefinition<TEntity, TValue> definition) where TEntity : WorldEntity
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (World == null || _attributeSlots == null ||
                !World.TryGetAttributeSlotIndex(definition, out int slotIndex) ||
                slotIndex >= _attributeSlots.Count) return null;
            return _attributeSlots[slotIndex] as GameAttribute<TEntity, TValue>;
        }

        public IReadOnlyList<TTarget> Related<TFrom, TTarget>(RelationDefinition<TFrom, TTarget> relation)
            where TFrom : WorldEntity where TTarget : WorldEntity
        {
            if (!(this is TFrom from)) throw new InvalidOperationException($"{GetType().Name} is not {typeof(TFrom).Name}.");
            return World.Related(from, relation);
        }

        public bool IsRelatedTo<TFrom, TTarget>(RelationDefinition<TFrom, TTarget> relation, TTarget target)
            where TFrom : WorldEntity where TTarget : WorldEntity
        {
            if (!(this is TFrom from)) throw new InvalidOperationException($"{GetType().Name} is not {typeof(TFrom).Name}.");
            return World.HasRelation(from, relation, target);
        }

        public bool HasTag<TEntity>(TagDefinition<TEntity> definition) where TEntity : WorldEntity
        {
            if (!(this is TEntity)) return false;
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            return _tags != null && _tags.Contains(definition);
        }

        public bool TryGetComponent<TEntity, TValue>(ComponentDefinition<TEntity, TValue> definition,
            out TValue value) where TEntity : WorldEntity
        {
            if (!(this is TEntity)) throw new InvalidOperationException($"{GetType().Name} is not {typeof(TEntity).Name}.");
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (_components != null && _components.TryGetValue(definition, out object boxed))
            {
                value = (TValue)boxed;
                return true;
            }
            value = default;
            return false;
        }

        public TValue GetComponent<TEntity, TValue>(ComponentDefinition<TEntity, TValue> definition)
            where TEntity : WorldEntity
        {
            if (!TryGetComponent(definition, out TValue value))
                throw new KeyNotFoundException($"Component {definition.Id} is not present on {Id}.");
            return value;
        }

        internal bool TryGetSlot(IAttributeDefinition definition, out IAttributeSlot slot) =>
            TryGetSlotByIndex(World.GetAttributeSlotIndex(definition), out slot);

        private bool TryGetSlotByIndex(int slotIndex, out IAttributeSlot slot)
        {
            slot = _attributeSlots != null && slotIndex >= 0 && slotIndex < _attributeSlots.Count
                ? _attributeSlots[slotIndex] : null;
            return slot != null;
        }

        internal IEnumerable<IAttributeSlot> Slots =>
            _attributeSlots == null ? Enumerable.Empty<IAttributeSlot>() : _attributeSlots.Where(item => item != null);
        internal bool AddTag(object definition)
        {
            if (_tags == null) _tags = new HashSet<object>();
            return _tags.Add(definition);
        }
        internal bool RemoveTag(object definition) => _tags != null && _tags.Remove(definition);
        internal bool SetComponent(object definition, object value)
        {
            if (_components == null) _components = new Dictionary<object, object>();
            if (_components.TryGetValue(definition, out object previous) && Equals(previous, value)) return false;
            _components[definition] = value;
            return true;
        }
        internal bool RemoveComponent(object definition) => _components != null && _components.Remove(definition);
    }

    internal interface IRelationDefinition
    {
        string StableId { get; }
    }

    public sealed class RelationDefinition<TFrom, TTo> : IRelationDefinition
        where TFrom : WorldEntity where TTo : WorldEntity
    {
        public StableId<RelationIdKind> Id { get; }
        public int MaximumFromCount { get; }
        public int MaximumToCount { get; }
        public bool AllowSelfRelation { get; }
        string IRelationDefinition.StableId => Id.Value;

        public RelationDefinition(string id, int maximumFromCount = 0, int maximumToCount = 0,
            bool allowSelfRelation = false)
        {
            if (maximumFromCount < 0) throw new ArgumentOutOfRangeException(nameof(maximumFromCount));
            if (maximumToCount < 0) throw new ArgumentOutOfRangeException(nameof(maximumToCount));
            Id = new StableId<RelationIdKind>(id);
            MaximumFromCount = maximumFromCount;
            MaximumToCount = maximumToCount;
            AllowSelfRelation = allowSelfRelation;
        }
    }

    internal interface IRelationStore
    {
        bool RemoveEntity(EntityId id);
    }

    internal sealed class RelationStore<TFrom, TTo> : IRelationStore
        where TFrom : WorldEntity where TTo : WorldEntity
    {
        private readonly Dictionary<EntityId, SortedSet<EntityId>> _outgoing =
            new Dictionary<EntityId, SortedSet<EntityId>>();
        private readonly Dictionary<EntityId, SortedSet<EntityId>> _incoming =
            new Dictionary<EntityId, SortedSet<EntityId>>();

        public bool Add(RelationDefinition<TFrom, TTo> definition, TFrom from, TTo to)
        {
            if (!definition.AllowSelfRelation && from.Id == to.Id)
                throw new InvalidOperationException($"Relation {definition.Id} does not allow self edges.");
            if (!_outgoing.TryGetValue(from.Id, out SortedSet<EntityId> targets))
                _outgoing.Add(from.Id, targets = new SortedSet<EntityId>());
            if (targets.Contains(to.Id)) return false;
            if (definition.MaximumFromCount > 0 && targets.Count >= definition.MaximumFromCount)
                throw new InvalidOperationException($"Relation {definition.Id} outbound cardinality exceeded.");
            if (!_incoming.TryGetValue(to.Id, out SortedSet<EntityId> sources))
                _incoming.Add(to.Id, sources = new SortedSet<EntityId>());
            if (definition.MaximumToCount > 0 && sources.Count >= definition.MaximumToCount)
                throw new InvalidOperationException($"Relation {definition.Id} inbound cardinality exceeded.");
            targets.Add(to.Id);
            sources.Add(from.Id);
            return true;
        }

        public bool Remove(TFrom from, TTo to)
        {
            if (!_outgoing.TryGetValue(from.Id, out SortedSet<EntityId> targets) || !targets.Remove(to.Id)) return false;
            if (targets.Count == 0) _outgoing.Remove(from.Id);
            if (_incoming.TryGetValue(to.Id, out SortedSet<EntityId> sources))
            {
                sources.Remove(from.Id);
                if (sources.Count == 0) _incoming.Remove(to.Id);
            }
            return true;
        }

        public IEnumerable<EntityId> Outgoing(EntityId id) =>
            _outgoing.TryGetValue(id, out SortedSet<EntityId> values) ? values : Enumerable.Empty<EntityId>();

        public IEnumerable<EntityId> Incoming(EntityId id) =>
            _incoming.TryGetValue(id, out SortedSet<EntityId> values) ? values : Enumerable.Empty<EntityId>();

        public bool Contains(EntityId from, EntityId to) =>
            _outgoing.TryGetValue(from, out SortedSet<EntityId> values) && values.Contains(to);

        public bool RemoveEntity(EntityId id)
        {
            bool changed = false;
            if (_outgoing.TryGetValue(id, out SortedSet<EntityId> targets))
            {
                foreach (EntityId target in targets)
                    if (_incoming.TryGetValue(target, out SortedSet<EntityId> sources)) sources.Remove(id);
                _outgoing.Remove(id);
                changed = true;
            }
            if (_incoming.TryGetValue(id, out SortedSet<EntityId> incoming))
            {
                foreach (EntityId source in incoming)
                    if (_outgoing.TryGetValue(source, out SortedSet<EntityId> values)) values.Remove(id);
                _incoming.Remove(id);
                changed = true;
            }
            return changed;
        }
    }

    public sealed partial class SimulationWorld
    {
        private readonly List<WorldEntity> _entitySlots = new List<WorldEntity> { null };
        private readonly Dictionary<Type, List<EntityId>> _entitiesByExactType =
            new Dictionary<Type, List<EntityId>>();
        private readonly EntityCollection _entityCollection;
        private readonly Dictionary<object, IRelationStore> _relations = new Dictionary<object, IRelationStore>();
        private readonly Dictionary<string, object> _structuralDefinitions =
            new Dictionary<string, object>(StringComparer.Ordinal);
        private readonly Dictionary<string, IAttributeDefinition> _attributeDefinitions =
            new Dictionary<string, IAttributeDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<IAttributeDefinition, int> _attributeSlotIndexes =
            new Dictionary<IAttributeDefinition, int>();
        private readonly Dictionary<IAttributeDefinition, List<IAttributeDefinition>> _dependents =
            new Dictionary<IAttributeDefinition, List<IAttributeDefinition>>();
        private readonly Dictionary<IAttributeDefinition, List<IAttributeDefinition>> _globalDependents =
            new Dictionary<IAttributeDefinition, List<IAttributeDefinition>>();
        private readonly Dictionary<object, List<IAttributeDefinition>> _relationAttributeDependents =
            new Dictionary<object, List<IAttributeDefinition>>();
        private readonly Dictionary<IAttributeDefinition, long> _attributeQueryVersions =
            new Dictionary<IAttributeDefinition, long>();
        private readonly Dictionary<IAttributeDefinition, Dictionary<EntityId, long>> _entityAttributeQueryVersions =
            new Dictionary<IAttributeDefinition, Dictionary<EntityId, long>>();
        private long _nextEntityId = 1;
        private int _liveEntityCount;
        private int _mutationBatchDepth;
        private int _versionPublicationDepth;
        private bool _versionPublicationPending;
        private bool _batchedVersionChange;
        private bool _processingMutationBatch;
        private readonly HashSet<QueryDependencyKey> _pendingQueryDependencyChanges =
            new HashSet<QueryDependencyKey>();
        private readonly HashSet<Tuple<WorldEntity, IAttributeDefinition>> _batchedAttributeChanges =
            new HashSet<Tuple<WorldEntity, IAttributeDefinition>>();
        private readonly HashSet<object> _batchedRelationChanges = new HashSet<object>();
        private readonly HashSet<Tuple<object, EntityId>> _batchedRelationSourceChanges =
            new HashSet<Tuple<object, EntityId>>();
        private bool _batchedFullStructureChange;
        private EntityId? _currentChangedRelationSource;
        private readonly Dictionary<object, long> _relationQueryVersions = new Dictionary<object, long>();
        private readonly Dictionary<object, Dictionary<EntityId, long>> _relationSourceQueryVersions =
            new Dictionary<object, Dictionary<EntityId, long>>();
        private long _structureQueryVersion;
        private readonly ulong _randomSeed;
        private readonly Dictionary<StableId<RandomStreamIdKind>, XorShiftRandom> _randomStreams =
            new Dictionary<StableId<RandomStreamIdKind>, XorShiftRandom>();
        public static StableId<RandomStreamIdKind> DefaultRandomStreamId { get; } =
            new StableId<RandomStreamIdKind>("simulation.default");

        public long CurrentTick { get; private set; }
        public long Version { get; private set; }
        public long StructureQueryVersion => _structureQueryVersion;
        public IReadOnlyCollection<WorldEntity> Entities => _entityCollection;
        public XorShiftRandom Random { get; }

        public SimulationWorld(ulong randomSeed = 1)
        {
            _randomSeed = randomSeed;
            _entityCollection = new EntityCollection(_entitySlots, () => _liveEntityCount);
            Random = new XorShiftRandom(randomSeed);
            _randomStreams.Add(DefaultRandomStreamId, Random);
            Variables = new TypedVariableStore(AdvanceVersion,
                id => Get<WorldEntity>(id) != null);
        }

        public XorShiftRandom GetRandomStream(StableId<RandomStreamIdKind> id)
        {
            if (_randomStreams.TryGetValue(id, out XorShiftRandom stream)) return stream;
            ulong hash = 14695981039346656037UL;
            foreach (char character in id.Value)
            {
                hash ^= character;
                hash *= 1099511628211UL;
            }
            stream = new XorShiftRandom(hash ^ _randomSeed);
            _randomStreams.Add(id, stream);
            return stream;
        }

        public IDisposable BeginMutationBatch()
        {
            if (_mutationBatchDepth == 0) BeginVersionPublication();
            _mutationBatchDepth++;
            return new MutationBatch(this);
        }

        public TEntity Create<TEntity>(Action<TEntity> initialize = null) where TEntity : WorldEntity, new()
        {
            if (_mutationBatchDepth > 0) return CreateCore(initialize);
            using (BeginMutationBatch()) return CreateCore(initialize);
        }

        private TEntity CreateCore<TEntity>(Action<TEntity> initialize) where TEntity : WorldEntity, new()
        {
            var entity = new TEntity();
            entity.Bind(this, new EntityId(_nextEntityId++));
            if (entity.Id.Value > int.MaxValue)
                throw new InvalidOperationException("The managed entity slot backend supports up to Int32.MaxValue IDs.");
            while (_entitySlots.Count <= entity.Id.Value) _entitySlots.Add(null);
            _entitySlots[(int)entity.Id.Value] = entity;
            _liveEntityCount++;
            Type entityType = entity.GetType();
            if (!_entitiesByExactType.TryGetValue(entityType, out List<EntityId> typeIds))
                _entitiesByExactType.Add(entityType, typeIds = new List<EntityId>());
            typeIds.Add(entity.Id);
            try
            {
                initialize?.Invoke(entity);
                NotifyStructureChanged();
                return entity;
            }
            catch
            {
                _entitySlots[(int)entity.Id.Value] = null;
                _liveEntityCount--;
                typeIds.Remove(entity.Id);
                foreach (IRelationStore relation in _relations.Values) relation.RemoveEntity(entity.Id);
                RemoveModifiersOwnedByOrTargeting(entity.Id);
                RemoveRuntimeStateOwnedByOrReferencing(entity.Id);
                throw;
            }
        }

        public bool Destroy(WorldEntity entity)
        {
            if (_mutationBatchDepth > 0) return DestroyCore(entity);
            using (BeginMutationBatch()) return DestroyCore(entity);
        }

        private bool DestroyCore(WorldEntity entity)
        {
            RequireOwned(entity);
            if (entity.Id.Value >= _entitySlots.Count ||
                !ReferenceEquals(_entitySlots[(int)entity.Id.Value], entity)) return false;
            _entitySlots[(int)entity.Id.Value] = null;
            _liveEntityCount--;
            foreach (IRelationStore relation in _relations.Values) relation.RemoveEntity(entity.Id);
            foreach (Dictionary<EntityId, long> versions in _relationSourceQueryVersions.Values)
                versions.Remove(entity.Id);
            foreach (Dictionary<EntityId, long> versions in _entityAttributeQueryVersions.Values)
                versions.Remove(entity.Id);
            RemoveModifiersOwnedByOrTargeting(entity.Id);
            RemoveRuntimeStateOwnedByOrReferencing(entity.Id);
            NotifyStructureChanged();
            return true;
        }

        public TEntity Get<TEntity>(EntityId id) where TEntity : WorldEntity
        {
            if (id.Value <= 0 || id.Value >= _entitySlots.Count) return null;
            return _entitySlots[(int)id.Value] as TEntity;
        }

        public IReadOnlyList<TEntity> All<TEntity>() where TEntity : WorldEntity
        {
            Type requested = typeof(TEntity);
            var result = new List<TEntity>();
            if (requested.IsSealed && _entitiesByExactType.TryGetValue(requested, out List<EntityId> exact))
            {
                foreach (EntityId id in exact)
                {
                    TEntity entity = Get<TEntity>(id);
                    if (entity != null) result.Add(entity);
                }
                return result.AsReadOnly();
            }
            foreach (WorldEntity entity in _entitySlots)
                if (entity is TEntity typed) result.Add(typed);
            return result.AsReadOnly();
        }

        public void RegisterAttribute(IAttributeDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (definition.IsDerived && definition.Dependencies.Count == 0 &&
                definition.GlobalDependencies.Count == 0 && definition.RelationDependencies.Count == 0)
                throw new InvalidOperationException(
                    $"Derived attribute {definition.Id} must declare at least one dependency.");
            if (_attributeDefinitions.TryGetValue(definition.Id.Value, out IAttributeDefinition existing))
            {
                if (!ReferenceEquals(existing, definition))
                    throw new InvalidOperationException($"Attribute ID {definition.Id} is already registered.");
                return;
            }

            ValidateAttributeRegistration(definition);
            _attributeDefinitions.Add(definition.Id.Value, definition);
            _attributeSlotIndexes.Add(definition, _attributeSlotIndexes.Count);
            foreach (IAttributeDefinition dependency in definition.Dependencies)
            {
                if (!_dependents.TryGetValue(dependency, out List<IAttributeDefinition> list))
                    _dependents.Add(dependency, list = new List<IAttributeDefinition>());
                list.Add(definition);
            }
            foreach (IAttributeDefinition dependency in definition.GlobalDependencies)
            {
                if (!_globalDependents.TryGetValue(dependency, out List<IAttributeDefinition> list))
                    _globalDependents.Add(dependency, list = new List<IAttributeDefinition>());
                list.Add(definition);
            }
            foreach (object relation in definition.RelationDependencies)
            {
                if (!_relationAttributeDependents.TryGetValue(relation, out List<IAttributeDefinition> list))
                    _relationAttributeDependents.Add(relation, list = new List<IAttributeDefinition>());
                list.Add(definition);
            }
            if (definition is IFreezableAttributeDefinition freezable) freezable.Freeze();
        }

        private void ValidateAttributeRegistration(IAttributeDefinition definition)
        {
            foreach (IAttributeDefinition dependency in definition.Dependencies)
                if (dependency.EntityType != definition.EntityType)
                    throw new InvalidOperationException(
                        $"Derived attribute {definition.Id} has a dependency owned by another entity type.");

            var visiting = new HashSet<IAttributeDefinition>();
            var visited = new HashSet<IAttributeDefinition>();
            Visit(definition, visiting, visited);
            foreach (IAttributeDefinition registered in _attributeDefinitions.Values)
                Visit(registered, visiting, visited);
        }

        internal int GetAttributeSlotIndex(IAttributeDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (!_attributeSlotIndexes.TryGetValue(definition, out int index))
                throw new InvalidOperationException($"Attribute {definition.Id} is not registered in this world.");
            return index;
        }

        internal bool TryGetAttributeSlotIndex(IAttributeDefinition definition, out int index)
        {
            if (definition != null && _attributeSlotIndexes.TryGetValue(definition, out index)) return true;
            index = -1;
            return false;
        }

        public void AddRelation<TFrom, TTo>(TFrom from, RelationDefinition<TFrom, TTo> definition, TTo to)
            where TFrom : WorldEntity where TTo : WorldEntity
        {
            RequireOwned(from);
            RequireOwned(to);
            RelationStore<TFrom, TTo> store = GetRelationStore(definition);
            if (store.Add(definition, from, to)) NotifyStructureChanged(definition, from.Id);
        }

        public bool RemoveRelation<TFrom, TTo>(TFrom from, RelationDefinition<TFrom, TTo> definition, TTo to)
            where TFrom : WorldEntity where TTo : WorldEntity
        {
            RequireOwned(from);
            RequireOwned(to);
            bool removed = GetRelationStore(definition).Remove(from, to);
            if (removed) NotifyStructureChanged(definition, from.Id);
            return removed;
        }

        public IReadOnlyList<TTo> Related<TFrom, TTo>(TFrom from, RelationDefinition<TFrom, TTo> definition)
            where TFrom : WorldEntity where TTo : WorldEntity
        {
            RequireOwned(from);
            return GetRelationStore(definition).Outgoing(from.Id).Select(Get<TTo>).Where(item => item != null).ToArray();
        }

        public IReadOnlyList<TFrom> RelatedFrom<TFrom, TTo>(TTo to, RelationDefinition<TFrom, TTo> definition)
            where TFrom : WorldEntity where TTo : WorldEntity
        {
            RequireOwned(to);
            return GetRelationStore(definition).Incoming(to.Id).Select(Get<TFrom>).Where(item => item != null).ToArray();
        }

        public bool HasRelation<TFrom, TTo>(TFrom from, RelationDefinition<TFrom, TTo> definition, TTo to)
            where TFrom : WorldEntity where TTo : WorldEntity
        {
            RequireOwned(from);
            RequireOwned(to);
            return GetRelationStore(definition).Contains(from.Id, to.Id);
        }

        public bool AddTag<TEntity>(TEntity entity, TagDefinition<TEntity> definition) where TEntity : WorldEntity
        {
            RequireOwned(entity);
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            RegisterStructuralDefinition("tag:", definition.Id.Value, definition);
            if (!entity.AddTag(definition)) return false;
            NotifyStructureChanged();
            return true;
        }

        public bool RemoveTag<TEntity>(TEntity entity, TagDefinition<TEntity> definition) where TEntity : WorldEntity
        {
            RequireOwned(entity);
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            RegisterStructuralDefinition("tag:", definition.Id.Value, definition);
            if (!entity.RemoveTag(definition)) return false;
            NotifyStructureChanged();
            return true;
        }

        public bool SetComponent<TEntity, TValue>(TEntity entity,
            ComponentDefinition<TEntity, TValue> definition, TValue value) where TEntity : WorldEntity
        {
            RequireOwned(entity);
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            RegisterStructuralDefinition("component:", definition.Id.Value, definition);
            if (!entity.SetComponent(definition, value)) return false;
            NotifyStructureChanged();
            return true;
        }

        public bool RemoveComponent<TEntity, TValue>(TEntity entity,
            ComponentDefinition<TEntity, TValue> definition) where TEntity : WorldEntity
        {
            RequireOwned(entity);
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            RegisterStructuralDefinition("component:", definition.Id.Value, definition);
            if (!entity.RemoveComponent(definition)) return false;
            NotifyStructureChanged();
            return true;
        }

        internal void NotifyAttributeChanged(WorldEntity entity, IAttributeDefinition definition)
        {
            if (_mutationBatchDepth > 0)
            {
                _batchedAttributeChanges.Add(Tuple.Create(entity, definition));
                return;
            }
            InvalidateDependents(entity, definition, new HashSet<IAttributeDefinition>());
            AdvanceAttributeQueryVersions(entity, definition, new HashSet<IAttributeDefinition>());
            using (BeginVersionPublicationScope())
            {
                AdvanceVersion();
                InvalidateModifiersForAttribute(entity, definition);
                InvalidateGlobalDerivedAttributes(definition, new HashSet<IAttributeDefinition>());
            }
        }

        internal void NotifyAttributeContributionChanged(WorldEntity entity, IAttributeDefinition definition)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (_mutationBatchDepth > 0)
            {
                _batchedAttributeChanges.Add(Tuple.Create(entity, definition));
                return;
            }
            InvalidateDependents(entity, definition, new HashSet<IAttributeDefinition>());
            AdvanceAttributeQueryVersions(entity, definition, new HashSet<IAttributeDefinition>());
            InvalidateGlobalDerivedAttributes(definition, new HashSet<IAttributeDefinition>());
        }

        internal long GetAttributeQueryVersion(IAttributeDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            return _attributeQueryVersions.TryGetValue(definition, out long version) ? version : 0;
        }

        internal long GetAttributeQueryVersion(WorldEntity entity, IAttributeDefinition definition)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            RequireOwned(entity);
            return _entityAttributeQueryVersions.TryGetValue(definition, out Dictionary<EntityId, long> versions) &&
                versions.TryGetValue(entity.Id, out long version) ? version : 0;
        }

        internal void NotifyStructureChanged(object changedRelation = null, EntityId? changedSource = null)
        {
            if (_mutationBatchDepth > 0)
            {
                if (changedRelation == null) _batchedFullStructureChange = true;
                else
                {
                    _batchedRelationChanges.Add(changedRelation);
                    if (changedSource.HasValue)
                        _batchedRelationSourceChanges.Add(Tuple.Create(changedRelation, changedSource.Value));
                }
                return;
            }
            AdvanceQueryVersion(changedRelation, changedSource);
            using (BeginVersionPublicationScope())
            {
                AdvanceVersion();
                InvalidateRelationDerivedAttributes(changedRelation);
                _currentChangedRelationSource = changedSource;
                try { ReconcileLiveModifiers(changedRelation); }
                finally { _currentChangedRelationSource = null; }
            }
        }

        internal long GetRelationQueryVersion(object relation)
        {
            if (relation == null) throw new ArgumentNullException(nameof(relation));
            return _relationQueryVersions.TryGetValue(relation, out long version) ? version : 0;
        }

        internal long GetRelationQueryVersion(object relation, EntityId source)
        {
            if (relation == null) throw new ArgumentNullException(nameof(relation));
            return _relationSourceQueryVersions.TryGetValue(relation, out Dictionary<EntityId, long> versions) &&
                versions.TryGetValue(source, out long version) ? version : 0;
        }

        internal long GetRelationQueryVersion(object relation, WorldEntity source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            RequireOwned(source);
            return GetRelationQueryVersion(relation, source.Id);
        }

        private void EndMutationBatch()
        {
            if (_mutationBatchDepth <= 0) throw new InvalidOperationException("Mutation batch is not active.");
            _mutationBatchDepth--;
            if (_mutationBatchDepth != 0) return;
            if (_batchedAttributeChanges.Count == 0 && _batchedRelationChanges.Count == 0 &&
                !_batchedFullStructureChange && !_batchedVersionChange)
            {
                EndVersionPublication();
                return;
            }

            try
            {
                _processingMutationBatch = true;
                AdvanceVersionCore();
                if (_batchedFullStructureChange) AdvanceQueryVersion(null, null);
                else
                {
                    foreach (object relation in _batchedRelationChanges.OrderBy(StableDefinitionId,
                                 StringComparer.Ordinal))
                        AdvanceQueryVersion(relation, null);
                    foreach (Tuple<object, EntityId> change in _batchedRelationSourceChanges
                                 .OrderBy(item => StableDefinitionId(item.Item1), StringComparer.Ordinal)
                                 .ThenBy(item => item.Item2))
                        AdvanceRelationSourceQueryVersion(change.Item1, change.Item2);
                }
                foreach (Tuple<WorldEntity, IAttributeDefinition> change in _batchedAttributeChanges
                             .OrderBy(item => item.Item1.Id)
                             .ThenBy(item => item.Item2.Id.Value, StringComparer.Ordinal))
                {
                    InvalidateDependents(change.Item1, change.Item2, new HashSet<IAttributeDefinition>());
                    AdvanceAttributeQueryVersions(change.Item1, change.Item2, new HashSet<IAttributeDefinition>());
                }
                BeginModifierInvalidationBatch();
                try
                {
                    foreach (Tuple<WorldEntity, IAttributeDefinition> change in _batchedAttributeChanges
                                 .OrderBy(item => item.Item1.Id)
                                 .ThenBy(item => item.Item2.Id.Value, StringComparer.Ordinal))
                    {
                        InvalidateModifiersForAttribute(change.Item1, change.Item2);
                        InvalidateGlobalDerivedAttributes(change.Item2, new HashSet<IAttributeDefinition>());
                    }
                    if (_batchedFullStructureChange) InvalidateRelationDerivedAttributes(null);
                    else
                        foreach (object relation in _batchedRelationChanges.OrderBy(StableDefinitionId,
                                     StringComparer.Ordinal))
                            InvalidateRelationDerivedAttributes(relation);
                    if (_batchedFullStructureChange) ReconcileLiveModifiers(null);
                    else
                        foreach (object relation in _batchedRelationChanges.OrderBy(StableDefinitionId,
                                     StringComparer.Ordinal))
                        {
                            EntityId[] changedSources = _batchedRelationSourceChanges
                                .Where(item => ReferenceEquals(item.Item1, relation))
                                .Select(item => item.Item2)
                                .OrderBy(item => item)
                                .ToArray();
                            if (changedSources.Length == 0)
                            {
                                ReconcileLiveModifiers(relation);
                                continue;
                            }
                            foreach (EntityId source in changedSources)
                            {
                                _currentChangedRelationSource = source;
                                try { ReconcileLiveModifiers(relation); }
                                finally { _currentChangedRelationSource = null; }
                            }
                        }
                }
                finally { EndModifierInvalidationBatch(); }
            }
            finally
            {
                _batchedAttributeChanges.Clear();
                _batchedRelationChanges.Clear();
                _batchedRelationSourceChanges.Clear();
                _batchedFullStructureChange = false;
                _batchedVersionChange = false;
                _processingMutationBatch = false;
                EndVersionPublication();
            }
        }

        private IDisposable BeginVersionPublicationScope()
        {
            BeginVersionPublication();
            return new VersionPublicationScope(this);
        }

        private void BeginVersionPublication() => _versionPublicationDepth++;

        private void EndVersionPublication()
        {
            if (_versionPublicationDepth <= 0)
                throw new InvalidOperationException("Version publication scope is not active.");
            _versionPublicationDepth--;
            if (_versionPublicationDepth == 0) PublishVersionAdvanced();
        }

        private void AdvanceVersion()
        {
            if (_mutationBatchDepth > 0 || _processingMutationBatch)
            {
                _batchedVersionChange = true;
                return;
            }
            AdvanceVersionCore();
        }

        private void AdvanceVersionCore()
        {
            Version++;
            _pendingQueryDependencyChanges.Add(new QueryDependencyKey(QueryDependencyKind.World));
            _versionPublicationPending = true;
            if (_versionPublicationDepth == 0) PublishVersionAdvanced();
        }

        private void PublishVersionAdvanced()
        {
            if (!_versionPublicationPending) return;
            _versionPublicationPending = false;
            QueryDependencyKey[] changes = _pendingQueryDependencyChanges.ToArray();
            _pendingQueryDependencyChanges.Clear();
            OnVersionAdvanced(changes);
        }

        private void AdvanceQueryVersion(object changedRelation, EntityId? changedSource = null)
        {
            if (changedRelation == null)
            {
                _structureQueryVersion++;
                _pendingQueryDependencyChanges.Add(new QueryDependencyKey(QueryDependencyKind.Structure));
                return;
            }

            _relationQueryVersions.TryGetValue(changedRelation, out long version);
            _relationQueryVersions[changedRelation] = version + 1;
            _pendingQueryDependencyChanges.Add(new QueryDependencyKey(QueryDependencyKind.Relation, changedRelation));
            if (changedSource.HasValue) AdvanceRelationSourceQueryVersion(changedRelation, changedSource.Value);
        }

        private void AdvanceRelationSourceQueryVersion(object relation, EntityId source)
        {
            if (!_relationSourceQueryVersions.TryGetValue(relation, out Dictionary<EntityId, long> versions))
                _relationSourceQueryVersions.Add(relation, versions = new Dictionary<EntityId, long>());
            versions.TryGetValue(source, out long version);
            versions[source] = version + 1;
            _pendingQueryDependencyChanges.Add(
                new QueryDependencyKey(QueryDependencyKind.SourceRelation, relation, source));
        }

        private sealed class MutationBatch : IDisposable
        {
            private SimulationWorld _world;
            public MutationBatch(SimulationWorld world) => _world = world;
            public void Dispose()
            {
                SimulationWorld world = _world;
                if (world == null) return;
                _world = null;
                world.EndMutationBatch();
            }
        }

        private sealed class VersionPublicationScope : IDisposable
        {
            private SimulationWorld _world;
            public VersionPublicationScope(SimulationWorld world) => _world = world;
            public void Dispose()
            {
                SimulationWorld world = _world;
                if (world == null) return;
                _world = null;
                world.EndVersionPublication();
            }
        }

        private void InvalidateDependents(WorldEntity entity, IAttributeDefinition definition,
            HashSet<IAttributeDefinition> visited)
        {
            if (!visited.Add(definition) || !_dependents.TryGetValue(definition, out List<IAttributeDefinition> list)) return;
            foreach (IAttributeDefinition dependent in list)
            {
                if (entity.TryGetSlot(dependent, out IAttributeSlot slot)) slot.MarkDirty();
                InvalidateDependents(entity, dependent, visited);
            }
        }

        private void AdvanceAttributeQueryVersions(WorldEntity entity, IAttributeDefinition definition,
            HashSet<IAttributeDefinition> visited)
        {
            if (!visited.Add(definition)) return;
            _attributeQueryVersions.TryGetValue(definition, out long globalVersion);
            _attributeQueryVersions[definition] = globalVersion + 1;
            _pendingQueryDependencyChanges.Add(
                new QueryDependencyKey(QueryDependencyKind.Attribute, definition));
            if (!_entityAttributeQueryVersions.TryGetValue(definition, out Dictionary<EntityId, long> versions))
                _entityAttributeQueryVersions.Add(definition, versions = new Dictionary<EntityId, long>());
            versions.TryGetValue(entity.Id, out long entityVersion);
            versions[entity.Id] = entityVersion + 1;
            _pendingQueryDependencyChanges.Add(
                new QueryDependencyKey(QueryDependencyKind.EntityAttribute, definition, entity.Id));

            if (!_dependents.TryGetValue(definition, out List<IAttributeDefinition> dependents)) return;
            foreach (IAttributeDefinition dependent in dependents)
                AdvanceAttributeQueryVersions(entity, dependent, visited);
        }

        private void InvalidateGlobalDerivedAttributes(IAttributeDefinition changed,
            HashSet<IAttributeDefinition> visited)
        {
            if (!visited.Add(changed) ||
                !_globalDependents.TryGetValue(changed, out List<IAttributeDefinition> dependents)) return;
            foreach (IAttributeDefinition dependent in dependents.OrderBy(item => item.Id.Value,
                         StringComparer.Ordinal))
            {
                InvalidateDerivedDefinition(dependent);
                InvalidateGlobalDerivedAttributes(dependent, visited);
            }
        }

        private void InvalidateRelationDerivedAttributes(object changedRelation)
        {
            IEnumerable<IAttributeDefinition> definitions = changedRelation == null
                ? _relationAttributeDependents.Values.SelectMany(item => item)
                : _relationAttributeDependents.TryGetValue(changedRelation,
                    out List<IAttributeDefinition> dependents)
                    ? dependents
                    : Enumerable.Empty<IAttributeDefinition>();
            foreach (IAttributeDefinition definition in definitions.Distinct()
                         .OrderBy(item => item.Id.Value, StringComparer.Ordinal))
            {
                InvalidateDerivedDefinition(definition);
                InvalidateGlobalDerivedAttributes(definition, new HashSet<IAttributeDefinition>());
            }
        }

        private void InvalidateDerivedDefinition(IAttributeDefinition definition)
        {
            foreach (WorldEntity entity in Entities)
            {
                if (!definition.EntityType.IsInstanceOfType(entity) ||
                    !entity.TryGetSlot(definition, out IAttributeSlot slot)) continue;
                slot.MarkDirty();
                InvalidateDependents(entity, definition, new HashSet<IAttributeDefinition>());
                AdvanceAttributeQueryVersions(entity, definition, new HashSet<IAttributeDefinition>());
                InvalidateModifiersForAttribute(entity, definition);
            }
        }

        private RelationStore<TFrom, TTo> GetRelationStore<TFrom, TTo>(RelationDefinition<TFrom, TTo> definition)
            where TFrom : WorldEntity where TTo : WorldEntity
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            RegisterStructuralDefinition("relation:", definition.Id.Value, definition);
            if (!_relations.TryGetValue(definition, out IRelationStore store))
                _relations.Add(definition, store = new RelationStore<TFrom, TTo>());
            return (RelationStore<TFrom, TTo>)store;
        }

        private void RegisterStructuralDefinition(string category, string id, object definition)
        {
            string key = category + id;
            if (_structuralDefinitions.TryGetValue(key, out object existing))
            {
                if (!ReferenceEquals(existing, definition))
                    throw new InvalidOperationException(
                        $"Structural definition ID {id} is already registered for " +
                        $"{category.Substring(0, category.Length - 1)}.");
                return;
            }
            _structuralDefinitions.Add(key, definition);
        }

        private void RequireOwned(WorldEntity entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (!ReferenceEquals(entity.World, this) || entity.Id.Value <= 0 ||
                entity.Id.Value >= _entitySlots.Count ||
                !ReferenceEquals(_entitySlots[(int)entity.Id.Value], entity))
                throw new InvalidOperationException("Entity does not belong to this world.");
        }

        private static string StableDefinitionId(object definition) =>
            definition is IRelationDefinition relation ? relation.StableId : definition?.GetType().FullName ?? string.Empty;

        private static void Visit(IAttributeDefinition definition, HashSet<IAttributeDefinition> visiting,
            HashSet<IAttributeDefinition> visited)
        {
            if (visited.Contains(definition)) return;
            if (!visiting.Add(definition)) throw new InvalidOperationException($"Derived attribute cycle detected at {definition.Id}.");
            foreach (IAttributeDefinition dependency in definition.Dependencies) Visit(dependency, visiting, visited);
            foreach (IAttributeDefinition dependency in definition.GlobalDependencies)
                Visit(dependency, visiting, visited);
            visiting.Remove(definition);
            visited.Add(definition);
        }

        partial void InvalidateModifiersForAttribute(WorldEntity changedEntity, IAttributeDefinition changedAttribute);
        partial void ReconcileLiveModifiers(object changedRelation);
        partial void BeginModifierInvalidationBatch();
        partial void EndModifierInvalidationBatch();
        partial void RemoveModifiersOwnedByOrTargeting(EntityId entityId);
        partial void RemoveRuntimeStateOwnedByOrReferencing(EntityId entityId);
        partial void OnVersionAdvanced(IReadOnlyCollection<QueryDependencyKey> changes);

        private sealed class EntityCollection : IReadOnlyCollection<WorldEntity>
        {
            private readonly List<WorldEntity> _slots;
            private readonly Func<int> _count;

            public EntityCollection(List<WorldEntity> slots, Func<int> count)
            {
                _slots = slots;
                _count = count;
            }

            public int Count => _count();

            public IEnumerator<WorldEntity> GetEnumerator()
            {
                for (int index = 1; index < _slots.Count; index++)
                {
                    WorldEntity entity = _slots[index];
                    if (entity != null) yield return entity;
                }
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
