using System;
using System.Collections.Generic;
using System.Linq;

namespace LegendaryTools.ModifierSystem
{
    [Serializable]
    public sealed class ModifierInstanceState
    {
        private readonly List<ModifierBindingState> _bindings = new List<ModifierBindingState>();
        public Guid InstanceId { get; internal set; }
        public string DefinitionId { get; internal set; }
        public long SourceEntityId { get; internal set; }
        public object Parameters { get; internal set; }
        public long AppliedTick { get; internal set; }
        public long? ExpirationTick { get; internal set; }
        public string StackingKey { get; internal set; }
        public bool IsActive { get; internal set; }
        public IReadOnlyList<ModifierBindingState> Bindings => _bindings.AsReadOnly();
        internal void AddBinding(ModifierBindingState binding) => _bindings.Add(binding);
    }

    [Serializable]
    public sealed class ModifierBindingState
    {
        private readonly List<ModifierTargetState> _targets = new List<ModifierTargetState>();
        public int BindingIndex { get; internal set; }
        public IReadOnlyList<ModifierTargetState> Targets => _targets.AsReadOnly();
        internal void AddTarget(ModifierTargetState target) => _targets.Add(target);
    }

    [Serializable]
    public sealed class ModifierTargetState
    {
        public long EntityId { get; internal set; }
        public object SnapshotMagnitude { get; internal set; }
        public long Sequence { get; internal set; }
        public bool Applied { get; internal set; }
        public Guid? ContributionId { get; internal set; }
        public CapabilityContribution? CapabilityDecision { get; internal set; }
    }

    [Serializable]
    public sealed class SimulationRuntimeState
    {
        private readonly List<Guid> _completedEffectExecutions = new List<Guid>();
        private readonly List<ModifierInstanceState> _modifiers = new List<ModifierInstanceState>();
        private readonly List<CapabilitySlotState> _capabilities = new List<CapabilitySlotState>();
        private readonly List<CounterState> _counters = new List<CounterState>();
        private readonly List<VariableState> _variables = new List<VariableState>();
        private readonly List<CapacityState> _capacities = new List<CapacityState>();
        private readonly List<RandomStreamState> _randomStreams = new List<RandomStreamState>();
        private readonly List<AttributeHistoryState> _attributeHistories = new List<AttributeHistoryState>();
        private readonly List<PersistentTriggerState> _triggers = new List<PersistentTriggerState>();
        public long CurrentTick { get; internal set; }
        public long NextEntityId { get; internal set; }
        public long NextModifierSequence { get; internal set; }
        public long NextContributionSequence { get; internal set; }
        public ulong RandomState { get; internal set; }
        public long NextTriggerRegistrationId { get; internal set; }
        public IReadOnlyList<Guid> CompletedEffectExecutions => _completedEffectExecutions.AsReadOnly();
        public IReadOnlyList<ModifierInstanceState> Modifiers => _modifiers.AsReadOnly();
        public IReadOnlyList<CapabilitySlotState> Capabilities => _capabilities.AsReadOnly();
        public IReadOnlyList<CounterState> Counters => _counters.AsReadOnly();
        public IReadOnlyList<VariableState> Variables => _variables.AsReadOnly();
        public IReadOnlyList<CapacityState> Capacities => _capacities.AsReadOnly();
        public IReadOnlyList<RandomStreamState> RandomStreams => _randomStreams.AsReadOnly();
        public IReadOnlyList<AttributeHistoryState> AttributeHistories => _attributeHistories.AsReadOnly();
        public IReadOnlyList<PersistentTriggerState> Triggers => _triggers.AsReadOnly();
        internal void AddCompletedExecution(Guid id) => _completedEffectExecutions.Add(id);
        internal void AddModifier(ModifierInstanceState modifier) => _modifiers.Add(modifier);
        internal void AddCapability(CapabilitySlotState capability) => _capabilities.Add(capability);
        internal void AddCounter(CounterState counter) => _counters.Add(counter);
        internal void AddVariable(VariableState variable) => _variables.Add(variable);
        internal void AddCapacity(CapacityState capacity) => _capacities.Add(capacity);
        internal void AddRandomStream(RandomStreamState stream) => _randomStreams.Add(stream);
        internal void AddAttributeHistory(AttributeHistoryState history) => _attributeHistories.Add(history);
        internal void AddTrigger(PersistentTriggerState trigger) => _triggers.Add(trigger);
    }

