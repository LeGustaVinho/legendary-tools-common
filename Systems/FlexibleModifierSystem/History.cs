using System;
using System.Collections.Generic;
using System.Linq;

namespace LegendaryTools.ModifierSystem
{
    public sealed class HistoryPolicy
    {
        public static HistoryPolicy Disabled { get; } = new HistoryPolicy(HistoryRecordMode.None);

        public HistoryRecordMode Mode { get; }
        public int MaximumRecords { get; }
        public long RetentionTicks { get; }
        public long SampleIntervalTicks { get; }
        public bool Persist { get; }
        public bool DiagnosticOnly { get; }
        public int MemoryBudgetBytes { get; }
        public HistoryOverflowPolicy OverflowPolicy { get; }
        public HistoryChangeKind Changes { get; }
        public int EstimatedRecordBytes { get; }
        public HistoryAggregateKind Aggregates { get; }
        internal Func<object, object, object> TotalAccumulator { get; }

        public HistoryPolicy(
            HistoryRecordMode mode,
            int maximumRecords = 0,
            long retentionTicks = 0,
            long sampleIntervalTicks = 1,
            bool persist = true,
            bool diagnosticOnly = false,
            int memoryBudgetBytes = 0,
            HistoryOverflowPolicy overflowPolicy = HistoryOverflowPolicy.DiscardOldest,
            HistoryChangeKind changes = HistoryChangeKind.All,
            int estimatedRecordBytes = 64,
            Func<object, object, object> totalAccumulator = null,
            HistoryAggregateKind aggregates = HistoryAggregateKind.All)
        {
            if (maximumRecords < 0) throw new ArgumentOutOfRangeException(nameof(maximumRecords));
            if (retentionTicks < 0) throw new ArgumentOutOfRangeException(nameof(retentionTicks));
            if (sampleIntervalTicks <= 0) throw new ArgumentOutOfRangeException(nameof(sampleIntervalTicks));
            if (memoryBudgetBytes < 0) throw new ArgumentOutOfRangeException(nameof(memoryBudgetBytes));
            if (estimatedRecordBytes <= 0) throw new ArgumentOutOfRangeException(nameof(estimatedRecordBytes));
            Mode = mode;
            MaximumRecords = maximumRecords;
            RetentionTicks = retentionTicks;
            SampleIntervalTicks = sampleIntervalTicks;
            Persist = persist;
            DiagnosticOnly = diagnosticOnly;
            MemoryBudgetBytes = memoryBudgetBytes;
            OverflowPolicy = overflowPolicy;
            Changes = changes;
            EstimatedRecordBytes = estimatedRecordBytes;
            TotalAccumulator = totalAccumulator;
            Aggregates = aggregates;
        }
    }

    public sealed class HistorySummary<T>
    {
        public long Count { get; private set; }
        public T First { get; private set; }
        public T Last { get; private set; }
        public T Minimum { get; private set; }
        public T Maximum { get; private set; }
        public T Total { get; private set; }
        public bool HasTotal { get; private set; }
        internal long ObservedCount => _observedCount;
        private readonly Func<T, T, T> _add;
        private readonly HistoryAggregateKind _aggregates;
        private long _observedCount;

        internal HistorySummary(Func<T, T, T> add = null,
            HistoryAggregateKind aggregates = HistoryAggregateKind.All)
        {
            _add = add;
            _aggregates = aggregates;
        }

        internal void Include(T value)
        {
            if (_observedCount == 0)
            {
                if ((_aggregates & HistoryAggregateKind.First) != 0) First = value;
                if ((_aggregates & HistoryAggregateKind.Minimum) != 0) Minimum = value;
                if ((_aggregates & HistoryAggregateKind.Maximum) != 0) Maximum = value;
                if (_add != null && (_aggregates & HistoryAggregateKind.Total) != 0)
                {
                    Total = value;
                    HasTotal = true;
                }
            }
            else
            {
                try
                {
                    if ((_aggregates & HistoryAggregateKind.Minimum) != 0 &&
                        Comparer<T>.Default.Compare(value, Minimum) < 0) Minimum = value;
                    if ((_aggregates & HistoryAggregateKind.Maximum) != 0 &&
                        Comparer<T>.Default.Compare(value, Maximum) > 0) Maximum = value;
                }
                catch (ArgumentException)
                {
                    // Event payloads need not be orderable. Count/last/total remain valid.
                }
                if (_add != null && (_aggregates & HistoryAggregateKind.Total) != 0)
                    Total = _add(Total, value);
            }
            if ((_aggregates & HistoryAggregateKind.Last) != 0) Last = value;
            if ((_aggregates & HistoryAggregateKind.Count) != 0) Count++;
            _observedCount++;
        }

