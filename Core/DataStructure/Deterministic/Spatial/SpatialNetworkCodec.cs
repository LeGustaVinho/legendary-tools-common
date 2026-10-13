using LegendaryTools.FixedPoint;

namespace LegendaryTools.Deterministic
{
    /// <summary>Optional full semantic fingerprint for configured codecs, inspected only during host binding registration.</summary>
    public interface ISpatialCodecFingerprint { ulong CodecFingerprint { get; } }

    public unsafe interface ISpatialBoundsNetworkAccess<TBounds> where TBounds : unmanaged
    {
        int Dimensions { get; }
        bool Canonicalize(in TBounds value, FixedBoundsNetworkProfile profile, out TBounds result, out FixedStatus status);
        bool Write(ref FixedBufferWriter writer, in TBounds value, FixedBoundsNetworkProfile profile, out FixedStatus status);
        bool Read(ref FixedBufferReader reader, FixedBoundsNetworkProfile profile, out TBounds value, out FixedStatus status);
        bool RawEquals(in TBounds a, in TBounds b);
    }
    public unsafe struct SpatialBounds2NetworkAccess : ISpatialBoundsNetworkAccess<FixedBounds2>
    {
        public int Dimensions => 2;
        public bool Canonicalize(in FixedBounds2 value, FixedBoundsNetworkProfile profile, out FixedBounds2 result, out FixedStatus status) => FixedBoundsNetwork.TryCanonicalize(value, profile, out result, out status);
        public bool Write(ref FixedBufferWriter writer, in FixedBounds2 value, FixedBoundsNetworkProfile profile, out FixedStatus status) => FixedBoundsNetwork.TryWrite(ref writer, value, profile, out status);
        public bool Read(ref FixedBufferReader reader, FixedBoundsNetworkProfile profile, out FixedBounds2 value, out FixedStatus status) => FixedBoundsNetwork.TryRead(ref reader, profile, out value, out status);
        public bool RawEquals(in FixedBounds2 a, in FixedBounds2 b)
        { a.GetStorage(out var x, out var y); b.GetStorage(out var u, out var v); return x.x.RawValue == u.x.RawValue && x.y.RawValue == u.y.RawValue && y.x.RawValue == v.x.RawValue && y.y.RawValue == v.y.RawValue; }
    }
    public unsafe struct SpatialBounds3NetworkAccess : ISpatialBoundsNetworkAccess<FixedBounds3>
    {
        public int Dimensions => 3;
        public bool Canonicalize(in FixedBounds3 value, FixedBoundsNetworkProfile profile, out FixedBounds3 result, out FixedStatus status) => FixedBoundsNetwork.TryCanonicalize(value, profile, out result, out status);
        public bool Write(ref FixedBufferWriter writer, in FixedBounds3 value, FixedBoundsNetworkProfile profile, out FixedStatus status) => FixedBoundsNetwork.TryWrite(ref writer, value, profile, out status);
        public bool Read(ref FixedBufferReader reader, FixedBoundsNetworkProfile profile, out FixedBounds3 value, out FixedStatus status) => FixedBoundsNetwork.TryRead(ref reader, profile, out value, out status);
        public bool RawEquals(in FixedBounds3 a, in FixedBounds3 b)
        {
            a.GetStorage(out var x, out var y); b.GetStorage(out var u, out var v);
            return x.x.RawValue == u.x.RawValue && x.y.RawValue == u.y.RawValue && x.z.RawValue == u.z.RawValue &&
                y.x.RawValue == v.x.RawValue && y.y.RawValue == v.y.RawValue && y.z.RawValue == v.z.RawValue;
        }
    }
    /// <summary>Compact logical Bounds codec. Writes accept only values already canonical under the negotiated profile.</summary>
    public readonly unsafe struct SpatialBoundsNetworkCodec<TBounds, TAccess> : IDeterministicCodec<TBounds>, ISpatialCodecFingerprint
        where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsNetworkAccess<TBounds>
    {
        public readonly FixedBoundsNetworkProfile Profile;
        public ulong CodecFingerprint { get; }
        public uint SchemaId => 0xd340u + (uint)default(TAccess).Dimensions;
        public ushort Version => 1;
        public bool IsValid => CodecFingerprint != 0;
        public SpatialBoundsNetworkCodec(FixedBoundsNetworkProfile profile)
        {
            Profile = profile; CodecFingerprint = 0;
            if (!profile.IsValid || profile.Dimensions != default(TAccess).Dimensions) return;
            byte* bytes = stackalloc byte[264]; var writer = new FixedBufferWriter(bytes, 264);
            if (FixedBoundsNetwork.TryWriteDescriptor(ref writer, profile, out _)) CodecFingerprint = DeterministicHash.Xxh64(bytes, writer.BytesWritten);
        }
        private static DeterministicStatus Convert(FixedStatus status) => (status & FixedStatus.BufferTooSmall) != 0 ? DeterministicStatus.BufferTooSmall :
            (status & FixedStatus.ProfileMismatch) != 0 ? DeterministicStatus.ProfileMismatch :
            (status & (FixedStatus.Uninitialized | FixedStatus.InvalidConfiguration)) != 0 ? DeterministicStatus.InvalidConfiguration : DeterministicStatus.InvalidEncoding;
        /// <summary>Apply this explicitly to authoritative state at a shared simulation boundary, before indexing/capturing it.</summary>
        public bool TryCanonicalize(in TBounds value, out TBounds canonical, out FixedStatus status) => default(TAccess).Canonicalize(value, Profile, out canonical, out status);
        private DeterministicStatus Encode(in TBounds value, byte* bytes, out int length)
        {
            length = 0; if (!IsValid) return DeterministicStatus.InvalidConfiguration;
            var access = default(TAccess);
            if (!access.Canonicalize(value, Profile, out var canonical, out var status)) return Convert(status);
            if (!access.RawEquals(value, canonical)) return DeterministicStatus.InvalidEncoding;
            for (int i = 0; i < 60; i++) bytes[i] = 0;
            var writer = new FixedBufferWriter(bytes, 60);
            if (!access.Write(ref writer, value, Profile, out status)) return Convert(status);
            length = writer.BytesWritten; return DeterministicStatus.None;
        }
        public int Measure(in TBounds value)
        { byte* bytes = stackalloc byte[60]; return Encode(value, bytes, out int length).IsSuccess() ? length + 8 : -1; }
        public DeterministicStatus TryWrite(ref DeterministicWriter writer, in TBounds value)
        {
            byte* bytes = stackalloc byte[60]; var status = Encode(value, bytes, out int length); if (!status.IsSuccess()) return status;
            if (!writer.Has(length + 8)) return DeterministicStatus.BufferTooSmall;
            var trial = writer; trial.TryUInt(CodecFingerprint, 8); trial.TryBytes(bytes, length); writer = trial; return DeterministicStatus.None;
        }
        public DeterministicStatus TryRead(ref DeterministicReader reader, out TBounds value)
        {
            value = default; if (!IsValid) return DeterministicStatus.InvalidConfiguration;
            var trial = reader; var status = trial.TryUInt(8, out ulong profile); if (!status.IsSuccess()) return status;
            if (profile != CodecFingerprint) return DeterministicStatus.ProfileMismatch;
            var bits = new FixedBufferReader(trial.Buffer + trial.Position, trial.Length - trial.Position);
            if (!default(TAccess).Read(ref bits, Profile, out var candidate, out var fixedStatus)) return Convert(fixedStatus);
            int bytes = (int)((bits.BitPosition + 7) / 8), remainder = (int)(bits.BitPosition & 7);
            if (remainder != 0 && (trial.Buffer[trial.Position + bytes - 1] >> remainder) != 0) return DeterministicStatus.InvalidEncoding;
            trial.Position += bytes; value = candidate; reader = trial; return DeterministicStatus.None;
        }
    }
}
