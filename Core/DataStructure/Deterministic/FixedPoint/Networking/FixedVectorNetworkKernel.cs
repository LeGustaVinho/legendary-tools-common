namespace LegendaryTools.FixedPoint
{
    internal static unsafe class FixedVectorNetworkKernel
    {
        private const uint Magic = 0x31565046; // FPV1, little endian.
        private static bool Validate(FixedVectorNetworkProfile profile, int dimensions, FixedRuntimeState state, out FixedStatus status)
        {
            status = !state.IsInitialized ? FixedStatus.Uninitialized : !profile.IsValid ? FixedStatus.InvalidConfiguration :
                profile.Dimensions != dimensions || !profile.X.Settings.NumericEquals(state.Settings) ? FixedStatus.ProfileMismatch : FixedStatus.None;
            return status.IsSuccess();
        }
        internal static bool Write(ref FixedBufferWriter writer, FixedVectorBits vector, int dimensions,
            FixedVectorNetworkProfile profile, FixedRuntimeState state, out FixedStatus status)
        {
            if (!Validate(profile, dimensions, state, out status)) return false;
            if (profile.X.Codec != FixedCodec.BitPacked && (writer.BitPosition & 7) != 0)
            { status = FixedStatus.InvalidEncoding; return false; }
            // Maximum is three ten-byte VarInts. Stage the entire value so failures cannot touch the destination.
            byte* scratch = stackalloc byte[30];
            for (int i = 0; i < 30; i++) scratch[i] = 0;
            var staging = new FixedBufferWriter(scratch, 30);
            for (int i = 0; i < dimensions; i++)
            {
                bool success = FixedNetworkKernel.Write(ref staging, vector.Get(i), profile.Get(i), state, out FixedStatus s);
                status |= s; if (!success) return false;
            }
            if (!writer.Has(staging.BitPosition)) { status |= FixedStatus.BufferTooSmall; return false; }
            var reader = new FixedBufferReader(scratch, staging.BytesWritten);
            long remaining = staging.BitPosition;
            while (remaining > 0)
            { int count = remaining > 64 ? 64 : (int)remaining; writer.WriteBits(reader.ReadBits(count), count); remaining -= count; }
            return true;
        }
        internal static bool Read(ref FixedBufferReader reader, int dimensions, FixedVectorNetworkProfile profile,
            FixedRuntimeState state, out FixedVectorBits vector, out FixedStatus status)
        {
            vector = FixedVectorKernel.NaN;
            if (!Validate(profile, dimensions, state, out status)) return false;
            FixedBufferReader trial = reader; FixedVectorBits candidate = default;
            for (int i = 0; i < dimensions; i++)
            {
                bool success = FixedNetworkKernel.Read(ref trial, profile.Get(i), state, out ulong raw, out FixedStatus s);
                status |= s; if (!success) return false; candidate.Set(i, raw);
            }
            reader = trial; vector = candidate; return true;
        }
        internal static FixedVectorBits Canonicalize(FixedVectorBits vector, int dimensions, FixedVectorNetworkProfile profile,
            FixedRuntimeState state, out FixedStatus status)
        {
            if (!Validate(profile, dimensions, state, out status)) return FixedVectorKernel.NaN;
            FixedVectorBits candidate = default;
            for (int i = 0; i < dimensions; i++)
            { candidate.Set(i, FixedNetworkKernel.Canonicalize(vector.Get(i), profile.Get(i), state, out FixedStatus s)); status |= s; }
            return status.IsSuccess() ? candidate : FixedVectorKernel.NaN;
        }
        internal static bool WriteDescriptor(ref FixedBufferWriter writer, FixedVectorNetworkProfile profile, out FixedStatus status)
        {
            status = !profile.IsValid ? FixedStatus.InvalidConfiguration : (writer.BitPosition & 7) != 0 ? FixedStatus.InvalidEncoding :
                !writer.Has(profile.DescriptorBytes * 8L) ? FixedStatus.BufferTooSmall : FixedStatus.None;
            if (!status.IsSuccess()) return false;
            writer.WriteBits(Magic, 32); writer.WriteBits(FixedVectorNetworkProfile.Version, 8); writer.WriteBits(profile.Dimensions, 8); writer.WriteBits(0, 16);
            // All coordinate descriptors and the total capacity have already been validated.
            for (int i = 0; i < profile.Dimensions; i++) FixedNetworkKernel.WriteDescriptor(ref writer, profile.Get(i), out _);
            return true;
        }
        internal static bool ReadDescriptor(ref FixedBufferReader reader, out FixedVectorNetworkProfile profile, out FixedStatus status)
        {
            profile = default; status = FixedStatus.None;
            if ((reader.BitPosition & 7) != 0) { status = FixedStatus.InvalidEncoding; return false; }
            if (!reader.Has(64)) { status = FixedStatus.BufferTooSmall; return false; }
            FixedBufferReader trial = reader;
            if (trial.ReadBits(32) != Magic || trial.ReadBits(8) != FixedVectorNetworkProfile.Version)
            { status = FixedStatus.InvalidEncoding; return false; }
            int dimensions = (int)trial.ReadBits(8);
            if ((dimensions != 2 && dimensions != 3) || trial.ReadBits(16) != 0)
            { status = FixedStatus.InvalidEncoding; return false; }
            if (!FixedNetworkKernel.ReadDescriptor(ref trial, out FixedNetworkProfile x, out status) ||
                !FixedNetworkKernel.ReadDescriptor(ref trial, out FixedNetworkProfile y, out status)) return false;
            FixedNetworkProfile z = default;
            if (dimensions == 3 && !FixedNetworkKernel.ReadDescriptor(ref trial, out z, out status)) return false;
            var candidate = dimensions == 2 ? new FixedVectorNetworkProfile(x, y) : new FixedVectorNetworkProfile(x, y, z);
            if (!candidate.IsValid) { status = FixedStatus.InvalidEncoding; return false; }
            reader = trial; profile = candidate; return true;
        }
    }
}