        internal void Restore(long count, T first, T last, T minimum, T maximum,
            T total = default, bool hasTotal = false, long observedCount = 0)
        {
            Count = count;
            First = first;
            Last = last;
            Minimum = minimum;
            Maximum = maximum;
            Total = total;
            HasTotal = hasTotal;
            _observedCount = observedCount > 0 ? observedCount : count;
        }
    }

    public readonly struct ValueChange<T>
    {
        public long Tick { get; }
        public T Previous { get; }
        public T Current { get; }
        public string Reason { get; }

        public ValueChange(long tick, T previous, T current, string reason)
        {
            Tick = tick;
            Previous = previous;
            Current = current;
            Reason = reason ?? string.Empty;
        }
    }

    internal sealed class HistoryBuffer<T>
    {
        private readonly HistoryPolicy _policy;
        private readonly List<ValueChange<T>> _records = new List<ValueChange<T>>();
        private readonly IReadOnlyList<ValueChange<T>> _recordsView;
        private long _lastSample = long.MinValue;
        private readonly int _maximumRecords;

        public IReadOnlyList<ValueChange<T>> Records => _recordsView;
        public HistorySummary<T> Summary { get; }

        public HistoryBuffer(HistoryPolicy policy)
        {
            _policy = policy ?? HistoryPolicy.Disabled;
            Func<T, T, T> total = _policy.TotalAccumulator == null
                ? (Func<T, T, T>)null
                : (left, right) => (T)_policy.TotalAccumulator(left, right);
            Summary = new HistorySummary<T>(total, _policy.Aggregates);
            _recordsView = _records.AsReadOnly();
            int budgetMaximum = _policy.MemoryBudgetBytes > 0
                ? Math.Max(1, _policy.MemoryBudgetBytes / _policy.EstimatedRecordBytes)
                : 0;
            _maximumRecords = _policy.MaximumRecords == 0 ? budgetMaximum
                : budgetMaximum == 0 ? _policy.MaximumRecords
                : Math.Min(_policy.MaximumRecords, budgetMaximum);
        }

        public void Record(long tick, T previous, T current, string reason, HistoryChangeKind changeKind)
        {
            if (!IsRecordingEnabled || (_policy.Changes & changeKind) == 0) return;
            Summary.Include(current);
            if (_policy.Mode == HistoryRecordMode.AggregateOnly) return;
            if (_policy.Mode == HistoryRecordMode.Sampled && _lastSample != long.MinValue &&
                tick - _lastSample < _policy.SampleIntervalTicks) return;

            TrimExpired(tick);
            if (_maximumRecords > 0 && _records.Count >= _maximumRecords)
            {
                if (_policy.OverflowPolicy == HistoryOverflowPolicy.RejectNewest) return;
                if (_policy.OverflowPolicy == HistoryOverflowPolicy.MergeOldest && _records.Count > 1)
                {
                    ValueChange<T> first = _records[0];
                    ValueChange<T> second = _records[1];
                    _records[0] = new ValueChange<T>(second.Tick, first.Previous, second.Current,
                        $"{first.Reason}; {second.Reason}");
                    _records.RemoveAt(1);
                }
                else
                    _records.RemoveAt(0);
            }

            _records.Add(new ValueChange<T>(tick, previous, current, reason));
            _lastSample = tick;
        }

        public AttributeHistoryState Capture(EntityId ownerId, string definitionId)
        {
            if (!_policy.Persist || _policy.Mode == HistoryRecordMode.None) return null;
            return CaptureState(ownerId, definitionId);
        }

