using LegendaryTools.FixedPoint;

namespace LegendaryTools.Deterministic
{
    /// <summary>Component value equality and ordering; wire serialization preserves all raw bits.</summary>
    public struct FixedQuaternionPolicy : IDeterministicEquality<FixedQuaternion>, IDeterministicComparer<FixedQuaternion>, IDeterministicCodec<FixedQuaternion>
    {
        public bool IsValid => FixedRuntime.IsInitialized;
        public uint SchemaId => 0x4120u;
        public ushort Version => 1;
        public bool Equals(in FixedQuaternion a, in FixedQuaternion b) => a.Equals(b);
        public ulong Hash(in FixedQuaternion value)
        {
            ulong hash = 17;
            for (int i = 0; i < 4; i++) hash = DeterministicHash.Mix(hash ^ unchecked((uint)value[i].GetHashCode()));
            return hash;
        }
        public int Compare(in FixedQuaternion a, in FixedQuaternion b)
        { for (int i = 0; i < 4; i++) { int result = a[i].CompareTo(b[i]); if (result != 0) return result; } return 0; }
        public int Measure(in FixedQuaternion value) => 32;
        public DeterministicStatus TryWrite(ref DeterministicWriter writer, in FixedQuaternion value)
        {
            if (!IsValid) return DeterministicStatus.InvalidConfiguration;
            if (!writer.Has(32)) return DeterministicStatus.BufferTooSmall;
            var trial = writer;
            for (int i = 0; i < 4; i++) trial.TryUInt(unchecked((ulong)value[i].RawValue), 8);
            writer = trial; return DeterministicStatus.None;
        }
        public DeterministicStatus TryRead(ref DeterministicReader reader, out FixedQuaternion value)
        {
            value = default;
            if (!IsValid) return DeterministicStatus.InvalidConfiguration;
            if (!reader.Has(32)) return DeterministicStatus.BufferTooSmall;
            var trial = reader;
            trial.TryUInt(8, out var x); trial.TryUInt(8, out var y); trial.TryUInt(8, out var z); trial.TryUInt(8, out var w);
            value = FixedQuaternion.FromRaw(unchecked((long)x), unchecked((long)y), unchecked((long)z), unchecked((long)w));
            reader = trial; return DeterministicStatus.None;
        }
    }
}
