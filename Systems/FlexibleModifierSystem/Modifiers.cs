using System;
using System.Collections.Generic;
using System.Linq;

namespace LegendaryTools.ModifierSystem
{
    public sealed class StackingPolicy
    {
        public StackingMode Mode { get; }
        public int MaximumStacks { get; }
        public bool GroupBySource { get; }

        public StackingPolicy(StackingMode mode = StackingMode.Stack, int maximumStacks = 0,
            bool groupBySource = false)
        {
            if (mode == StackingMode.MaximumStacks && maximumStacks <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumStacks));
            Mode = mode;
            MaximumStacks = maximumStacks;
            GroupBySource = groupBySource || mode == StackingMode.GroupBySource;
        }
    }

    public readonly struct ModifierMagnitudeContext<TSource, TTarget, TParameters>
        where TSource : WorldEntity where TTarget : WorldEntity
    {
        public SimulationWorld World { get; }
        public TSource Source { get; }
        public TTarget Target { get; }
        public TParameters Parameters { get; }

        public ModifierMagnitudeContext(SimulationWorld world, TSource source, TTarget target, TParameters parameters)
        {
            World = world;
            Source = source;
            Target = target;
            Parameters = parameters;
        }
    }

    public readonly struct AffectedAttribute
    {
        public EntityId EntityId { get; }
        public StableId<AttributeIdKind> AttributeId { get; }
        public int BindingIndex { get; }

        public AffectedAttribute(EntityId entityId, StableId<AttributeIdKind> attributeId, int bindingIndex)
        {
            EntityId = entityId;
            AttributeId = attributeId;
            BindingIndex = bindingIndex;
        }
    }

    public readonly struct AffectedCapability
    {
        public EntityId EntityId { get; }
        public StableId<CapabilityIdKind> CapabilityId { get; }
        public int BindingIndex { get; }

        public AffectedCapability(EntityId entityId, StableId<CapabilityIdKind> capabilityId, int bindingIndex)
        {
            EntityId = entityId;
            CapabilityId = capabilityId;
            BindingIndex = bindingIndex;
        }
    }

    public readonly struct AffectedCapacity
    {
        public EntityId EntityId { get; }
        public StableId<CapacityIdKind> CapacityId { get; }
        public int BindingIndex { get; }

        public AffectedCapacity(EntityId entityId, StableId<CapacityIdKind> capacityId, int bindingIndex)
        {
            EntityId = entityId;
            CapacityId = capacityId;
            BindingIndex = bindingIndex;
        }
    }

    public readonly struct ConditionState
    {
        public string Description { get; }
        public bool IsSatisfied { get; }
        public EntityId? TargetEntityId { get; }
        public int? BindingIndex { get; }

        public ConditionState(string description, bool isSatisfied)
            : this(description, isSatisfied, null, null)
        {
        }

        public ConditionState(string description, bool isSatisfied, EntityId? targetEntityId, int? bindingIndex)
        {
            Description = description ?? string.Empty;
            IsSatisfied = isSatisfied;
            TargetEntityId = targetEntityId;
            BindingIndex = bindingIndex;
        }
    }

    public readonly struct ModifierDependency
    {
        public IAttributeDefinition Attribute { get; }
        public ModifierDependencyScope Scope { get; }

        public ModifierDependency(IAttributeDefinition attribute, ModifierDependencyScope scope)
        {
            Attribute = attribute ?? throw new ArgumentNullException(nameof(attribute));
            Scope = scope;
        }
    }

    public readonly struct ModifierRelationDependency
    {
        public object Relation { get; }
        public RelationDependencyScope Scope { get; }

        internal ModifierRelationDependency(object relation, RelationDependencyScope scope)
        {
            Relation = relation ?? throw new ArgumentNullException(nameof(relation));
            Scope = scope;
        }
    }

    internal interface IModifierDefinition
    {
        StableId<ModifierIdKind> Id { get; }
        StackingPolicy Stacking { get; }
        long? DurationTicks { get; }
        Type SourceType { get; }
        bool AcceptsSource(object source);
        double GetStrength(object source, object parameters);
        bool SourceCondition(SimulationWorld world, object source, object parameters);
        string ConditionDescription { get; }
        bool IsTimeDependent { get; }
        bool IsFrozen { get; }
        void Freeze();
        IReadOnlyList<ModifierDependency> AttributeDependencies { get; }
        IReadOnlyList<ModifierRelationDependency> RelationDependencies { get; }
        IReadOnlyList<IModifierBinding> Bindings { get; }
    }

    internal interface IModifierBinding
    {
        int BindingIndex { get; }
        TargetTracking TargetTracking { get; }
        void Validate(SimulationWorld world, ModifierInstance instance);
        void Reconcile(SimulationWorld world, ModifierInstance instance, bool initial);
        ModifierBindingState Capture(SimulationWorld world, ModifierInstance instance, int bindingIndex);
        void Restore(SimulationWorld world, ModifierInstance instance, ModifierBindingState state);
        void Remove(SimulationWorld world, ModifierInstance instance);
        IEnumerable<EntityId> DependencyTargets(ModifierInstance instance);
        IEnumerable<ConditionState> EvaluateConditions(SimulationWorld world, ModifierInstance instance);
    }

    public sealed class ModifierDefinition<TSource, TParameters> : IModifierDefinition where TSource : WorldEntity
    {
        private readonly List<IModifierBinding> _bindings = new List<IModifierBinding>();
        private readonly Func<TSource, TParameters, double> _strength;
        private readonly Func<SimulationWorld, TSource, TParameters, bool> _condition;
        private readonly List<ModifierDependency> _attributeDependencies = new List<ModifierDependency>();
        private readonly List<ModifierRelationDependency> _relationDependencies =
            new List<ModifierRelationDependency>();
        private bool _isTimeDependent;
        private bool _isFrozen;

        public StableId<ModifierIdKind> Id { get; }
        public StackingPolicy Stacking { get; }
        public long? DurationTicks { get; }
        public Type SourceType => typeof(TSource);
        public string ConditionDescription { get; }
        bool IModifierDefinition.IsTimeDependent => _isTimeDependent;
        bool IModifierDefinition.IsFrozen => _isFrozen;
        IReadOnlyList<ModifierDependency> IModifierDefinition.AttributeDependencies => _attributeDependencies;
        IReadOnlyList<ModifierRelationDependency> IModifierDefinition.RelationDependencies =>
            _relationDependencies;
        IReadOnlyList<IModifierBinding> IModifierDefinition.Bindings => _bindings;

        public ModifierDefinition(string id, StackingPolicy stacking = null, long? durationTicks = null,
            Func<TSource, TParameters, double> strength = null,
            Func<SimulationWorld, TSource, TParameters, bool> condition = null,
            string conditionDescription = null)
        {
            if (durationTicks.HasValue && durationTicks.Value <= 0)
                throw new ArgumentOutOfRangeException(nameof(durationTicks));
            Id = new StableId<ModifierIdKind>(id);
            Stacking = stacking ?? new StackingPolicy();
            if (Stacking.Mode == StackingMode.RefreshDuration && !durationTicks.HasValue)
                throw new ArgumentException("Refresh-duration stacking requires a duration.", nameof(durationTicks));
            DurationTicks = durationTicks;
            _strength = strength;
            _condition = condition;
            ConditionDescription = conditionDescription ?? string.Empty;
        }

        public ModifierDefinition<TSource, TParameters> Affects<TTarget, TValue>(
            PreparedTargetQuery<TSource, TTarget> targets,
            AttributeDefinition<TTarget, TValue> attribute,
            ModifierOperation operation,
            Func<ModifierMagnitudeContext<TSource, TTarget, TParameters>, TValue> magnitude,
            MagnitudeEvaluation magnitudeEvaluation = MagnitudeEvaluation.Snapshot,
            TargetTracking targetTracking = TargetTracking.Live,
            Func<ModifierMagnitudeContext<TSource, TTarget, TParameters>, bool> condition = null,
            string conditionDescription = null,
            int priority = 0)
            where TTarget : WorldEntity
        {
            EnsureMutable();
            if (targets == null) throw new ArgumentNullException(nameof(targets));
            if (attribute == null) throw new ArgumentNullException(nameof(attribute));
            if (magnitude == null) throw new ArgumentNullException(nameof(magnitude));
            if (!attribute.IsModifiable)
                throw new InvalidOperationException($"Attribute {attribute.Id} is not registered as modifiable.");
            if (!attribute.Policy.SupportedOperations.Contains(operation))
                throw new InvalidOperationException($"Attribute {attribute.Id} rejects {operation}.");
            _bindings.Add(new ModifierBinding<TSource, TTarget, TParameters, TValue>(_bindings.Count, targets, attribute,
                operation, magnitude, magnitudeEvaluation, targetTracking, condition, conditionDescription, priority));
            return this;
        }

        public ModifierDefinition<TSource, TParameters> DependsOn(IAttributeDefinition attribute,
            ModifierDependencyScope scope = ModifierDependencyScope.Source)
        {
            EnsureMutable();
            var dependency = new ModifierDependency(attribute, scope);
            if (!_attributeDependencies.Any(item => ReferenceEquals(item.Attribute, attribute) && item.Scope == scope))
                _attributeDependencies.Add(dependency);
            return this;
        }

        public ModifierDefinition<TSource, TParameters> DependsOnRelation<TFrom, TTo>(
            RelationDefinition<TFrom, TTo> relation,
            RelationDependencyScope scope = RelationDependencyScope.Source)
            where TFrom : WorldEntity where TTo : WorldEntity
        {
            EnsureMutable();
            if (relation == null) throw new ArgumentNullException(nameof(relation));
            if (!_relationDependencies.Any(item => ReferenceEquals(item.Relation, relation) &&
                item.Scope == scope))
                _relationDependencies.Add(new ModifierRelationDependency(relation, scope));
            return this;
        }

        public ModifierDefinition<TSource, TParameters> DependsOnTime()
        {
            EnsureMutable();
            _isTimeDependent = true;
            return this;
        }

        public ModifierDefinition<TSource, TParameters> AffectsCapability<TTarget>(
            PreparedTargetQuery<TSource, TTarget> targets,
            CapabilityDefinition<TTarget> capability,
            Func<ModifierMagnitudeContext<TSource, TTarget, TParameters>, CapabilityContribution> decision,
            MagnitudeEvaluation decisionEvaluation = MagnitudeEvaluation.Live,
            TargetTracking targetTracking = TargetTracking.Live,
            Func<ModifierMagnitudeContext<TSource, TTarget, TParameters>, bool> condition = null,
            string conditionDescription = null,
            int priority = 0)
            where TTarget : WorldEntity
        {
            EnsureMutable();
            if (targets == null) throw new ArgumentNullException(nameof(targets));
            if (capability == null) throw new ArgumentNullException(nameof(capability));
            if (decision == null) throw new ArgumentNullException(nameof(decision));
            _bindings.Add(new CapabilityModifierBinding<TSource, TTarget, TParameters>(_bindings.Count,
                targets, capability, decision, decisionEvaluation, targetTracking, condition,
                conditionDescription, priority));
            return this;
        }

        public ModifierDefinition<TSource, TParameters> AffectsCapacity<TTarget, TItem>(
            PreparedTargetQuery<TSource, TTarget> targets,
            CapacityDefinition<TTarget, TItem> capacity,
            ModifierOperation operation,
            Func<ModifierMagnitudeContext<TSource, TTarget, TParameters>, int> magnitude,
            MagnitudeEvaluation magnitudeEvaluation = MagnitudeEvaluation.Snapshot,
            TargetTracking targetTracking = TargetTracking.Live,
            Func<ModifierMagnitudeContext<TSource, TTarget, TParameters>, bool> condition = null,
            string conditionDescription = null,
            int priority = 0)
            where TTarget : WorldEntity where TItem : WorldEntity
        {
            EnsureMutable();
            if (targets == null) throw new ArgumentNullException(nameof(targets));
            if (capacity == null) throw new ArgumentNullException(nameof(capacity));
            if (magnitude == null) throw new ArgumentNullException(nameof(magnitude));
            if (!NumericValuePolicies.Int32().SupportedOperations.Contains(operation))
                throw new InvalidOperationException($"Capacity {capacity.Id} rejects {operation}.");
            _bindings.Add(new CapacityModifierBinding<TSource, TTarget, TItem, TParameters>(_bindings.Count,
                targets, capacity, operation, magnitude, magnitudeEvaluation, targetTracking, condition,
                conditionDescription, priority));
            return this;
        }

        private void EnsureMutable()
        {
            if (_isFrozen)
                throw new InvalidOperationException(
                    $"Modifier definition {Id} is registered and can no longer be changed.");
        }

        void IModifierDefinition.Freeze() => _isFrozen = true;

        double IModifierDefinition.GetStrength(object source, object parameters) =>
            _strength?.Invoke((TSource)source, (TParameters)parameters) ?? 0d;

        bool IModifierDefinition.SourceCondition(SimulationWorld world, object source, object parameters) =>
            _condition?.Invoke(world, (TSource)source, (TParameters)parameters) ?? true;

        bool IModifierDefinition.AcceptsSource(object source) => source is TSource;
    }

    public sealed class ModifierInstance
    {
        private readonly List<AffectedAttribute> _affected = new List<AffectedAttribute>();
        private readonly List<AffectedCapability> _affectedCapabilities = new List<AffectedCapability>();
        private readonly List<AffectedCapacity> _affectedCapacities = new List<AffectedCapacity>();
        private readonly IReadOnlyList<AffectedAttribute> _affectedView;
        private readonly IReadOnlyList<AffectedCapability> _affectedCapabilitiesView;
        private readonly IReadOnlyList<AffectedCapacity> _affectedCapacitiesView;
        private readonly Dictionary<IModifierBinding, HashSet<EntityId>> _bindingTargets =
            new Dictionary<IModifierBinding, HashSet<EntityId>>();
        private readonly Dictionary<IModifierBinding, object> _bindingRuntime =
            new Dictionary<IModifierBinding, object>();

        internal IModifierDefinition DefinitionInternal { get; }
        internal object SourceInternal { get; }
        internal object ParametersInternal { get; }
        internal bool Removed { get; set; }

        public Guid InstanceId { get; }
        public StableId<ModifierIdKind> DefinitionId => DefinitionInternal.Id;
        public WorldEntity Source => (WorldEntity)SourceInternal;
        public object Parameters => ParametersInternal;
        public long AppliedTick { get; }
        public long? ExpirationTick { get; internal set; }
        public string StackingKey { get; }
        public bool IsActive { get; internal set; }
        public double Strength { get; }
        public IReadOnlyList<AffectedAttribute> AffectedAttributes => _affectedView;
        public IReadOnlyList<AffectedCapability> AffectedCapabilities => _affectedCapabilitiesView;
        public IReadOnlyList<AffectedCapacity> AffectedCapacities => _affectedCapacitiesView;
        private IReadOnlyList<ConditionState> _conditions = Array.Empty<ConditionState>();
        public IReadOnlyList<ConditionState> Conditions
        {
            get => _conditions;
            internal set => _conditions = value == null
                ? (IReadOnlyList<ConditionState>)Array.Empty<ConditionState>()
                : Array.AsReadOnly(value.ToArray());
        }
        public long? RemainingTicks => ExpirationTick.HasValue
            ? Math.Max(0, ExpirationTick.Value - Source.World.CurrentTick)
            : (long?)null;

        internal ModifierInstance(Guid instanceId, IModifierDefinition definition, object source, object parameters,
            long appliedTick, string stackingKey)
        {
            InstanceId = instanceId;
            _affectedView = _affected.AsReadOnly();
            _affectedCapabilitiesView = _affectedCapabilities.AsReadOnly();
            _affectedCapacitiesView = _affectedCapacities.AsReadOnly();
            DefinitionInternal = definition;
            SourceInternal = source;
            ParametersInternal = parameters;
            AppliedTick = appliedTick;
            StackingKey = stackingKey ?? string.Empty;
            ExpirationTick = definition.DurationTicks.HasValue ? appliedTick + definition.DurationTicks.Value : (long?)null;
            Strength = definition.GetStrength(source, parameters);
        }

        internal HashSet<EntityId> TargetsFor(IModifierBinding binding)
        {
            if (!_bindingTargets.TryGetValue(binding, out HashSet<EntityId> targets))
                _bindingTargets.Add(binding, targets = new HashSet<EntityId>());
            return targets;
        }

        internal void AddAffected(EntityId entityId, StableId<AttributeIdKind> attributeId, int bindingIndex) =>
            _affected.Add(new AffectedAttribute(entityId, attributeId, bindingIndex));

        internal void RemoveAffected(EntityId entityId, StableId<AttributeIdKind> attributeId, int bindingIndex) =>
            _affected.RemoveAll(item => item.EntityId == entityId && item.AttributeId == attributeId &&
                item.BindingIndex == bindingIndex);

        internal void AddAffectedCapability(EntityId entityId, StableId<CapabilityIdKind> capabilityId,
            int bindingIndex) =>
            _affectedCapabilities.Add(new AffectedCapability(entityId, capabilityId, bindingIndex));

        internal void RemoveAffectedCapability(EntityId entityId, StableId<CapabilityIdKind> capabilityId,
            int bindingIndex) =>
            _affectedCapabilities.RemoveAll(item => item.EntityId == entityId && item.CapabilityId == capabilityId &&
                item.BindingIndex == bindingIndex);

        internal void AddAffectedCapacity(EntityId entityId, StableId<CapacityIdKind> capacityId,
            int bindingIndex) => _affectedCapacities.Add(new AffectedCapacity(entityId, capacityId, bindingIndex));

        internal void RemoveAffectedCapacity(EntityId entityId, StableId<CapacityIdKind> capacityId,
            int bindingIndex) =>
            _affectedCapacities.RemoveAll(item => item.EntityId == entityId && item.CapacityId == capacityId &&
                item.BindingIndex == bindingIndex);

        internal TRuntime RuntimeFor<TRuntime>(IModifierBinding binding, Func<TRuntime> create)
            where TRuntime : class
        {
            if (!_bindingRuntime.TryGetValue(binding, out object runtime))
                _bindingRuntime.Add(binding, runtime = create());
            return (TRuntime)runtime;
        }
    }

    internal sealed class ModifierBinding<TSource, TTarget, TParameters, TValue> : IModifierBinding
        where TSource : WorldEntity where TTarget : WorldEntity
    {
        private readonly PreparedTargetQuery<TSource, TTarget> _targets;
        private readonly AttributeDefinition<TTarget, TValue> _attribute;
        private readonly ModifierOperation _operation;
        private readonly Func<ModifierMagnitudeContext<TSource, TTarget, TParameters>, TValue> _magnitude;
        private readonly MagnitudeEvaluation _magnitudeEvaluation;
        private readonly Func<ModifierMagnitudeContext<TSource, TTarget, TParameters>, bool> _condition;
        private readonly string _conditionDescription;
        private readonly int _priority;

        public int BindingIndex { get; }
        public TargetTracking TargetTracking { get; }

        public ModifierBinding(int bindingIndex, PreparedTargetQuery<TSource, TTarget> targets,
            AttributeDefinition<TTarget, TValue> attribute, ModifierOperation operation,
            Func<ModifierMagnitudeContext<TSource, TTarget, TParameters>, TValue> magnitude,
            MagnitudeEvaluation magnitudeEvaluation, TargetTracking targetTracking,
            Func<ModifierMagnitudeContext<TSource, TTarget, TParameters>, bool> condition,
            string conditionDescription, int priority)
        {
            BindingIndex = bindingIndex;
            _targets = targets;
            _attribute = attribute;
            _operation = operation;
            _magnitude = magnitude;
            _magnitudeEvaluation = magnitudeEvaluation;
            TargetTracking = targetTracking;
            _condition = condition;
            _conditionDescription = conditionDescription ?? string.Empty;
            _priority = priority;
        }

        public void Validate(SimulationWorld world, ModifierInstance instance)
        {
            foreach (TTarget target in _targets.Execute(world, (TSource)instance.SourceInternal))
            {
                GameAttribute<TTarget, TValue> attribute = target.GetAttribute(_attribute);
                if (attribute != null && !_attribute.Policy.SupportedOperations.Contains(_operation))
                    throw new InvalidOperationException($"Attribute {_attribute.Id} rejects {_operation}.");
            }
        }

        public void Reconcile(SimulationWorld world, ModifierInstance instance, bool initial)
        {
            if (!initial && TargetTracking == TargetTracking.Snapshot) return;
            TSource source = (TSource)instance.SourceInternal;
            TParameters parameters = (TParameters)instance.ParametersInternal;
            HashSet<EntityId> existing = instance.TargetsFor(this);
            var desired = new HashSet<EntityId>(_targets.Execute(world, source).Select(item => item.Id));

            if (TargetTracking == TargetTracking.Live)
            {
                foreach (EntityId departed in existing.Where(id => !desired.Contains(id)).ToArray())
                {
                    TTarget target = world.Get<TTarget>(departed);
                    GameAttribute<TTarget, TValue> attribute = target?.GetAttribute(_attribute);
                    attribute?.RemoveContribution(instance.InstanceId, BindingIndex);
                    existing.Remove(departed);
                    instance.RemoveAffected(departed, _attribute.Id, BindingIndex);
                }
            }

            foreach (EntityId targetId in desired.OrderBy(id => id))
            {
                if (existing.Contains(targetId)) continue;
                TTarget target = world.Get<TTarget>(targetId);
                GameAttribute<TTarget, TValue> attribute = target?.GetAttribute(_attribute);
                if (attribute == null) continue;
                var context = new ModifierMagnitudeContext<TSource, TTarget, TParameters>(world, source, target, parameters);
                TValue snapshot = default;
                if (_magnitudeEvaluation == MagnitudeEvaluation.Snapshot) snapshot = _magnitude(context);
                Func<bool> active = () => instance.IsActive && (_condition?.Invoke(context) ?? true);
                long sequence = world.NextContributionSequence();
                AttributeContribution<TValue> contribution = _magnitudeEvaluation == MagnitudeEvaluation.Snapshot
                    ? new AttributeContribution<TValue>(instance.InstanceId, instance.DefinitionId,
                        source.Id, _operation, _priority, sequence, snapshot, active,
                        instance.DefinitionId.Value, _conditionDescription, BindingIndex)
                    : new AttributeContribution<TValue>(instance.InstanceId, instance.DefinitionId,
                        source.Id, _operation, _priority, sequence, () => _magnitude(context), active,
                        instance.DefinitionId.Value, _conditionDescription, BindingIndex);
                attribute.AddContribution(contribution);
                existing.Add(targetId);
                instance.AddAffected(targetId, _attribute.Id, BindingIndex);
            }
        }

        public ModifierBindingState Capture(SimulationWorld world, ModifierInstance instance, int bindingIndex)
        {
            var state = new ModifierBindingState { BindingIndex = bindingIndex };
            foreach (EntityId id in instance.TargetsFor(this).OrderBy(item => item))
            {
                TTarget target = world.Get<TTarget>(id);
                GameAttribute<TTarget, TValue> attribute = target?.GetAttribute(_attribute);
                AttributeContribution<TValue> contribution = attribute?.Modifiers.FirstOrDefault(item =>
                    item.ModifierInstanceId == instance.InstanceId && item.BindingIndex == BindingIndex) ?? default;
                if (contribution.ModifierInstanceId != Guid.Empty)
                    state.AddTarget(new ModifierTargetState
                    {
                        EntityId = id.Value,
                        SnapshotMagnitude = contribution.Magnitude,
                        Sequence = contribution.Sequence
                    });
            }
            return state;
        }

        public void Restore(SimulationWorld world, ModifierInstance instance, ModifierBindingState state)
        {
            TSource source = (TSource)instance.SourceInternal;
            TParameters parameters = (TParameters)instance.ParametersInternal;
            HashSet<EntityId> existing = instance.TargetsFor(this);
            foreach (ModifierTargetState saved in state.Targets.OrderBy(item => item.EntityId))
            {
                var id = new EntityId(saved.EntityId);
                TTarget target = world.Get<TTarget>(id);
                GameAttribute<TTarget, TValue> attribute = target?.GetAttribute(_attribute);
                if (attribute == null) continue;
                var context = new ModifierMagnitudeContext<TSource, TTarget, TParameters>(world, source, target, parameters);
                TValue snapshot = saved.SnapshotMagnitude is TValue value ? value : default;
                Func<bool> active = () => instance.IsActive && (_condition?.Invoke(context) ?? true);
                AttributeContribution<TValue> contribution = _magnitudeEvaluation == MagnitudeEvaluation.Snapshot
                    ? new AttributeContribution<TValue>(instance.InstanceId, instance.DefinitionId,
                        source.Id, _operation, _priority, saved.Sequence, snapshot, active,
                        instance.DefinitionId.Value, _conditionDescription, BindingIndex)
                    : new AttributeContribution<TValue>(instance.InstanceId, instance.DefinitionId,
                        source.Id, _operation, _priority, saved.Sequence, () => _magnitude(context), active,
                        instance.DefinitionId.Value, _conditionDescription, BindingIndex);
                attribute.AddContribution(contribution);
                existing.Add(id);
                instance.AddAffected(id, _attribute.Id, BindingIndex);
            }
        }

        public void Remove(SimulationWorld world, ModifierInstance instance)
        {
            foreach (EntityId id in instance.TargetsFor(this).ToArray())
            {
                TTarget target = world.Get<TTarget>(id);
                target?.GetAttribute(_attribute)?.RemoveContribution(instance.InstanceId, BindingIndex);
                instance.RemoveAffected(id, _attribute.Id, BindingIndex);
            }
            instance.TargetsFor(this).Clear();
        }

        public IEnumerable<EntityId> DependencyTargets(ModifierInstance instance) => instance.TargetsFor(this);

        public IEnumerable<ConditionState> EvaluateConditions(SimulationWorld world, ModifierInstance instance)
        {
            if (_condition == null || string.IsNullOrEmpty(_conditionDescription)) yield break;
            TSource source = (TSource)instance.SourceInternal;
            TParameters parameters = (TParameters)instance.ParametersInternal;
            foreach (EntityId id in instance.TargetsFor(this).OrderBy(item => item))
            {
                TTarget target = world.Get<TTarget>(id);
                if (target == null) continue;
                var context = new ModifierMagnitudeContext<TSource, TTarget, TParameters>(
                    world, source, target, parameters);
                yield return new ConditionState(_conditionDescription, _condition(context), id, BindingIndex);
            }
        }
    }

    internal sealed class CapabilityModifierBinding<TSource, TTarget, TParameters> : IModifierBinding
        where TSource : WorldEntity where TTarget : WorldEntity
    {
        private sealed class Runtime
        {
            public bool Initialized;
            public readonly HashSet<EntityId> Members = new HashSet<EntityId>();
            public readonly Dictionary<EntityId, CapabilityContribution> Decisions =
                new Dictionary<EntityId, CapabilityContribution>();
            public readonly Dictionary<EntityId, Guid> Contributions = new Dictionary<EntityId, Guid>();
        }

        private readonly PreparedTargetQuery<TSource, TTarget> _targets;
        private readonly CapabilityDefinition<TTarget> _capability;
        private readonly Func<ModifierMagnitudeContext<TSource, TTarget, TParameters>, CapabilityContribution> _decision;
        private readonly MagnitudeEvaluation _decisionEvaluation;
        private readonly Func<ModifierMagnitudeContext<TSource, TTarget, TParameters>, bool> _condition;
        private readonly string _conditionDescription;
        private readonly int _priority;

        public int BindingIndex { get; }
        public TargetTracking TargetTracking { get; }

        public CapabilityModifierBinding(int bindingIndex, PreparedTargetQuery<TSource, TTarget> targets,
            CapabilityDefinition<TTarget> capability,
            Func<ModifierMagnitudeContext<TSource, TTarget, TParameters>, CapabilityContribution> decision,
            MagnitudeEvaluation decisionEvaluation, TargetTracking targetTracking,
            Func<ModifierMagnitudeContext<TSource, TTarget, TParameters>, bool> condition,
            string conditionDescription, int priority)
        {
            BindingIndex = bindingIndex;
            _targets = targets;
            _capability = capability;
            _decision = decision;
            _decisionEvaluation = decisionEvaluation;
            TargetTracking = targetTracking;
            _condition = condition;
            _conditionDescription = conditionDescription ?? string.Empty;
            _priority = priority;
        }

        public void Validate(SimulationWorld world, ModifierInstance instance) => world.RegisterCapability(_capability);

        public void Reconcile(SimulationWorld world, ModifierInstance instance, bool initial)
        {
            Runtime runtime = instance.RuntimeFor(this, () => new Runtime());
            TSource source = (TSource)instance.SourceInternal;
            TParameters parameters = (TParameters)instance.ParametersInternal;
            if (!runtime.Initialized || TargetTracking == TargetTracking.Live)
            {
                var desired = new HashSet<EntityId>(_targets.Execute(world, source).Select(item => item.Id));
                foreach (EntityId departed in runtime.Members.Where(item => !desired.Contains(item)).ToArray())
                {
                    RemoveContribution(world, instance, runtime, departed);
                    runtime.Members.Remove(departed);
                    runtime.Decisions.Remove(departed);
                }
                runtime.Members.UnionWith(desired);
                runtime.Initialized = true;
            }

            foreach (EntityId id in runtime.Members.OrderBy(item => item))
            {
                TTarget target = world.Get<TTarget>(id);
                if (target == null) continue;
                var context = new ModifierMagnitudeContext<TSource, TTarget, TParameters>(world, source, target,
                    parameters);
                CapabilityContribution decision;
                if (_decisionEvaluation == MagnitudeEvaluation.Snapshot &&
                    runtime.Decisions.TryGetValue(id, out CapabilityContribution captured))
                    decision = captured;
                else
                {
                    decision = _decision(context);
                    runtime.Decisions[id] = decision;
                }
                bool active = instance.IsActive && (_condition?.Invoke(context) ?? true);
                if (!active)
                {
                    RemoveContribution(world, instance, runtime, id);
                    continue;
                }
                if (runtime.Contributions.TryGetValue(id, out Guid existing))
                {
                    CapabilityEvaluation<TTarget> evaluation = world.EvaluateCapability(target, _capability);
                    CapabilityDecisionContribution current = evaluation.Contributions.First(item => item.Id == existing);
                    if (current.Decision == decision) continue;
                    RemoveContribution(world, instance, runtime, id);
                }
                Guid contributionId = world.AddCapabilityContribution(target, _capability, decision, source,
                    _priority, instance.DefinitionId.Value, null, instance.InstanceId);
                runtime.Contributions[id] = contributionId;
                instance.AddAffectedCapability(id, _capability.Id, BindingIndex);
            }
        }

        public ModifierBindingState Capture(SimulationWorld world, ModifierInstance instance, int bindingIndex)
        {
            Runtime runtime = instance.RuntimeFor(this, () => new Runtime());
            var state = new ModifierBindingState { BindingIndex = bindingIndex };
            foreach (EntityId id in runtime.Members.OrderBy(item => item))
            {
                runtime.Decisions.TryGetValue(id, out CapabilityContribution decision);
                bool applied = runtime.Contributions.TryGetValue(id, out Guid contributionId);
                state.AddTarget(new ModifierTargetState
                {
                    EntityId = id.Value,
                    Applied = applied,
                    ContributionId = applied ? contributionId : (Guid?)null,
                    CapabilityDecision = decision
                });
            }
            return state;
        }

        public void Restore(SimulationWorld world, ModifierInstance instance, ModifierBindingState state)
        {
            Runtime runtime = instance.RuntimeFor(this, () => new Runtime());
            TSource source = (TSource)instance.SourceInternal;
            runtime.Initialized = true;
            foreach (ModifierTargetState saved in state.Targets)
            {
                var id = new EntityId(saved.EntityId);
                TTarget target = world.Get<TTarget>(id);
                if (target == null) continue;
                CapabilityContribution decision = saved.CapabilityDecision ?? CapabilityContribution.Neutral;
                runtime.Members.Add(id);
                runtime.Decisions[id] = decision;
                if (!saved.Applied || !saved.ContributionId.HasValue) continue;
                Guid contributionId = world.AddCapabilityContribution(target, _capability, decision, source,
                    _priority, instance.DefinitionId.Value, saved.ContributionId, instance.InstanceId);
                runtime.Contributions[id] = contributionId;
                instance.AddAffectedCapability(id, _capability.Id, BindingIndex);
            }
        }

        public void Remove(SimulationWorld world, ModifierInstance instance)
        {
            Runtime runtime = instance.RuntimeFor(this, () => new Runtime());
            foreach (EntityId id in runtime.Contributions.Keys.ToArray())
                RemoveContribution(world, instance, runtime, id);
            runtime.Members.Clear();
            runtime.Decisions.Clear();
        }

        public IEnumerable<EntityId> DependencyTargets(ModifierInstance instance) =>
            instance.RuntimeFor(this, () => new Runtime()).Members;

        public IEnumerable<ConditionState> EvaluateConditions(SimulationWorld world, ModifierInstance instance)
        {
            if (_condition == null || string.IsNullOrEmpty(_conditionDescription)) yield break;
            TSource source = (TSource)instance.SourceInternal;
            TParameters parameters = (TParameters)instance.ParametersInternal;
            foreach (EntityId id in instance.RuntimeFor(this, () => new Runtime()).Members.OrderBy(item => item))
            {
                TTarget target = world.Get<TTarget>(id);
                if (target == null) continue;
                var context = new ModifierMagnitudeContext<TSource, TTarget, TParameters>(
                    world, source, target, parameters);
                yield return new ConditionState(_conditionDescription, _condition(context), id, BindingIndex);
            }
        }

        private void RemoveContribution(SimulationWorld world, ModifierInstance instance, Runtime runtime, EntityId id)
        {
            if (!runtime.Contributions.TryGetValue(id, out Guid contributionId)) return;
            world.RemoveCapabilityContribution(contributionId);
            runtime.Contributions.Remove(id);
            instance.RemoveAffectedCapability(id, _capability.Id, BindingIndex);
        }
    }

    internal sealed class CapacityModifierBinding<TSource, TTarget, TItem, TParameters> : IModifierBinding
        where TSource : WorldEntity where TTarget : WorldEntity where TItem : WorldEntity
    {
        private readonly PreparedTargetQuery<TSource, TTarget> _targets;
        private readonly CapacityDefinition<TTarget, TItem> _capacity;
        private readonly ModifierOperation _operation;
        private readonly Func<ModifierMagnitudeContext<TSource, TTarget, TParameters>, int> _magnitude;
        private readonly MagnitudeEvaluation _magnitudeEvaluation;
        private readonly Func<ModifierMagnitudeContext<TSource, TTarget, TParameters>, bool> _condition;
        private readonly string _conditionDescription;
        private readonly int _priority;

        public int BindingIndex { get; }
        public TargetTracking TargetTracking { get; }

        public CapacityModifierBinding(int bindingIndex, PreparedTargetQuery<TSource, TTarget> targets,
            CapacityDefinition<TTarget, TItem> capacity, ModifierOperation operation,
            Func<ModifierMagnitudeContext<TSource, TTarget, TParameters>, int> magnitude,
            MagnitudeEvaluation magnitudeEvaluation, TargetTracking targetTracking,
            Func<ModifierMagnitudeContext<TSource, TTarget, TParameters>, bool> condition,
            string conditionDescription, int priority)
        {
            BindingIndex = bindingIndex;
            _targets = targets;
            _capacity = capacity;
            _operation = operation;
            _magnitude = magnitude;
            _magnitudeEvaluation = magnitudeEvaluation;
            TargetTracking = targetTracking;
            _condition = condition;
            _conditionDescription = conditionDescription ?? string.Empty;
            _priority = priority;
        }

        public void Validate(SimulationWorld world, ModifierInstance instance)
        {
            foreach (TTarget target in _targets.Execute(world, (TSource)instance.SourceInternal))
                if (world.GetCapacity(target, _capacity) == null) continue;
        }

        public void Reconcile(SimulationWorld world, ModifierInstance instance, bool initial)
        {
            if (!initial && TargetTracking == TargetTracking.Snapshot)
            {
                foreach (EntityId id in instance.TargetsFor(this))
                {
                    TTarget target = world.Get<TTarget>(id);
                    if (target != null) world.GetCapacity(target, _capacity)?.Recalculate();
                }
                return;
            }
            TSource source = (TSource)instance.SourceInternal;
            TParameters parameters = (TParameters)instance.ParametersInternal;
            HashSet<EntityId> existing = instance.TargetsFor(this);
            var desired = new HashSet<EntityId>(_targets.Execute(world, source).Select(item => item.Id));
            foreach (EntityId departed in existing.Where(id => !desired.Contains(id)).ToArray())
            {
                TTarget target = world.Get<TTarget>(departed);
                CapacityCollection<TTarget, TItem> capacity = target == null ? null : world.GetCapacity(target, _capacity);
                capacity?.RemoveModifier(instance.InstanceId, BindingIndex);
                existing.Remove(departed);
                instance.RemoveAffectedCapacity(departed, _capacity.Id, BindingIndex);
            }
            foreach (EntityId id in desired.OrderBy(item => item))
            {
                TTarget target = world.Get<TTarget>(id);
                CapacityCollection<TTarget, TItem> capacity = target == null ? null : world.GetCapacity(target, _capacity);
                if (capacity == null) continue;
                if (existing.Contains(id))
                {
                    capacity.Recalculate();
                    continue;
                }
                var context = new ModifierMagnitudeContext<TSource, TTarget, TParameters>(world, source, target,
                    parameters);
                int snapshot = _magnitudeEvaluation == MagnitudeEvaluation.Snapshot ? _magnitude(context) : 0;
                Func<bool> active = () => instance.IsActive && (_condition?.Invoke(context) ?? true);
                long sequence = world.NextContributionSequence();
                capacity.AddModifier(_magnitudeEvaluation == MagnitudeEvaluation.Snapshot
                    ? new CapacityModifierContribution(instance.InstanceId, BindingIndex, _operation,
                        _priority, sequence, snapshot, active)
                    : new CapacityModifierContribution(instance.InstanceId, BindingIndex, _operation,
                        _priority, sequence, () => _magnitude(context), active));
                existing.Add(id);
                instance.AddAffectedCapacity(id, _capacity.Id, BindingIndex);
            }
        }

        public ModifierBindingState Capture(SimulationWorld world, ModifierInstance instance, int bindingIndex)
        {
            var state = new ModifierBindingState { BindingIndex = bindingIndex };
            foreach (EntityId id in instance.TargetsFor(this).OrderBy(item => item))
            {
                TTarget target = world.Get<TTarget>(id);
                CapacityModifierContribution contribution = target == null ? default :
                    world.GetCapacity(target, _capacity)?.Modifiers.FirstOrDefault(item =>
                        item.ModifierInstanceId == instance.InstanceId && item.BindingIndex == BindingIndex) ?? default;
                if (contribution.ModifierInstanceId == Guid.Empty) continue;
                state.AddTarget(new ModifierTargetState
                {
                    EntityId = id.Value,
                    Applied = true,
                    SnapshotMagnitude = contribution.Magnitude,
                    Sequence = contribution.Sequence
                });
            }
            return state;
        }

        public void Restore(SimulationWorld world, ModifierInstance instance, ModifierBindingState state)
        {
            TSource source = (TSource)instance.SourceInternal;
            TParameters parameters = (TParameters)instance.ParametersInternal;
            foreach (ModifierTargetState saved in state.Targets)
            {
                var id = new EntityId(saved.EntityId);
                TTarget target = world.Get<TTarget>(id);
                CapacityCollection<TTarget, TItem> capacity = target == null ? null : world.GetCapacity(target, _capacity);
                if (capacity == null) continue;
                var context = new ModifierMagnitudeContext<TSource, TTarget, TParameters>(world, source, target,
                    parameters);
                int snapshot = saved.SnapshotMagnitude is int value ? value : 0;
                Func<bool> active = () => instance.IsActive && (_condition?.Invoke(context) ?? true);
                capacity.AddModifier(_magnitudeEvaluation == MagnitudeEvaluation.Snapshot
                    ? new CapacityModifierContribution(instance.InstanceId, BindingIndex, _operation,
                        _priority, saved.Sequence, snapshot, active)
                    : new CapacityModifierContribution(instance.InstanceId, BindingIndex, _operation,
                        _priority, saved.Sequence, () => _magnitude(context), active));
                instance.TargetsFor(this).Add(id);
                instance.AddAffectedCapacity(id, _capacity.Id, BindingIndex);
            }
        }

        public void Remove(SimulationWorld world, ModifierInstance instance)
        {
            foreach (EntityId id in instance.TargetsFor(this).ToArray())
            {
                TTarget target = world.Get<TTarget>(id);
                if (target != null) world.GetCapacity(target, _capacity)?.RemoveModifier(instance.InstanceId, BindingIndex);
                instance.RemoveAffectedCapacity(id, _capacity.Id, BindingIndex);
            }
            instance.TargetsFor(this).Clear();
        }

        public IEnumerable<EntityId> DependencyTargets(ModifierInstance instance) => instance.TargetsFor(this);

        public IEnumerable<ConditionState> EvaluateConditions(SimulationWorld world, ModifierInstance instance)
        {
            if (_condition == null || string.IsNullOrEmpty(_conditionDescription)) yield break;
            TSource source = (TSource)instance.SourceInternal;
            TParameters parameters = (TParameters)instance.ParametersInternal;
            foreach (EntityId id in instance.TargetsFor(this).OrderBy(item => item))
            {
                TTarget target = world.Get<TTarget>(id);
                if (target == null) continue;
                var context = new ModifierMagnitudeContext<TSource, TTarget, TParameters>(
                    world, source, target, parameters);
                yield return new ConditionState(_conditionDescription, _condition(context), id, BindingIndex);
            }
        }
    }

    public sealed partial class SimulationWorld
    {
        private readonly List<ModifierInstance> _modifierInstances = new List<ModifierInstance>();
        private IReadOnlyList<ModifierInstance> _modifierInstancesView;
        private readonly SortedDictionary<long, List<Guid>> _modifierExpirations =
            new SortedDictionary<long, List<Guid>>();
        private long _nextModifierSequence = 1;
        private long _nextContributionSequence = 1;
        private bool _reconcilingModifiers;
        private bool _collectingModifierInvalidations;
        private readonly HashSet<ModifierInstance> _collectedModifierInvalidations =
            new HashSet<ModifierInstance>();
        private readonly Dictionary<string, IModifierDefinition> _modifierDefinitions =
            new Dictionary<string, IModifierDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<Tuple<EntityId, IAttributeDefinition>, HashSet<ModifierInstance>>
            _modifierAttributeDependencies =
                new Dictionary<Tuple<EntityId, IAttributeDefinition>, HashSet<ModifierInstance>>();
        private readonly Dictionary<IAttributeDefinition, HashSet<ModifierInstance>> _globalModifierDependencies =
            new Dictionary<IAttributeDefinition, HashSet<ModifierInstance>>();
        private readonly Dictionary<object, HashSet<ModifierInstance>> _modifierRelationDependencies =
            new Dictionary<object, HashSet<ModifierInstance>>();
        private readonly Dictionary<Tuple<object, EntityId>, HashSet<ModifierInstance>>
            _modifierSourceRelationDependencies =
                new Dictionary<Tuple<object, EntityId>, HashSet<ModifierInstance>>();
        private readonly Dictionary<ModifierInstance, List<Action>> _modifierDependencyRemovals =
            new Dictionary<ModifierInstance, List<Action>>();
        private readonly HashSet<ModifierInstance> _unindexedAttributeModifiers = new HashSet<ModifierInstance>();
        private readonly HashSet<ModifierInstance> _unindexedRelationModifiers = new HashSet<ModifierInstance>();
        private readonly SortedDictionary<Guid, ModifierInstance> _timeDependentModifiers =
            new SortedDictionary<Guid, ModifierInstance>();

        public IReadOnlyList<ModifierInstance> Modifiers =>
            _modifierInstancesView ?? (_modifierInstancesView = _modifierInstances.AsReadOnly());

        public ModifierInstance ApplyModifier<TSource, TParameters>(
            ModifierDefinition<TSource, TParameters> definition, TSource source, TParameters parameters,
            string stackingKey = null) where TSource : WorldEntity
        {
            using (BeginVersionPublicationScope())
            {
                if (definition == null) throw new ArgumentNullException(nameof(definition));
                RequireOwned(source);
                RegisterModifier(definition);
                if (((IModifierDefinition)definition).Bindings.Count == 0)
                    throw new InvalidOperationException($"Modifier {definition.Id} has no attribute bindings.");

                ModifierInstance stacked = ResolveStacking(definition, source, parameters, stackingKey);
                if (stacked != null) return stacked;

                var instance = new ModifierInstance(NextDeterministicGuid(), definition, source, parameters,
                    CurrentTick, stackingKey);
                foreach (IModifierBinding binding in ((IModifierDefinition)definition).Bindings)
                    binding.Validate(this, instance);

                instance.IsActive = ((IModifierDefinition)definition).SourceCondition(this, source, parameters);
                instance.Conditions = string.IsNullOrEmpty(definition.ConditionDescription)
                    ? Array.Empty<ConditionState>()
                    : new[] { new ConditionState(definition.ConditionDescription, instance.IsActive) };
                _modifierInstances.Add(instance);
                try
                {
                    foreach (IModifierBinding binding in ((IModifierDefinition)definition).Bindings)
                        binding.Reconcile(this, instance, true);
                    UpdateConditionStates(instance);
                }
                catch
                {
                    RemoveModifier(instance);
                    throw;
                }
                ScheduleExpiration(instance);
                ReindexModifierDependencies(instance);
                if (instance.DefinitionInternal.IsTimeDependent)
                    _timeDependentModifiers[instance.InstanceId] = instance;
                AdvanceVersion();
                return instance;
            }
        }

        public bool RemoveModifier(ModifierInstance instance)
        {
            using (BeginVersionPublicationScope())
            {
                if (instance == null || instance.Removed || !_modifierInstances.Remove(instance)) return false;
                foreach (IModifierBinding binding in instance.DefinitionInternal.Bindings)
                    binding.Remove(this, instance);
                instance.Removed = true;
                instance.IsActive = false;
                UnindexModifierDependencies(instance);
                _timeDependentModifiers.Remove(instance.InstanceId);
                AdvanceVersion();
                return true;
            }
        }

        public void AdvanceTo(long tick)
        {
            using (BeginVersionPublicationScope())
            {
                if (tick < CurrentTick)
                    throw new ArgumentOutOfRangeException(nameof(tick), "Simulation time cannot move backwards.");
                if (tick != CurrentTick) AdvanceVersion();
                CurrentTick = tick;
                while (_modifierExpirations.Count > 0)
                {
                    KeyValuePair<long, List<Guid>> first = _modifierExpirations.First();
                    if (first.Key > tick) break;
                    _modifierExpirations.Remove(first.Key);
                    foreach (Guid id in first.Value)
                    {
                        ModifierInstance instance = _modifierInstances.FirstOrDefault(item => item.InstanceId == id);
                        if (instance != null && instance.ExpirationTick <= tick) RemoveModifier(instance);
                    }
                }
                foreach (ModifierInstance instance in _timeDependentModifiers.Values.ToArray())
                    ReevaluateModifier(instance, true);
                EvaluateTimeTriggers();
            }
        }

        internal long NextContributionSequence() => _nextContributionSequence++;

        public void RegisterModifier<TSource, TParameters>(ModifierDefinition<TSource, TParameters> definition)
            where TSource : WorldEntity
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (_modifierDefinitions.TryGetValue(definition.Id.Value, out IModifierDefinition existing))
            {
                if (!ReferenceEquals(existing, definition))
                    throw new InvalidOperationException($"Modifier ID {definition.Id} is already registered.");
                return;
            }
            _modifierDefinitions.Add(definition.Id.Value, definition);
            ((IModifierDefinition)definition).Freeze();
        }

        partial void InvalidateModifiersForAttribute(WorldEntity changedEntity, IAttributeDefinition changedAttribute)
        {
            var affected = new HashSet<ModifierInstance>(_unindexedAttributeModifiers);
            if (_modifierAttributeDependencies.TryGetValue(Tuple.Create(changedEntity.Id, changedAttribute),
                out HashSet<ModifierInstance> scoped)) affected.UnionWith(scoped);
            if (_globalModifierDependencies.TryGetValue(changedAttribute, out HashSet<ModifierInstance> global))
                affected.UnionWith(global);
            if (_collectingModifierInvalidations)
            {
                _collectedModifierInvalidations.UnionWith(affected);
                return;
            }
            foreach (ModifierInstance instance in affected.OrderBy(item => item.InstanceId).ToArray())
            {
                ReevaluateModifier(instance, true);
            }
        }

        partial void ReconcileLiveModifiers(object changedRelation)
        {
            if (_reconcilingModifiers) return;
            _reconcilingModifiers = true;
            try
            {
                IEnumerable<ModifierInstance> candidates;
                if (changedRelation == null)
                    candidates = _modifierInstances.ToArray();
                else
                {
                    var selected = new HashSet<ModifierInstance>(_unindexedRelationModifiers);
                    if (_modifierRelationDependencies.TryGetValue(changedRelation,
                        out HashSet<ModifierInstance> indexed)) selected.UnionWith(indexed);
                    if (_currentChangedRelationSource.HasValue &&
                        _modifierSourceRelationDependencies.TryGetValue(
                            Tuple.Create(changedRelation, _currentChangedRelationSource.Value),
                            out HashSet<ModifierInstance> sourceIndexed))
                        selected.UnionWith(sourceIndexed);
                    candidates = selected.OrderBy(item => item.InstanceId).ToArray();
                }
                if (_collectingModifierInvalidations)
                {
                    _collectedModifierInvalidations.UnionWith(candidates);
                    return;
                }
                foreach (ModifierInstance instance in candidates.OrderBy(item => item.InstanceId))
                    ReevaluateModifier(instance, true);
            }
            finally { _reconcilingModifiers = false; }
        }

        partial void BeginModifierInvalidationBatch()
        {
            _collectingModifierInvalidations = true;
            _collectedModifierInvalidations.Clear();
        }

        partial void EndModifierInvalidationBatch()
        {
            _collectingModifierInvalidations = false;
            ModifierInstance[] pending = _collectedModifierInvalidations.OrderBy(item => item.InstanceId).ToArray();
            _collectedModifierInvalidations.Clear();
            foreach (ModifierInstance instance in pending) ReevaluateModifier(instance, true);
        }

        partial void RemoveModifiersOwnedByOrTargeting(EntityId entityId)
        {
            foreach (ModifierInstance instance in _modifierInstances.Where(item =>
                         item.Source.Id == entityId ||
                         item.AffectedAttributes.Any(target => target.EntityId == entityId) ||
                         item.AffectedCapabilities.Any(target => target.EntityId == entityId) ||
                         item.AffectedCapacities.Any(target => target.EntityId == entityId)).ToArray())
                RemoveModifier(instance);
        }

        private ModifierInstance ResolveStacking<TSource, TParameters>(
            ModifierDefinition<TSource, TParameters> definition, TSource source, TParameters parameters,
            string stackingKey) where TSource : WorldEntity
        {
            IEnumerable<ModifierInstance> matching = _modifierInstances.Where(item =>
                item.DefinitionId == definition.Id &&
                (!definition.Stacking.GroupBySource || item.Source.Id == source.Id) &&
                string.Equals(item.StackingKey, stackingKey ?? string.Empty, StringComparison.Ordinal));
            ModifierInstance[] instances = matching.ToArray();
            switch (definition.Stacking.Mode)
            {
                case StackingMode.Stack:
                    return null;
                case StackingMode.GroupBySource:
                    foreach (ModifierInstance item in instances) RemoveModifier(item);
                    return null;
                case StackingMode.MaximumStacks:
                    if (instances.Length >= definition.Stacking.MaximumStacks)
                        return instances.OrderByDescending(item => item.AppliedTick).First();
                    return null;
                case StackingMode.Replace:
                    foreach (ModifierInstance item in instances) RemoveModifier(item);
                    return null;
                case StackingMode.RefreshDuration:
                    if (instances.Length == 0) return null;
                    ModifierInstance refresh = instances[0];
                    refresh.ExpirationTick = definition.DurationTicks.HasValue
                        ? CurrentTick + definition.DurationTicks.Value : (long?)null;
                    ScheduleExpiration(refresh);
                    AdvanceVersion();
                    return refresh;
                case StackingMode.KeepStrongest:
                    double strength = ((IModifierDefinition)definition).GetStrength(source, parameters);
                    ModifierInstance strongest = instances.OrderByDescending(item => item.Strength).FirstOrDefault();
                    if (strongest != null && strongest.Strength >= strength) return strongest;
                    foreach (ModifierInstance item in instances) RemoveModifier(item);
                    return null;
                default: return null;
            }
        }

        private void ScheduleExpiration(ModifierInstance instance)
        {
            if (!instance.ExpirationTick.HasValue) return;
            if (!_modifierExpirations.TryGetValue(instance.ExpirationTick.Value, out List<Guid> values))
                _modifierExpirations.Add(instance.ExpirationTick.Value, values = new List<Guid>());
            values.Add(instance.InstanceId);
        }

        private void ReevaluateModifier(ModifierInstance instance, bool reconcileTargets)
        {
            if (instance == null || instance.Removed) return;
            bool active = instance.DefinitionInternal.SourceCondition(this, instance.SourceInternal,
                instance.ParametersInternal);
            instance.IsActive = active;
            if (!string.IsNullOrEmpty(instance.DefinitionInternal.ConditionDescription))
                instance.Conditions = new[]
                {
                    new ConditionState(instance.DefinitionInternal.ConditionDescription, active)
                };
            if (reconcileTargets)
                foreach (IModifierBinding binding in instance.DefinitionInternal.Bindings)
                    binding.Reconcile(this, instance, false);
            UpdateConditionStates(instance);
            foreach (AffectedAttribute target in instance.AffectedAttributes)
            {
                WorldEntity entity = Get<WorldEntity>(target.EntityId);
                if (entity != null && _attributeDefinitions.TryGetValue(target.AttributeId.Value,
                    out IAttributeDefinition definition) && entity.TryGetSlot(definition, out IAttributeSlot slot))
                {
                    slot.MarkDirty();
                    NotifyAttributeContributionChanged(entity, definition);
                }
            }
            ReindexModifierDependencies(instance);
        }

        private void UpdateConditionStates(ModifierInstance instance)
        {
            var states = new List<ConditionState>();
            if (!string.IsNullOrEmpty(instance.DefinitionInternal.ConditionDescription))
                states.Add(new ConditionState(instance.DefinitionInternal.ConditionDescription, instance.IsActive));
            foreach (IModifierBinding binding in instance.DefinitionInternal.Bindings)
                states.AddRange(binding.EvaluateConditions(this, instance));
            instance.Conditions = states.AsReadOnly();
        }

        private void ReindexModifierDependencies(ModifierInstance instance)
        {
            UnindexModifierDependencies(instance);
            var removals = new List<Action>();
            _modifierDependencyRemovals[instance] = removals;
            IReadOnlyList<ModifierDependency> attributes = instance.DefinitionInternal.AttributeDependencies;
            if (attributes.Count == 0)
            {
                _unindexedAttributeModifiers.Add(instance);
                removals.Add(() => _unindexedAttributeModifiers.Remove(instance));
            }
            else
            {
                foreach (ModifierDependency dependency in attributes)
                {
                    if (dependency.Scope == ModifierDependencyScope.Global)
                        Index(_globalModifierDependencies, dependency.Attribute, instance, removals);
                    else if (dependency.Scope == ModifierDependencyScope.Source)
                        Index(_modifierAttributeDependencies,
                            Tuple.Create(instance.Source.Id, dependency.Attribute), instance, removals);
                    else
                        foreach (EntityId targetId in instance.DefinitionInternal.Bindings
                                     .SelectMany(binding => binding.DependencyTargets(instance)).Distinct())
                            Index(_modifierAttributeDependencies, Tuple.Create(targetId, dependency.Attribute),
                                instance, removals);
                }
            }

            IReadOnlyList<ModifierRelationDependency> relations =
                instance.DefinitionInternal.RelationDependencies;
            if (relations.Count == 0)
            {
                _unindexedRelationModifiers.Add(instance);
                removals.Add(() => _unindexedRelationModifiers.Remove(instance));
            }
            else
                foreach (ModifierRelationDependency dependency in relations)
                {
                    if (dependency.Scope == RelationDependencyScope.Global)
                        Index(_modifierRelationDependencies, dependency.Relation, instance, removals);
                    else
                        Index(_modifierSourceRelationDependencies,
                            Tuple.Create(dependency.Relation, instance.Source.Id), instance, removals);
                }
        }

        private void UnindexModifierDependencies(ModifierInstance instance)
        {
            if (!_modifierDependencyRemovals.TryGetValue(instance, out List<Action> removals)) return;
            foreach (Action remove in removals) remove();
            _modifierDependencyRemovals.Remove(instance);
        }

        private static void Index<TKey>(Dictionary<TKey, HashSet<ModifierInstance>> index, TKey key,
            ModifierInstance instance, List<Action> removals)
        {
            if (!index.TryGetValue(key, out HashSet<ModifierInstance> values))
                index.Add(key, values = new HashSet<ModifierInstance>());
            values.Add(instance);
            removals.Add(() =>
            {
                values.Remove(instance);
                if (values.Count == 0) index.Remove(key);
            });
        }

        private Guid NextDeterministicGuid()
        {
            byte[] bytes = new byte[16];
            Array.Copy(BitConverter.GetBytes(_nextModifierSequence++), 0, bytes, 0, 8);
            Array.Copy(BitConverter.GetBytes(0x4C544D4F44535953L), 0, bytes, 8, 8);
            return new Guid(bytes);
        }
    }
}