        internal AttributeHistoryState CaptureTransactional(EntityId ownerId, string definitionId) =>
            CaptureState(ownerId, definitionId);

        private AttributeHistoryState CaptureState(EntityId ownerId, string definitionId)
        {
            var state = new AttributeHistoryState
            {
                OwnerEntityId = ownerId.Value,
                DefinitionId = definitionId,
                LastSampleTick = _lastSample,
                SummaryCount = Summary.Count,
                SummaryFirst = Summary.First,
                SummaryLast = Summary.Last,
                SummaryMinimum = Summary.Minimum,
                SummaryMaximum = Summary.Maximum,
                SummaryTotal = Summary.Total,
                SummaryHasTotal = Summary.HasTotal,
                SummaryObservedCount = Summary.ObservedCount
            };
            foreach (ValueChange<T> record in _records)
                state.AddRecord(new HistoricalValueState
                {
                    Tick = record.Tick,
                    Previous = record.Previous,
                    Current = record.Current,
                    Reason = record.Reason
                });
            return state;
        }

        public void Restore(AttributeHistoryState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            _records.Clear();
            _lastSample = state.LastSampleTick;
            Summary.Restore(state.SummaryCount,
                ConvertValue(state.SummaryFirst),
                ConvertValue(state.SummaryLast),
                ConvertValue(state.SummaryMinimum),
                ConvertValue(state.SummaryMaximum),
                ConvertValue(state.SummaryTotal),
                state.SummaryHasTotal,
                state.SummaryObservedCount);
            if (!IsRecordingEnabled) return;
            foreach (HistoricalValueState record in state.Records)
                _records.Add(new ValueChange<T>(record.Tick, ConvertValue(record.Previous),
                    ConvertValue(record.Current), record.Reason));
        }

        public void Clear()
        {
            _records.Clear();
            _lastSample = long.MinValue;
            Summary.Restore(0, default, default, default, default, default, false, 0);
        }

        private static T ConvertValue(object value) => value is T typed ? typed : default;

        private bool IsRecordingEnabled
        {
            get
            {
                if (_policy.Mode == HistoryRecordMode.None) return false;
#if DEBUG || UNITY_EDITOR
                return true;
#else
                return !_policy.DiagnosticOnly;
#endif
            }
        }

        private void TrimExpired(long tick)
        {
            if (_policy.RetentionTicks <= 0) return;
            int count = 0;
            while (count < _records.Count && tick - _records[count].Tick > _policy.RetentionTicks) count++;
            if (count > 0) _records.RemoveRange(0, count);
        }
    }

    internal interface IHistoryStreamDefinition
    {
        StableId<HistoryIdKind> Id { get; }
        Type ValueType { get; }
        IHistoryStream Create(VariableOwnerId? owner);
    }

    internal interface IHistoryStream
    {
        StableId<HistoryIdKind> DefinitionId { get; }
        VariableOwnerId? Owner { get; }
        HistoryStreamState Capture();
        void Restore(HistoryStreamState state);
    }

    public sealed class HistoryStreamDefinition<T> : IHistoryStreamDefinition
    {
        public StableId<HistoryIdKind> Id { get; }
        public HistoryPolicy Policy { get; }

        public HistoryStreamDefinition(string id, HistoryPolicy policy)
        {
            Id = new StableId<HistoryIdKind>(id);
            Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        Type IHistoryStreamDefinition.ValueType => typeof(T);
        IHistoryStream IHistoryStreamDefinition.Create(VariableOwnerId? owner) =>
            new HistoryStream<T>(this, owner);
    }

    public sealed class HistoryStream<T> : IHistoryStream
    {
        private readonly HistoryStreamDefinition<T> _definition;
        private readonly HistoryBuffer<T> _buffer;
        private readonly Dictionary<T, long> _completedStateTicks = new Dictionary<T, long>();
        private T _currentState;
        private long _currentStateEnteredTick;
        private bool _hasCurrentState;
        private T _last;
        private bool _hasLast;

