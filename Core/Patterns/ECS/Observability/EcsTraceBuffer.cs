using System;
using System.Text;

namespace LegendaryTools.Common.Core.Patterns.ECS.Observability
{
    /// <summary>
    /// Simple ring-buffer trace for debugging and tooling.
    /// Does not depend on core instrumentation hooks (you can push markers from systems/tools).
    /// </summary>
    public sealed class EcsTraceBuffer
    {
        /// <summary>
        /// Public, read-only trace entry for external tooling and debug UIs.
        /// </summary>
        public readonly struct TraceEntry
        {
            public readonly int Tick;
            public readonly int SystemOrder;
            public readonly long TimestampUtcTicks;
            public readonly TraceLevel Level;
            public readonly string Message;

            public TraceEntry(int tick, int systemOrder, long timestampUtcTicks, TraceLevel level, string message)
            {
                Tick = tick;
                SystemOrder = systemOrder;
                TimestampUtcTicks = timestampUtcTicks;
                Level = level;
                Message = message ?? string.Empty;
            }

            public override string ToString()
            {
                return $"[{Level}] tick={Tick} sys={SystemOrder} ts={TimestampUtcTicks} msg={Message}";
            }
        }

        public enum TraceLevel : byte
        {
            Info = 0,
            Warning = 1,
            Error = 2
        }

        private readonly TraceEntry[] _buffer;
        private int _next;
        private int _count;

        public EcsTraceBuffer(int capacity = 2048)
        {
            if (capacity < 64) capacity = 64;

            _buffer = new TraceEntry[capacity];
            _next = 0;
            _count = 0;
        }

        public int Capacity => _buffer.Length;

        public int Count => _count;

        /// <summary>
        /// Pushes a trace entry (allocates only for the message string).
        /// </summary>
        public void Push(int tick, int systemOrder, TraceLevel level, string message)
        {
            _buffer[_next] = new TraceEntry(
                tick,
                systemOrder,
                DateTime.UtcNow.Ticks,
                level,
                message);

            _next++;
            if (_next >= _buffer.Length) _next = 0;

            if (_count < _buffer.Length) _count++;
        }

        /// <summary>
        /// Dumps the buffer into a readable string (allocating).
        /// </summary>
        public string Dump(int maxLines = 200)
        {
            if (maxLines < 1) maxLines = 1;

            StringBuilder sb = new(4096);
            sb.Append("EcsTraceBuffer(Count=").Append(_count).Append(", Capacity=").Append(_buffer.Length)
                .AppendLine(")");

            int lines = 0;
            Enumerator it = EnumerateNewestFirst();
            while (it.MoveNext())
            {
                sb.Append("  ").Append(it.Current.ToString()).AppendLine();
                if (++lines >= maxLines) break;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Enumerates newest entries first.
        /// </summary>
        public Enumerator EnumerateNewestFirst()
        {
            return new Enumerator(_buffer, _next, _count);
        }

        /// <summary>
        /// Value-type enumerator to avoid allocations. Newest entries first.
        /// </summary>
        public struct Enumerator
        {
            private readonly TraceEntry[] _buffer;
            private int _remaining;
            private int _index;

            public Enumerator(TraceEntry[] buffer, int next, int count)
            {
                _buffer = buffer;
                _remaining = count;

                // Start from the newest item (one before next).
                _index = next - 1;
                if (_index < 0) _index = buffer.Length - 1;

                Current = default;
            }

            public TraceEntry Current { get; private set; }

            public bool MoveNext()
            {
                if (_remaining <= 0) return false;

                Current = _buffer[_index];

                _index--;
                if (_index < 0) _index = _buffer.Length - 1;

                _remaining--;
                return true;
            }
        }
    }
}