    [Serializable]
    public sealed class CapabilitySlotState
    {
        private readonly List<CapabilityContributionState> _contributions = new List<CapabilityContributionState>();
        public string DefinitionId { get; internal set; }
        public long OwnerEntityId { get; internal set; }
        public IReadOnlyList<CapabilityContributionState> Contributions => _contributions.AsReadOnly();
        internal void AddContribution(CapabilityContributionState contribution) => _contributions.Add(contribution);
    }

    [Serializable]
    public sealed class CapabilityContributionState
    {
        public Guid Id { get; internal set; }
        public CapabilityContribution Decision { get; internal set; }
        public int Priority { get; internal set; }
        public long? SourceEntityId { get; internal set; }
        public string Source { get; internal set; }
    }

    [Serializable]
    public sealed class CounterState
    {
        public string KeyId { get; internal set; }
        public long OwnerEntityId { get; internal set; }
        public Type ValueType { get; internal set; }
        public object Value { get; internal set; }
    }

    [Serializable]
    public sealed class VariableState
    {
        public Type ValueType { get; internal set; }
        public string KeyId { get; internal set; }
        public VariableScope Scope { get; internal set; }
        public long? OwnerEntityId { get; internal set; }
        public VariableOwnerKind? OwnerKind { get; internal set; }
        public string OwnerKey { get; internal set; }
        public object Value { get; internal set; }
    }

    [Serializable]
    public sealed class CapacityState
    {
        private readonly List<long> _itemEntityIds = new List<long>();
        private readonly List<long> _disabledEntityIds = new List<long>();
        public string DefinitionId { get; internal set; }
        public long OwnerEntityId { get; internal set; }
        public int BaseCapacity { get; internal set; }
        public IReadOnlyList<long> ItemEntityIds => _itemEntityIds.AsReadOnly();
        public IReadOnlyList<long> DisabledEntityIds => _disabledEntityIds.AsReadOnly();
        internal void AddItem(EntityId id) => _itemEntityIds.Add(id.Value);
        internal void AddDisabled(EntityId id) => _disabledEntityIds.Add(id.Value);
    }

    [Serializable]
    public sealed class RandomStreamState
    {
        public string Id { get; internal set; }
        public ulong State { get; internal set; }
    }

    [Serializable]
    public sealed class HistoricalValueState
    {
        public long Tick { get; internal set; }
        public object Previous { get; internal set; }
        public object Current { get; internal set; }
        public string Reason { get; internal set; }
    }

    [Serializable]
    public sealed class AttributeHistoryState
    {
        private readonly List<HistoricalValueState> _records = new List<HistoricalValueState>();
        public long OwnerEntityId { get; internal set; }
        public string DefinitionId { get; internal set; }
        public long LastSampleTick { get; internal set; }
        public long SummaryCount { get; internal set; }
        public object SummaryFirst { get; internal set; }
        public object SummaryLast { get; internal set; }
        public object SummaryMinimum { get; internal set; }
        public object SummaryMaximum { get; internal set; }
        public IReadOnlyList<HistoricalValueState> Records => _records.AsReadOnly();
        internal void AddRecord(HistoricalValueState record) => _records.Add(record);
    }

    [Serializable]
    public sealed class PersistentTriggerState
    {
        public string DefinitionId { get; internal set; }
        public object State { get; internal set; }
        public bool IsActive { get; internal set; }
        public string Explanation { get; internal set; }
    }

    public interface ISimulationPersistenceAdapter
    {
        object CaptureDomainState(SimulationWorld world);
        void RestoreDomainState(SimulationWorld world, object state);
        object SerializeModifierParameters(StableId<ModifierIdKind> definitionId, object parameters);
        object DeserializeModifierParameters(StableId<ModifierIdKind> definitionId, object state);
    }

