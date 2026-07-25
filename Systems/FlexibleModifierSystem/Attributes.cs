using System;
using System.Collections.Generic;
using System.Linq;

namespace LegendaryTools.ModifierSystem
{
    public interface IAttributeDefinition
    {
        StableId<AttributeIdKind> Id { get; }
        Type EntityType { get; }
        Type ValueType { get; }
        bool IsDerived { get; }
        bool IsModifiable { get; }
        IReadOnlyList<IAttributeDefinition> Dependencies { get; }
        IReadOnlyList<IAttributeDefinition> GlobalDependencies { get; }
        IReadOnlyList<object> RelationDependencies { get; }
    }

    public interface IDomainAggregator<TEntity, TValue> where TEntity : WorldEntity
    {
        IReadOnlyList<IAttributeDefinition> Dependencies { get; }
        TValue Evaluate(TEntity entity);
        string Explain(TEntity entity);
    }

    internal interface IAttributeSlot
    {
        IAttributeDefinition Definition { get; }
        void MarkDirty();
        bool RemoveContribution(Guid modifierInstanceId);
        object BoxedBaseValue { get; }
        object BoxedFinalValue { get; }
        AttributeHistoryState CaptureHistory(EntityId ownerId);
        void RestoreHistory(AttributeHistoryState state);
        void ClearHistory();
        void RecalculateSilently();
    }

    internal interface IFreezableAttributeDefinition
    {
        bool IsFrozen { get; }
        void Freeze();
    }

    public sealed class AttributeDefinition<TEntity, TValue> : IAttributeDefinition, IFreezableAttributeDefinition
        where TEntity : WorldEntity
    {
        private readonly Func<TEntity, TValue> _formula;
        private Func<TEntity, string> _explain;
        private readonly List<IAttributeDefinition> _dependencies;
        private readonly List<IAttributeDefinition> _globalDependencies = new List<IAttributeDefinition>();
        private readonly List<object> _relationDependencies = new List<object>();
        private readonly IReadOnlyList<IAttributeDefinition> _dependenciesView;
        private readonly IReadOnlyList<IAttributeDefinition> _globalDependenciesView;
        private readonly IReadOnlyList<object> _relationDependenciesView;
        private bool _isFrozen;

        public StableId<AttributeIdKind> Id { get; }
        public Type EntityType => typeof(TEntity);
        public Type ValueType => typeof(TValue);
        public IAttributeValuePolicy<TValue> Policy { get; }
        public bool IsDerived => _formula != null;
        public bool IsModifiable { get; }
        public IReadOnlyList<IAttributeDefinition> Dependencies => _dependenciesView;
        public IReadOnlyList<IAttributeDefinition> GlobalDependencies => _globalDependenciesView;
        public IReadOnlyList<object> RelationDependencies => _relationDependenciesView;
        public HistoryPolicy History { get; }

        public AttributeDefinition(
            string id,
            IAttributeValuePolicy<TValue> policy,
            HistoryPolicy history = null,
            bool isModifiable = true)
            : this(id, policy, null, null, history, isModifiable)
        {
        }

        private AttributeDefinition(
            string id,
            IAttributeValuePolicy<TValue> policy,
            Func<TEntity, TValue> formula,
            IEnumerable<IAttributeDefinition> dependencies,
            HistoryPolicy history,
            bool isModifiable)
        {
            Id = new StableId<AttributeIdKind>(id);
            Policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _formula = formula;
            _explain = null;
            _dependencies = dependencies == null
                ? new List<IAttributeDefinition>()
                : new List<IAttributeDefinition>(dependencies);
            if (_dependencies.Any(item => item == null))
                throw new ArgumentException("Dependencies cannot contain null.", nameof(dependencies));
            _dependenciesView = _dependencies.AsReadOnly();
            _globalDependenciesView = _globalDependencies.AsReadOnly();
            _relationDependenciesView = _relationDependencies.AsReadOnly();
            History = history ?? HistoryPolicy.Disabled;
            IsModifiable = isModifiable;
        }

        public static AttributeDefinition<TEntity, TValue> Derived(
            string id,
            IAttributeValuePolicy<TValue> policy,
            Func<TEntity, TValue> formula,
            IEnumerable<IAttributeDefinition> dependencies,
            HistoryPolicy history = null,
            bool isModifiable = true) =>
            new AttributeDefinition<TEntity, TValue>(id, policy,
                formula ?? throw new ArgumentNullException(nameof(formula)), dependencies, history, isModifiable);

        public static AttributeDefinition<TEntity, TValue> Derived(
            string id,
            IAttributeValuePolicy<TValue> policy,
            IDomainAggregator<TEntity, TValue> aggregator,
            HistoryPolicy history = null,
            bool isModifiable = true)
        {
            if (aggregator == null) throw new ArgumentNullException(nameof(aggregator));
            var definition = new AttributeDefinition<TEntity, TValue>(id, policy, aggregator.Evaluate,
                aggregator.Dependencies, history, isModifiable);
            definition._explain = aggregator.Explain;
            return definition;
        }

        internal TValue Calculate(TEntity owner) => _formula(owner);
        internal string Explain(TEntity owner) => _explain?.Invoke(owner) ?? "Derived formula";

        public AttributeDefinition<TEntity, TValue> DependsOnGlobal(IAttributeDefinition dependency)
        {
            EnsureMutable();
            if (!IsDerived) throw new InvalidOperationException("Only derived attributes can declare dependencies.");
            if (dependency == null) throw new ArgumentNullException(nameof(dependency));
            if (!_globalDependencies.Contains(dependency)) _globalDependencies.Add(dependency);
            return this;
        }

        public AttributeDefinition<TEntity, TValue> DependsOnRelation<TFrom, TTo>(
            RelationDefinition<TFrom, TTo> relation)
            where TFrom : WorldEntity where TTo : WorldEntity
        {
            EnsureMutable();
            if (!IsDerived) throw new InvalidOperationException("Only derived attributes can declare dependencies.");
            if (relation == null) throw new ArgumentNullException(nameof(relation));
            if (!_relationDependencies.Contains(relation)) _relationDependencies.Add(relation);
            return this;
        }

        private void EnsureMutable()
        {
            if (_isFrozen)
                throw new InvalidOperationException(
                    $"Attribute definition {Id} is registered and can no longer be changed.");
        }

        bool IFreezableAttributeDefinition.IsFrozen => _isFrozen;
        void IFreezableAttributeDefinition.Freeze() => _isFrozen = true;
    }

