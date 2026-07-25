using System;
using System.Collections.Generic;
using System.Linq;

namespace LegendaryTools.ModifierSystem
{
    internal interface ICapabilityDefinition
    {
        StableId<CapabilityIdKind> Id { get; }
        bool AcceptsOwner(WorldEntity owner);
        ICapabilitySlot CreateSlot(WorldEntity owner);
    }

    public sealed class CapabilityDefinition<TEntity> : ICapabilityDefinition where TEntity : WorldEntity
    {
        public StableId<CapabilityIdKind> Id { get; }
        public CapabilityResolutionPolicy Policy { get; }
        public bool DefaultValue { get; }
        public int RequiredAllowCount { get; }
        public IReadOnlyList<string> RequiredSources { get; }
        public IReadOnlyList<StableId<CapabilitySourceIdKind>> RequiredSourceIds { get; }

        public CapabilityDefinition(string id, CapabilityResolutionPolicy policy,
            bool defaultValue = false, int requiredAllowCount = 0,
            IEnumerable<string> requiredSources = null,
            IEnumerable<StableId<CapabilitySourceIdKind>> requiredSourceIds = null)
        {
            if (requiredAllowCount < 0) throw new ArgumentOutOfRangeException(nameof(requiredAllowCount));
            Id = new StableId<CapabilityIdKind>(id);
            Policy = policy;
            DefaultValue = defaultValue;
            RequiredAllowCount = requiredAllowCount;
            RequiredSources = Array.AsReadOnly((requiredSources ?? Enumerable.Empty<string>())
                .Select(item => item ?? string.Empty)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray());
            RequiredSourceIds = Array.AsReadOnly(RequiredSources
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => new StableId<CapabilitySourceIdKind>(item))
                .Concat(requiredSourceIds ?? Enumerable.Empty<StableId<CapabilitySourceIdKind>>())
                .Distinct()
                .OrderBy(item => item)
                .ToArray());
        }

