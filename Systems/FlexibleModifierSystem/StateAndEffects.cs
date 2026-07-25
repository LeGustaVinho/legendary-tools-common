using System;
using System.Collections.Generic;
using System.Linq;
using DeterministicFixedPoint;

namespace LegendaryTools.ModifierSystem
{
    internal interface IDeclarativeCollection
    {
        StableId<CollectionIdKind> DefinitionId { get; }
        EntityId OwnerId { get; }
        IReadOnlyList<EntityId> BaseItemIds { get; }
        void RestoreBaseItems(SimulationWorld world, IReadOnlyList<EntityId> itemIds);
        void RemoveEntity(EntityId entityId);
        void Deactivate();
    }

    public sealed class CollectionDefinition<TOwner, TItem>
        where TOwner : WorldEntity where TItem : WorldEntity
    {
        public StableId<CollectionIdKind> Id { get; }
        public CollectionDefinition(string id) => Id = new StableId<CollectionIdKind>(id);
    }

    public readonly struct CollectionMembershipContribution<TItem> where TItem : WorldEntity
    {
        private readonly Func<bool> _active;
        public Guid ModifierInstanceId { get; }
        public int BindingIndex { get; }
        public TItem Item { get; }
        public CollectionMembershipDecision Decision { get; }
        public int Priority { get; }
        public long Sequence { get; }
        public bool IsActive => _active == null || _active();

        internal CollectionMembershipContribution(Guid modifierInstanceId, int bindingIndex, TItem item,
            CollectionMembershipDecision decision, int priority, long sequence, Func<bool> active)
        {
            ModifierInstanceId = modifierInstanceId;
            BindingIndex = bindingIndex;
            Item = item ?? throw new ArgumentNullException(nameof(item));
            Decision = decision;
            Priority = priority;
            Sequence = sequence;
            _active = active;
        }
    }

    public sealed class DeclarativeCollection<TOwner, TItem> : IDeclarativeCollection
        where TOwner : WorldEntity where TItem : WorldEntity
    {
        private readonly HashSet<TItem> _baseItems = new HashSet<TItem>();
        private readonly List<CollectionMembershipContribution<TItem>> _contributions =
            new List<CollectionMembershipContribution<TItem>>();
        private readonly IReadOnlyList<CollectionMembershipContribution<TItem>> _contributionsView;
        private readonly Action _changed;
        private IReadOnlyList<TItem> _resolvedCache = Array.Empty<TItem>();
        private HashSet<TItem> _resolvedSet = new HashSet<TItem>();
        private long _resolvedWorldVersion = -1;
        private bool _resolutionDirty = true;
        private bool _active = true;

        public TOwner Owner { get; }
        public CollectionDefinition<TOwner, TItem> Definition { get; }
        public IReadOnlyList<CollectionMembershipContribution<TItem>> Contributions => _contributionsView;
        public IReadOnlyList<TItem> Items => Resolve();
        public long ResolutionCount { get; private set; }
        public event Action<DeclarativeCollection<TOwner, TItem>> Changed;
        StableId<CollectionIdKind> IDeclarativeCollection.DefinitionId => Definition.Id;
        EntityId IDeclarativeCollection.OwnerId => Owner.Id;
        IReadOnlyList<EntityId> IDeclarativeCollection.BaseItemIds =>
            _baseItems.Select(item => item.Id).OrderBy(item => item).ToArray();

        internal DeclarativeCollection(TOwner owner, CollectionDefinition<TOwner, TItem> definition,
            Action changed)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _changed = changed;
            _contributionsView = _contributions.AsReadOnly();
        }

        public bool AddBase(TItem item)
        {
            Owner.World.EnsureMutationAllowed();
            EnsureActive();
            EnsureOwned(item);
            if (!_baseItems.Add(item)) return false;
            NotifyChanged();
            return true;
        }

        public bool RemoveBase(TItem item)
        {
            Owner.World.EnsureMutationAllowed();
            EnsureActive();
            if (!_baseItems.Remove(item)) return false;
            NotifyChanged();
            return true;
        }

        public bool Contains(TItem item)
        {
            if (item == null) return false;
            Resolve();
            return _resolvedSet.Contains(item);
        }

        internal void AddContribution(CollectionMembershipContribution<TItem> contribution)
        {
            EnsureActive();
            EnsureOwned(contribution.Item);
            _contributions.Add(contribution);
            NotifyChanged();
        }

        internal bool RemoveContributions(Guid modifierInstanceId, int bindingIndex)
        {
            int removed = _contributions.RemoveAll(item =>
                item.ModifierInstanceId == modifierInstanceId && item.BindingIndex == bindingIndex);
            if (removed != 0) NotifyChanged();
            return removed != 0;
        }

        private IReadOnlyList<TItem> Resolve()
        {
            EnsureActive();
            if (!_resolutionDirty && _resolvedWorldVersion == Owner.World.Version) return _resolvedCache;
            var winners =
                new Dictionary<TItem, CollectionMembershipContribution<TItem>?>();
            foreach (TItem item in _baseItems) winners[item] = null;
            foreach (CollectionMembershipContribution<TItem> contribution in _contributions)
            {
                if (!contribution.IsActive) continue;
                if (!winners.TryGetValue(contribution.Item,
                        out CollectionMembershipContribution<TItem>? winner) ||
                    !winner.HasValue || contribution.Priority > winner.Value.Priority ||
                    contribution.Priority == winner.Value.Priority &&
                    contribution.Sequence > winner.Value.Sequence)
                    winners[contribution.Item] = contribution;
            }
            var ordered = new List<TItem>(winners.Keys);
            ordered.Sort((left, right) => left.Id.CompareTo(right.Id));
            var resolved = new List<TItem>(ordered.Count);
            foreach (TItem item in ordered)
            {
                CollectionMembershipContribution<TItem>? winner = winners[item];
                bool included = winner.HasValue
                    ? winner.Value.Decision == CollectionMembershipDecision.Include
                    : _baseItems.Contains(item);
                if (included) resolved.Add(item);
            }
            _resolvedCache = resolved.AsReadOnly();
            _resolvedSet = new HashSet<TItem>(resolved);
            _resolvedWorldVersion = Owner.World.Version;
            _resolutionDirty = false;
            ResolutionCount++;
            return _resolvedCache;
        }

        private void EnsureOwned(TItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (!ReferenceEquals(item.World, Owner.World) ||
                !ReferenceEquals(Owner.World.Get<TItem>(item.Id), item))
                throw new InvalidOperationException("Collection items must belong to the owner's world.");
        }

        private void EnsureActive()
        {
            if (!_active) throw new ObjectDisposedException($"Collection {Definition.Id}");
        }