    [Serializable]
    public sealed class SimulationSaveState
    {
        public SimulationRuntimeState Runtime { get; internal set; }
        public object Domain { get; internal set; }
    }

    public sealed partial class SimulationWorld
    {
        public SimulationRuntimeState CaptureRuntimeState(ISimulationPersistenceAdapter adapter)
        {
            if (adapter == null) throw new ArgumentNullException(nameof(adapter));
            var state = new SimulationRuntimeState
            {
                CurrentTick = CurrentTick,
                NextEntityId = _nextEntityId,
                NextModifierSequence = _nextModifierSequence,
                NextContributionSequence = _nextContributionSequence,
                RandomState = Random.State,
                NextTriggerRegistrationId = _nextTriggerRegistrationId,
            };
            foreach (Guid execution in _completedEffectExecutions.OrderBy(item => item))
                state.AddCompletedExecution(execution);
            foreach (KeyValuePair<StableId<RandomStreamIdKind>, XorShiftRandom> stream in _randomStreams
                         .OrderBy(item => item.Key.Value, StringComparer.Ordinal))
                state.AddRandomStream(new RandomStreamState { Id = stream.Key.Value, State = stream.Value.State });
            foreach (WorldEntity entity in Entities)
            foreach (IAttributeSlot slot in entity.Slots)
            {
                AttributeHistoryState history = slot.CaptureHistory(entity.Id);
                if (history != null) state.AddAttributeHistory(history);
            }
            foreach (ITriggerInstance trigger in _triggers.Values.Where(item => item.PersistState)
                         .OrderBy(item => item.DefinitionId.Value, StringComparer.Ordinal))
                state.AddTrigger(new PersistentTriggerState
                {
                    DefinitionId = trigger.DefinitionId.Value,
                    State = trigger.BoxedState,
                    IsActive = trigger.IsActive,
                    Explanation = trigger.Explanation
                });
            foreach (ICapabilitySlot slot in _capabilities.Values
                         .OrderBy(item => item.OwnerId).ThenBy(item => item.DefinitionId.Value))
            {
                var capability = new CapabilitySlotState
                {
                    DefinitionId = slot.DefinitionId.Value,
                    OwnerEntityId = slot.OwnerId.Value
                };
                foreach (CapabilityDecisionContribution contribution in slot.Contributions.Where(item =>
                             !item.ModifierInstanceId.HasValue))
                    capability.AddContribution(new CapabilityContributionState
                    {
                        Id = contribution.Id,
                        Decision = contribution.Decision,
                        Priority = contribution.Priority,
                        SourceEntityId = contribution.SourceId?.Value,
                        Source = contribution.Source
                    });
                state.AddCapability(capability);
            }
            foreach (ITypedCounter counter in _counters.Values.Cast<ITypedCounter>()
                         .OrderBy(item => item.OwnerId).ThenBy(item => item.KeyId, StringComparer.Ordinal))
                state.AddCounter(new CounterState
                {
                    KeyId = counter.KeyId,
                    OwnerEntityId = counter.OwnerId.Value,
                    ValueType = counter.ValueType,
                    Value = counter.BoxedValue
                });
            foreach (KeyValuePair<Tuple<Type, string, VariableScope, VariableOwnerId?>, object> variable in Variables.Entries
                         .OrderBy(item => item.Key.Item3)
                         .ThenBy(item => item.Key.Item4.HasValue ? (int)item.Key.Item4.Value.Kind : -1)
                         .ThenBy(item => item.Key.Item4.HasValue ? item.Key.Item4.Value.Value : string.Empty,
                             StringComparer.Ordinal)
                         .ThenBy(item => item.Key.Item2, StringComparer.Ordinal))
                state.AddVariable(new VariableState
                {
                    ValueType = variable.Key.Item1,
                    KeyId = variable.Key.Item2,
                    Scope = variable.Key.Item3,
                    OwnerEntityId = ToEntityId(variable.Key.Item4),
                    OwnerKind = variable.Key.Item4?.Kind,
                    OwnerKey = variable.Key.Item4?.Value,
                    Value = variable.Value
                });
            foreach (ICapacityCollection collection in _capacities.Values
                         .OrderBy(item => item.OwnerId).ThenBy(item => item.DefinitionId.Value, StringComparer.Ordinal))
            {
                var capacity = new CapacityState
                {
                    DefinitionId = collection.DefinitionId.Value,
                    OwnerEntityId = collection.OwnerId.Value,
                    BaseCapacity = collection.BaseCapacity
                };
                foreach (EntityId id in collection.ItemIds) capacity.AddItem(id);
                foreach (EntityId id in collection.DisabledItems.OrderBy(item => item)) capacity.AddDisabled(id);
                state.AddCapacity(capacity);
            }
            foreach (ModifierInstance modifier in _modifierInstances)
            {
                var modifierState = new ModifierInstanceState
                {
                    InstanceId = modifier.InstanceId,
                    DefinitionId = modifier.DefinitionId.Value,
                    SourceEntityId = modifier.Source.Id.Value,
                    Parameters = adapter.SerializeModifierParameters(modifier.DefinitionId, modifier.Parameters),
                    AppliedTick = modifier.AppliedTick,
                    ExpirationTick = modifier.ExpirationTick,
                    StackingKey = modifier.StackingKey,
                    IsActive = modifier.IsActive
                };
                for (int index = 0; index < modifier.DefinitionInternal.Bindings.Count; index++)
                    modifierState.AddBinding(modifier.DefinitionInternal.Bindings[index].Capture(this, modifier, index));
                state.AddModifier(modifierState);
            }
            return state;
        }