    public readonly struct EvaluationStage<T>
    {
        public AttributeEvaluationStage Stage { get; }
        public T Value { get; }
        public Guid? ModifierInstanceId { get; }
        public string Description { get; }

        public EvaluationStage(AttributeEvaluationStage stage, T value, Guid? modifierInstanceId, string description)
        {
            Stage = stage;
            Value = value;
            ModifierInstanceId = modifierInstanceId;
            Description = description ?? string.Empty;
        }
    }

    public readonly struct AttributeContribution<T>
    {
        private readonly Func<T> _magnitude;
        private readonly Func<bool> _active;
        private readonly T _snapshotMagnitude;
        private readonly bool _isSnapshot;

        public Guid ModifierInstanceId { get; }
        public StableId<ModifierIdKind> DefinitionId { get; }
        public EntityId SourceId { get; }
        public ModifierOperation Operation { get; }
        public int Priority { get; }
        public long Sequence { get; }
        public int BindingIndex { get; }
        public string SourceDescription { get; }
        public string ConditionDescription { get; }
        public bool IsActive => _active == null || _active();
        public T Magnitude => _isSnapshot ? _snapshotMagnitude : _magnitude();

        internal AttributeContribution(Guid modifierInstanceId, StableId<ModifierIdKind> definitionId,
            EntityId sourceId, ModifierOperation operation, int priority, long sequence,
            Func<T> magnitude, Func<bool> active, string sourceDescription, string conditionDescription = null,
            int bindingIndex = 0)
        {
            ModifierInstanceId = modifierInstanceId;
            DefinitionId = definitionId;
            SourceId = sourceId;
            Operation = operation;
            Priority = priority;
            Sequence = sequence;
            BindingIndex = bindingIndex;
            _magnitude = magnitude ?? throw new ArgumentNullException(nameof(magnitude));
            _active = active;
            _snapshotMagnitude = default;
            _isSnapshot = false;
            SourceDescription = sourceDescription ?? string.Empty;
            ConditionDescription = conditionDescription ?? string.Empty;
        }

        internal AttributeContribution(Guid modifierInstanceId, StableId<ModifierIdKind> definitionId,
            EntityId sourceId, ModifierOperation operation, int priority, long sequence,
            T snapshotMagnitude, Func<bool> active, string sourceDescription, string conditionDescription = null,
            int bindingIndex = 0)
        {
            ModifierInstanceId = modifierInstanceId;
            DefinitionId = definitionId;
            SourceId = sourceId;
            Operation = operation;
            Priority = priority;
            Sequence = sequence;
            BindingIndex = bindingIndex;
            _magnitude = null;
            _active = active;
            _snapshotMagnitude = snapshotMagnitude;
            _isSnapshot = true;
            SourceDescription = sourceDescription ?? string.Empty;
            ConditionDescription = conditionDescription ?? string.Empty;
        }
    }

