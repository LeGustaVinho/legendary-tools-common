using LegendaryTools.FixedPoint;

namespace LegendaryTools.Deterministic
{
    /// <summary>Philox4x32-10 state; cached words are derived by FixedRandom.RestoreState.</summary>
    public struct FixedRandomStateCodec : IDeterministicCodec<FixedRandomState>
    {
        public uint SchemaId => 0x4110u;
        public ushort Version => FixedRandom.SamplerVersion;
        public int Measure(in FixedRandomState value) => FixedRandomState.Bytes;
        public DeterministicStatus TryWrite(ref DeterministicWriter writer, in FixedRandomState value)
        {
            if (!writer.Has(FixedRandomState.Bytes)) return DeterministicStatus.BufferTooSmall;
            var trial = writer;
            trial.TryUInt(value.Key, 8); trial.TryUInt(value.SequenceId, 8); trial.TryUInt(value.WordPosition, 8);
            writer = trial; return DeterministicStatus.None;
        }
        public DeterministicStatus TryRead(ref DeterministicReader reader, out FixedRandomState value)
        {
            value = default;
            if (!reader.Has(FixedRandomState.Bytes)) return DeterministicStatus.BufferTooSmall;
            var trial = reader;
            trial.TryUInt(8, out ulong key); trial.TryUInt(8, out ulong sequence); trial.TryUInt(8, out ulong position);
            value = new FixedRandomState(key, sequence, position); reader = trial; return DeterministicStatus.None;
        }
    }

    public static class FixedDeterministicProfile
    {
        /// <summary>Opt-in world profile for simulations using quaternions, scalar arithmetic and Philox.</summary>
        public static DeterministicStatus TryQuaternionFingerprint(out ulong fingerprint)
        {
            var status = TryFingerprint(out fingerprint);
            if (status.IsSuccess()) fingerprint = DeterministicHash.Mix(fingerprint ^ DeterministicHash.Mix(FixedRuntime.Settings.QuaternionFingerprint));
            return status;
        }
        public static DeterministicStatus TryFingerprint(out ulong fingerprint)
        {
            fingerprint = 0;
            if (!FixedRuntime.IsInitialized) return DeterministicStatus.InvalidConfiguration;
            fingerprint = DeterministicHash.Mix(FixedRuntime.Settings.NumericFingerprint ^
                DeterministicHash.Mix(0x5048494c4f583431UL ^ ((ulong)FixedRandom.AlgorithmId << 32) ^ FixedRandom.SamplerVersion));
            return DeterministicStatus.None;
        }
    }
}
