using System;

namespace LegendaryTools.FixedPoint
{
    /// <summary>Negotiated once alongside the FixedPoint numeric profile. Payloads do not repeat this descriptor.</summary>
    public readonly struct FixedRandomNetworkProfile
    {
        public const byte Version = 1;
        public const int DescriptorBytes = 16;
        public readonly byte ProtocolVersion, Algorithm;
        public readonly ushort Samplers;
        public readonly ulong NumericFingerprint;
        public FixedRandomNetworkProfile(FixedSettings settings)
        { ProtocolVersion = Version; Algorithm = FixedRandom.AlgorithmId; Samplers = FixedRandom.SamplerVersion; NumericFingerprint = settings.IsValid ? settings.NumericFingerprint : 0; }
        internal FixedRandomNetworkProfile(byte protocol, byte algorithm, ushort samplers, ulong fingerprint)
        { ProtocolVersion = protocol; Algorithm = algorithm; Samplers = samplers; NumericFingerprint = fingerprint; }
        public bool IsValid => ProtocolVersion == Version && Algorithm == FixedRandom.AlgorithmId && Samplers == FixedRandom.SamplerVersion && NumericFingerprint != 0;
        public bool IsCompatible(FixedRandomNetworkProfile other) => IsValid && other.IsValid && NumericFingerprint == other.NumericFingerprint;
    }

    public static unsafe class FixedRandomNetwork
    {
        private const uint Magic = 0x474E5246; // "FRNG" little-endian.
        private static bool Validate(FixedRandomNetworkProfile profile, out FixedStatus status)
        {
            status = !FixedRuntime.IsInitialized ? FixedStatus.Uninitialized : !profile.IsValid ? FixedStatus.InvalidConfiguration :
                profile.NumericFingerprint != FixedRuntime.Settings.NumericFingerprint ? FixedStatus.ProfileMismatch : FixedStatus.None;
            return status.IsSuccess();
        }
        private static bool WriteCapacity(ref FixedBufferWriter writer, int bits, out FixedStatus status)
        {
            status = (writer.BitPosition & 7) != 0 ? FixedStatus.InvalidEncoding : !writer.Has(bits) ? FixedStatus.BufferTooSmall : FixedStatus.None;
            return status.IsSuccess();
        }
        private static bool ReadCapacity(ref FixedBufferReader reader, int bits, out FixedStatus status)
        {
            status = (reader.BitPosition & 7) != 0 ? FixedStatus.InvalidEncoding : !reader.Has(bits) ? FixedStatus.BufferTooSmall : FixedStatus.None;
            return status.IsSuccess();
        }
        public static bool TryWriteDescriptor(ref FixedBufferWriter writer, FixedRandomNetworkProfile profile, out FixedStatus status)
        {
            status = profile.IsValid ? FixedStatus.None : FixedStatus.InvalidConfiguration;
            if (!status.IsSuccess() || !WriteCapacity(ref writer, FixedRandomNetworkProfile.DescriptorBytes * 8, out status)) return false;
            writer.WriteBits(Magic, 32); writer.WriteBits(profile.ProtocolVersion, 8); writer.WriteBits(profile.Algorithm, 8);
            writer.WriteBits(profile.Samplers, 16); writer.WriteBits(profile.NumericFingerprint, 64); return true;
        }
        public static bool TryReadDescriptor(ref FixedBufferReader reader, out FixedRandomNetworkProfile profile, out FixedStatus status)
        {
            profile = default;
            if (!ReadCapacity(ref reader, FixedRandomNetworkProfile.DescriptorBytes * 8, out status)) return false;
            FixedBufferReader trial = reader;
            if (trial.ReadBits(32) != Magic) { status = FixedStatus.InvalidEncoding; return false; }
            var candidate = new FixedRandomNetworkProfile((byte)trial.ReadBits(8), (byte)trial.ReadBits(8), (ushort)trial.ReadBits(16), trial.ReadBits(64));
            if (!candidate.IsValid) { status = FixedStatus.ProfileMismatch; return false; }
            profile = candidate; reader = trial; return true;
        }
        public static bool TryWriteState(ref FixedBufferWriter writer, FixedRandomState state, FixedRandomNetworkProfile profile, out FixedStatus status)
        {
            if (!Validate(profile, out status) || !WriteCapacity(ref writer, FixedRandomState.Bytes * 8, out status)) return false;
            writer.WriteBits(state.Key, 64); writer.WriteBits(state.SequenceId, 64); writer.WriteBits(state.WordPosition, 64); return true;
        }
        public static bool TryReadState(ref FixedBufferReader reader, FixedRandomNetworkProfile profile, out FixedRandomState state, out FixedStatus status)
        {
            state = default;
            if (!Validate(profile, out status) || !ReadCapacity(ref reader, FixedRandomState.Bytes * 8, out status)) return false;
            state = new FixedRandomState(reader.ReadBits(64), reader.ReadBits(64), reader.ReadBits(64)); return true;
        }
        public static bool TryWritePosition(ref FixedBufferWriter writer, ulong position, FixedRandomNetworkProfile profile, out FixedStatus status, FixedCodec codec = FixedCodec.VarInt)
        {
            if (!Validate(profile, out status)) return false;
            if (codec != FixedCodec.FixedWidth && codec != FixedCodec.VarInt) { status = FixedStatus.InvalidConfiguration; return false; }
            int bytes = 8;
            if (codec == FixedCodec.VarInt) { bytes = 1; ulong rest = position; while ((rest >>= 7) != 0) bytes++; }
            if (!WriteCapacity(ref writer, bytes * 8, out status)) return false;
            if (codec == FixedCodec.FixedWidth) { writer.WriteBits(position, 64); return true; }
            for (int i = 0; i < bytes; i++)
            { byte payload = (byte)(position & 127); position >>= 7; writer.WriteBits((ulong)(payload | (position != 0 ? 128 : 0)), 8); }
            return true;
        }
        public static bool TryReadPosition(ref FixedBufferReader reader, ref FixedRandom random, FixedRandomNetworkProfile profile, out FixedStatus status, FixedCodec codec = FixedCodec.VarInt)
        {
            if (!Validate(profile, out status)) return false;
            if (codec != FixedCodec.FixedWidth && codec != FixedCodec.VarInt) { status = FixedStatus.InvalidConfiguration; return false; }
            if (!ReadCapacity(ref reader, codec == FixedCodec.FixedWidth ? 64 : 8, out status)) return false;
            FixedBufferReader trial = reader; ulong position = 0;
            if (codec == FixedCodec.FixedWidth) position = trial.ReadBits(64);
            else
            {
                bool terminated = false;
                for (int i = 0; i < 10; i++)
                {
                    if (!trial.Has(8)) { status = FixedStatus.BufferTooSmall; return false; }
                    byte value = (byte)trial.ReadBits(8); ulong payload = (ulong)(value & 127);
                    if (i == 9 && (payload > 1 || (value & 128) != 0)) { status = FixedStatus.InvalidEncoding; return false; }
                    position |= payload << (i * 7);
                    if ((value & 128) == 0)
                    {
                        if (i > 0 && payload == 0) { status = FixedStatus.InvalidEncoding; return false; }
                        terminated = true; break;
                    }
                }
                if (!terminated) { status = FixedStatus.InvalidEncoding; return false; }
            }
            random.Seek(position); reader = trial; return true;
        }

