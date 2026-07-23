using System;
using System.Collections.Generic;

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
            int estimatedRecordBytes = 64)
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
        }
    }

    public sealed class HistorySummary<T>
    {
        public long Count { get; private set; }
        public T First { get; private set; }
        public T Last { get; private set; }
        public T Minimum { get; private set; }
        public T Maximum { get; private set; }

        internal void Include(T value)
        {
            if (Count == 0)
            {
                First = value;
                Minimum = value;
                Maximum = value;
            }
            else
            {
                if (Comparer<T>.Default.Compare(value, Minimum) < 0) Minimum = value;
                if (Comparer<T>.Default.Compare(value, Maximum) > 0) Maximum = value;
            }
            Last = value;
            Count++;
        }

        internal void Restore(long count, T first, T last, T minimum, T maximum)
        {
            Count = count;
            First = first;
            Last = last;
            Minimum = minimum;
            Maximum = maximum;
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
        public HistorySummary<T> Summary { get; } = new HistorySummary<T>();

        public HistoryBuffer(HistoryPolicy policy)
        {
            _policy = policy ?? HistoryPolicy.Disabled;
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
            var state = new AttributeHistoryState
            {
                OwnerEntityId = ownerId.Value,
                DefinitionId = definitionId,
                LastSampleTick = _lastSample,
                SummaryCount = Summary.Count,
                SummaryFirst = Summary.First,
                SummaryLast = Summary.Last,
                SummaryMinimum = Summary.Minimum,
                SummaryMaximum = Summary.Maximum
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
                ConvertValue(state.SummaryMaximum));
            if (!IsRecordingEnabled) return;
            foreach (HistoricalValueState record in state.Records)
                _records.Add(new ValueChange<T>(record.Tick, ConvertValue(record.Previous),
                    ConvertValue(record.Current), record.Reason));
        }

        public void Clear()
        {
            _records.Clear();
            _lastSample = long.MinValue;
            Summary.Restore(0, default, default, default, default);
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
}
