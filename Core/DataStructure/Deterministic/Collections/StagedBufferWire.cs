namespace LegendaryTools.Deterministic
{
    public enum DeterministicCollectionKind : byte
    { Array = 1, Cell, List, Queue, Stack, Deque, RingBuffer, Dictionary, HashSet, SortedList, SortedDictionary, SortedSet, PriorityQueue, BitSet, SparseSet, SlotMap, LinkedList, MultiMap, Graph, FrozenGraph, SpatialIndex2, SpatialIndex3 }

    /// <summary>Borrowed workspace. Use pinned GC bytes with Managed and native bytes with Burst; pointers must outlive the entire operation.</summary>
    public unsafe struct CanonicalScratch
    {
        public readonly byte* Buffer;
        public readonly int Capacity;
        public int Position;
        public CanonicalScratch(byte* buffer, int capacity) { Buffer = buffer; Capacity = capacity; Position = 0; }
        public bool Overlaps(byte* input, int length)
        {
            if (Buffer == null || input == null || Capacity <= 0 || length <= 0) return false;
            return Buffer < input + length && input < Buffer + Capacity;
        }
        public DeterministicStatus TryReserve<T>(int count, out T* values) where T : unmanaged
        {
            values = null; if (count < 0 || Position < 0 || Capacity < 0 || Position > Capacity) return DeterministicStatus.InvalidConfiguration;
            if (count == 0) return DeterministicStatus.None;
            if (Buffer == null) return DeterministicStatus.BufferTooSmall;
            long bytes = (long)count * sizeof(T); ulong address = (ulong)(Buffer + Position); int padding = (int)((16 - (address & 15)) & 15);
            if (bytes + padding > Capacity - Position) return DeterministicStatus.BufferTooSmall;
            values = (T*)(Buffer + Position + padding); Position += padding + (int)bytes; return DeterministicStatus.None;
        }
    }
    public unsafe interface IDeterministicWireLayout<T, TState> where T : unmanaged where TState : unmanaged
    {
        int ItemCount(in TState state);
        int PhysicalIndex(in TState state, int capacity, int logicalIndex);
        DeterministicStatus ValidateMetadata(in TState state, int capacity);
        DeterministicStatus Validate(T* staged, in TState state, int capacity, ref CanonicalScratch scratch);
    }
    /// <summary>Staged values are borrowed and are valid only while their caller-owned scratch buffer is alive.</summary>
    public readonly unsafe struct CanonicalBufferStage<T, TState> where T : unmanaged where TState : unmanaged
    {
        public readonly T* Items;
        public readonly int Capacity;
        public readonly TState State;
        internal CanonicalBufferStage(T* items, int capacity, TState state) { Items = items; Capacity = capacity; State = state; }
    }
    /// <summary>Portable field encoding shared by both storage backends. Read/validate first, reserve all compound buffers, then publish.</summary>
    public static unsafe class StagedBufferWire<T, TState, TCodec, TStateCodec, TLayout, TBuffer>
        where T : unmanaged where TState : unmanaged
        where TCodec : unmanaged, IDeterministicCodec<T> where TStateCodec : unmanaged, IDeterministicCodec<TState>
        where TLayout : unmanaged, IDeterministicWireLayout<T, TState> where TBuffer : struct, IDeterministicImportBuffer<T>
    {
        public const uint Magic = 0x3143444c; // LDC1, little endian
        public const ushort WireVersion = 1;
        private static DeterministicStatus WriteBody(ref DeterministicWriter writer, ref TBuffer buffer, in TState state,
            DeterministicCollectionKind kind, in TCodec codec, in TStateCodec stateCodec, in TLayout layout)
        {
            if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            if (kind < DeterministicCollectionKind.Array ||
                kind > DeterministicCollectionKind.MultiMap && kind != DeterministicCollectionKind.SpatialIndex2 && kind != DeterministicCollectionKind.SpatialIndex3 ||
                codec.SchemaId == 0 || codec.Version == 0 || stateCodec.SchemaId == 0 || stateCodec.Version == 0)
                return DeterministicStatus.InvalidConfiguration;
            var status = layout.ValidateMetadata(state, buffer.Capacity); if (!status.IsSuccess()) return status;
            status = writer.TryUInt(Magic, 4); if (!status.IsSuccess()) return status;
            status = writer.TryUInt(WireVersion, 2); if (!status.IsSuccess()) return status;
            status = writer.TryUInt((byte)kind, 1); if (!status.IsSuccess()) return status;
            status = writer.TryUInt(codec.SchemaId, 4); if (!status.IsSuccess()) return status;
            status = writer.TryUInt(codec.Version, 2); if (!status.IsSuccess()) return status;
            status = writer.TryUInt(stateCodec.SchemaId, 4); if (!status.IsSuccess()) return status;
            status = writer.TryUInt(stateCodec.Version, 2); if (!status.IsSuccess()) return status;
            status = writer.TryVarUInt((uint)buffer.Capacity); if (!status.IsSuccess()) return status;
            status = stateCodec.TryWrite(ref writer, state); if (!status.IsSuccess()) return status;
            int count = layout.ItemCount(state);
            for (int i = 0; i < count; i++)
            { var item = buffer.Read(layout.PhysicalIndex(state, buffer.Capacity, i)); status = codec.TryWrite(ref writer, item); if (!status.IsSuccess()) return status; }
            return DeterministicStatus.None;
        }
        public static DeterministicStatus TryWrite(ref DeterministicWriter writer, ref TBuffer buffer, in TState state,
            DeterministicCollectionKind kind, in TCodec codec, in TStateCodec stateCodec, in TLayout layout)
        {
            if (writer.IsMeasuring) return WriteBody(ref writer, ref buffer, state, kind, codec, stateCodec, layout);
            if (writer.HasPremeasuredCapacity)
            {
                var planned = writer; var plannedStatus = WriteBody(ref planned, ref buffer, state, kind, codec, stateCodec, layout);
                if (plannedStatus.IsSuccess()) writer = planned; return plannedStatus;
            }
            var measured = DeterministicWriter.CreateMeasure(); var status = WriteBody(ref measured, ref buffer, state, kind, codec, stateCodec, layout);
            if (!status.IsSuccess()) return status;
            if (!writer.Has(measured.Position)) return DeterministicStatus.BufferTooSmall;
            var trial = writer; status = WriteBody(ref trial, ref buffer, state, kind, codec, stateCodec, layout);
            if (status.IsSuccess()) writer = trial; return status;
        }
        public static DeterministicStatus TryReadStaged(ref DeterministicReader reader, ref CanonicalScratch scratch, ref TBuffer buffer,
            DeterministicCollectionKind kind, in TCodec codec, in TStateCodec stateCodec, in TLayout layout, out CanonicalBufferStage<T, TState> stage)
        {
            stage = default; if (!buffer.IsCreated) return DeterministicStatus.Disposed;
            if (scratch.Overlaps(reader.Buffer, reader.Length)) return DeterministicStatus.InvalidConfiguration;
            var trial = reader; var workspace = scratch;
            var status = trial.TryUInt(4, out ulong magic); if (!status.IsSuccess()) return status;
            status = trial.TryUInt(2, out ulong version); if (!status.IsSuccess()) return status;
            status = trial.TryUInt(1, out ulong wireKind); if (!status.IsSuccess()) return status;
            status = trial.TryUInt(4, out ulong schema); if (!status.IsSuccess()) return status;
            status = trial.TryUInt(2, out ulong codecVersion); if (!status.IsSuccess()) return status;
            status = trial.TryUInt(4, out ulong stateSchema); if (!status.IsSuccess()) return status;
            status = trial.TryUInt(2, out ulong stateVersion); if (!status.IsSuccess()) return status;
            if (magic != Magic || version != WireVersion || wireKind != (byte)kind || schema != codec.SchemaId || codecVersion != codec.Version || stateSchema != stateCodec.SchemaId || stateVersion != stateCodec.Version)
                return DeterministicStatus.ProfileMismatch;
            status = trial.TryVarUInt(out ulong capacity); if (!status.IsSuccess()) return status;
            if (capacity > int.MaxValue) return DeterministicStatus.InvalidEncoding;
            status = buffer.CanReplaceCapacity((int)capacity); if (!status.IsSuccess()) return status;
            status = stateCodec.TryRead(ref trial, out TState state); if (!status.IsSuccess()) return status;
            status = layout.ValidateMetadata(state, (int)capacity); if (!status.IsSuccess()) return status;
            int count = layout.ItemCount(state); if (count < 0 || count > (int)capacity) return DeterministicStatus.InvalidEncoding;
            status = workspace.TryReserve<T>(count, out T* values); if (!status.IsSuccess()) return status;
            for (int i = 0; i < count; i++) { status = codec.TryRead(ref trial, out values[i]); if (!status.IsSuccess()) return status; }
            status = layout.Validate(values, state, (int)capacity, ref workspace); if (!status.IsSuccess()) return status;
            stage = new CanonicalBufferStage<T, TState>(values, (int)capacity, state); reader = trial; scratch = workspace; return DeterministicStatus.None;
        }
        public static void PublishPrepared(ref TBuffer buffer, in CanonicalBufferStage<T, TState> stage, in TLayout layout)
        {
            int count = layout.ItemCount(stage.State);
            for (int i = 0; i < count; i++) buffer.Write(layout.PhysicalIndex(stage.State, stage.Capacity, i), stage.Items[i]);
            buffer.CommitWrite();
        }
    }
}
