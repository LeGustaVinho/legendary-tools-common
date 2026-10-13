namespace LegendaryTools.FixedPoint
{
    internal static unsafe class FixedBoundsNetworkKernel
    {
        private const uint Magic = 0x31425046; // FPB1, little endian.
        private static bool Validate(FixedBoundsNetworkProfile profile, int dimensions, FixedRuntimeState numeric,
            FixedBoundsRuntimeState geometry, out FixedStatus status)
        {
            status = !numeric.IsInitialized || !geometry.IsInitialized ? FixedStatus.Uninitialized :
                !profile.IsValid ? FixedStatus.InvalidConfiguration : profile.Dimensions != dimensions ||
                profile.Representation != geometry.Settings.Representation || !profile.First.X.Settings.NumericEquals(numeric.Settings)
                    ? FixedStatus.ProfileMismatch : FixedStatus.None;
            return status.IsSuccess();
        }
        private static void Copy(ref FixedBufferWriter destination, byte* staged, int bytes, long bits)
        {
            // Capacity is checked before publication. Only complete, aligned bytes can use this path.
            if ((destination.BitPosition & 7) == 0 && (bits & 7) == 0)
            {
                int start = (int)(destination.BitPosition / 8);
                for (int i = 0; i < bytes; i++) destination.Buffer[start + i] = staged[i];
                destination.BitPosition += bits;
                return;
            }
            var reader = new FixedBufferReader(staged, bytes);
            while (bits > 0) { int count = bits > 64 ? 64 : (int)bits; destination.WriteBits(reader.ReadBits(count), count); bits -= count; }
        }
        internal static FixedBoundsBits Canonicalize(FixedBoundsBits value, int dimensions, FixedBoundsNetworkProfile profile,
            FixedRuntimeState numeric, FixedBoundsRuntimeState geometry, out FixedStatus status)
        {
            if (!Validate(profile, dimensions, numeric, geometry, out status) ||
                !FixedBoundsKernel.Validate(value, dimensions, numeric, geometry, out status)) return FixedBoundsBits.Invalid;
            var a = FixedVectorNetworkKernel.Canonicalize(value.First, dimensions, profile.First, numeric, out FixedStatus sa);
            var b = FixedVectorNetworkKernel.Canonicalize(value.Second, dimensions, profile.Second, numeric, out FixedStatus sb);
            status |= sa | sb;
            if (!status.IsSuccess()) return FixedBoundsBits.Invalid;
            var result = new FixedBoundsBits(a, b);
            if (!FixedBoundsKernel.Validate(result, dimensions, numeric, geometry, out var validation))
            { status |= validation; return FixedBoundsBits.Invalid; }
            return result;
        }
        internal static bool Write(ref FixedBufferWriter writer, FixedBoundsBits value, int dimensions, FixedBoundsNetworkProfile profile,
            FixedRuntimeState numeric, FixedBoundsRuntimeState geometry, out FixedStatus status)
        {
            var candidate = Canonicalize(value, dimensions, profile, numeric, geometry, out status);
            if (!status.IsSuccess()) return false;
            if (profile.First.X.Codec != FixedCodec.BitPacked && (writer.BitPosition & 7) != 0)
            { status |= FixedStatus.InvalidEncoding; return false; }
            byte* staged = stackalloc byte[60];
            for (int i = 0; i < 60; i++) staged[i] = 0;
            var trial = new FixedBufferWriter(staged, 60);
            // Canonicalization already validated both field profiles and the joint geometry.
            // Stage scalars directly: vector-level staging and bit copies are redundant here.
            for (int field = 0; field < dimensions * 2; field++)
            {
                bool first = field < dimensions;
                int axis = first ? field : field - dimensions;
                bool ok = FixedNetworkKernel.Write(ref trial, first ? candidate.First.Get(axis) : candidate.Second.Get(axis),
                    first ? profile.First.Get(axis) : profile.Second.Get(axis), numeric, out var scalarStatus);
                status |= scalarStatus;
                if (!ok) return false;
            }
            if (!writer.Has(trial.BitPosition)) { status |= FixedStatus.BufferTooSmall; return false; }
            Copy(ref writer, staged, trial.BytesWritten, trial.BitPosition); return true;
        }
        internal static bool Read(ref FixedBufferReader reader, int dimensions, FixedBoundsNetworkProfile profile,
            FixedRuntimeState numeric, FixedBoundsRuntimeState geometry, out FixedBoundsBits value, out FixedStatus status)
        {
            value = FixedBoundsBits.Invalid;
            if (!Validate(profile, dimensions, numeric, geometry, out status)) return false;
            var trial = reader;
            bool first = FixedVectorNetworkKernel.Read(ref trial, dimensions, profile.First, numeric, out var a, out var sa); status |= sa;
            if (!first) return false;
            bool second = FixedVectorNetworkKernel.Read(ref trial, dimensions, profile.Second, numeric, out var b, out var sb); status |= sb;
            if (!second) return false;
            var candidate = new FixedBoundsBits(a, b);
            if (!FixedBoundsKernel.Validate(candidate, dimensions, numeric, geometry, out _))
            { status |= FixedStatus.InvalidEncoding; return false; }
            value = candidate; reader = trial; return true;
        }
        internal static bool WriteDescriptor(ref FixedBufferWriter writer, FixedBoundsNetworkProfile profile, out FixedStatus status)
        {
            status = !profile.IsValid ? FixedStatus.InvalidConfiguration : (writer.BitPosition & 7) != 0 ? FixedStatus.InvalidEncoding :
                !writer.Has(profile.DescriptorBytes * 8L) ? FixedStatus.BufferTooSmall : FixedStatus.None;
            if (!status.IsSuccess()) return false;
            byte* staged = stackalloc byte[264];
            for (int i = 0; i < 264; i++) staged[i] = 0;
            var trial = new FixedBufferWriter(staged, 264);
            trial.WriteBits(Magic, 32); trial.WriteBits(FixedBoundsNetworkProfile.Version, 8);
            trial.WriteBits((ulong)profile.Dimensions, 8); trial.WriteBits((byte)profile.Representation, 8); trial.WriteBits(0, 8);
            if (!FixedVectorNetworkKernel.WriteDescriptor(ref trial, profile.First, out status) ||
                !FixedVectorNetworkKernel.WriteDescriptor(ref trial, profile.Second, out status)) return false;
            Copy(ref writer, staged, trial.BytesWritten, trial.BitPosition); return true;
        }
        internal static bool ReadDescriptor(ref FixedBufferReader reader, out FixedBoundsNetworkProfile profile, out FixedStatus status)
        {
            profile = default; status = FixedStatus.None;
            if ((reader.BitPosition & 7) != 0) { status = FixedStatus.InvalidEncoding; return false; }
            if (!reader.Has(64)) { status = FixedStatus.BufferTooSmall; return false; }
            var trial = reader;
            if (trial.ReadBits(32) != Magic || trial.ReadBits(8) != FixedBoundsNetworkProfile.Version)
            { status = FixedStatus.InvalidEncoding; return false; }
            int dimensions = (int)trial.ReadBits(8); var representation = (FixedBoundsRepresentation)trial.ReadBits(8);
            if (dimensions != 2 && dimensions != 3 || representation > FixedBoundsRepresentation.CenterExtents || trial.ReadBits(8) != 0)
            { status = FixedStatus.InvalidEncoding; return false; }
            if (!FixedVectorNetworkKernel.ReadDescriptor(ref trial, out var first, out status) ||
                !FixedVectorNetworkKernel.ReadDescriptor(ref trial, out var second, out status)) return false;
            var candidate = new FixedBoundsNetworkProfile(representation, first, second);
            if (!candidate.IsValid || candidate.Dimensions != dimensions) { status = FixedStatus.InvalidEncoding; return false; }
            profile = candidate; reader = trial; return true;
        }
    }
}