        public object CaptureDomainState(ISimulationPersistenceAdapter adapter)
        {
            if (adapter == null) throw new ArgumentNullException(nameof(adapter));
            return adapter.CaptureDomainState(this);
        }

        public SimulationSaveState CaptureSaveState(ISimulationPersistenceAdapter adapter) => new SimulationSaveState
        {
            Runtime = CaptureRuntimeState(adapter),
            Domain = CaptureDomainState(adapter)
        };

        public void RestoreSaveState(SimulationSaveState save, ISimulationPersistenceAdapter adapter)
        {
            using (BeginVersionPublicationScope())
            {
                if (save == null) throw new ArgumentNullException(nameof(save));
                if (adapter == null) throw new ArgumentNullException(nameof(adapter));
                foreach (ModifierInstance modifier in _modifierInstances.ToArray()) RemoveModifier(modifier);
                _modifierExpirations.Clear();
                adapter.RestoreDomainState(this, save.Domain);
                RestoreContinuityMetadataCore(save.Runtime);

                foreach (ModifierInstanceState saved in save.Runtime.Modifiers)
                {
                    if (!_modifierDefinitions.TryGetValue(saved.DefinitionId, out IModifierDefinition definition))
                        throw new InvalidOperationException($"Modifier definition {saved.DefinitionId} must be registered before loading.");
                    WorldEntity source = Get<WorldEntity>(new EntityId(saved.SourceEntityId));
                    if (source == null || !definition.AcceptsSource(source))
                        throw new InvalidOperationException($"Modifier source {saved.SourceEntityId} is missing or has the wrong type.");
                    object parameters = adapter.DeserializeModifierParameters(definition.Id, saved.Parameters);
                    var instance = new ModifierInstance(saved.InstanceId, definition, source, parameters,
                        saved.AppliedTick, saved.StackingKey)
                    {
                        ExpirationTick = saved.ExpirationTick,
                        IsActive = saved.IsActive
                    };
                    instance.Conditions = string.IsNullOrEmpty(definition.ConditionDescription)
                        ? Array.Empty<ConditionState>()
                        : new[] { new ConditionState(definition.ConditionDescription, saved.IsActive) };
                    _modifierInstances.Add(instance);
                    foreach (ModifierBindingState bindingState in saved.Bindings)
                    {
                        if (bindingState.BindingIndex < 0 || bindingState.BindingIndex >= definition.Bindings.Count)
                            throw new InvalidOperationException($"Invalid binding index in modifier {saved.DefinitionId}.");
                        definition.Bindings[bindingState.BindingIndex].Restore(this, instance, bindingState);
                    }
                    UpdateConditionStates(instance);
                    ScheduleExpiration(instance);
                    ReindexModifierDependencies(instance);
                    if (definition.IsTimeDependent)
                        _timeDependentModifiers[instance.InstanceId] = instance;
                }
                foreach (WorldEntity entity in Entities)
                foreach (IAttributeSlot slot in entity.Slots)
                    slot.RecalculateSilently();
                RestoreAttributeHistories(save.Runtime);
                AdvanceQueryVersion(null, null);
                AdvanceVersion();
            }
        }

