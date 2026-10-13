namespace LegendaryTools.Deterministic
{
    /// <summary>Borrowed byte buffer. Try methods preserve the cursor and destination on failure.</summary>
    public unsafe struct DeterministicWriter
    {
        public readonly byte* Buffer;
        public readonly int Capacity;
        public int Position;
        private readonly bool measuring;
        // A registry receipt is scoped to the current state, after its successful measurement.
        internal int PremeasuredEnd;
        internal bool HasPremeasuredCapacity => PremeasuredEnd != 0 && !measuring;
        public bool IsMeasuring => measuring;
        public DeterministicWriter(byte* buffer, int capacity, int position = 0)
        { Buffer = buffer; Capacity = capacity; Position = position; measuring = false; PremeasuredEnd = 0; }
        private DeterministicWriter(bool measuring)
        { Buffer = null; Capacity = int.MaxValue; Position = 0; this.measuring = measuring; PremeasuredEnd = 0; }
        public static DeterministicWriter CreateMeasure() => new DeterministicWriter(true);
        public bool Has(int bytes) => bytes >= 0 && Position >= 0 && Capacity >= 0 && Position <= (PremeasuredEnd == 0 ? Capacity : System.Math.Min(Capacity, PremeasuredEnd)) - bytes &&
            (measuring || Buffer != null || bytes == 0);
        public DeterministicStatus TryUInt(ulong value, int bytes)
        {
            if (bytes < 1 || bytes > 8) return DeterministicStatus.InvalidConfiguration;
            if (bytes < 8 && value >= (1UL << (bytes * 8))) return DeterministicStatus.OutOfRange;
            if (!Has(bytes)) return DeterministicStatus.BufferTooSmall;
            if (measuring) { Position += bytes; return DeterministicStatus.None; }
            for (int i = 0; i < bytes; i++) Buffer[Position + i] = (byte)(value >> (8 * i));
            Position += bytes; return DeterministicStatus.None;
        }
        public DeterministicStatus TryVarUInt(ulong value)
        {
            int bytes = CanonicalBinary.VarUIntBytes(value);
            if (!Has(bytes)) return DeterministicStatus.BufferTooSmall;
            if (measuring) { Position += bytes; return DeterministicStatus.None; }
            while (value >= 128) { Buffer[Position++] = (byte)((value & 127) | 128); value >>= 7; }
            Buffer[Position++] = (byte)value; return DeterministicStatus.None;
        }
        public DeterministicStatus TryVarInt(long value) => TryVarUInt(unchecked(((ulong)value << 1) ^ (ulong)(value >> 63)));
        public DeterministicStatus TryBytes(byte* source, int length)
        {
            if (length < 0 || (source == null && length != 0)) return DeterministicStatus.InvalidConfiguration;
            if (!Has(length)) return DeterministicStatus.BufferTooSmall;
            if (measuring) { Position += length; return DeterministicStatus.None; }
            byte* target = Buffer + Position;
            if (length >= 32 && (target + length <= source || source + length <= target))
                new System.ReadOnlySpan<byte>(source, length).CopyTo(new System.Span<byte>(target, length));
            else if (target > source && target < source + length)
                for (int i = length - 1; i >= 0; i--) target[i] = source[i];
            else for (int i = 0; i < length; i++) target[i] = source[i];
            Position += length; return DeterministicStatus.None;
        }
    }

    public unsafe struct DeterministicReader
    {
        public readonly byte* Buffer;
        public readonly int Length;
        public int Position;
        public DeterministicReader(byte* buffer, int length, int position = 0)
        { Buffer = buffer; Length = length; Position = position; }
        public bool Has(int bytes) => bytes >= 0 && Position >= 0 && Length >= 0 && Position <= Length - bytes &&
            (Buffer != null || bytes == 0);
        public DeterministicStatus TryUInt(int bytes, out ulong value)
        {
            value = 0;
            if (bytes < 1 || bytes > 8) return DeterministicStatus.InvalidConfiguration;
            if (!Has(bytes)) return DeterministicStatus.BufferTooSmall;
            for (int i = 0; i < bytes; i++) value |= (ulong)Buffer[Position + i] << (8 * i);
            Position += bytes; return DeterministicStatus.None;
        }
        public DeterministicStatus TryVarUInt(out ulong value)
        {
            value = 0; int cursor = Position;
            for (int i = 0; i < 10; i++)
            {
                if (cursor < 0 || cursor >= Length || Buffer == null) { value = 0; return DeterministicStatus.BufferTooSmall; }
                byte b = Buffer[cursor++];
                if (i == 9 && b > 1) { value = 0; return DeterministicStatus.InvalidEncoding; }
                value |= (ulong)(b & 127) << (7 * i);
                if ((b & 128) == 0)
                {
                    if (i > 0 && b == 0) { value = 0; return DeterministicStatus.InvalidEncoding; }
                    Position = cursor; return DeterministicStatus.None;
                }
            }
            value = 0; return DeterministicStatus.InvalidEncoding;
        }
        public DeterministicStatus TryVarInt(out long value)
        {
            var status = TryVarUInt(out ulong bits);
            value = status.IsSuccess() ? unchecked((long)(bits >> 1) ^ -((long)bits & 1)) : 0;
            return status;
        }
    }

    public static class CanonicalBinary
    {
        public static int VarUIntBytes(ulong value)
        { int bytes = 1; while (value >= 128) { value >>= 7; bytes++; } return bytes; }
    }

    public unsafe struct Int32Policy : IDeterministicEquality<int>, IDeterministicComparer<int>, IDeterministicCodec<int>
    {
        public bool IsValid => true;
        public uint SchemaId => 1;
        public ushort Version => 1;
        public bool Equals(in int a, in int b) => a == b;
        public ulong Hash(in int value) => DeterministicHash.Mix(unchecked((uint)value));
        public int Compare(in int a, in int b) => a.CompareTo(b);
        public int Measure(in int value) => CanonicalBinary.VarUIntBytes(unchecked(((ulong)(long)value << 1) ^ (ulong)((long)value >> 63)));
        public DeterministicStatus TryWrite(ref DeterministicWriter writer, in int value) => writer.TryVarInt(value);
        public DeterministicStatus TryRead(ref DeterministicReader reader, out int value)
        {
            var trial = reader; var status = trial.TryVarInt(out long raw); value = 0;
            if (!status.IsSuccess()) return status;
            if (raw < int.MinValue || raw > int.MaxValue) return DeterministicStatus.InvalidEncoding;
            value = (int)raw; reader = trial; return DeterministicStatus.None;
        }
    }

    public struct UInt64Policy : IDeterministicEquality<ulong>, IDeterministicComparer<ulong>, IDeterministicCodec<ulong>
    {
        public bool IsValid => true;
        public uint SchemaId => 8;
        public ushort Version => 1;
        public bool Equals(in ulong a, in ulong b) => a == b;
        public ulong Hash(in ulong value) => DeterministicHash.Mix(value);
        public int Compare(in ulong a, in ulong b) => a.CompareTo(b);
        public int Measure(in ulong value) => CanonicalBinary.VarUIntBytes(value);
        public DeterministicStatus TryWrite(ref DeterministicWriter writer, in ulong value) => writer.TryVarUInt(value);
        public DeterministicStatus TryRead(ref DeterministicReader reader, out ulong value) => reader.TryVarUInt(out value);
    }

    public struct StableHandlePolicy : IDeterministicEquality<StableHandle>, IDeterministicComparer<StableHandle>, IDeterministicCodec<StableHandle>
    {
        public bool IsValid => true;
        public uint SchemaId => 11;
        public ushort Version => 1;
        public bool Equals(in StableHandle a, in StableHandle b) => a.Equals(b);
        public ulong Hash(in StableHandle value) => DeterministicHash.Mix(value.Packed);
        public int Compare(in StableHandle a, in StableHandle b) => a.CompareTo(b);
        public int Measure(in StableHandle value) => 8;
        public DeterministicStatus TryWrite(ref DeterministicWriter writer, in StableHandle value) => writer.TryUInt(value.Packed, 8);
        public DeterministicStatus TryRead(ref DeterministicReader reader, out StableHandle value)
        {
            var status = reader.TryUInt(8, out ulong raw);
            value = new StableHandle((uint)raw, (uint)(raw >> 32)); return status;
        }
    }
}
