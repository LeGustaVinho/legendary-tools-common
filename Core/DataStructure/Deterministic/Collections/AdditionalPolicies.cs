using System;

namespace LegendaryTools.Deterministic
{
    public struct BooleanPolicy : IDeterministicEquality<bool>, IDeterministicComparer<bool>, IDeterministicCodec<bool>
    {
        public bool IsValid => true;
        public uint SchemaId => 9;
        public ushort Version => 1;
        public bool Equals(in bool a, in bool b) => a == b;
        public ulong Hash(in bool value) => DeterministicHash.Mix(value ? 1UL : 0UL);
        public int Compare(in bool a, in bool b) => a == b ? 0 : a ? 1 : -1;
        public int Measure(in bool value) => 1;
        public DeterministicStatus TryWrite(ref DeterministicWriter writer, in bool value) => writer.TryUInt(value ? 1UL : 0UL, 1);
        public DeterministicStatus TryRead(ref DeterministicReader reader, out bool value)
        {
            value = false; var trial = reader; var status = trial.TryUInt(1, out ulong raw); if (!status.IsSuccess()) return status;
            if (raw > 1) return DeterministicStatus.InvalidEncoding;
            value = raw == 1; reader = trial; return DeterministicStatus.None;
        }
    }
    /// <summary>Enums use fixed-width bits. Applications assign a stable schema ID and declare the signedness of their underlying type.</summary>
    public unsafe struct EnumPolicy<T> : IDeterministicEquality<T>, IDeterministicComparer<T>, IDeterministicCodec<T> where T : unmanaged, Enum
    {
        private readonly uint schemaId;
        private readonly bool signed;
        public EnumPolicy(uint schemaId, bool signedUnderlyingType = false) { this.schemaId = schemaId; signed = signedUnderlyingType; }
        public bool IsValid => schemaId >= 0x10000 && sizeof(T) <= 8;
        public uint SchemaId => schemaId;
        public ushort Version => 1;
        public bool Equals(in T a, in T b) => PrimitiveEquality<T>.Bits(a) == PrimitiveEquality<T>.Bits(b);
        public ulong Hash(in T value) => DeterministicHash.Mix(PrimitiveEquality<T>.Bits(value));
        public int Compare(in T a, in T b)
        {
            ulong left = PrimitiveEquality<T>.Bits(a), right = PrimitiveEquality<T>.Bits(b);
            if (signed) { ulong mask = 1UL << (sizeof(T) * 8 - 1); left ^= mask; right ^= mask; }
            return left < right ? -1 : left > right ? 1 : 0;
        }
        public int Measure(in T value) => sizeof(T);
        public DeterministicStatus TryWrite(ref DeterministicWriter writer, in T value)
            => !IsValid ? DeterministicStatus.InvalidConfiguration : writer.TryUInt(PrimitiveEquality<T>.Bits(value), sizeof(T));
        public DeterministicStatus TryRead(ref DeterministicReader reader, out T value)
        {
            value = default; if (!IsValid) return DeterministicStatus.InvalidConfiguration;
            var status = reader.TryUInt(sizeof(T), out ulong bits); if (!status.IsSuccess()) return status;
            T decoded = default; byte* p = (byte*)&decoded; for (int i = 0; i < sizeof(T); i++) p[i] = (byte)(bits >> (8 * i));
            value = decoded; return DeterministicStatus.None;
        }
    }
}