        public void RestoreContinuityMetadata(SimulationRuntimeState state)
        {
            RestoreContinuityMetadataCore(state);
            foreach (WorldEntity entity in Entities)
            foreach (IAttributeSlot slot in entity.Slots)
                slot.RecalculateSilently();
            RestoreAttributeHistories(state);
        }

        private void RestoreContinuityMetadataCore(SimulationRuntimeState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            CurrentTick = state.CurrentTick;
            _nextEntityId = state.NextEntityId;
            _nextModifierSequence = state.NextModifierSequence;
            _nextContributionSequence = state.NextContributionSequence;
            _nextTriggerRegistrationId = Math.Max(_nextTriggerRegistrationId, state.NextTriggerRegistrationId);
            RestoreRandomStreams(state);
            _completedEffectExecutions.Clear();
            foreach (Guid id in state.CompletedEffectExecutions) _completedEffectExecutions.Add(id);
            RestoreCapabilities(state);
            RestoreCounters(state);
            RestoreVariables(state);
            RestoreCapacities(state);
            RestoreTriggers(state);
        }

        private void RestoreCapabilities(SimulationRuntimeState state)
        {
            _capabilities.Clear();
            _capabilityContributionOwners.Clear();
            foreach (CapabilitySlotState saved in state.Capabilities)
            {
                if (!_capabilityDefinitions.TryGetValue(saved.DefinitionId, out ICapabilityDefinition definition))
                    throw new InvalidOperationException($"Capability {saved.DefinitionId} must be registered before loading.");
                WorldEntity owner = Get<WorldEntity>(new EntityId(saved.OwnerEntityId));
                if (owner == null || !definition.AcceptsOwner(owner))
                    throw new InvalidOperationException($"Capability owner {saved.OwnerEntityId} is missing or invalid.");
                ICapabilitySlot slot = definition.CreateSlot(owner);
                _capabilities.Add(Tuple.Create((object)definition, owner.Id), slot);
                foreach (CapabilityContributionState contribution in saved.Contributions)
                {
                    var value = new CapabilityDecisionContribution(contribution.Id, contribution.Decision,
                        contribution.Priority,
                        contribution.SourceEntityId.HasValue
                            ? new EntityId(contribution.SourceEntityId.Value) : (EntityId?)null,
                        contribution.Source);
                    slot.Add(value);
                    _capabilityContributionOwners.Add(contribution.Id, slot);
                }
            }
        }

        private void RestoreCounters(SimulationRuntimeState state)
        {
            foreach (CounterState saved in state.Counters)
            {
                var key = Tuple.Create(saved.ValueType, saved.KeyId, new EntityId(saved.OwnerEntityId));
                if (!_counters.TryGetValue(key, out object counter) || !(counter is ITypedCounter typed))
                    throw new InvalidOperationException($"Counter {saved.KeyId} must be created by the domain adapter before loading.");
                typed.RestoreBoxedValue(saved.Value);
            }
        }

        private void RestoreVariables(SimulationRuntimeState state)
        {
            Variables.Clear();
            foreach (VariableState saved in state.Variables)
                Variables.Restore(saved.ValueType, saved.KeyId, saved.Scope,
                    RestoreVariableOwner(saved),
                    saved.Value);
        }

        private static long? ToEntityId(VariableOwnerId? owner)
        {
            if (!owner.HasValue || owner.Value.Kind != VariableOwnerKind.Entity) return null;
            return long.TryParse(owner.Value.Value, out long value) ? value : (long?)null;
        }