        bool ICapabilityDefinition.AcceptsOwner(WorldEntity owner) => owner is TEntity;
        ICapabilitySlot ICapabilityDefinition.CreateSlot(WorldEntity owner) =>
            new CapabilitySlot<TEntity>(this, (TEntity)owner);
    }

    public readonly struct CapabilityDecisionContribution
    {
        public Guid Id { get; }
        public CapabilityContribution Decision { get; }
        public int Priority { get; }
        public EntityId? SourceId { get; }
        public string Source { get; }
        public StableId<CapabilitySourceIdKind>? SourceKey { get; }
        public Guid? ModifierInstanceId { get; }

        public CapabilityDecisionContribution(Guid id, CapabilityContribution decision, int priority,
            EntityId? sourceId, string source, Guid? modifierInstanceId = null,
            StableId<CapabilitySourceIdKind>? sourceKey = null)
        {
            Id = id;
            Decision = decision;
            Priority = priority;
            SourceId = sourceId;
            Source = source ?? string.Empty;
            SourceKey = sourceKey;
            ModifierInstanceId = modifierInstanceId;
        }
    }

    public sealed class CapabilityEvaluation<TEntity> where TEntity : WorldEntity
    {
        public CapabilityDefinition<TEntity> Definition { get; }
        public TEntity Owner { get; }
        public bool IsAllowed { get; }
        public IReadOnlyList<CapabilityDecisionContribution> Contributions { get; }
        public CapabilityDecisionContribution? WinningContribution { get; }

        internal CapabilityEvaluation(CapabilityDefinition<TEntity> definition, TEntity owner, bool isAllowed,
            IReadOnlyList<CapabilityDecisionContribution> contributions,
            CapabilityDecisionContribution? winningContribution)
        {
            Definition = definition;
            Owner = owner;
            IsAllowed = isAllowed;
            Contributions = contributions == null
                ? (IReadOnlyList<CapabilityDecisionContribution>)Array.Empty<CapabilityDecisionContribution>()
                : Array.AsReadOnly(contributions.ToArray());
            WinningContribution = winningContribution;
        }
    }

    internal interface ICapabilitySlot
    {
        StableId<CapabilityIdKind> DefinitionId { get; }
        EntityId OwnerId { get; }
        IReadOnlyList<CapabilityDecisionContribution> Contributions { get; }
        void Add(CapabilityDecisionContribution contribution);
        bool Remove(Guid id);
    }

    internal sealed class CapabilityContributionRollbackState
    {
        public ICapabilitySlot Slot { get; }
        public CapabilityDecisionContribution Contribution { get; }

        public CapabilityContributionRollbackState(ICapabilitySlot slot,
            CapabilityDecisionContribution contribution)
        {
            Slot = slot;
            Contribution = contribution;
        }
    }

    internal sealed class CapabilitySlot<TEntity> : ICapabilitySlot where TEntity : WorldEntity
    {
        private readonly CapabilityDefinition<TEntity> _definition;
        private readonly TEntity _owner;
        private readonly List<CapabilityDecisionContribution> _contributions =
            new List<CapabilityDecisionContribution>();
        public StableId<CapabilityIdKind> DefinitionId => _definition.Id;
        public EntityId OwnerId => _owner.Id;
        public IReadOnlyList<CapabilityDecisionContribution> Contributions => _contributions;

        public CapabilitySlot(CapabilityDefinition<TEntity> definition, TEntity owner)
        {
            _definition = definition;
            _owner = owner;
        }

        public void Add(CapabilityDecisionContribution contribution)
        {
            _contributions.Add(contribution);
            _contributions.Sort((left, right) =>
            {
                int priority = right.Priority.CompareTo(left.Priority);
                return priority != 0 ? priority : left.Id.CompareTo(right.Id);
            });
        }

        public bool Remove(Guid id) => _contributions.RemoveAll(item => item.Id == id) > 0;

        public CapabilityEvaluation<TEntity> Evaluate()
        {
            CapabilityDecisionContribution? winner = null;
            bool result;
            switch (_definition.Policy)
            {
                case CapabilityResolutionPolicy.HighestPriorityWins:
                    CapabilityDecisionContribution first = _contributions.FirstOrDefault(item =>
                        item.Decision != CapabilityContribution.Neutral);
                    if (first.Decision == CapabilityContribution.Neutral) result = _definition.DefaultValue;
                    else
                    {
                        winner = first;
                        result = first.Decision == CapabilityContribution.Allow;
                    }
                    break;
                case CapabilityResolutionPolicy.AllRequiredMustAllow:
                    if (_contributions.Any(item => item.Decision == CapabilityContribution.Deny))
                    {
                        winner = _contributions.First(item => item.Decision == CapabilityContribution.Deny);
                        result = false;
                    }
                    else
                    {
                        int allows = _contributions.Count(item => item.Decision == CapabilityContribution.Allow);
                        bool requiredSourcesAllow = _definition.RequiredSourceIds.Count == 0 ||
                            _definition.RequiredSourceIds.All(required =>
                                _contributions.Any(item =>
                                    item.Decision == CapabilityContribution.Allow &&
                                    item.SourceKey.HasValue && item.SourceKey.Value == required));
                        result = requiredSourcesAllow && (_definition.RequiredAllowCount == 0
                            ? (allows > 0 || _definition.DefaultValue)
                            : allows >= _definition.RequiredAllowCount);
                    }
                    break;
                case CapabilityResolutionPolicy.AllowUnlessDenied:
                case CapabilityResolutionPolicy.DenyOverridesAllow:
                default:
                    CapabilityDecisionContribution? deny = _contributions.Cast<CapabilityDecisionContribution?>()
                        .FirstOrDefault(item => item.Value.Decision == CapabilityContribution.Deny);
                    if (deny.HasValue) { winner = deny; result = false; }
                    else
                    {
                        CapabilityDecisionContribution? allow = _contributions.Cast<CapabilityDecisionContribution?>()
                            .FirstOrDefault(item => item.Value.Decision == CapabilityContribution.Allow);
                        winner = allow;
                        result = allow.HasValue || _definition.DefaultValue;
                    }
                    break;
            }
            return new CapabilityEvaluation<TEntity>(_definition, _owner, result, _contributions.ToArray(), winner);
        }
    }

    public sealed class CapabilityContributionHandle : IDisposable
    {
        private readonly SimulationWorld _world;
        private bool _disposed;
        public Guid Id { get; }

        internal CapabilityContributionHandle(SimulationWorld world, Guid id) { _world = world; Id = id; }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _world.RemoveCapabilityContribution(Id);
        }
    }

    public sealed partial class SimulationWorld
    {
        private readonly Dictionary<Tuple<object, EntityId>, ICapabilitySlot> _capabilities =
            new Dictionary<Tuple<object, EntityId>, ICapabilitySlot>();
        private readonly Dictionary<Guid, ICapabilitySlot> _capabilityContributionOwners =
            new Dictionary<Guid, ICapabilitySlot>();
        private readonly Dictionary<string, ICapabilityDefinition> _capabilityDefinitions =
            new Dictionary<string, ICapabilityDefinition>(StringComparer.Ordinal);

        public void RegisterCapability<TEntity>(CapabilityDefinition<TEntity> definition)
            where TEntity : WorldEntity
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (_capabilityDefinitions.TryGetValue(definition.Id.Value, out ICapabilityDefinition existing))
            {
                if (!ReferenceEquals(existing, definition))
                    throw new InvalidOperationException($"Capability ID {definition.Id} is already registered.");
                return;
            }
            _capabilityDefinitions.Add(definition.Id.Value, definition);
        }

        public CapabilityContributionHandle ContributeCapability<TEntity>(TEntity owner,
            CapabilityDefinition<TEntity> definition, CapabilityContribution decision,
            WorldEntity source = null, int priority = 0, string sourceDescription = null)
            where TEntity : WorldEntity
        {
            EnsureMutationAllowed();
            RequireOwned(owner);
            if (source != null) RequireOwned(source);
            RegisterCapability(definition);
            Guid id = AddCapabilityContribution(owner, definition, decision, source, priority,
                sourceDescription, null, null);
            return new CapabilityContributionHandle(this, id);
        }

        public CapabilityContributionHandle ContributeCapability<TEntity>(TEntity owner,
            CapabilityDefinition<TEntity> definition, CapabilityContribution decision,
            StableId<CapabilitySourceIdKind> sourceKey, WorldEntity source = null, int priority = 0,
            string sourceDescription = null)
            where TEntity : WorldEntity
        {
            EnsureMutationAllowed();
            RequireOwned(owner);
            if (source != null) RequireOwned(source);
            RegisterCapability(definition);
            Guid id = AddCapabilityContribution(owner, definition, decision, source, priority,
                sourceDescription ?? sourceKey.Value, null, null, sourceKey);
            return new CapabilityContributionHandle(this, id);
        }

        public CapabilityEvaluation<TEntity> EvaluateCapability<TEntity>(TEntity owner,
            CapabilityDefinition<TEntity> definition) where TEntity : WorldEntity
        {
            RequireOwned(owner);
            RegisterCapability(definition);
            return GetCapabilitySlot(owner, definition).Evaluate();
        }

        internal void RemoveCapabilityContribution(Guid id)
        {
            EnsureMutationAllowed();
            if (!_capabilityContributionOwners.TryGetValue(id, out ICapabilitySlot slot)) return;
            slot.Remove(id);
            _capabilityContributionOwners.Remove(id);
            AdvanceVersion();
        }

        internal CapabilityContributionRollbackState RemoveCapabilityContributionForEffect(Guid id)
        {
            EnsureMutationAllowed();
            if (!_capabilityContributionOwners.TryGetValue(id, out ICapabilitySlot slot)) return null;
            CapabilityDecisionContribution contribution =
                slot.Contributions.First(item => item.Id == id);
            slot.Remove(id);
            _capabilityContributionOwners.Remove(id);
            AdvanceVersion();
            return new CapabilityContributionRollbackState(slot, contribution);
        }

        internal void RestoreCapabilityContributionForEffect(CapabilityContributionRollbackState state)
        {
            EnsureMutationAllowed();
            if (state == null) throw new ArgumentNullException(nameof(state));
            state.Slot.Add(state.Contribution);
            _capabilityContributionOwners.Add(state.Contribution.Id, state.Slot);
            AdvanceVersion();
        }

        internal Guid AddCapabilityContribution<TEntity>(TEntity owner, CapabilityDefinition<TEntity> definition,
            CapabilityContribution decision, WorldEntity source, int priority, string sourceDescription,
            Guid? contributionId, Guid? modifierInstanceId,
            StableId<CapabilitySourceIdKind>? sourceKey = null) where TEntity : WorldEntity
        {
            RequireOwned(owner);
            if (source != null) RequireOwned(source);
            RegisterCapability(definition);
            CapabilitySlot<TEntity> slot = GetCapabilitySlot(owner, definition);
            Guid id = contributionId ?? NextDeterministicGuid();
            slot.Add(new CapabilityDecisionContribution(id, decision, priority, source?.Id,
                sourceDescription ?? source?.GetType().Name, modifierInstanceId,
                sourceKey ?? (!string.IsNullOrWhiteSpace(sourceDescription)
                    ? new StableId<CapabilitySourceIdKind>(sourceDescription)
                    : (StableId<CapabilitySourceIdKind>?)null)));
            _capabilityContributionOwners.Add(id, slot);
            AdvanceVersion();
            return id;
        }

        private CapabilitySlot<TEntity> GetCapabilitySlot<TEntity>(TEntity owner,
            CapabilityDefinition<TEntity> definition) where TEntity : WorldEntity
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            var key = Tuple.Create((object)definition, owner.Id);
            if (!_capabilities.TryGetValue(key, out ICapabilitySlot value))
                _capabilities.Add(key, value = new CapabilitySlot<TEntity>(definition, owner));
            return (CapabilitySlot<TEntity>)value;
        }
    }
}