        private void NotifyChanged()
        {
            _resolutionDirty = true;
            _changed?.Invoke();
            Action<DeclarativeCollection<TOwner, TItem>> handlers = Changed;
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                Delegate capturedHandler = handler;
                Owner.World.PublishEffectAwareNotification(capturedHandler,
                    () => ((Action<DeclarativeCollection<TOwner, TItem>>)capturedHandler)(this));
            }
        }

        void IDeclarativeCollection.RemoveEntity(EntityId entityId)
        {
            _baseItems.RemoveWhere(item => item.Id == entityId);
            _contributions.RemoveAll(item => item.Item.Id == entityId);
        }

        void IDeclarativeCollection.RestoreBaseItems(SimulationWorld world,
            IReadOnlyList<EntityId> itemIds)
        {
            _baseItems.Clear();
            foreach (EntityId id in itemIds)
            {
                TItem item = world.Get<TItem>(id);
                if (item == null)
                    throw new InvalidOperationException($"Collection item {id} is missing.");
                _baseItems.Add(item);
            }
            NotifyChanged();
        }

        void IDeclarativeCollection.Deactivate()
        {
            _active = false;
            _baseItems.Clear();
            _contributions.Clear();
        }
    }

    public readonly struct CounterKey<TEntity, TValue> where TEntity : WorldEntity
    {
        public StableId<CounterIdKind> Id { get; }
        public CounterKey(string id) => Id = new StableId<CounterIdKind>(id);
    }

    public readonly struct VariableKey<TValue>
    {
        public StableId<VariableIdKind> Id { get; }
        public VariableKey(string id) => Id = new StableId<VariableIdKind>(id);
    }

    public enum VariableScope { World, Entity, Effect, EventChain }

    public enum VariableOwnerKind { Entity, Effect, EventChain }

    public readonly struct VariableOwnerId : IEquatable<VariableOwnerId>
    {
        public VariableOwnerKind Kind { get; }
        public string Value { get; }
        private VariableOwnerId(VariableOwnerKind kind, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Variable owner ID is required.", nameof(value));
            Kind = kind;
            Value = value;
        }
        public static VariableOwnerId Entity(EntityId id) =>
            new VariableOwnerId(VariableOwnerKind.Entity, id.Value.ToString());
        public static VariableOwnerId Effect(Guid id) =>
            new VariableOwnerId(VariableOwnerKind.Effect, id.ToString("N"));
        public static VariableOwnerId EventChain(string id) =>
            new VariableOwnerId(VariableOwnerKind.EventChain, id);
        public bool Equals(VariableOwnerId other) => Kind == other.Kind &&
            string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is VariableOwnerId other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { return ((int)Kind * 397) ^ StringComparer.Ordinal.GetHashCode(Value ?? string.Empty); }
        }
        public override string ToString() => $"{Kind}:{Value}";
    }

    public readonly struct TriggerEvaluation
    {
        public bool IsActive { get; }
        public string Explanation { get; }

        public TriggerEvaluation(bool isActive, string explanation = null)
        {
            IsActive = isActive;
            Explanation = explanation ?? string.Empty;
        }
    }

    public sealed class TriggerDefinition<TState>
    {
        private readonly QueryDependency[] _dependencies;
        internal IReadOnlyList<QueryDependencyKey> DependencyKeys { get; }
        internal TriggerEvaluation Evaluate(SimulationWorld world, TState state) => _evaluate(world, state);
        private readonly Func<SimulationWorld, TState, TriggerEvaluation> _evaluate;

        public StableId<TriggerIdKind> Id { get; }
        public bool DependsOnTime { get; }
        public bool PersistState { get; }

        public TriggerDefinition(string id, Func<SimulationWorld, TState, TriggerEvaluation> evaluate,
            bool dependsOnTime = false, bool persistState = true, params QueryDependency[] dependencies)
        {
            Id = new StableId<TriggerIdKind>(id);
            _evaluate = evaluate ?? throw new ArgumentNullException(nameof(evaluate));
            DependsOnTime = dependsOnTime;
            PersistState = persistState;
            _dependencies = dependencies == null ? Array.Empty<QueryDependency>() : dependencies.ToArray();
            if (_dependencies.Any(item => item == null))
                throw new ArgumentException("Trigger dependencies cannot contain null.", nameof(dependencies));
            if (_dependencies.Length == 0 && !dependsOnTime)
                _dependencies = new[]
                {
                    new QueryDependency(world => world.Version,
                        new QueryDependencyKey(QueryDependencyKind.World))
                };
            DependencyKeys = _dependencies.Select(item => item.Key).Distinct().ToArray();
        }
    }

    public sealed class TriggerTransition
    {
        public StableId<TriggerIdKind> TriggerId { get; }
        public bool Previous { get; }
        public bool Current { get; }
        public long Tick { get; }
        public string Explanation { get; }

        internal TriggerTransition(StableId<TriggerIdKind> triggerId, bool previous, bool current,
            long tick, string explanation)
        {
            TriggerId = triggerId;
            Previous = previous;
            Current = current;
            Tick = tick;
            Explanation = explanation ?? string.Empty;
        }
    }

    public sealed class TriggerEvaluationFailure
    {
        public StableId<TriggerIdKind> TriggerId { get; }
        public Exception Exception { get; }
        internal TriggerEvaluationFailure(StableId<TriggerIdKind> triggerId, Exception exception)
        {
            TriggerId = triggerId;
            Exception = exception;
        }
    }

    internal interface ITriggerInstance
    {
        long RegistrationId { get; }
        StableId<TriggerIdKind> DefinitionId { get; }
        bool DependsOnTime { get; }
        bool PersistState { get; }
        bool IsDisposed { get; }
        bool IsActive { get; }
        string Explanation { get; }
        IReadOnlyList<QueryDependencyKey> DependencyKeys { get; }
        object BoxedState { get; }
        TriggerTransition Evaluate(SimulationWorld world, bool emitTransition);
        void Restore(object state, bool isActive, string explanation);
        void MarkDisposed();
    }

    public sealed class TriggerInstance<TState> : ITriggerInstance, IDisposable
    {
        private SimulationWorld _world;
        private readonly TriggerDefinition<TState> _definition;
        internal long RegistrationId { get; }
        public TState State { get; private set; }
        public bool IsActive { get; private set; }
        public string Explanation { get; private set; } = string.Empty;
        public bool IsDisposed => _world == null;
        public TriggerDefinition<TState> Definition => _definition;

        internal TriggerInstance(SimulationWorld world, long registrationId,
            TriggerDefinition<TState> definition, TState state)
        {
            _world = world;
            RegistrationId = registrationId;
            _definition = definition;
            State = state;
        }

        public void SetState(TState state)
        {
            if (_world == null) throw new ObjectDisposedException(nameof(TriggerInstance<TState>));
            _world.EnsureMutationAllowed();
            State = state;
            _world.NotifyTriggerStateChanged(this);
        }

        public void Dispose()
        {
            SimulationWorld world = _world;
            if (world == null) return;
            _world = null;
            world.RemoveTrigger(RegistrationId);
        }

        long ITriggerInstance.RegistrationId => RegistrationId;
        StableId<TriggerIdKind> ITriggerInstance.DefinitionId => _definition.Id;
        bool ITriggerInstance.DependsOnTime => _definition.DependsOnTime;
        bool ITriggerInstance.PersistState => _definition.PersistState;
        IReadOnlyList<QueryDependencyKey> ITriggerInstance.DependencyKeys => _definition.DependencyKeys;
        object ITriggerInstance.BoxedState => State;
        TriggerTransition ITriggerInstance.Evaluate(SimulationWorld world, bool emitTransition)
        {
            bool previous = IsActive;
            TriggerEvaluation evaluation = _definition.Evaluate(world, State);
            IsActive = evaluation.IsActive;
            Explanation = evaluation.Explanation;
            return emitTransition && previous != IsActive
                ? new TriggerTransition(_definition.Id, previous, IsActive, world.CurrentTick, Explanation)
                : null;
        }
        void ITriggerInstance.Restore(object state, bool isActive, string explanation)
        {
            State = state is TState typed ? typed : default;
            IsActive = isActive;
            Explanation = explanation ?? string.Empty;
        }
        void ITriggerInstance.MarkDisposed() => _world = null;
    }

    internal interface ITypedCounter
    {
        string KeyId { get; }
        EntityId OwnerId { get; }
        Type ValueType { get; }
        object BoxedValue { get; }
        void RestoreBoxedValue(object value);
        void Deactivate();
    }

    public sealed class TypedCounter<TEntity, TValue> : ITypedCounter where TEntity : WorldEntity
    {
        private readonly Func<TValue, TValue, TValue> _add;
        private readonly Action _changed;
        private bool _active = true;
        public CounterKey<TEntity, TValue> Key { get; }
        public TEntity Owner { get; }
        public TValue Value { get; private set; }
        public event Action<TValue, TValue> ValueChanged;
        string ITypedCounter.KeyId => Key.Id.Value;
        EntityId ITypedCounter.OwnerId => Owner.Id;
        Type ITypedCounter.ValueType => typeof(TValue);
        object ITypedCounter.BoxedValue => Value;

        internal TypedCounter(CounterKey<TEntity, TValue> key, TEntity owner, TValue initial,
            Func<TValue, TValue, TValue> add, Action changed)
        {
            Key = key;
            Owner = owner;
            Value = initial;
            _add = add;
            _changed = changed;
        }

        public void Increment(TValue amount) => Set(_add(Value, amount));
        public void Set(TValue value)
        {
            Owner.World.EnsureMutationAllowed();
            if (!_active) throw new ObjectDisposedException($"Counter {Key.Id}");
            if (EqualityComparer<TValue>.Default.Equals(Value, value)) return;
            TValue previous = Value;
            Value = value;
            ValueChanged?.Invoke(previous, value);
            _changed?.Invoke();
        }

        void ITypedCounter.RestoreBoxedValue(object value) => Set((TValue)value);
        void ITypedCounter.Deactivate() => _active = false;
    }

    public sealed class TypedVariableStore
    {
        private readonly Action _changed;
        private readonly Action _ensureMutationAllowed;
        private readonly Func<EntityId, bool> _entityExists;
        private readonly Dictionary<Tuple<Type, string, VariableScope, VariableOwnerId?>, object> _values =
            new Dictionary<Tuple<Type, string, VariableScope, VariableOwnerId?>, object>();
        public event Action<VariableScope, EntityId?, string> ValueChanged;
        public event Action<VariableScope, VariableOwnerId?, string> ScopedValueChanged;

        internal TypedVariableStore(Action changed = null, Func<EntityId, bool> entityExists = null,
            Action ensureMutationAllowed = null)
        {
            _changed = changed;
            _entityExists = entityExists;
            _ensureMutationAllowed = ensureMutationAllowed;
        }

        public void Set<T>(VariableKey<T> key, T value, VariableScope scope = VariableScope.World,
            EntityId? owner = null)
            => SetCore(key, value, scope, owner.HasValue ? VariableOwnerId.Entity(owner.Value) : (VariableOwnerId?)null);

        public void Set<T>(VariableKey<T> key, T value, VariableScope scope, VariableOwnerId owner)
            => SetCore(key, value, scope, owner);

        private void SetCore<T>(VariableKey<T> key, T value, VariableScope scope, VariableOwnerId? owner)
        {
            _ensureMutationAllowed?.Invoke();
            ValidateOwner(scope, owner);
            _values[Tuple.Create(typeof(T), key.Id.Value, scope, owner)] = value;
            NotifyChanged(scope, owner, key.Id.Value);
            _changed?.Invoke();
        }

        public bool TryGet<T>(VariableKey<T> key, out T value, VariableScope scope = VariableScope.World,
            EntityId? owner = null)
            => TryGetCore(key, out value, scope,
                owner.HasValue ? VariableOwnerId.Entity(owner.Value) : (VariableOwnerId?)null);

        public bool TryGet<T>(VariableKey<T> key, out T value, VariableScope scope, VariableOwnerId owner)
            => TryGetCore(key, out value, scope, owner);

        private bool TryGetCore<T>(VariableKey<T> key, out T value, VariableScope scope, VariableOwnerId? owner)
        {
            ValidateOwner(scope, owner, false);
            if (_values.TryGetValue(Tuple.Create(typeof(T), key.Id.Value, scope, owner), out object boxed))
            {
                value = (T)boxed;
                return true;
            }
            value = default;
            return false;
        }

        public bool Remove<T>(VariableKey<T> key, VariableScope scope = VariableScope.World,
            EntityId? owner = null)
            => RemoveCore<T>(key, scope,
                owner.HasValue ? VariableOwnerId.Entity(owner.Value) : (VariableOwnerId?)null);

        public bool Remove<T>(VariableKey<T> key, VariableScope scope, VariableOwnerId owner) =>
            RemoveCore<T>(key, scope, owner);

        private bool RemoveCore<T>(VariableKey<T> key, VariableScope scope, VariableOwnerId? owner)
        {
            _ensureMutationAllowed?.Invoke();
            ValidateOwner(scope, owner, false);
            bool removed = _values.Remove(Tuple.Create(typeof(T), key.Id.Value, scope, owner));
            if (removed)
            {
                NotifyChanged(scope, owner, key.Id.Value);
                _changed?.Invoke();
            }
            return removed;
        }

        internal IEnumerable<KeyValuePair<Tuple<Type, string, VariableScope, VariableOwnerId?>, object>> Entries => _values;
        internal void Clear() => _values.Clear();
        internal void RemoveOwner(EntityId owner)
        {
            VariableOwnerId variableOwner = VariableOwnerId.Entity(owner);
            RemoveOwner(variableOwner);
        }

        internal void RemoveOwner(VariableOwnerId owner)
        {
            foreach (Tuple<Type, string, VariableScope, VariableOwnerId?> key in _values.Keys
                         .Where(item => item.Item4.HasValue && item.Item4.Value.Equals(owner)).ToArray())
                _values.Remove(key);
        }
        internal void Restore(Type type, string id, VariableScope scope, VariableOwnerId? owner, object value)
        {
            ValidateOwner(scope, owner);
            _values[Tuple.Create(type, id, scope, owner)] = value;
        }

        private void NotifyChanged(VariableScope scope, VariableOwnerId? owner, string id)
        {
            ScopedValueChanged?.Invoke(scope, owner, id);
            EntityId? entity = null;
            if (owner.HasValue && owner.Value.Kind == VariableOwnerKind.Entity &&
                long.TryParse(owner.Value.Value, out long value) && value > 0)
                entity = new EntityId(value);
            ValueChanged?.Invoke(scope, entity, id);
        }

        private void ValidateOwner(VariableScope scope, VariableOwnerId? owner, bool requireActiveEntity = true)
        {
            if (scope == VariableScope.World)
            {
                if (owner.HasValue) throw new InvalidOperationException("World variables cannot have an owner.");
                return;
            }
            if (!owner.HasValue) throw new InvalidOperationException($"{scope} variables require an owner.");
            VariableOwnerKind expected = scope == VariableScope.Entity
                ? VariableOwnerKind.Entity
                : scope == VariableScope.Effect
                    ? VariableOwnerKind.Effect
                    : VariableOwnerKind.EventChain;
            if (owner.Value.Kind != expected)
                throw new InvalidOperationException($"{scope} variables require a {expected} owner.");
            if (scope == VariableScope.Entity)
            {
                if (!long.TryParse(owner.Value.Value, out long value) || value <= 0)
                    throw new InvalidOperationException("Entity variable owner is invalid.");
                var entityId = new EntityId(value);
                if (requireActiveEntity && _entityExists != null && !_entityExists(entityId))
                    throw new InvalidOperationException($"Entity variable owner {entityId} is not active.");
            }
        }
    }

    internal interface ICapacityCollection
    {
        StableId<CapacityIdKind> DefinitionId { get; }
        EntityId OwnerId { get; }
        int BaseCapacity { get; }
        int Capacity { get; }
        IReadOnlyList<EntityId> ItemIds { get; }
        IReadOnlyCollection<EntityId> DisabledItems { get; }
        bool RemoveEntity(EntityId entityId);
        void Deactivate();
        void Restore(SimulationWorld world, int baseCapacity, IReadOnlyList<EntityId> itemIds,
            IReadOnlyCollection<EntityId> disabledIds);
    }

    public readonly struct CapacityModifierContribution
    {
        private readonly Func<int> _magnitude;
        private readonly Func<bool> _active;
        private readonly int _snapshotMagnitude;
        private readonly bool _isSnapshot;
        public Guid ModifierInstanceId { get; }
        public int BindingIndex { get; }
        public ModifierOperation Operation { get; }
        public int Priority { get; }
        public long Sequence { get; }
        public int Magnitude => _isSnapshot ? _snapshotMagnitude : _magnitude();
        public bool IsActive => _active == null || _active();

        internal CapacityModifierContribution(Guid modifierInstanceId, int bindingIndex,
            ModifierOperation operation, int priority, long sequence, Func<int> magnitude, Func<bool> active)
        {
            ModifierInstanceId = modifierInstanceId;
            BindingIndex = bindingIndex;
            Operation = operation;
            Priority = priority;
            Sequence = sequence;
            _magnitude = magnitude;
            _active = active;
            _snapshotMagnitude = 0;
            _isSnapshot = false;
        }

        internal CapacityModifierContribution(Guid modifierInstanceId, int bindingIndex,
            ModifierOperation operation, int priority, long sequence, int snapshotMagnitude, Func<bool> active)
        {
            ModifierInstanceId = modifierInstanceId;
            BindingIndex = bindingIndex;
            Operation = operation;
            Priority = priority;
            Sequence = sequence;
            _magnitude = null;
            _active = active;
            _snapshotMagnitude = snapshotMagnitude;
            _isSnapshot = true;
        }
    }

    public sealed class CapacityDefinition<TOwner, TItem>
        where TOwner : WorldEntity where TItem : WorldEntity
    {
        public StableId<CapacityIdKind> Id { get; }
        public CapacityOverflowPolicy OverflowPolicy { get; }
        public CapacitySelectionPolicy SelectionPolicy { get; }
        public Func<TItem, DetS64> Ranking { get; }
        public Func<int, DetS64> OverCapacityPenalty { get; }

        public CapacityDefinition(string id, CapacityOverflowPolicy overflowPolicy,
            CapacitySelectionPolicy selectionPolicy = CapacitySelectionPolicy.OldestFirst,
            Func<TItem, DetS64> ranking = null, Func<int, DetS64> overCapacityPenalty = null)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Capacity ID is required.", nameof(id));
            Id = new StableId<CapacityIdKind>(id);
            OverflowPolicy = overflowPolicy;
            SelectionPolicy = selectionPolicy;
            Ranking = ranking;
            OverCapacityPenalty = overCapacityPenalty;
        }
    }

    public sealed class CapacityCollection<TOwner, TItem> : ICapacityCollection
        where TOwner : WorldEntity where TItem : WorldEntity
    {
        internal sealed class TransactionalState
        {
            public int BaseCapacity;
            public int Capacity;
            public TItem[] Items;
            public Dictionary<EntityId, long> MembershipSequences;
            public long NextMembershipSequence;
            public EntityId[] Disabled;
            public DetS64 CurrentOverCapacityPenalty;
            public bool RequiresOverflowDecision;
        }

        private readonly List<TItem> _items = new List<TItem>();
        private readonly Dictionary<EntityId, long> _membershipSequences =
            new Dictionary<EntityId, long>();
        private long _nextMembershipSequence = 1;
        private readonly HashSet<EntityId> _disabled = new HashSet<EntityId>();
        private readonly Action _changed;
        private readonly List<CapacityModifierContribution> _contributions =
            new List<CapacityModifierContribution>();
        private readonly IReadOnlyList<TItem> _itemsView;
        private readonly IReadOnlyList<CapacityModifierContribution> _contributionsView;
        private bool _active = true;
        public TOwner Owner { get; }
        public CapacityDefinition<TOwner, TItem> Definition { get; }
        public int BaseCapacity { get; private set; }
        public int Capacity { get; private set; }
        public IReadOnlyList<TItem> Items => _itemsView;
        public IReadOnlyCollection<EntityId> DisabledItems =>
            Array.AsReadOnly(_disabled.OrderBy(item => item).ToArray());
        public bool IsOverCapacity => _items.Count > Capacity;
        public int OverCapacityAmount => Math.Max(0, _items.Count - Capacity);
        public DetS64 CurrentOverCapacityPenalty { get; private set; }
        public bool RequiresOverflowDecision { get; private set; }
        public IReadOnlyList<CapacityModifierContribution> Modifiers => _contributionsView;
        public event Action<CapacityCollection<TOwner, TItem>> Changed;
        public event Action<CapacityCollection<TOwner, TItem>> OverflowDecisionRequested;
        StableId<CapacityIdKind> ICapacityCollection.DefinitionId => Definition.Id;
        EntityId ICapacityCollection.OwnerId => Owner.Id;
        IReadOnlyList<EntityId> ICapacityCollection.ItemIds => _items.Select(item => item.Id).ToArray();

        internal CapacityCollection(TOwner owner, CapacityDefinition<TOwner, TItem> definition, int capacity,
            Action changed = null)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _itemsView = _items.AsReadOnly();
            _contributionsView = _contributions.AsReadOnly();
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            BaseCapacity = capacity;
            Capacity = capacity;
            _changed = changed;
        }

        public bool TryAdd(TItem item)
        {
            Owner.World.EnsureMutationAllowed();
            EnsureActive();
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (!ReferenceEquals(item.World, Owner.World) ||
                !ReferenceEquals(Owner.World.Get<TItem>(item.Id), item))
                throw new InvalidOperationException("Capacity items must belong to the owner's world.");
            if (_items.Contains(item)) return false;
            if (_items.Count >= Capacity && Definition.OverflowPolicy == CapacityOverflowPolicy.PreserveAndBlockNew)
                return false;
            _items.Add(item);
            _membershipSequences[item.Id] = _nextMembershipSequence++;
            RefreshAfterMembershipChange();
            return true;
        }

        public bool Remove(TItem item)
        {
            Owner.World.EnsureMutationAllowed();
            EnsureActive();
            if (!_items.Remove(item)) return false;
            _membershipSequences.Remove(item.Id);
            _disabled.Remove(item.Id);
            RefreshAfterMembershipChange();
            return true;
        }

        public void SetCapacity(int value)
        {
            Owner.World.EnsureMutationAllowed();
            EnsureActive();
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            BaseCapacity = value;
            Recalculate();
        }

        public void ResolveOverflowDecision(CapacityDecisionAction action, IEnumerable<TItem> selected = null)
        {
            Owner.World.EnsureMutationAllowed();
            EnsureActive();
            if (!RequiresOverflowDecision)
                throw new InvalidOperationException("This capacity is not waiting for an overflow decision.");
            int excess = OverCapacityAmount;
            if (action == CapacityDecisionAction.Preserve)
            {
                RequiresOverflowDecision = false;
                NotifyChanged();
                return;
            }
            TItem[] choices = (selected ?? Enumerable.Empty<TItem>()).Distinct().ToArray();
            if (choices.Length != excess)
                throw new InvalidOperationException($"Exactly {excess} overflow entries must be selected.");
            if (choices.Any(item => item == null || !_items.Contains(item)))
                throw new InvalidOperationException("Every selected overflow entry must belong to the capacity.");
            if (action == CapacityDecisionAction.DisableSelected)
            {
                _disabled.Clear();
                foreach (TItem item in choices.OrderBy(item => item.Id)) _disabled.Add(item.Id);
            }
            else
            {
                foreach (TItem item in choices.OrderBy(item => item.Id))
                {
                    _items.Remove(item);
                    _membershipSequences.Remove(item.Id);
                }
                _disabled.RemoveWhere(id => choices.Any(item => item.Id == id));
            }
            RequiresOverflowDecision = false;
            CurrentOverCapacityPenalty = Definition.OverflowPolicy == CapacityOverflowPolicy.PreserveWithPenalty
                ? EvaluatePenalty() : DetS64.Zero;
            NotifyChanged();
        }

        internal void AddModifier(CapacityModifierContribution contribution)
        {
            if (contribution.ModifierInstanceId == Guid.Empty)
                throw new ArgumentException("A valid contribution is required.", nameof(contribution));
            _contributions.Add(contribution);
            _contributions.Sort((left, right) =>
            {
                int operation = OperationOrder(left.Operation).CompareTo(OperationOrder(right.Operation));
                if (operation != 0) return operation;
                int priority = left.Priority.CompareTo(right.Priority);
                return priority != 0 ? priority : left.Sequence.CompareTo(right.Sequence);
            });
            Recalculate();
        }

        internal void RemoveModifier(Guid instanceId, int bindingIndex)
        {
            if (_contributions.RemoveAll(item => item.ModifierInstanceId == instanceId &&
                item.BindingIndex == bindingIndex) > 0) Recalculate();
        }

        internal void Recalculate()
        {
            int value = BaseCapacity;
            IAttributeValuePolicy<int> policy = NumericValuePolicies.Int32();
            foreach (CapacityModifierContribution contribution in _contributions)
                if (contribution.IsActive)
                    value = policy.Apply(value, contribution.Operation, contribution.Magnitude);
            if (value < 0) throw new InvalidOperationException($"Capacity {Definition.Id} evaluated below zero.");
            if (Definition.OverflowPolicy == CapacityOverflowPolicy.ClampReductionToUsage)
                value = Math.Max(value, _items.Count);
            Capacity = value;
            ResolveOverflow();
            NotifyChanged();
        }

        internal TransactionalState CaptureTransactionalState() =>
            new TransactionalState
            {
                BaseCapacity = BaseCapacity,
                Capacity = Capacity,
                Items = _items.ToArray(),
                MembershipSequences = new Dictionary<EntityId, long>(_membershipSequences),
                NextMembershipSequence = _nextMembershipSequence,
                Disabled = _disabled.OrderBy(item => item).ToArray(),
                CurrentOverCapacityPenalty = CurrentOverCapacityPenalty,
                RequiresOverflowDecision = RequiresOverflowDecision,
            };

        internal void RestoreTransactionalState(TransactionalState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            EnsureActive();
            _items.Clear();
            _items.AddRange(state.Items);
            _membershipSequences.Clear();
            foreach (KeyValuePair<EntityId, long> pair in state.MembershipSequences)
                _membershipSequences.Add(pair.Key, pair.Value);
            _nextMembershipSequence = state.NextMembershipSequence;
            _disabled.Clear();
            foreach (EntityId id in state.Disabled) _disabled.Add(id);
            BaseCapacity = state.BaseCapacity;
            Capacity = state.Capacity;
            CurrentOverCapacityPenalty = state.CurrentOverCapacityPenalty;
            RequiresOverflowDecision = state.RequiresOverflowDecision;
            NotifyChanged();
        }

        private void ResolveOverflow()
        {
            _disabled.Clear();
            CurrentOverCapacityPenalty = DetS64.Zero;
            RequiresOverflowDecision = false;
            int excess = _items.Count - Capacity;
            if (excess <= 0) return;
            if (Definition.OverflowPolicy == CapacityOverflowPolicy.PreserveWithPenalty)
            {
                CurrentOverCapacityPenalty = EvaluatePenalty();
                return;
            }
            if (Definition.OverflowPolicy == CapacityOverflowPolicy.RequestDecision ||
                ((Definition.OverflowPolicy == CapacityOverflowPolicy.DisableExcess ||
                  Definition.OverflowPolicy == CapacityOverflowPolicy.RemoveExcess) &&
                 Definition.SelectionPolicy == CapacitySelectionPolicy.PlayerSelection))
            {
                RequiresOverflowDecision = true;
                Action<CapacityCollection<TOwner, TItem>> handlers = OverflowDecisionRequested;
                if (handlers != null)
                    foreach (Delegate handler in handlers.GetInvocationList())
                    {
                        Delegate capturedHandler = handler;
                        Owner.World.PublishEffectAwareNotification(capturedHandler,
                            () => ((Action<CapacityCollection<TOwner, TItem>>)capturedHandler)(this));
                    }
                return;
            }
            if (Definition.OverflowPolicy != CapacityOverflowPolicy.DisableExcess &&
                Definition.OverflowPolicy != CapacityOverflowPolicy.RemoveExcess) return;
            TItem[] selected = SelectItems(excess).ToArray();
            if (Definition.OverflowPolicy == CapacityOverflowPolicy.DisableExcess)
                foreach (TItem item in selected) _disabled.Add(item.Id);
            else
                foreach (TItem item in selected)
                {
                    _items.Remove(item);
                    _membershipSequences.Remove(item.Id);
                }
        }

        private IEnumerable<TItem> SelectItems(int count)
        {
            IEnumerable<TItem> ordered;
            switch (Definition.SelectionPolicy)
            {
                case CapacitySelectionPolicy.NewestFirst:
                    ordered = _items.OrderByDescending(item => _membershipSequences[item.Id])
                        .ThenBy(item => item.Id);
                    break;
                case CapacitySelectionPolicy.LowestPriority:
                case CapacitySelectionPolicy.ExplicitRanking:
                    if (Definition.Ranking == null) throw new InvalidOperationException("This selection policy requires a ranking function.");
                    ordered = _items.OrderBy(Definition.Ranking).ThenBy(item => item.Id); break;
                case CapacitySelectionPolicy.HighestUpkeep:
                    if (Definition.Ranking == null) throw new InvalidOperationException("HighestUpkeep requires a ranking function.");
                    ordered = _items.OrderByDescending(Definition.Ranking).ThenBy(item => item.Id); break;
                case CapacitySelectionPolicy.PlayerSelection:
                    throw new InvalidOperationException("Overflow requires an explicit player selection.");
                default:
                    ordered = _items.OrderBy(item => _membershipSequences[item.Id])
                        .ThenBy(item => item.Id);
                    break;
            }
            return ordered.Take(count);
        }

        private DetS64 EvaluatePenalty()
        {
            DetS64 penalty = Definition.OverCapacityPenalty?.Invoke(OverCapacityAmount) ??
                DetS64.FromLong(OverCapacityAmount);
            if (penalty < DetS64.Zero)
                throw new InvalidOperationException("Over-capacity penalty must be non-negative.");
            return penalty;
        }

        void ICapacityCollection.Restore(SimulationWorld world, int baseCapacity, IReadOnlyList<EntityId> itemIds,
            IReadOnlyCollection<EntityId> disabledIds)
        {
            _items.Clear();
            _membershipSequences.Clear();
            _nextMembershipSequence = 1;
            foreach (EntityId id in itemIds)
            {
                TItem item = world.Get<TItem>(id);
                if (item == null) throw new InvalidOperationException($"Capacity item {id} is missing.");
                _items.Add(item);
                _membershipSequences[item.Id] = _nextMembershipSequence++;
            }
            BaseCapacity = baseCapacity;
            Capacity = Definition.OverflowPolicy == CapacityOverflowPolicy.ClampReductionToUsage
                ? Math.Max(baseCapacity, _items.Count)
                : baseCapacity;
            _disabled.Clear();
            foreach (EntityId id in disabledIds) _disabled.Add(id);
            int excess = OverCapacityAmount;
            CurrentOverCapacityPenalty = Definition.OverflowPolicy == CapacityOverflowPolicy.PreserveWithPenalty &&
                excess > 0 ? EvaluatePenalty() : DetS64.Zero;
            RequiresOverflowDecision = excess > 0 &&
                (Definition.OverflowPolicy == CapacityOverflowPolicy.RequestDecision ||
                 ((Definition.OverflowPolicy == CapacityOverflowPolicy.DisableExcess ||
                   Definition.OverflowPolicy == CapacityOverflowPolicy.RemoveExcess) &&
                  Definition.SelectionPolicy == CapacitySelectionPolicy.PlayerSelection));
            NotifyChanged();
        }

        bool ICapacityCollection.RemoveEntity(EntityId entityId)
        {
            int removed = _items.RemoveAll(item => item.Id == entityId);
            if (removed != 0) _membershipSequences.Remove(entityId);
            bool disabled = _disabled.Remove(entityId);
            if (removed == 0 && !disabled) return false;
            RefreshAfterMembershipChange();
            return true;
        }

        void ICapacityCollection.Deactivate() => _active = false;

        private void EnsureActive()
        {
            if (!_active || !ReferenceEquals(Owner.World.Get<TOwner>(Owner.Id), Owner))
                throw new ObjectDisposedException($"Capacity {Definition.Id}");
        }

        private void RefreshAfterMembershipChange()
        {
            if (Definition.OverflowPolicy == CapacityOverflowPolicy.ClampReductionToUsage)
                Recalculate();
            else
            {
                ResolveOverflow();
                NotifyChanged();
            }
        }

        private void NotifyChanged()
        {
            _changed?.Invoke();
            Action<CapacityCollection<TOwner, TItem>> handlers = Changed;
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                Delegate capturedHandler = handler;
                Owner.World.PublishEffectAwareNotification(capturedHandler,
                    () => ((Action<CapacityCollection<TOwner, TItem>>)capturedHandler)(this));
            }
        }

        private static int OperationOrder(ModifierOperation operation)
        {
            switch (operation)
            {
                case ModifierOperation.Add: return 0;
                case ModifierOperation.Multiply: return 1;
                case ModifierOperation.Replace: return 2;
                default: return 3;
            }
        }
    }

    public readonly struct EffectValidation
    {
        public bool IsValid { get; }
        public string Reason { get; }
        private EffectValidation(bool isValid, string reason) { IsValid = isValid; Reason = reason ?? string.Empty; }
        public static EffectValidation Valid() => new EffectValidation(true, null);
        public static EffectValidation Rejected(string reason) => new EffectValidation(false, reason);
    }

    public readonly struct EffectResult
    {
        public EffectStatus Status { get; }
        public string Message { get; }
        public IReadOnlyList<object> Events { get; }
        public EffectResult(EffectStatus status, string message, IReadOnlyList<object> events = null)
        {
            Status = status;
            Message = message ?? string.Empty;
            Events = events == null
                ? (IReadOnlyList<object>)Array.Empty<object>()
                : Array.AsReadOnly(events.ToArray());
        }
    }

    public sealed class EffectTransaction
    {
        private readonly List<Tuple<Action, Action>> _steps = new List<Tuple<Action, Action>>();
        public int StepCount => _steps.Count;
        internal void Add(Action commit, Action rollback = null) =>
            _steps.Add(Tuple.Create(commit ?? throw new ArgumentNullException(nameof(commit)), rollback));

        internal void Commit(EffectAtomicity atomicity)
        {
            if (atomicity == EffectAtomicity.Atomic)
            {
                int missingRollback = _steps.FindIndex(step => step.Item2 == null);
                if (missingRollback >= 0)
                    throw new InvalidOperationException(
                        $"Atomic effect step {missingRollback} has no rollback action.");
            }

            int applying = 0;
            try
            {
                for (; applying < _steps.Count; applying++) _steps[applying].Item1();
            }
            catch (Exception commitFailure)
            {
                if (atomicity == EffectAtomicity.Atomic)
                {
                    // The currently executing step may have mutated state before throwing. Its
                    // rollback therefore runs as well as every previously completed rollback.
                    var failures = new List<Exception> { commitFailure };
                    for (int index = Math.Min(applying, _steps.Count - 1); index >= 0; index--)
                    {
                        try { _steps[index].Item2?.Invoke(); }
                        catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
                    }
                    if (failures.Count > 1)
                        throw new AggregateException("Effect commit and one or more rollback steps failed.", failures);
                }
                throw;
            }
        }
    }

    public readonly struct TriggerEvaluationNotice
    {
        public StableId<TriggerIdKind> TriggerId { get; }
        public bool IsActive { get; }
        public string Explanation { get; }

        internal TriggerEvaluationNotice(StableId<TriggerIdKind> triggerId, bool isActive,
            string explanation)
        {
            TriggerId = triggerId;
            IsActive = isActive;
            Explanation = explanation ?? string.Empty;
        }
    }

    public sealed class TriggerModifierLink<TState, TSource, TParameters> : IDisposable
        where TSource : WorldEntity
    {
        private readonly SimulationWorld _world;
        private readonly TriggerInstance<TState> _trigger;
        private readonly ModifierDefinition<TSource, TParameters> _modifier;
        private readonly Func<TState, TSource> _source;
        private readonly Func<TState, TParameters> _parameters;
        private readonly Func<TState, string> _stackingKey;
        private ModifierInstance _instance;
        private TSource _lastSource;
        private TParameters _lastParameters;
        private bool _hasLastParameters;
        private bool _reconciling;
        private bool _disposed;

        internal TriggerModifierLink(SimulationWorld world, TriggerInstance<TState> trigger,
            ModifierDefinition<TSource, TParameters> modifier, Func<TState, TSource> source,
            Func<TState, TParameters> parameters, Func<TState, string> stackingKey)
        {
            _world = world;
            _trigger = trigger;
            _modifier = modifier;
            _source = source;
            _parameters = parameters;
            _stackingKey = stackingKey;
            _world.TriggerEvaluated += OnTriggerEvaluated;
            _world.RuntimeStateRestored += OnRuntimeStateRestored;
            Reconcile();
        }

        public ModifierInstance ProducedModifier => _instance;

        private void OnTriggerEvaluated(TriggerEvaluationNotice notice)
        {
            if (notice.TriggerId == _trigger.Definition.Id) Reconcile();
        }

        private void OnRuntimeStateRestored() => Reconcile();

        private void Reconcile()
        {
            if (_disposed || _reconciling || _trigger.IsDisposed ||
                _world.IsRestoringRuntimeState) return;
            _reconciling = true;
            try
            {
                if (!_trigger.IsActive)
                {
                    RemoveProducedModifier();
                    return;
                }

                TSource source = _source(_trigger.State);
                if (source == null)
                    throw new InvalidOperationException("A trigger modifier source cannot be null.");
                TParameters parameters = _parameters(_trigger.State);
                string stackingKey = ResolveStackingKey();
                bool unchanged = _instance != null && !_instance.Removed &&
                    ReferenceEquals(source, _lastSource) && _hasLastParameters &&
                    EqualityComparer<TParameters>.Default.Equals(parameters, _lastParameters);
                if (unchanged) return;

                RemoveProducedModifier();
                _instance = _world.Modifiers.FirstOrDefault(item =>
                    item.DefinitionId == _modifier.Id &&
                    ReferenceEquals(item.Source, source) &&
                    string.Equals(item.StackingKey, stackingKey, StringComparison.Ordinal) &&
                    item.Parameters is TParameters existingParameters &&
                    EqualityComparer<TParameters>.Default.Equals(existingParameters, parameters));
                if (_instance == null)
                    _instance = _world.ApplyModifier(_modifier, source, parameters, stackingKey);
                _lastSource = source;
                _lastParameters = parameters;
                _hasLastParameters = true;
            }
            finally { _reconciling = false; }
        }

        private string ResolveStackingKey() =>
            _stackingKey?.Invoke(_trigger.State) ??
            $"trigger:{_trigger.Definition.Id.Value}";

        private void RemoveProducedModifier()
        {
            if (_instance != null && !_instance.Removed) _world.RemoveModifier(_instance);
            _instance = null;
            _lastSource = null;
            _lastParameters = default;
            _hasLastParameters = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _world.TriggerEvaluated -= OnTriggerEvaluated;
            _world.RuntimeStateRestored -= OnRuntimeStateRestored;
            RemoveProducedModifier();
        }
    }

    public sealed class EffectEntityReference<TEntity> where TEntity : WorldEntity
    {
        public TEntity Value { get; internal set; }
    }

    public sealed class EffectValueReference<TValue>
    {
        public TValue Value { get; internal set; }
    }

    public sealed class EffectContext
    {
        private readonly List<object> _events = new List<object>();
        private readonly IReadOnlyList<object> _eventsView;
        public SimulationWorld World { get; }
        public EffectTransaction Transaction { get; } = new EffectTransaction();
        public IDeterministicRandom Random { get; }
        public Guid? ExecutionId { get; }
        public IReadOnlyList<object> Events => _eventsView;

        internal EffectContext(SimulationWorld world, IDeterministicRandom random, Guid? executionId)
        {
            World = world;
            Random = random;
            ExecutionId = executionId;
            _eventsView = _events.AsReadOnly();
        }
        public void Stage(Action commit, Action rollback = null) => Transaction.Add(commit, rollback);
        public void Emit(object domainEvent) => _events.Add(domainEvent ?? throw new ArgumentNullException(nameof(domainEvent)));

        public void StageSetBaseValue<TEntity, TValue>(GameAttribute<TEntity, TValue> attribute, TValue value,
            string reason = null) where TEntity : WorldEntity
        {
            if (attribute == null) throw new ArgumentNullException(nameof(attribute));
            GameAttribute<TEntity, TValue>.TransactionalState previous = null;
            Stage(() =>
                {
                    previous = attribute.CaptureTransactionalState();
                    attribute.SetBaseValue(value, reason);
                },
                () => attribute.RestoreTransactionalState(previous));
        }

        public void StageAddRelation<TFrom, TTo>(TFrom from, RelationDefinition<TFrom, TTo> relation, TTo to)
            where TFrom : WorldEntity where TTo : WorldEntity
        {
            bool changed = false;
            Stage(() =>
                {
                    if (World.HasRelation(from, relation, to)) return;
                    World.AddRelation(from, relation, to);
                    changed = true;
                },
                () =>
                {
                    if (changed) World.RemoveRelation(from, relation, to);
                });
        }

        public void StageRemoveRelation<TFrom, TTo>(TFrom from, RelationDefinition<TFrom, TTo> relation, TTo to)
            where TFrom : WorldEntity where TTo : WorldEntity
        {
            bool changed = false;
            Stage(() => changed = World.RemoveRelation(from, relation, to),
                () =>
                {
                    if (changed) World.AddRelation(from, relation, to);
                });
        }

        public void StageAddTag<TEntity>(TEntity entity, TagDefinition<TEntity> definition)
            where TEntity : WorldEntity
        {
            bool changed = false;
            Stage(() => changed = World.AddTag(entity, definition),
                () =>
                {
                    if (changed) World.RemoveTag(entity, definition);
                });
        }

        public void StageRemoveTag<TEntity>(TEntity entity, TagDefinition<TEntity> definition)
            where TEntity : WorldEntity
        {
            bool changed = false;
            Stage(() => changed = World.RemoveTag(entity, definition),
                () =>
                {
                    if (changed) World.AddTag(entity, definition);
                });
        }

        public void StageSetComponent<TEntity, TValue>(TEntity entity,
            ComponentDefinition<TEntity, TValue> definition, TValue value) where TEntity : WorldEntity
        {
            bool existed = false;
            TValue previous = default;
            Stage(() =>
                {
                    existed = entity.TryGetComponent(definition, out previous);
                    World.SetComponent(entity, definition, value);
                },
                () =>
                {
                    if (existed) World.SetComponent(entity, definition, previous);
                    else World.RemoveComponent(entity, definition);
                });
        }

        public void StageRemoveComponent<TEntity, TValue>(TEntity entity,
            ComponentDefinition<TEntity, TValue> definition) where TEntity : WorldEntity
        {
            bool existed = false;
            TValue previous = default;
            Stage(() =>
                {
                    existed = entity.TryGetComponent(definition, out previous);
                    World.RemoveComponent(entity, definition);
                },
                () =>
                {
                    if (existed) World.SetComponent(entity, definition, previous);
                });
        }

        public void StageAddCollectionItem<TOwner, TItem>(
            DeclarativeCollection<TOwner, TItem> collection, TItem item)
            where TOwner : WorldEntity where TItem : WorldEntity
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            bool changed = false;
            Stage(() => changed = collection.AddBase(item),
                () =>
                {
                    if (changed) collection.RemoveBase(item);
                });
        }

        public void StageRemoveCollectionItem<TOwner, TItem>(
            DeclarativeCollection<TOwner, TItem> collection, TItem item)
            where TOwner : WorldEntity where TItem : WorldEntity
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            bool changed = false;
            Stage(() => changed = collection.RemoveBase(item),
                () =>
                {
                    if (changed) collection.AddBase(item);
                });
        }

        public void StageAddCapacityItem<TOwner, TItem>(
            CapacityCollection<TOwner, TItem> capacity, TItem item)
            where TOwner : WorldEntity where TItem : WorldEntity
        {
            if (capacity == null) throw new ArgumentNullException(nameof(capacity));
            CapacityCollection<TOwner, TItem>.TransactionalState previous = null;
            Stage(() =>
                {
                    previous = capacity.CaptureTransactionalState();
                    if (!capacity.TryAdd(item))
                        throw new InvalidOperationException($"Item {item?.Id} could not be added to capacity.");
                },
                () => capacity.RestoreTransactionalState(previous));
        }

        public void StageRemoveCapacityItem<TOwner, TItem>(
            CapacityCollection<TOwner, TItem> capacity, TItem item)
            where TOwner : WorldEntity where TItem : WorldEntity
        {
            if (capacity == null) throw new ArgumentNullException(nameof(capacity));
            CapacityCollection<TOwner, TItem>.TransactionalState previous = null;
            Stage(() =>
                {
                    previous = capacity.CaptureTransactionalState();
                    if (!capacity.Remove(item))
                        throw new InvalidOperationException($"Item {item?.Id} is not in the capacity.");
                },
                () => capacity.RestoreTransactionalState(previous));
        }

        public void StageSetCapacity<TOwner, TItem>(CapacityCollection<TOwner, TItem> capacity,
            int value) where TOwner : WorldEntity where TItem : WorldEntity
        {
            if (capacity == null) throw new ArgumentNullException(nameof(capacity));
            CapacityCollection<TOwner, TItem>.TransactionalState previous = null;
            Stage(() =>
                {
                    previous = capacity.CaptureTransactionalState();
                    capacity.SetCapacity(value);
                },
                () => capacity.RestoreTransactionalState(previous));
        }

        public void StageResolveOverflowDecision<TOwner, TItem>(
            CapacityCollection<TOwner, TItem> capacity, CapacityDecisionAction action,
            IEnumerable<TItem> selected = null)
            where TOwner : WorldEntity where TItem : WorldEntity
        {
            if (capacity == null) throw new ArgumentNullException(nameof(capacity));
            TItem[] selection = selected?.ToArray();
            CapacityCollection<TOwner, TItem>.TransactionalState previous = null;
            Stage(() =>
                {
                    previous = capacity.CaptureTransactionalState();
                    capacity.ResolveOverflowDecision(action, selection);
                },
                () => capacity.RestoreTransactionalState(previous));
        }

        public EffectValueReference<CapabilityContributionHandle> StageContributeCapability<TEntity>(
            TEntity owner, CapabilityDefinition<TEntity> definition, CapabilityContribution decision,
            WorldEntity source = null, int priority = 0, string sourceDescription = null)
            where TEntity : WorldEntity
        {
            var reference = new EffectValueReference<CapabilityContributionHandle>();
            Stage(() => reference.Value = World.ContributeCapability(owner, definition, decision,
                    source, priority, sourceDescription),
                () =>
                {
                    reference.Value?.Dispose();
                    reference.Value = null;
                });
            return reference;
        }

        public void StageRemoveCapability(CapabilityContributionHandle contribution)
        {
            if (contribution == null) throw new ArgumentNullException(nameof(contribution));
            CapabilityContributionRollbackState previous = null;
            Stage(() =>
                {
                    previous = World.RemoveCapabilityContributionForEffect(contribution.Id);
                    if (previous == null)
                        throw new InvalidOperationException(
                            $"Capability contribution {contribution.Id} is not active in this world.");
                },
                () =>
                {
                    if (previous != null) World.RestoreCapabilityContributionForEffect(previous);
                });
        }

        public EffectValueReference<DeclarativeCollection<TOwner, TItem>>
            StageCreateCollection<TOwner, TItem>(TOwner owner,
                CollectionDefinition<TOwner, TItem> definition, IEnumerable<TItem> baseItems = null)
            where TOwner : WorldEntity where TItem : WorldEntity
        {
            TItem[] capturedItems = baseItems?.ToArray();
            var reference =
                new EffectValueReference<DeclarativeCollection<TOwner, TItem>>();
            Stage(() => reference.Value = World.CreateCollection(owner, definition, capturedItems),
                () =>
                {
                    if (reference.Value != null) World.RemoveCollection(owner, definition);
                    reference.Value = null;
                });
            return reference;
        }

        public EffectValueReference<CapacityCollection<TOwner, TItem>>
            StageCreateCapacity<TOwner, TItem>(TOwner owner,
                CapacityDefinition<TOwner, TItem> definition, int capacity)
            where TOwner : WorldEntity where TItem : WorldEntity
        {
            var reference =
                new EffectValueReference<CapacityCollection<TOwner, TItem>>();
            Stage(() => reference.Value = World.CreateCapacity(owner, definition, capacity),
                () =>
                {
                    if (reference.Value != null) World.RemoveCapacity(owner, definition);
                    reference.Value = null;
                });
            return reference;
        }

        public EffectEntityReference<TEntity> StageCreate<TEntity>(Action<TEntity> initialize = null)
            where TEntity : WorldEntity, new()
        {
            var reference = new EffectEntityReference<TEntity>();
            Stage(() => reference.Value = World.Create(initialize),
                () =>
                {
                    if (reference.Value != null) World.Destroy(reference.Value);
                    reference.Value = null;
                });
            return reference;
        }

        public void StageDestroy(WorldEntity entity, Action rollback)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (rollback == null)
                throw new ArgumentNullException(nameof(rollback),
                    "Destroy operations require an explicit domain restoration action.");
            Stage(() =>
                {
                    if (!World.Destroy(entity))
                        throw new InvalidOperationException($"Entity {entity.Id} could not be destroyed.");
                }, rollback);
        }

        public void StageApplyModifier<TSource, TParameters>(ModifierDefinition<TSource, TParameters> definition,
            TSource source, TParameters parameters, string stackingKey = null) where TSource : WorldEntity
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (definition.Stacking.Mode == StackingMode.Replace ||
                definition.Stacking.Mode == StackingMode.KeepStrongest ||
                definition.Stacking.Mode == StackingMode.GroupBySource)
                throw new InvalidOperationException(
                    "Destructive stacking policies require an effect-specific compensation step.");
            ModifierInstance applied = null;
            ModifierInstance existing = null;
            long? existingExpiration = null;
            Stage(() =>
                {
                    var before = new HashSet<ModifierInstance>(World.Modifiers);
                    var expirations = before.ToDictionary(item => item, item => item.ExpirationTick);
                    applied = World.ApplyModifier(definition, source, parameters, stackingKey);
                    if (before.Contains(applied))
                    {
                        existing = applied;
                        existingExpiration = expirations[applied];
                    }
                },
                () =>
                {
                    if (applied == null) return;
                    if (existing == null) World.RemoveModifier(applied);
                    else existing.ExpirationTick = existingExpiration;
                });
        }
    }

    public interface IGameEffect<TParameters>
    {
        StableId<EffectIdKind> Id { get; }
        bool IsIdempotent { get; }
        EffectAtomicity Atomicity { get; }
        EffectReversibility Reversibility { get; }
        EffectValidation Validate(SimulationWorld world, TParameters parameters);
        EffectStatus Stage(EffectContext context, TParameters parameters);
    }

    /// <summary>
    /// Explicit post-commit reversal contract for effects that declare
    /// <see cref="EffectReversibility.Compensation"/>.
    /// </summary>
    public interface ICompensatingGameEffect<TParameters> : IGameEffect<TParameters>
    {
        EffectValidation ValidateCompensation(SimulationWorld world, TParameters parameters,
            Guid executionId);
        EffectStatus StageCompensation(EffectContext context, TParameters parameters,
            Guid executionId);
    }

    public interface IRandomizedGameEffect
    {
        StableId<RandomStreamIdKind> RandomStreamId { get; }
        int ExpectedRandomDrawCount { get; }
    }

    public interface IEffectEventContract
    {
        IReadOnlyCollection<Type> EmittedEventTypes { get; }
    }

    public sealed class DomainEventDispatchFailure
    {
        public object DomainEvent { get; }
        public Delegate Handler { get; }
        public Exception Exception { get; }

        internal DomainEventDispatchFailure(object domainEvent, Delegate handler, Exception exception)
        {
            DomainEvent = domainEvent;
            Handler = handler;
            Exception = exception;
        }
    }

    public sealed class EffectObserverDispatchFailure
    {
        public Delegate Handler { get; }
        public Exception Exception { get; }

        internal EffectObserverDispatchFailure(Delegate handler, Exception exception)
        {
            Handler = handler;
            Exception = exception;
        }
    }

    internal sealed class CountingDeterministicRandom : IDeterministicRandom
    {
        private readonly IDeterministicRandom _inner;
        public int DrawCount { get; private set; }
        public ulong State => _inner.State;

        public CountingDeterministicRandom(IDeterministicRandom inner) =>
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        public int NextInt(int minimumInclusive, int maximumExclusive)
        {
            DrawCount++;
            return _inner.NextInt(minimumInclusive, maximumExclusive);
        }
    }

    public sealed partial class SimulationWorld
    {
        private int _effectStagingDepth;
        private int _effectCommitAuthorizationDepth;
        private EffectObservationScope _effectObservationScope;
        private readonly Dictionary<Tuple<string, EntityId>, IDeclarativeCollection> _declarativeCollections =
            new Dictionary<Tuple<string, EntityId>, IDeclarativeCollection>();
        private readonly Dictionary<string, object> _collectionDefinitions =
            new Dictionary<string, object>(StringComparer.Ordinal);
        private readonly Dictionary<Tuple<Type, string, EntityId>, object> _counters =
            new Dictionary<Tuple<Type, string, EntityId>, object>();
        private readonly HashSet<Guid> _completedEffectExecutions = new HashSet<Guid>();
        private readonly HashSet<Guid> _compensatedEffectExecutions = new HashSet<Guid>();
        private readonly Dictionary<Tuple<string, EntityId>, ICapacityCollection> _capacities =
            new Dictionary<Tuple<string, EntityId>, ICapacityCollection>();
        private readonly Dictionary<string, object> _capacityDefinitions =
            new Dictionary<string, object>(StringComparer.Ordinal);
        public TypedVariableStore Variables { get; private set; }
        public event Action<object> DomainEventEmitted;
        public event Action<DomainEventDispatchFailure> DomainEventDispatchFailed;
        public event Action<EffectObserverDispatchFailure> EffectObserverDispatchFailed;
        private readonly SortedDictionary<long, ITriggerInstance> _triggers =
            new SortedDictionary<long, ITriggerInstance>();
        private readonly Dictionary<string, ITriggerInstance> _triggersByDefinition =
            new Dictionary<string, ITriggerInstance>(StringComparer.Ordinal);
        private readonly Dictionary<QueryDependencyKey, SortedSet<long>> _triggerDependencyIndex =
            new Dictionary<QueryDependencyKey, SortedSet<long>>();
        private readonly SortedSet<long> _timeDependentTriggerIds = new SortedSet<long>();
        private long _nextTriggerRegistrationId = 1;
        public event Action<TriggerTransition> TriggerTransitioned;
        public event Action<TriggerEvaluationNotice> TriggerEvaluated;
        public event Action<TriggerEvaluationFailure> TriggerEvaluationFailed;

        internal void EnsureMutationAllowed()
        {
            if (_effectStagingDepth > 0 && _effectCommitAuthorizationDepth == 0)
                throw new InvalidOperationException(
                    "Effects may mutate simulation state only from staged transaction commit actions.");
        }

        private IDisposable BeginEffectStaging() =>
            new EffectMutationScope(this, false);

        private IDisposable AuthorizeEffectCommit() =>
            new EffectMutationScope(this, true);

        internal void PublishEffectAwareNotification(Delegate handler, Action notification)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (notification == null) throw new ArgumentNullException(nameof(notification));
            if (_effectObservationScope != null)
            {
                _effectObservationScope.Enqueue(handler, notification);
                return;
            }
            notification();
        }

        private EffectObservationScope BeginEffectObservation() =>
            new EffectObservationScope(this, _effectObservationScope);

        private void ReportEffectObserverFailure(Delegate handler, Exception exception)
        {
            Action<EffectObserverDispatchFailure> handlers = EffectObserverDispatchFailed;
            if (handlers == null) return;
            var failure = new EffectObserverDispatchFailure(handler, exception);
            foreach (Delegate failureHandler in handlers.GetInvocationList())
            {
                try { ((Action<EffectObserverDispatchFailure>)failureHandler)(failure); }
                catch { }
            }
        }

        private sealed class EffectObservationScope : IDisposable
        {
            private SimulationWorld _world;
            private readonly EffectObservationScope _previous;
            private readonly List<Tuple<Delegate, Action>> _notifications =
                new List<Tuple<Delegate, Action>>();
            private readonly long _version;
            private readonly long _structureQueryVersion;
            private readonly bool _versionPublicationPending;
            private readonly QueryDependencyKey[] _pendingQueryDependencyChanges;
            private readonly Dictionary<object, long> _relationQueryVersions;
            private readonly Dictionary<object, Dictionary<EntityId, long>> _relationSourceQueryVersions;
            private readonly Dictionary<IAttributeDefinition, long> _attributeQueryVersions;
            private readonly Dictionary<IAttributeDefinition, Dictionary<EntityId, long>>
                _entityAttributeQueryVersions;
            private readonly long _nextEntityId;
            private readonly long _nextModifierSequence;
            private readonly long _nextContributionSequence;
            private readonly long _nextRelationMutationSequence;
            private bool _completed;

            public EffectObservationScope(SimulationWorld world, EffectObservationScope previous)
            {
                _world = world;
                _previous = previous;
                _version = world.Version;
                _structureQueryVersion = world._structureQueryVersion;
                _versionPublicationPending = world._versionPublicationPending;
                _pendingQueryDependencyChanges = world._pendingQueryDependencyChanges.ToArray();
                _relationQueryVersions = new Dictionary<object, long>(world._relationQueryVersions);
                _relationSourceQueryVersions = CloneNested(world._relationSourceQueryVersions);
                _attributeQueryVersions =
                    new Dictionary<IAttributeDefinition, long>(world._attributeQueryVersions);
                _entityAttributeQueryVersions = CloneNested(world._entityAttributeQueryVersions);
                _nextEntityId = world._nextEntityId;
                _nextModifierSequence = world._nextModifierSequence;
                _nextContributionSequence = world._nextContributionSequence;
                _nextRelationMutationSequence = world._nextRelationMutationSequence;
                world._effectObservationScope = this;
            }

            public void Enqueue(Delegate handler, Action notification) =>
                _notifications.Add(Tuple.Create(handler, notification));

            public void Complete()
            {
                SimulationWorld world = _world;
                if (world == null || _completed) return;
                _completed = true;
                world._effectObservationScope = _previous;
                foreach (Tuple<Delegate, Action> notification in _notifications)
                {
                    try { notification.Item2(); }
                    catch (Exception exception)
                    {
                        world.ReportEffectObserverFailure(notification.Item1, exception);
                    }
                }
                _notifications.Clear();
            }

            public void Dispose()
            {
                SimulationWorld world = _world;
                if (world == null) return;
                _world = null;
                if (_completed) return;
                world._effectObservationScope = _previous;
                _notifications.Clear();
                world.Version = _version;
                world._structureQueryVersion = _structureQueryVersion;
                world._versionPublicationPending = _versionPublicationPending;
                world._pendingQueryDependencyChanges.Clear();
                foreach (QueryDependencyKey change in _pendingQueryDependencyChanges)
                    world._pendingQueryDependencyChanges.Add(change);
                RestoreDictionary(world._relationQueryVersions, _relationQueryVersions);
                RestoreNested(world._relationSourceQueryVersions, _relationSourceQueryVersions);
                RestoreDictionary(world._attributeQueryVersions, _attributeQueryVersions);
                RestoreNested(world._entityAttributeQueryVersions, _entityAttributeQueryVersions);
                world._nextEntityId = _nextEntityId;
                world._nextModifierSequence = _nextModifierSequence;
                world._nextContributionSequence = _nextContributionSequence;
                world._nextRelationMutationSequence = _nextRelationMutationSequence;
            }

            private static Dictionary<TKey, Dictionary<EntityId, long>> CloneNested<TKey>(
                Dictionary<TKey, Dictionary<EntityId, long>> source)
            {
                var clone = new Dictionary<TKey, Dictionary<EntityId, long>>();
                foreach (KeyValuePair<TKey, Dictionary<EntityId, long>> pair in source)
                    clone.Add(pair.Key, new Dictionary<EntityId, long>(pair.Value));
                return clone;
            }

            private static void RestoreDictionary<TKey>(Dictionary<TKey, long> target,
                Dictionary<TKey, long> source)
            {
                target.Clear();
                foreach (KeyValuePair<TKey, long> pair in source) target.Add(pair.Key, pair.Value);
            }

            private static void RestoreNested<TKey>(
                Dictionary<TKey, Dictionary<EntityId, long>> target,
                Dictionary<TKey, Dictionary<EntityId, long>> source)
            {
                target.Clear();
                foreach (KeyValuePair<TKey, Dictionary<EntityId, long>> pair in source)
                    target.Add(pair.Key, new Dictionary<EntityId, long>(pair.Value));
            }
        }

        private sealed class EffectMutationScope : IDisposable
        {
            private SimulationWorld _world;
            private readonly bool _commit;
            public EffectMutationScope(SimulationWorld world, bool commit)
            {
                _world = world;
                _commit = commit;
                if (commit) world._effectCommitAuthorizationDepth++;
                else world._effectStagingDepth++;
            }
            public void Dispose()
            {
                SimulationWorld world = _world;
                if (world == null) return;
                _world = null;
                if (_commit) world._effectCommitAuthorizationDepth--;
                else world._effectStagingDepth--;
            }
        }

        public DeclarativeCollection<TOwner, TItem> CreateCollection<TOwner, TItem>(TOwner owner,
            CollectionDefinition<TOwner, TItem> definition, IEnumerable<TItem> baseItems = null)
            where TOwner : WorldEntity where TItem : WorldEntity
        {
            EnsureMutationAllowed();
            RequireOwned(owner);
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (_collectionDefinitions.TryGetValue(definition.Id.Value, out object registered))
            {
                if (!ReferenceEquals(registered, definition))
                    throw new InvalidOperationException(
                        $"Collection definition ID {definition.Id} is already registered.");
            }
            else _collectionDefinitions.Add(definition.Id.Value, definition);
            var key = Tuple.Create(definition.Id.Value, owner.Id);
            if (_declarativeCollections.ContainsKey(key))
                throw new InvalidOperationException($"Collection {definition.Id} already exists for {owner.Id}.");
            var collection = new DeclarativeCollection<TOwner, TItem>(owner, definition, AdvanceVersion);
            _declarativeCollections.Add(key, collection);
            if (baseItems != null)
                foreach (TItem item in baseItems) collection.AddBase(item);
            AdvanceVersion();
            return collection;
        }

        public DeclarativeCollection<TOwner, TItem> GetCollection<TOwner, TItem>(TOwner owner,
            CollectionDefinition<TOwner, TItem> definition)
            where TOwner : WorldEntity where TItem : WorldEntity
        {
            RequireOwned(owner);
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            return _declarativeCollections.TryGetValue(Tuple.Create(definition.Id.Value, owner.Id),
                out IDeclarativeCollection collection)
                ? (DeclarativeCollection<TOwner, TItem>)collection
                : null;
        }

        public bool RemoveCollection<TOwner, TItem>(TOwner owner,
            CollectionDefinition<TOwner, TItem> definition)
            where TOwner : WorldEntity where TItem : WorldEntity
        {
            EnsureMutationAllowed();
            RequireOwned(owner);
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            var key = Tuple.Create(definition.Id.Value, owner.Id);
            if (!_declarativeCollections.TryGetValue(key, out IDeclarativeCollection collection))
                return false;
            collection.Deactivate();
            _declarativeCollections.Remove(key);
            AdvanceVersion();
            return true;
        }

        internal void RemoveDeclarativeCollectionEntity(EntityId entityId)
        {
            foreach (Tuple<string, EntityId> key in _declarativeCollections.Keys
                         .Where(item => item.Item2 == entityId).ToArray())
            {
                _declarativeCollections[key].Deactivate();
                _declarativeCollections.Remove(key);
            }
            foreach (IDeclarativeCollection collection in _declarativeCollections.Values)
                collection.RemoveEntity(entityId);
        }

        public void EndVariableScope(VariableOwnerId owner)
        {
            EnsureMutationAllowed();
            if (owner.Kind == VariableOwnerKind.Entity)
                throw new InvalidOperationException("Entity variable scopes end when their entity is destroyed.");
            Variables.RemoveOwner(owner);
            AdvanceVersion();
        }

        public TriggerInstance<TState> RegisterTrigger<TState>(TriggerDefinition<TState> definition, TState state)
        {
            EnsureMutationAllowed();
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (_triggersByDefinition.ContainsKey(definition.Id.Value))
                throw new InvalidOperationException($"Trigger {definition.Id} is already registered.");
            long id = _nextTriggerRegistrationId++;
            var instance = new TriggerInstance<TState>(this, id, definition, state);
            _triggers.Add(id, instance);
            _triggersByDefinition.Add(definition.Id.Value, instance);
            foreach (QueryDependencyKey dependency in definition.DependencyKeys)
            {
                if (!_triggerDependencyIndex.TryGetValue(dependency, out SortedSet<long> values))
                    _triggerDependencyIndex.Add(dependency, values = new SortedSet<long>());
                values.Add(id);
            }
            if (definition.DependsOnTime) _timeDependentTriggerIds.Add(id);
            EvaluateTrigger(instance, false);
            AdvanceVersion();
            return instance;
        }

        public TriggerModifierLink<TState, TSource, TParameters> ProduceModifierFromTrigger<TState, TSource,
            TParameters>(TriggerInstance<TState> trigger,
            ModifierDefinition<TSource, TParameters> modifier,
            Func<TState, TSource> source,
            Func<TState, TParameters> parameters,
            Func<TState, string> stackingKey = null)
            where TSource : WorldEntity
        {
            if (trigger == null) throw new ArgumentNullException(nameof(trigger));
            if (trigger.IsDisposed) throw new ObjectDisposedException(nameof(trigger));
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            return new TriggerModifierLink<TState, TSource, TParameters>(this, trigger, modifier,
                source, parameters, stackingKey);
        }

        internal void NotifyTriggerStateChanged(ITriggerInstance instance)
        {
            using (BeginVersionPublicationScope())
            {
                EvaluateTrigger(instance, true);
                AdvanceVersion();
            }
        }

        internal void RemoveTrigger(long id)
        {
            EnsureMutationAllowed();
            if (!_triggers.TryGetValue(id, out ITriggerInstance instance)) return;
            _triggers.Remove(id);
            _triggersByDefinition.Remove(instance.DefinitionId.Value);
            _timeDependentTriggerIds.Remove(id);
            foreach (QueryDependencyKey dependency in instance.DependencyKeys)
            {
                if (!_triggerDependencyIndex.TryGetValue(dependency, out SortedSet<long> values)) continue;
                values.Remove(id);
                if (values.Count == 0) _triggerDependencyIndex.Remove(dependency);
            }
            instance.MarkDisposed();
            AdvanceVersion();
        }

        internal void OnTriggerDependenciesChanged(IReadOnlyCollection<QueryDependencyKey> changes)
        {
            var candidates = new SortedSet<long>();
            foreach (QueryDependencyKey change in changes)
                if (_triggerDependencyIndex.TryGetValue(change, out SortedSet<long> values))
                    candidates.UnionWith(values);
            foreach (long id in candidates)
                if (_triggers.TryGetValue(id, out ITriggerInstance instance)) EvaluateTrigger(instance, true);
        }

        internal void EvaluateTimeTriggers()
        {
            foreach (long id in _timeDependentTriggerIds)
                if (_triggers.TryGetValue(id, out ITriggerInstance instance)) EvaluateTrigger(instance, true);
        }

        private void EvaluateTrigger(ITriggerInstance instance, bool emitTransition)
        {
            if (instance == null || instance.IsDisposed) return;
            try
            {
                TriggerTransition transition = instance.Evaluate(this, emitTransition);
                var notice = new TriggerEvaluationNotice(instance.DefinitionId, instance.IsActive,
                    instance.Explanation);
                Action<TriggerEvaluationNotice> evaluatedHandlers = TriggerEvaluated;
                if (evaluatedHandlers != null)
                    foreach (Delegate handler in evaluatedHandlers.GetInvocationList())
                    {
                        try { ((Action<TriggerEvaluationNotice>)handler)(notice); }
                        catch (Exception exception) { ReportTriggerFailure(instance.DefinitionId, exception); }
                    }
                if (transition != null)
                {
                    Action<TriggerTransition> handlers = TriggerTransitioned;
                    if (handlers == null) return;
                    foreach (Delegate handler in handlers.GetInvocationList())
                    {
                        try { ((Action<TriggerTransition>)handler)(transition); }
                        catch (Exception exception) { ReportTriggerFailure(instance.DefinitionId, exception); }
                    }
                }
            }
            catch (Exception exception)
            {
                ReportTriggerFailure(instance.DefinitionId, exception);
            }
        }

        private void ReportTriggerFailure(StableId<TriggerIdKind> triggerId, Exception exception)
        {
            Action<TriggerEvaluationFailure> handlers = TriggerEvaluationFailed;
            if (handlers == null) return;
            var failure = new TriggerEvaluationFailure(triggerId, exception);
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try { ((Action<TriggerEvaluationFailure>)handler)(failure); }
                catch { }
            }
        }

        public CapacityCollection<TOwner, TItem> CreateCapacity<TOwner, TItem>(TOwner owner,
            CapacityDefinition<TOwner, TItem> definition, int capacity)
            where TOwner : WorldEntity where TItem : WorldEntity
        {
            EnsureMutationAllowed();
            RequireOwned(owner);
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (_capacityDefinitions.TryGetValue(definition.Id.Value, out object registered))
            {
                if (!ReferenceEquals(registered, definition))
                    throw new InvalidOperationException(
                        $"Capacity definition ID {definition.Id} is already registered.");
            }
            else
                _capacityDefinitions.Add(definition.Id.Value, definition);
            var key = Tuple.Create(definition.Id.Value, owner.Id);
            if (_capacities.ContainsKey(key))
                throw new InvalidOperationException($"Capacity {definition.Id} already exists for {owner.Id}.");
            var collection = new CapacityCollection<TOwner, TItem>(owner, definition, capacity,
                AdvanceVersion);
            _capacities.Add(key, collection);
            AdvanceVersion();
            return collection;
        }

        public CapacityCollection<TOwner, TItem> GetCapacity<TOwner, TItem>(TOwner owner,
            CapacityDefinition<TOwner, TItem> definition)
            where TOwner : WorldEntity where TItem : WorldEntity
        {
            RequireOwned(owner);
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            return _capacities.TryGetValue(Tuple.Create(definition.Id.Value, owner.Id),
                out ICapacityCollection collection)
                ? (CapacityCollection<TOwner, TItem>)collection
                : null;
        }

        public bool RemoveCapacity<TOwner, TItem>(TOwner owner,
            CapacityDefinition<TOwner, TItem> definition)
            where TOwner : WorldEntity where TItem : WorldEntity
        {
            EnsureMutationAllowed();
            RequireOwned(owner);
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            var key = Tuple.Create(definition.Id.Value, owner.Id);
            if (!_capacities.TryGetValue(key, out ICapacityCollection capacity)) return false;
            capacity.Deactivate();
            _capacities.Remove(key);
            AdvanceVersion();
            return true;
        }

        public TypedCounter<TEntity, TValue> Counter<TEntity, TValue>(CounterKey<TEntity, TValue> key,
            TEntity owner, TValue initial, Func<TValue, TValue, TValue> add) where TEntity : WorldEntity
        {
            EnsureMutationAllowed();
            RequireOwned(owner);
            var dictionaryKey = Tuple.Create(typeof(TValue), key.Id.Value, owner.Id);
            if (!_counters.TryGetValue(dictionaryKey, out object value))
                _counters.Add(dictionaryKey, value = new TypedCounter<TEntity, TValue>(key, owner, initial, add,
                    AdvanceVersion));
            return (TypedCounter<TEntity, TValue>)value;
        }

        public EffectResult ExecuteEffect<TParameters>(IGameEffect<TParameters> effect, TParameters parameters,
            Guid? executionId = null, IDeterministicRandom random = null)
        {
            EnsureMutationAllowed();
            using (BeginVersionPublicationScope())
            {
                if (effect == null) throw new ArgumentNullException(nameof(effect));
                if (effect.Atomicity == EffectAtomicity.PartialAllowed && !effect.IsIdempotent)
                    return new EffectResult(EffectStatus.Rejected,
                        "Partial effects must be idempotent or use a resumable domain-specific contract.");
                if (effect.Reversibility == EffectReversibility.Compensation &&
                    !(effect is ICompensatingGameEffect<TParameters>))
                    return new EffectResult(EffectStatus.Rejected,
                        "Effects declaring compensation must implement ICompensatingGameEffect<TParameters>.");
                if (!effect.IsIdempotent && !executionId.HasValue)
                    return new EffectResult(EffectStatus.Rejected,
                        "A stable execution ID is required for non-idempotent effects.");
                if (executionId.HasValue && _completedEffectExecutions.Contains(executionId.Value))
                    return new EffectResult(EffectStatus.Duplicate, "This effect execution already completed.");
                EffectValidation validation;
                try
                {
                    using (BeginEffectStaging()) validation = effect.Validate(this, parameters);
                }
                catch (Exception exception)
                {
                    return new EffectResult(EffectStatus.Failed, exception.Message);
                }
                if (!validation.IsValid) return new EffectResult(EffectStatus.Rejected, validation.Reason);

                IDeterministicRandom selectedRandom = random;
                if (selectedRandom == null)
                    selectedRandom = effect is IRandomizedGameEffect randomized
                        ? GetRandomStream(randomized.RandomStreamId)
                        : Random;
                if (!(selectedRandom is IRewindableDeterministicRandom rewindable))
                    return new EffectResult(EffectStatus.Rejected,
                        "Effects require a rewindable deterministic random stream.");
                ulong randomState = rewindable.State;
                var countedRandom = new CountingDeterministicRandom(selectedRandom);
                var context = new EffectContext(this, countedRandom, executionId);
                try
                {
                    EffectStatus staged;
                    using (BeginEffectStaging()) staged = effect.Stage(context, parameters);
                    if (staged != EffectStatus.Succeeded)
                    {
                        rewindable.RestoreState(randomState);
                        return new EffectResult(staged, "Effect did not commit.");
                    }
                    if (context.Events.Count != 0)
                    {
                        if (!(effect is IEffectEventContract eventContract))
                        {
                            rewindable.RestoreState(randomState);
                            return new EffectResult(EffectStatus.Rejected,
                                "Effects that emit domain events must declare their event types.");
                        }
                        object undeclared = context.Events.FirstOrDefault(domainEvent =>
                            !eventContract.EmittedEventTypes.Any(type =>
                                type != null && type.IsInstanceOfType(domainEvent)));
                        if (undeclared != null)
                        {
                            rewindable.RestoreState(randomState);
                            return new EffectResult(EffectStatus.Rejected,
                                $"Effect emitted undeclared event type {undeclared.GetType().FullName}.");
                        }
                    }
                    int expectedDraws = effect is IRandomizedGameEffect randomContract
                        ? randomContract.ExpectedRandomDrawCount
                        : 0;
                    if (expectedDraws < 0)
                    {
                        rewindable.RestoreState(randomState);
                        return new EffectResult(EffectStatus.Rejected,
                            "Expected random draw count cannot be negative.");
                    }
                    if (countedRandom.DrawCount != expectedDraws)
                    {
                        rewindable.RestoreState(randomState);
                        return new EffectResult(EffectStatus.Rejected,
                            $"Effect consumed {countedRandom.DrawCount} random values; contract requires {expectedDraws}.");
                    }
                    using (EffectObservationScope observation = BeginEffectObservation())
                    {
                        try
                        {
                            using (AuthorizeEffectCommit())
                            using (BeginMutationBatch()) context.Transaction.Commit(effect.Atomicity);
                        }
                        catch
                        {
                            if (effect.Atomicity == EffectAtomicity.PartialAllowed) observation.Complete();
                            throw;
                        }
                        if (executionId.HasValue) _completedEffectExecutions.Add(executionId.Value);
                        observation.Complete();
                    }
                    foreach (object domainEvent in context.Events) PublishDomainEvent(domainEvent);
                    AdvanceVersion();
                    return new EffectResult(EffectStatus.Succeeded, string.Empty, context.Events);
                }
                catch (Exception exception)
                {
                    rewindable.RestoreState(randomState);
                    return new EffectResult(EffectStatus.Failed, exception.Message, context.Events);
                }
                finally
                {
                    if (executionId.HasValue)
                        Variables.RemoveOwner(VariableOwnerId.Effect(executionId.Value));
                }
            }
        }

        public EffectResult CompensateEffect<TParameters>(ICompensatingGameEffect<TParameters> effect,
            TParameters parameters, Guid executionId, IDeterministicRandom random = null)
        {
            EnsureMutationAllowed();
            using (BeginVersionPublicationScope())
            {
                if (effect == null) throw new ArgumentNullException(nameof(effect));
                if (effect.Reversibility != EffectReversibility.Compensation)
                    return new EffectResult(EffectStatus.Rejected,
                        "The effect does not declare compensation.");
                if (!_completedEffectExecutions.Contains(executionId))
                    return new EffectResult(EffectStatus.Rejected,
                        "Only a completed effect execution can be compensated.");
                if (_compensatedEffectExecutions.Contains(executionId))
                    return new EffectResult(EffectStatus.Duplicate,
                        "This effect execution was already compensated.");

                EffectValidation validation;
                try
                {
                    using (BeginEffectStaging())
                        validation = effect.ValidateCompensation(this, parameters, executionId);
                }
                catch (Exception exception)
                {
                    return new EffectResult(EffectStatus.Failed, exception.Message);
                }
                if (!validation.IsValid)
                    return new EffectResult(EffectStatus.Rejected, validation.Reason);

                IDeterministicRandom selectedRandom = random;
                if (selectedRandom == null)
                    selectedRandom = effect is IRandomizedGameEffect randomized
                        ? GetRandomStream(randomized.RandomStreamId)
                        : Random;
                if (!(selectedRandom is IRewindableDeterministicRandom rewindable))
                    return new EffectResult(EffectStatus.Rejected,
                        "Effect compensation requires a rewindable deterministic random stream.");

                ulong randomState = rewindable.State;
                var countedRandom = new CountingDeterministicRandom(selectedRandom);
                var context = new EffectContext(this, countedRandom, executionId);
                try
                {
                    EffectStatus staged;
                    using (BeginEffectStaging())
                        staged = effect.StageCompensation(context, parameters, executionId);
                    if (staged != EffectStatus.Succeeded)
                    {
                        rewindable.RestoreState(randomState);
                        return new EffectResult(staged, "Effect compensation did not commit.");
                    }
                    if (context.Events.Count != 0)
                    {
                        if (!(effect is IEffectEventContract eventContract))
                        {
                            rewindable.RestoreState(randomState);
                            return new EffectResult(EffectStatus.Rejected,
                                "Compensations that emit domain events must declare their event types.");
                        }
                        object undeclared = context.Events.FirstOrDefault(domainEvent =>
                            !eventContract.EmittedEventTypes.Any(type =>
                                type != null && type.IsInstanceOfType(domainEvent)));
                        if (undeclared != null)
                        {
                            rewindable.RestoreState(randomState);
                            return new EffectResult(EffectStatus.Rejected,
                                $"Compensation emitted undeclared event type {undeclared.GetType().FullName}.");
                        }
                    }
                    if (countedRandom.DrawCount != 0)
                    {
                        rewindable.RestoreState(randomState);
                        return new EffectResult(EffectStatus.Rejected,
                            "Compensation cannot consume random values.");
                    }
                    using (EffectObservationScope observation = BeginEffectObservation())
                    {
                        using (AuthorizeEffectCommit())
                        using (BeginMutationBatch())
                            context.Transaction.Commit(EffectAtomicity.Atomic);
                        _compensatedEffectExecutions.Add(executionId);
                        observation.Complete();
                    }
                    foreach (object domainEvent in context.Events) PublishDomainEvent(domainEvent);
                    AdvanceVersion();
                    return new EffectResult(EffectStatus.Succeeded, string.Empty, context.Events);
                }
                catch (Exception exception)
                {
                    rewindable.RestoreState(randomState);
                    return new EffectResult(EffectStatus.Failed, exception.Message, context.Events);
                }
                finally
                {
                    Variables.RemoveOwner(VariableOwnerId.Effect(executionId));
                }
            }
        }

        private void PublishDomainEvent(object domainEvent)
        {
            Action<object> handlers = DomainEventEmitted;
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try { ((Action<object>)handler)(domainEvent); }
                catch (Exception exception)
                {
                    Action<DomainEventDispatchFailure> failureHandlers = DomainEventDispatchFailed;
                    if (failureHandlers == null) continue;
                    var failure = new DomainEventDispatchFailure(domainEvent, handler, exception);
                    foreach (Delegate failureHandler in failureHandlers.GetInvocationList())
                    {
                        try { ((Action<DomainEventDispatchFailure>)failureHandler)(failure); }
                        catch { }
                    }
                }
            }
        }

        partial void RemoveRuntimeStateOwnedByOrReferencing(EntityId entityId)
        {
            RemoveDeclarativeCollectionEntity(entityId);
            RemoveHistoryOwner(entityId);
            foreach (Tuple<Type, string, EntityId> key in _counters.Keys
                         .Where(item => item.Item3 == entityId).ToArray())
            {
                if (_counters.TryGetValue(key, out object counter) && counter is ITypedCounter typed)
                    typed.Deactivate();
                _counters.Remove(key);
            }
            Variables.RemoveOwner(entityId);

            foreach (Tuple<string, EntityId> key in _capacities.Keys
                         .Where(item => item.Item2 == entityId).ToArray())
            {
                _capacities[key].Deactivate();
                _capacities.Remove(key);
            }
            foreach (ICapacityCollection capacity in _capacities.Values)
                capacity.RemoveEntity(entityId);

            foreach (KeyValuePair<Tuple<object, EntityId>, ICapabilitySlot> pair in _capabilities
                         .Where(item => item.Key.Item2 == entityId).ToArray())
            {
                foreach (CapabilityDecisionContribution contribution in pair.Value.Contributions.ToArray())
                    _capabilityContributionOwners.Remove(contribution.Id);
                _capabilities.Remove(pair.Key);
            }
            foreach (ICapabilitySlot slot in _capabilities.Values)
            foreach (CapabilityDecisionContribution contribution in slot.Contributions
                         .Where(item => item.SourceId == entityId).ToArray())
            {
                slot.Remove(contribution.Id);
                _capabilityContributionOwners.Remove(contribution.Id);
            }
        }
    }
}