        private static VariableOwnerId? RestoreVariableOwner(VariableState saved)
        {
            if (saved.OwnerKind.HasValue)
            {
                switch (saved.OwnerKind.Value)
                {
                    case VariableOwnerKind.Entity:
                        if (saved.OwnerEntityId.HasValue) return VariableOwnerId.Entity(new EntityId(saved.OwnerEntityId.Value));
                        if (long.TryParse(saved.OwnerKey, out long entityId) && entityId > 0)
                            return VariableOwnerId.Entity(new EntityId(entityId));
                        break;
                    case VariableOwnerKind.Effect:
                        if (Guid.TryParse(saved.OwnerKey, out Guid effectId)) return VariableOwnerId.Effect(effectId);
                        break;
                    case VariableOwnerKind.EventChain:
                        return VariableOwnerId.EventChain(saved.OwnerKey);
                }
                throw new InvalidOperationException($"Invalid owner for variable {saved.KeyId}.");
            }
            return saved.OwnerEntityId.HasValue
                ? VariableOwnerId.Entity(new EntityId(saved.OwnerEntityId.Value))
                : (VariableOwnerId?)null;
        }

        private void RestoreCapacities(SimulationRuntimeState state)
        {
            foreach (CapacityState saved in state.Capacities)
            {
                var key = Tuple.Create(saved.DefinitionId, new EntityId(saved.OwnerEntityId));
                if (!_capacities.TryGetValue(key, out ICapacityCollection collection))
                    throw new InvalidOperationException($"Capacity {saved.DefinitionId} must be created before loading.");
                collection.Restore(this, saved.BaseCapacity,
                    saved.ItemEntityIds.Select(value => new EntityId(value)).ToArray(),
                    saved.DisabledEntityIds.Select(value => new EntityId(value)).ToArray());
            }
        }

        private void RestoreRandomStreams(SimulationRuntimeState state)
        {
            _randomStreams.Clear();
            RandomStreamState savedDefault = state.RandomStreams.FirstOrDefault(item =>
                string.Equals(item.Id, DefaultRandomStreamId.Value, StringComparison.Ordinal));
            Random.RestoreState(savedDefault?.State ?? state.RandomState);
            _randomStreams.Add(DefaultRandomStreamId, Random);
            foreach (RandomStreamState saved in state.RandomStreams)
            {
                var id = new StableId<RandomStreamIdKind>(saved.Id);
                if (id == DefaultRandomStreamId) continue;
                var stream = new XorShiftRandom(1);
                stream.RestoreState(saved.State);
                _randomStreams.Add(id, stream);
            }
        }

        private void RestoreAttributeHistories(SimulationRuntimeState state)
        {
            foreach (WorldEntity entity in Entities)
            foreach (IAttributeSlot slot in entity.Slots)
                slot.ClearHistory();
            foreach (AttributeHistoryState saved in state.AttributeHistories)
            {
                WorldEntity owner = Get<WorldEntity>(new EntityId(saved.OwnerEntityId));
                if (owner == null)
                    throw new InvalidOperationException($"History owner {saved.OwnerEntityId} is missing.");
                IAttributeDefinition definition = owner.AttributeDefinitions.FirstOrDefault(item =>
                    string.Equals(item.Id.Value, saved.DefinitionId, StringComparison.Ordinal));
                if (definition == null || !owner.TryGetSlot(definition, out IAttributeSlot slot))
                    throw new InvalidOperationException(
                        $"History attribute {saved.DefinitionId} is missing on {saved.OwnerEntityId}.");
                slot.RestoreHistory(saved);
            }
        }

        private void RestoreTriggers(SimulationRuntimeState state)
        {
            foreach (PersistentTriggerState saved in state.Triggers)
            {
                if (!_triggersByDefinition.TryGetValue(saved.DefinitionId, out ITriggerInstance trigger))
                    throw new InvalidOperationException(
                        $"Trigger {saved.DefinitionId} must be registered by the domain adapter before loading.");
                trigger.Restore(saved.State, saved.IsActive, saved.Explanation);
            }
        }
    }
}