    public sealed class GameAttribute<TEntity, TValue> : IAttributeSlot where TEntity : WorldEntity
    {
        internal sealed class TransactionalState
        {
            public TValue BaseValue;
            public TValue FinalValue;
            public bool Dirty;
            public EvaluationStage<TValue>[] Stages;
            public AttributeHistoryState History;
        }

        private readonly TEntity _owner;
        private readonly AttributeDefinition<TEntity, TValue> _definition;
        private readonly List<AttributeContribution<TValue>> _contributions = new List<AttributeContribution<TValue>>();
        private readonly List<EvaluationStage<TValue>> _stages = new List<EvaluationStage<TValue>>();
        private readonly IReadOnlyList<AttributeContribution<TValue>> _contributionsView;
        private readonly IReadOnlyList<EvaluationStage<TValue>> _stagesView;
        private readonly HistoryBuffer<TValue> _history;
        private IReadOnlyList<AttributeContribution<TValue>> _combinedContributions;
        private long _combinedSharedVersion = -1;
        private int _localContributionVersion;
        private int _combinedLocalVersion = -1;
        private TValue _baseValue;
        private TValue _finalValue;
        private bool _dirty = true;

        public event Action<GameAttribute<TEntity, TValue>, TValue, TValue> BaseValueChanged;
        public event Action<GameAttribute<TEntity, TValue>, TValue, TValue> FinalValueChanged;

        public AttributeDefinition<TEntity, TValue> Definition => _definition;
        IAttributeDefinition IAttributeSlot.Definition => _definition;
        public TEntity Owner => _owner;
        public TValue BaseValue => _baseValue;
        public TValue FinalValue { get { Evaluate(); return _finalValue; } }
        public IReadOnlyList<AttributeContribution<TValue>> Modifiers => AllContributions();
        public IReadOnlyList<EvaluationStage<TValue>> EvaluationStages { get { Evaluate(); return _stagesView; } }
        public IReadOnlyList<ValueChange<TValue>> History => _history.Records;
        public HistorySummary<TValue> HistorySummary => _history.Summary;
        object IAttributeSlot.BoxedBaseValue => BaseValue;
        object IAttributeSlot.BoxedFinalValue => FinalValue;
        AttributeHistoryState IAttributeSlot.CaptureHistory(EntityId ownerId) =>
            _history.Capture(ownerId, _definition.Id.Value);
        void IAttributeSlot.RestoreHistory(AttributeHistoryState state) => _history.Restore(state);
        void IAttributeSlot.ClearHistory() => _history.Clear();
        void IAttributeSlot.RecalculateSilently()
        {
            _dirty = true;
            Evaluate(false);
        }

        internal GameAttribute(TEntity owner, AttributeDefinition<TEntity, TValue> definition, TValue baseValue)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _contributionsView = _contributions.AsReadOnly();
            _stagesView = _stages.AsReadOnly();
            Validate(baseValue);
            _baseValue = baseValue;
            _finalValue = baseValue;
            _history = new HistoryBuffer<TValue>(definition.History);
        }