        public HistoryStreamDefinition<T> Definition => _definition;
        public VariableOwnerId? Owner { get; }
        public IReadOnlyList<ValueChange<T>> Records => _buffer.Records;
        public HistorySummary<T> Summary => _buffer.Summary;
        public bool HasCurrentState => _hasCurrentState;
        public T CurrentState => _currentState;
        StableId<HistoryIdKind> IHistoryStream.DefinitionId => _definition.Id;

        internal HistoryStream(HistoryStreamDefinition<T> definition, VariableOwnerId? owner)
        {
            _definition = definition;
            Owner = owner;
            _buffer = new HistoryBuffer<T>(definition.Policy);
        }

        internal void Record(long tick, T value, string reason, HistoryChangeKind kind)
        {
            T previous = _hasLast ? _last : default;
            _buffer.Record(tick, previous, value, reason, kind);
            _last = value;
            _hasLast = true;
        }

        internal void Transition(long tick, T state, string reason)
        {
            if (_hasCurrentState && EqualityComparer<T>.Default.Equals(_currentState, state)) return;
            if (_hasCurrentState)
            {
                long elapsed = Math.Max(0, tick - _currentStateEnteredTick);
                _completedStateTicks.TryGetValue(_currentState, out long total);
                _completedStateTicks[_currentState] = total + elapsed;
            }
            Record(tick, state, reason, HistoryChangeKind.StateTransition);
            _currentState = state;
            _currentStateEnteredTick = tick;
            _hasCurrentState = true;
        }

        public long TimeSpentInState(T state, long currentTick)
        {
            _completedStateTicks.TryGetValue(state, out long total);
            if (_hasCurrentState && EqualityComparer<T>.Default.Equals(_currentState, state))
                total += Math.Max(0, currentTick - _currentStateEnteredTick);
            return total;
        }

        HistoryStreamState IHistoryStream.Capture()
        {
            if (!_definition.Policy.Persist) return null;
            AttributeHistoryState captured = _buffer.Capture(new EntityId(1), _definition.Id.Value);
            if (captured == null) return null;
            var state = new HistoryStreamState
            {
                DefinitionId = _definition.Id.Value,
                OwnerKind = Owner?.Kind,
                OwnerKey = Owner?.Value,
                LastSampleTick = captured.LastSampleTick,
                SummaryCount = captured.SummaryCount,
                SummaryFirst = captured.SummaryFirst,
                SummaryLast = captured.SummaryLast,
                SummaryMinimum = captured.SummaryMinimum,
                SummaryMaximum = captured.SummaryMaximum,
                SummaryTotal = captured.SummaryTotal,
                SummaryHasTotal = captured.SummaryHasTotal,
                SummaryObservedCount = captured.SummaryObservedCount,
                HasCurrentState = _hasCurrentState,
                CurrentState = _currentState,
                CurrentStateEnteredTick = _currentStateEnteredTick
            };
            foreach (HistoricalValueState record in captured.Records)
                state.AddRecord(record);
            foreach (KeyValuePair<T, long> duration in _completedStateTicks)
                state.AddStateDuration(new HistoryStateDuration
                {
                    State = duration.Key,
                    Ticks = duration.Value
                });
            return state;
        }

        void IHistoryStream.Restore(HistoryStreamState state)
        {
            var attribute = new AttributeHistoryState
            {
                OwnerEntityId = 1,
                DefinitionId = state.DefinitionId,
                LastSampleTick = state.LastSampleTick,
                SummaryCount = state.SummaryCount,
                SummaryFirst = state.SummaryFirst,
                SummaryLast = state.SummaryLast,
                SummaryMinimum = state.SummaryMinimum,
                SummaryMaximum = state.SummaryMaximum,
                SummaryTotal = state.SummaryTotal,
                SummaryHasTotal = state.SummaryHasTotal,
                SummaryObservedCount = state.SummaryObservedCount
            };
            foreach (HistoricalValueState record in state.Records) attribute.AddRecord(record);
            _buffer.Restore(attribute);
            _completedStateTicks.Clear();
            foreach (HistoryStateDuration duration in state.StateDurations)
                if (duration.State is T typed) _completedStateTicks[typed] = duration.Ticks;
            _hasCurrentState = state.HasCurrentState;
            _currentState = state.CurrentState is T current ? current : default;
            _currentStateEnteredTick = state.CurrentStateEnteredTick;
            if (_buffer.Records.Count > 0)
            {
                _last = _buffer.Records[_buffer.Records.Count - 1].Current;
                _hasLast = true;
            }
        }
    }

