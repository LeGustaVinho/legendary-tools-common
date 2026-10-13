using System;

namespace LegendaryTools.Deterministic
{
    public enum DeterministicEventDelivery : byte { Speculated, Confirmed, Retracted }
    public readonly struct DeterministicEventId : IEquatable<DeterministicEventId>, IComparable<DeterministicEventId>
    {
        public readonly long Tick;
        public readonly uint SystemId;
        public readonly StableHandle Entity;
        public readonly ulong Sequence;
        public DeterministicEventId(long tick, uint systemId, StableHandle entity, ulong sequence)
        { Tick = tick; SystemId = systemId; Entity = entity; Sequence = sequence; }
        public int CompareTo(DeterministicEventId other)
        {
            int c = Tick.CompareTo(other.Tick); if (c != 0) return c;
            c = SystemId.CompareTo(other.SystemId); if (c != 0) return c;
            c = Entity.CompareTo(other.Entity); return c != 0 ? c : Sequence.CompareTo(other.Sequence);
        }
        public bool Equals(DeterministicEventId other) => CompareTo(other) == 0;
        public override bool Equals(object obj) => obj is DeterministicEventId other && Equals(other);
        public override int GetHashCode() => unchecked((int)Tick ^ (int)SystemId ^ Entity.GetHashCode() ^ (int)Sequence);
    }
    public readonly struct DeterministicEvent<T> where T : unmanaged
    {
        public readonly DeterministicEventId Id;
        public readonly T Payload;
        public DeterministicEvent(DeterministicEventId id, T payload) { Id = id; Payload = payload; }
    }
    /// <summary>Presentation only. Callbacks must not mutate deterministic state or throw.</summary>
    public interface IDeterministicEventSink<T> where T : unmanaged
    { void OnEvent(DeterministicEventDelivery delivery, in DeterministicEvent<T> value); }
    internal struct EventRow { internal long Tick; internal int Count; internal byte Prepared, Published; }
    /// <summary>Exclusive simulation writer. Publishing happens only after the tick and checkpoint succeed.</summary>
    public unsafe struct DeterministicEventWriter<T> where T : unmanaged
    {
        private readonly EventRow* row;
        private readonly DeterministicEvent<T>* events;
        private readonly int capacity;
        internal DeterministicEventWriter(EventRow* row, DeterministicEvent<T>* events, int capacity)
        { this.row = row; this.events = events; this.capacity = capacity; }
        public DeterministicStatus TryEmit(uint systemId, StableHandle entity, ulong sequence, in T value)
        {
            if (row == null) return DeterministicStatus.Disposed;
            if (systemId == 0 || row->Prepared != 0 || row->Published != 0) return DeterministicStatus.InvalidConfiguration;
            if (row->Count >= capacity) return DeterministicStatus.CapacityExceeded;
            events[row->Count++] = new DeterministicEvent<T>(new DeterministicEventId(row->Tick, systemId, entity, sequence), value);
            return DeterministicStatus.None;
        }
    }