        public void SetBaseValue(TValue value, string reason = null)
        {
            _owner.World.EnsureMutationAllowed();
            if (_definition.IsDerived) throw new InvalidOperationException($"{_definition.Id} is derived and has no mutable base value.");
            Validate(value);
            if (EqualityComparer<TValue>.Default.Equals(_baseValue, value)) return;
            TValue previous = _baseValue;
            _baseValue = value;
            MarkDirty();
            _history.Record(_owner.World.CurrentTick, previous, value, reason ?? "Base value changed",
                HistoryChangeKind.BaseValue);
            _owner.World.NotifyAttributeChanged(_owner, _definition);
            Action<GameAttribute<TEntity, TValue>, TValue, TValue> handlers = BaseValueChanged;
            if (handlers != null)
                foreach (Delegate handler in handlers.GetInvocationList())
                {
                    Delegate capturedHandler = handler;
                    _owner.World.PublishEffectAwareNotification(capturedHandler,
                        () => ((Action<GameAttribute<TEntity, TValue>, TValue, TValue>)capturedHandler)(
                            this, previous, value));
                }
        }

        internal void AddContribution(AttributeContribution<TValue> contribution)
        {
            if (!_definition.IsModifiable)
                throw new InvalidOperationException($"{_definition.Id} is not registered as modifiable.");
            if (!_definition.Policy.SupportedOperations.Contains(contribution.Operation))
                throw new InvalidOperationException($"{_definition.Id} rejects {contribution.Operation}.");
            if (_contributions.Count == 0 ||
                CompareContributions(_contributions[_contributions.Count - 1], contribution) <= 0)
                _contributions.Add(contribution);
            else
            {
                int low = 0;
                int high = _contributions.Count;
                while (low < high)
                {
                    int middle = low + ((high - low) >> 1);
                    if (CompareContributions(_contributions[middle], contribution) <= 0)
                        low = middle + 1;
                    else high = middle;
                }
                _contributions.Insert(low, contribution);
            }
            _localContributionVersion++;
            _combinedContributions = null;
            MarkDirty();
            _owner.World.NotifyAttributeContributionChanged(_owner, _definition);
        }

        internal bool RemoveContribution(Guid modifierInstanceId, int? bindingIndex = null)
        {
            int removed = _contributions.RemoveAll(item => item.ModifierInstanceId == modifierInstanceId &&
                (!bindingIndex.HasValue || item.BindingIndex == bindingIndex.Value));
            if (removed > 0)
            {
                _localContributionVersion++;
                _combinedContributions = null;
                MarkDirty();
                _owner.World.NotifyAttributeContributionChanged(_owner, _definition);
            }
            return removed > 0;
        }

        bool IAttributeSlot.RemoveContribution(Guid modifierInstanceId) => RemoveContribution(modifierInstanceId);

        internal void MarkDirty() => _dirty = true;
        void IAttributeSlot.MarkDirty() => MarkDirty();

        internal TransactionalState CaptureTransactionalState() =>
            new TransactionalState
            {
                BaseValue = _baseValue,
                FinalValue = _finalValue,
                Dirty = _dirty,
                Stages = _stages.ToArray(),
                History = _history.CaptureTransactional(_owner.Id, _definition.Id.Value),
            };

        internal void RestoreTransactionalState(TransactionalState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            _baseValue = state.BaseValue;
            _finalValue = state.FinalValue;
            _dirty = state.Dirty;
            _stages.Clear();
            _stages.AddRange(state.Stages);
            _history.Restore(state.History);
            _owner.World.NotifyAttributeChanged(_owner, _definition);
        }