        public static bool TryWriteDescriptor(Span<byte> buffer, ref int offset, FixedRandomNetworkProfile profile, out FixedStatus status)
        {
            fixed (byte* pointer = buffer)
            { var writer = new FixedBufferWriter(pointer, buffer.Length, (long)offset * 8); if (!TryWriteDescriptor(ref writer, profile, out status)) return false; offset = writer.BytesWritten; return true; }
        }
        public static bool TryReadDescriptor(ReadOnlySpan<byte> buffer, ref int offset, out FixedRandomNetworkProfile profile, out FixedStatus status)
        {
            fixed (byte* pointer = buffer)
            { var reader = new FixedBufferReader(pointer, buffer.Length, (long)offset * 8); if (!TryReadDescriptor(ref reader, out profile, out status)) return false; offset = (int)(reader.BitPosition / 8); return true; }
        }
        public static bool TryWriteState(Span<byte> buffer, ref int offset, FixedRandomState state, FixedRandomNetworkProfile profile, out FixedStatus status)
        {
            fixed (byte* pointer = buffer)
            { var writer = new FixedBufferWriter(pointer, buffer.Length, (long)offset * 8); if (!TryWriteState(ref writer, state, profile, out status)) return false; offset = writer.BytesWritten; return true; }
        }
        public static bool TryReadState(ReadOnlySpan<byte> buffer, ref int offset, FixedRandomNetworkProfile profile, out FixedRandomState state, out FixedStatus status)
        {
            fixed (byte* pointer = buffer)
            { var reader = new FixedBufferReader(pointer, buffer.Length, (long)offset * 8); if (!TryReadState(ref reader, profile, out state, out status)) return false; offset = (int)(reader.BitPosition / 8); return true; }
        }
        public static bool TryWritePosition(Span<byte> buffer, ref int offset, ulong position, FixedRandomNetworkProfile profile, out FixedStatus status, FixedCodec codec = FixedCodec.VarInt)
        {
            fixed (byte* pointer = buffer)
            { var writer = new FixedBufferWriter(pointer, buffer.Length, (long)offset * 8); if (!TryWritePosition(ref writer, position, profile, out status, codec)) return false; offset = writer.BytesWritten; return true; }
        }
        public static bool TryReadPosition(ReadOnlySpan<byte> buffer, ref int offset, ref FixedRandom random, FixedRandomNetworkProfile profile, out FixedStatus status, FixedCodec codec = FixedCodec.VarInt)
        {
            fixed (byte* pointer = buffer)
            { var reader = new FixedBufferReader(pointer, buffer.Length, (long)offset * 8); if (!TryReadPosition(ref reader, ref random, profile, out status, codec)) return false; offset = (int)(reader.BitPosition / 8); return true; }
        }
    }
}