    public sealed unsafe class DeterministicEventLedger<T> : IDisposable where T : unmanaged
    {
        private readonly IDeterministicMemory memory;
        private readonly IDeterministicEventSink<T> sink;
        private readonly int capacity, eventsPerTick;
        private readonly bool speculative;
        private EventRow* rows;
        private DeterministicEvent<T>* events;
        private bool disposed;
        public long ConfirmedThrough { get; private set; }
        public long NewestTick { get; private set; }
        public DeterministicEventLedger(DeterministicRuntimeContext context, int historyCapacity, int maxEventsPerTick,
            IDeterministicEventSink<T> sink = null, bool deliverSpeculative = true, long initialTick = 0)
        {
            if (context == null || historyCapacity < 2 || maxEventsPerTick <= 0 || initialTick < 0)
                DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            capacity = historyCapacity; eventsPerTick = maxEventsPerTick; this.sink = sink; speculative = deliverSpeculative;
            ConfirmedThrough = NewestTick = initialTick - 1;
            long metadata = ((long)sizeof(EventRow) * capacity + 15) & ~15L;
            long bytes = metadata + (long)sizeof(DeterministicEvent<T>) * capacity * eventsPerTick;
            if (bytes > int.MaxValue - 15) DeterministicStatus.CapacityExceeded.ThrowIfFailure();
            memory = context.Memory.Allocate((int)bytes); rows = (EventRow*)memory.Pointer; events = (DeterministicEvent<T>*)(memory.Pointer + metadata);
            for (int i = 0; i < capacity; i++) rows[i].Tick = -1;
        }
        private int Slot(long tick) => (int)(tick % capacity);
        public DeterministicStatus TryBegin(long tick, out DeterministicEventWriter<T> writer)
        {
            writer = default; if (disposed) return DeterministicStatus.Disposed;
            if (tick < 0 || tick == long.MaxValue || tick != NewestTick + 1) return DeterministicStatus.OutOfRange;
            int slot = Slot(tick);
            if (rows[slot].Published != 0 && rows[slot].Tick > ConfirmedThrough) return DeterministicStatus.HistoryExhausted;
            rows[slot] = new EventRow { Tick = tick };
            writer = new DeterministicEventWriter<T>(rows + slot, events + slot * eventsPerTick, eventsPerTick); return DeterministicStatus.None;
        }
        public DeterministicStatus TryPreparePublish(long tick)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (tick < 0) return DeterministicStatus.OutOfRange;
            int slot = Slot(tick); ref EventRow row = ref rows[slot];
            if (row.Tick != tick || row.Published != 0) return DeterministicStatus.Invalidated;
            var values = events + slot * eventsPerTick;
            bool ordered = true;
            for (int i = 1; i < row.Count; i++)
                if (values[i - 1].Id.CompareTo(values[i].Id) > 0) { ordered = false; break; }
            if (!ordered) Sort(values, row.Count);
            for (int i = 1; i < row.Count; i++) if (values[i - 1].Id.Equals(values[i].Id)) return DeterministicStatus.ProtocolConflict;
            row.Prepared = 1; return DeterministicStatus.None;
        }
        private static void Sort(DeterministicEvent<T>* values, int count)
        {
            if (count <= 32)
            {
                for (int i = 1; i < count; i++)
                {
                    var value = values[i]; int j = i - 1;
                    while (j >= 0 && values[j].Id.CompareTo(value.Id) > 0) { values[j + 1] = values[j]; j--; }
                    values[j + 1] = value;
                }
                return;
            }
            // IDs must be unique before publication, so stability among equal IDs is not observable.
            // Heapsort bounds the worst case without allocating an auxiliary payload buffer.
            for (int root = count / 2 - 1; root >= 0; root--) Sift(values, root, count);
            for (int end = count - 1; end > 0; end--)
            {
                var value = values[0]; values[0] = values[end]; values[end] = value;
                Sift(values, 0, end);
            }
        }
        private static void Sift(DeterministicEvent<T>* values, int root, int count)
        {
            var value = values[root];
            while (root < count / 2)
            {
                int child = root * 2 + 1;
                if (child + 1 < count && values[child].Id.CompareTo(values[child + 1].Id) < 0) child++;
                if (value.Id.CompareTo(values[child].Id) >= 0) break;
                values[root] = values[child]; root = child;
            }
            values[root] = value;
        }
        public DeterministicStatus TryPublish(long tick)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (tick < 0 || tick != NewestTick + 1) return DeterministicStatus.OutOfRange;
            int slot = Slot(tick); ref EventRow row = ref rows[slot];
            if (row.Tick != tick || row.Prepared == 0 || row.Published != 0) return DeterministicStatus.Invalidated;
            row.Published = 1; NewestTick = tick;
            if (speculative && sink != null) for (int i = 0; i < row.Count; i++) sink.OnEvent(DeterministicEventDelivery.Speculated, events[slot * eventsPerTick + i]);
            return DeterministicStatus.None;
        }
        public DeterministicStatus TryAbandon(long tick)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (tick < 0) return DeterministicStatus.OutOfRange;
            int slot = Slot(tick); if (rows[slot].Tick != tick || rows[slot].Published != 0) return DeterministicStatus.Invalidated;
            rows[slot] = default; rows[slot].Tick = -1; return DeterministicStatus.None;
        }
        public DeterministicStatus TryConfirmThrough(long tick)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (tick < ConfirmedThrough || tick > NewestTick) return DeterministicStatus.OutOfRange;
            for (long at = ConfirmedThrough + 1; at <= tick; at++)
                if (rows[Slot(at)].Tick != at || rows[Slot(at)].Published == 0) return DeterministicStatus.HistoryExhausted;
            for (long at = ConfirmedThrough + 1; at <= tick; at++)
            {
                int slot = Slot(at);
                if (sink != null) for (int i = 0; i < rows[slot].Count; i++) sink.OnEvent(DeterministicEventDelivery.Confirmed, events[slot * eventsPerTick + i]);
            }
            ConfirmedThrough = tick; return DeterministicStatus.None;
        }
        public DeterministicStatus TryRetractFrom(long tick)
        {
            if (disposed) return DeterministicStatus.Disposed;
            if (tick < 0 || tick <= ConfirmedThrough || tick > NewestTick + 1) return DeterministicStatus.ProtocolConflict;
            for (long at = NewestTick; at >= tick; at--)
            {
                int slot = Slot(at); if (rows[slot].Tick != at) return DeterministicStatus.HistoryExhausted;
            }
            for (long at = NewestTick; at >= tick; at--)
            {
                int slot = Slot(at);
                if (speculative && sink != null) for (int i = rows[slot].Count - 1; i >= 0; i--)
                    sink.OnEvent(DeterministicEventDelivery.Retracted, events[slot * eventsPerTick + i]);
                rows[slot] = default; rows[slot].Tick = -1;
            }
            NewestTick = tick - 1; return DeterministicStatus.None;
        }
        public void Dispose() { if (disposed) return; disposed = true; memory.Dispose(); rows = null; events = null; }
    }
}