        private void Evaluate(bool recordChanges = true)
        {
            if (!_dirty) return;
            TValue previous = _finalValue;
            TValue value = _definition.IsDerived ? _definition.Calculate(_owner) : _baseValue;
            _stages.Clear();
            _stages.Add(new EvaluationStage<TValue>(AttributeEvaluationStage.Base, value, null,
                _definition.IsDerived ? _definition.Explain(_owner) : "Base value"));

            foreach (AttributeContribution<TValue> contribution in AllContributions())
            {
                if (!contribution.IsActive) continue;
                value = _definition.Policy.Apply(value, contribution.Operation, contribution.Magnitude);
                Validate(value);
                _stages.Add(new EvaluationStage<TValue>(ToStage(contribution.Operation), value,
                    contribution.ModifierInstanceId, contribution.SourceDescription));
            }

            _finalValue = value;
            _dirty = false;
            _stages.Add(new EvaluationStage<TValue>(AttributeEvaluationStage.Final, value, null, "Final value"));
            if (recordChanges && !EqualityComparer<TValue>.Default.Equals(previous, value))
            {
                _history.Record(_owner.World.CurrentTick, previous, value, "Final value evaluated",
                    HistoryChangeKind.FinalValue);
                Action<GameAttribute<TEntity, TValue>, TValue, TValue> handlers = FinalValueChanged;
                if (handlers != null)
                    foreach (Delegate handler in handlers.GetInvocationList())
                    {
                        Delegate capturedHandler = handler;
                        _owner.World.PublishEffectAwareNotification(capturedHandler,
                            () => ((Action<GameAttribute<TEntity, TValue>, TValue, TValue>)capturedHandler)(
                                this, previous, value));
                    }
            }
        }

        private IReadOnlyList<AttributeContribution<TValue>> AllContributions()
        {
            long sharedVersion = _owner.World.GetSharedAttributeContributionVersion(_definition);
            if (_combinedContributions != null && _combinedSharedVersion == sharedVersion &&
                _combinedLocalVersion == _localContributionVersion)
                return _combinedContributions;
            var combined = new List<AttributeContribution<TValue>>(_contributions.Count + 4);
            combined.AddRange(_contributions);
            _owner.World.AppendSharedAttributeContributions(_owner, _definition, combined);
            if (combined.Count == _contributions.Count)
            {
                _combinedContributions = _contributionsView;
                _combinedSharedVersion = sharedVersion;
                _combinedLocalVersion = _localContributionVersion;
                return _combinedContributions;
            }
            combined.Sort(CompareContributions);
            _combinedContributions = combined.AsReadOnly();
            _combinedSharedVersion = sharedVersion;
            _combinedLocalVersion = _localContributionVersion;
            return _combinedContributions;
        }

        private void Validate(TValue value)
        {
            if (!_definition.Policy.IsValid(value, out string error))
                throw new ArgumentOutOfRangeException(nameof(value), error);
        }

        private static int CompareContributions(AttributeContribution<TValue> left, AttributeContribution<TValue> right)
        {
            int result = OperationOrder(left.Operation).CompareTo(OperationOrder(right.Operation));
            if (result != 0) return result;
            result = left.Priority.CompareTo(right.Priority);
            return result != 0 ? result : left.Sequence.CompareTo(right.Sequence);
        }

        private static int OperationOrder(ModifierOperation operation)
        {
            switch (operation)
            {
                case ModifierOperation.Add: return 0;
                case ModifierOperation.Multiply: return 1;
                case ModifierOperation.Replace: return 2;
                case ModifierOperation.Minimum:
                case ModifierOperation.Maximum:
                case ModifierOperation.ClampMinimum:
                case ModifierOperation.ClampMaximum: return 3;
                default: return 4;
            }
        }

        private static AttributeEvaluationStage ToStage(ModifierOperation operation)
        {
            switch (operation)
            {
                case ModifierOperation.Add: return AttributeEvaluationStage.Additive;
                case ModifierOperation.Multiply: return AttributeEvaluationStage.Multiplicative;
                case ModifierOperation.Replace: return AttributeEvaluationStage.Replacement;
                case ModifierOperation.Minimum:
                case ModifierOperation.Maximum:
                case ModifierOperation.ClampMinimum:
                case ModifierOperation.ClampMaximum: return AttributeEvaluationStage.Limits;
                default: return AttributeEvaluationStage.Custom;
            }
        }
    }
}
