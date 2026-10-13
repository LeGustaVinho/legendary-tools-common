using System;
using System.Runtime.CompilerServices;

namespace LegendaryTools.Deterministic
{
    public readonly struct DeterministicCommandHeader : IComparable<DeterministicCommandHeader>
    {
        public readonly ushort Phase;
        public readonly uint SystemId, ProducerId;
        public readonly StableHandle Entity;
        public readonly ulong Sequence;
        public DeterministicCommandHeader(ushort phase, uint systemId, StableHandle entity, ulong sequence, uint producerId)
        { Phase = phase; SystemId = systemId; Entity = entity; Sequence = sequence; ProducerId = producerId; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(DeterministicCommandHeader other)
        {
            int phase = Phase - other.Phase; if (phase != 0) return phase;
            if (SystemId != other.SystemId) return SystemId < other.SystemId ? -1 : 1;
            if (Entity.Index != other.Entity.Index) return Entity.Index < other.Entity.Index ? -1 : 1;
            if (Entity.Generation != other.Entity.Generation) return Entity.Generation < other.Entity.Generation ? -1 : 1;
            if (Sequence != other.Sequence) return Sequence < other.Sequence ? -1 : 1;
            return ProducerId == other.ProducerId ? 0 : ProducerId < other.ProducerId ? -1 : 1;
        }
    }
    public readonly struct DeterministicCommand<T> where T : unmanaged
    {
        public readonly DeterministicCommandHeader Header;
        public readonly T Payload;
        public DeterministicCommand(DeterministicCommandHeader header, T payload) { Header = header; Payload = payload; }
    }

    /// <summary>One exclusive producer partition. Borrow only until reset/disposal; complete jobs before merge.</summary>
    public unsafe struct DeterministicCommandWriter<T> where T : unmanaged
    {
        private readonly DeterministicCommand<T>* commands;
        private readonly int* count;
        private readonly int capacity;
        private readonly uint producerId;
        internal DeterministicCommand<T>* Commands => commands;
        internal int* CountPointer => count;
        internal int Capacity => capacity;
        internal uint ProducerId => producerId;
        internal DeterministicCommandWriter(DeterministicCommand<T>* commands, int* count, int capacity, uint producerId)
        { this.commands = commands; this.count = count; this.capacity = capacity; this.producerId = producerId; }
        public DeterministicStatus TryEnqueue(ushort phase, uint systemId, StableHandle entity, in T payload)
        {
            if (count == null || commands == null) return DeterministicStatus.Disposed;
            return TryEnqueue(phase, systemId, entity, (ulong)*count, payload);
        }
        public DeterministicStatus TryEnqueue(ushort phase, uint systemId, StableHandle entity, ulong sequence, in T payload)
        {
            if (count == null || commands == null) return DeterministicStatus.Disposed;
            if (systemId == 0 || producerId == 0) return DeterministicStatus.InvalidConfiguration;
            if (*count >= capacity) return DeterministicStatus.CapacityExceeded;
            commands[*count] = new DeterministicCommand<T>(new DeterministicCommandHeader(phase, systemId, entity, sequence, producerId), payload);
            (*count)++; return DeterministicStatus.None;
        }
    }

    /// <summary>Fixed per-producer partitions merged by phase, system, entity, sequence and stable producer ID.</summary>
    public sealed unsafe class DeterministicCommandBuffer<T> : IDisposable where T : unmanaged
    {
        private readonly IDeterministicMemory memory;
        private readonly uint[] producers;
        private readonly int perProducer, totalCapacity;
        // Bound stack scratch independently of user capacity; bigger batches use the in-place heap.
        private const int MaxIndexSortCount = 4096;
        private DeterministicCommand<T>* partitions, merged;
        private int* counts;
        private bool disposed;
        public DeterministicBackend Backend { get; }
        public int ProducerCount => producers.Length;
        public int Count { get; private set; }
        public DeterministicCommandBuffer(DeterministicRuntimeContext context, ReadOnlySpan<uint> producerIds, int capacityPerProducer)
        {
            if (context == null || producerIds.Length == 0 || capacityPerProducer <= 0)
                DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            producers = producerIds.ToArray();
            for (int i = 0; i < producers.Length; i++)
            {
                if (producers[i] == 0) DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
                for (int j = 0; j < i; j++) if (producers[i] == producers[j]) DeterministicStatus.DuplicateKey.ThrowIfFailure();
            }
            Backend = context.Backend; perProducer = capacityPerProducer; totalCapacity = checked(perProducer * producers.Length);
            long offset = ((long)sizeof(int) * producers.Length + 15) & ~15L;
            long bytes = offset + (long)sizeof(DeterministicCommand<T>) * totalCapacity * 2;
            if (bytes > int.MaxValue - 15) DeterministicStatus.CapacityExceeded.ThrowIfFailure();
            memory = context.Memory.Allocate((int)bytes); counts = (int*)memory.Pointer;
            partitions = (DeterministicCommand<T>*)(memory.Pointer + offset); merged = partitions + totalCapacity;
        }
        public DeterministicStatus TryGetWriter(uint producerId, out DeterministicCommandWriter<T> writer)
        {
            writer = default; if (disposed) return DeterministicStatus.Disposed;
            for (int i = 0; i < producers.Length; i++) if (producers[i] == producerId)
            { writer = new DeterministicCommandWriter<T>(partitions + i * perProducer, counts + i, perProducer, producerId); return DeterministicStatus.None; }
            return DeterministicStatus.NotFound;
        }
        public DeterministicStatus TryMerge(out ReadOnlySpan<DeterministicCommand<T>> commands)
        {
            commands = default; if (disposed) return DeterministicStatus.Disposed;
            int count = 0;
            for (int producer = 0; producer < producers.Length; producer++)
            {
                if (counts[producer] < 0 || counts[producer] > perProducer) return DeterministicStatus.ProtocolConflict;
                count += counts[producer];
            }
            bool ordered = true, hasPrevious = false; DeterministicCommandHeader previous = default;
            for (int producer = 0; producer < producers.Length && ordered; producer++)
                for (int i = 0; i < counts[producer]; i++)
                {
                    var header = partitions[producer * perProducer + i].Header;
                    if (hasPrevious && previous.CompareTo(header) > 0) { ordered = false; break; }
                    previous = header; hasPrevious = true;
                }
            if (!ordered && sizeof(T) >= 64 && count <= MaxIndexSortCount)
            {
                int* order = stackalloc int[count]; int rank = 0;
                for (int producer = 0; producer < producers.Length; producer++)
                    for (int i = 0; i < counts[producer]; i++) order[rank++] = producer * perProducer + i;
                for (int i = count / 2 - 1; i >= 0; i--) SiftIndices(order, i, count);
                for (int end = count - 1; end > 0; end--)
                { int value = order[0]; order[0] = order[end]; order[end] = value; SiftIndices(order, 0, end); }
                for (int i = 0; i < count; i++) merged[i] = partitions[order[i]];
            }
            else
            {
                int offset = 0;
                for (int producer = 0; producer < producers.Length; producer++)
                {
                    int length = counts[producer];
                    RuntimeBytes.Copy((byte*)(partitions + producer * perProducer), (byte*)(merged + offset), length * sizeof(DeterministicCommand<T>));
                    offset += length;
                }
                if (!ordered)
                {
                    for (int i = count / 2 - 1; i >= 0; i--) Sift(i, count);
                    for (int end = count - 1; end > 0; end--) { Swap(0, end); Sift(0, end); }
                }
            }
            for (int i = 1; i < count; i++) if (merged[i - 1].Header.CompareTo(merged[i].Header) == 0) return DeterministicStatus.ProtocolConflict;
            Count = count; commands = new ReadOnlySpan<DeterministicCommand<T>>(merged, count); return DeterministicStatus.None;
        }
        private void Swap(int a, int b) { var value = merged[a]; merged[a] = merged[b]; merged[b] = value; }
        private void Sift(int root, int count)
        {
            int start = root; var value = merged[root];
            while (root < count / 2)
            {
                int child = root * 2 + 1;
                if (child + 1 < count && merged[child].Header.CompareTo(merged[child + 1].Header) < 0) child++;
                if (value.Header.CompareTo(merged[child].Header) >= 0) break;
                merged[root] = merged[child]; root = child;
            }
            if (root != start) merged[root] = value;
        }
        private void SiftIndices(int* order, int root, int count)
        {
            int start = root, value = order[root]; var header = partitions[value].Header;
            while (root < count / 2)
            {
                int child = root * 2 + 1;
                if (child + 1 < count && partitions[order[child]].Header.CompareTo(partitions[order[child + 1]].Header) < 0) child++;
                if (header.CompareTo(partitions[order[child]].Header) >= 0) break;
                order[root] = order[child]; root = child;
            }
            if (root != start) order[root] = value;
        }
        public DeterministicStatus TryReset()
        {
            if (disposed) return DeterministicStatus.Disposed;
            for (int i = 0; i < producers.Length; i++) counts[i] = 0; Count = 0; return DeterministicStatus.None;
        }
        public void Dispose()
        { if (disposed) return; disposed = true; memory.Dispose(); partitions = merged = null; counts = null; Count = 0; }
    }

    public struct DeterministicCommandCodec<T, TCodec> : IDeterministicCodec<DeterministicCommand<T>>
        where T : unmanaged where TCodec : unmanaged, IDeterministicCodec<T>
    {
        private TCodec codec;
        public DeterministicCommandCodec(TCodec codec) { this.codec = codec; }
        public uint SchemaId => CanonicalFields.CombineSchema(0xd310, codec.SchemaId, codec.Version);
        public ushort Version => 1;
        public int Measure(in DeterministicCommand<T> value)
        { int bytes = codec.Measure(value.Payload); return bytes < 0 || bytes > int.MaxValue - 26 ? -1 : bytes + 26; }
        public DeterministicStatus TryWrite(ref DeterministicWriter writer, in DeterministicCommand<T> value)
        {
            int bytes = Measure(value); if (bytes < 0 || value.Header.SystemId == 0 || value.Header.ProducerId == 0) return DeterministicStatus.InvalidConfiguration;
            var measure = DeterministicWriter.CreateMeasure(); var valid = codec.TryWrite(ref measure, value.Payload);
            if (!valid.IsSuccess()) return valid;
            if (measure.Position != bytes - 26) return DeterministicStatus.ProtocolConflict;
            if (!writer.Has(bytes)) return DeterministicStatus.BufferTooSmall;
            var trial = writer;
            trial.TryUInt(value.Header.Phase, 2); trial.TryUInt(value.Header.SystemId, 4);
            trial.TryUInt(value.Header.Entity.Packed, 8); trial.TryUInt(value.Header.Sequence, 8); trial.TryUInt(value.Header.ProducerId, 4);
            var status = codec.TryWrite(ref trial, value.Payload); if (status.IsSuccess()) writer = trial; return status;
        }
        public DeterministicStatus TryRead(ref DeterministicReader reader, out DeterministicCommand<T> value)
        {
            value = default; var trial = reader; if (!trial.Has(26)) return DeterministicStatus.BufferTooSmall;
            trial.TryUInt(2, out ulong phase); trial.TryUInt(4, out ulong system); trial.TryUInt(8, out ulong entity);
            trial.TryUInt(8, out ulong sequence); trial.TryUInt(4, out ulong producer);
            if (system == 0 || producer == 0) return DeterministicStatus.InvalidEncoding;
            var status = codec.TryRead(ref trial, out T payload); if (!status.IsSuccess()) return status;
            value = new DeterministicCommand<T>(new DeterministicCommandHeader((ushort)phase, (uint)system,
                new StableHandle((uint)entity, (uint)(entity >> 32)), sequence, (uint)producer), payload);
            reader = trial; return DeterministicStatus.None;
        }
    }
}