    public sealed class EventHistoryRegistration<TEvent> : IDisposable
    {
        private SimulationWorld _world;
        private readonly HistoryStreamDefinition<TEvent> _definition;
        private readonly Func<TEvent, VariableOwnerId?> _owner;

        internal EventHistoryRegistration(SimulationWorld world,
            HistoryStreamDefinition<TEvent> definition, Func<TEvent, VariableOwnerId?> owner)
        {
            _world = world;
            _definition = definition;
            _owner = owner;
            world.DomainEventEmitted += OnEvent;
        }

        private void OnEvent(object value)
        {
            if (value is TEvent typed)
                _world.RecordHistory(_definition, typed, _owner?.Invoke(typed), "Domain event",
                    HistoryChangeKind.Event);
        }

        public void Dispose()
        {
            if (_world == null) return;
            _world.DomainEventEmitted -= OnEvent;
            _world = null;
        }
    }

    public sealed partial class SimulationWorld
    {
        private readonly Dictionary<string, IHistoryStreamDefinition> _historyDefinitions =
            new Dictionary<string, IHistoryStreamDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<Tuple<object, VariableOwnerId?>, IHistoryStream> _historyStreams =
            new Dictionary<Tuple<object, VariableOwnerId?>, IHistoryStream>();

        public void RegisterHistory<T>(HistoryStreamDefinition<T> definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (_historyDefinitions.TryGetValue(definition.Id.Value, out IHistoryStreamDefinition existing))
            {
                if (!ReferenceEquals(existing, definition))
                    throw new InvalidOperationException($"History ID {definition.Id} is already registered.");
                return;
            }
            _historyDefinitions.Add(definition.Id.Value, definition);
        }

        public HistoryStream<T> GetHistory<T>(HistoryStreamDefinition<T> definition,
            VariableOwnerId? owner = null)
        {
            RegisterHistory(definition);
            var key = Tuple.Create((object)definition, owner);
            if (!_historyStreams.TryGetValue(key, out IHistoryStream stream))
                _historyStreams.Add(key, stream = new HistoryStream<T>(definition, owner));
            return (HistoryStream<T>)stream;
        }

        public HistoryStream<T> GetEntityHistory<TEntity, T>(HistoryStreamDefinition<T> definition,
            TEntity owner) where TEntity : WorldEntity
        {
            RequireOwned(owner);
            return GetHistory(definition, VariableOwnerId.Entity(owner.Id));
        }

        public void RecordHistory<T>(HistoryStreamDefinition<T> definition, T value,
            VariableOwnerId? owner = null, string reason = null,
            HistoryChangeKind kind = HistoryChangeKind.Event)
        {
            EnsureMutationAllowed();
            GetHistory(definition, owner).Record(CurrentTick, value, reason, kind);
            AdvanceVersion();
        }

        public void TransitionHistory<T>(HistoryStreamDefinition<T> definition, T state,
            VariableOwnerId? owner = null, string reason = null)
        {
            EnsureMutationAllowed();
            GetHistory(definition, owner).Transition(CurrentTick, state, reason);
            AdvanceVersion();
        }

        public EventHistoryRegistration<TEvent> TrackDomainEvents<TEvent>(
            HistoryStreamDefinition<TEvent> definition,
            Func<TEvent, VariableOwnerId?> owner = null)
        {
            RegisterHistory(definition);
            return new EventHistoryRegistration<TEvent>(this, definition, owner);
        }

        internal void RemoveHistoryOwner(EntityId entityId)
        {
            VariableOwnerId owner = VariableOwnerId.Entity(entityId);
            foreach (Tuple<object, VariableOwnerId?> key in _historyStreams.Keys
                         .Where(item => item.Item2.HasValue && item.Item2.Value.Equals(owner)).ToArray())
                _historyStreams.Remove(key);
        }
    }
}
