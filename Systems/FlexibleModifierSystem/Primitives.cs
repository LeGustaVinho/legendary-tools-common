using System;

namespace LegendaryTools.ModifierSystem
{
    public readonly struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
    {
        public long Value { get; }

        public EntityId(long value)
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
        }

        public bool Equals(EntityId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EntityId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(EntityId other) => Value.CompareTo(other.Value);
        public override string ToString() => Value.ToString();
        public static bool operator ==(EntityId left, EntityId right) => left.Equals(right);
        public static bool operator !=(EntityId left, EntityId right) => !left.Equals(right);
    }

    public readonly struct StableId<TKind> : IEquatable<StableId<TKind>>, IComparable<StableId<TKind>>
    {
        public string Value { get; }

        public StableId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A stable ID is required.", nameof(value));
            Value = value;
        }

        public bool Equals(StableId<TKind> other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is StableId<TKind> other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public int CompareTo(StableId<TKind> other) => string.CompareOrdinal(Value, other.Value);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(StableId<TKind> left, StableId<TKind> right) => left.Equals(right);
        public static bool operator !=(StableId<TKind> left, StableId<TKind> right) => !left.Equals(right);
    }

    public sealed class AttributeIdKind { private AttributeIdKind() { } }
    public sealed class RelationIdKind { private RelationIdKind() { } }
    public sealed class ModifierIdKind { private ModifierIdKind() { } }
    public sealed class CapabilityIdKind { private CapabilityIdKind() { } }
    public sealed class CounterIdKind { private CounterIdKind() { } }
    public sealed class VariableIdKind { private VariableIdKind() { } }
    public sealed class EffectIdKind { private EffectIdKind() { } }
    public sealed class CapacityIdKind { private CapacityIdKind() { } }
    public sealed class RandomStreamIdKind { private RandomStreamIdKind() { } }
    public sealed class TagIdKind { private TagIdKind() { } }
    public sealed class ComponentIdKind { private ComponentIdKind() { } }
    public sealed class TriggerIdKind { private TriggerIdKind() { } }

    public enum ModifierOperation
    {
        Add,
        Multiply,
        Replace,
        ClampMinimum,
        ClampMaximum,
        Minimum,
        Maximum,
        Custom
    }

    public enum MagnitudeEvaluation
    {
        Snapshot,
        Live
    }

    public enum TargetTracking
    {
        Snapshot,
        Live
    }

    public enum ModifierDependencyScope
    {
        Source,
        Target,
        Global
    }

    public enum RelationDependencyScope
    {
        Source,
        Global
    }

    public enum StackingMode
    {
        Stack,
        Replace,
        KeepStrongest,
        RefreshDuration,
        MaximumStacks,
        GroupBySource
    }

    public enum CapabilityContribution
    {
        Neutral,
        Allow,
        Deny
    }

    public enum CapabilityResolutionPolicy
    {
        DenyOverridesAllow,
        AllowUnlessDenied,
        HighestPriorityWins,
        AllRequiredMustAllow
    }

    public enum CapacityOverflowPolicy
    {
        PreserveAndBlockNew,
        PreserveWithPenalty,
        DisableExcess,
        RemoveExcess,
        ClampReductionToUsage,
        RequestDecision,
        AllowExceeded
    }

    public enum CapacitySelectionPolicy
    {
        OldestFirst,
        NewestFirst,
        LowestPriority,
        HighestUpkeep,
        ExplicitRanking,
        PlayerSelection
    }

    public enum CapacityDecisionAction
    {
        Preserve,
        DisableSelected,
        RemoveSelected
    }

    public enum EffectStatus
    {
        Succeeded,
        Failed,
        Rejected,
        NoChange,
        Duplicate
    }

    public enum EffectAtomicity { Atomic, PartialAllowed }
    public enum EffectReversibility { Rollback, Compensation, None }

    public enum HistoryRecordMode
    {
        None,
        Exact,
        Sampled,
        AggregateOnly
    }

    [Flags]
    public enum HistoryChangeKind
    {
        None = 0,
        BaseValue = 1,
        FinalValue = 2,
        All = BaseValue | FinalValue
    }

    public enum HistoryOverflowPolicy
    {
        DiscardOldest,
        RejectNewest,
        MergeOldest
    }

    public enum AttributeEvaluationStage
    {
        Base,
        Additive,
        Multiplicative,
        Replacement,
        Limits,
        Custom,
        Final
    }
}
