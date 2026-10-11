using System;
namespace LegendaryTools.FixedPoint
{
    public static unsafe class FixedNetwork
    {
        public static bool TryWriteDescriptor(ref FixedBufferWriter writer, FixedNetworkProfile profile, out FixedStatus status) => FixedNetworkKernel.WriteDescriptor(ref writer, profile, out status);
        public static bool TryReadDescriptor(ref FixedBufferReader reader, out FixedNetworkProfile profile, out FixedStatus status) => FixedNetworkKernel.ReadDescriptor(ref reader, out profile, out status);
        public static bool TryNegotiate(FixedNetworkProfile local, FixedNetworkProfile remote, out FixedStatus status)
        { status = local.IsCompatible(remote) ? FixedStatus.None : FixedStatus.ProfileMismatch; return status.IsSuccess(); }
        public static bool TryWrite(ref FixedBufferWriter writer, Fixed32 value, FixedNetworkProfile profile, out FixedStatus status)
        { if (profile.Kind != Fixed32.Kind) { status = FixedStatus.ProfileMismatch; return false; } return FixedNetworkKernel.Write(ref writer, value.Bits, profile, FixedRuntime.State, out status); }
        public static bool TryRead(ref FixedBufferReader reader, FixedNetworkProfile profile, out Fixed32 value, out FixedStatus status)
        {
            value = Fixed32.NaN;
            if (profile.Kind != Fixed32.Kind) { status = FixedStatus.ProfileMismatch; return false; }
            bool success = FixedNetworkKernel.Read(ref reader, profile, FixedRuntime.State, out ulong raw, out status);
            value = Fixed32.FromBits(raw); return success;
        }
        public static Fixed32 Canonicalize(Fixed32 value, FixedNetworkProfile profile, out FixedStatus status)
        { if (profile.Kind != Fixed32.Kind) { status = FixedStatus.ProfileMismatch; return Fixed32.NaN; } return Fixed32.FromBits(FixedNetworkKernel.Canonicalize(value.Bits, profile, FixedRuntime.State, out status)); }
        public static bool TryWrite(Span<byte> buffer, ref int byteOffset, Fixed32 value, FixedNetworkProfile profile, out FixedStatus status)
        {
            fixed (byte* pointer = buffer)
            { var writer = new FixedBufferWriter(pointer, buffer.Length, (long)byteOffset * 8); bool success = TryWrite(ref writer, value, profile, out status); if (success) byteOffset = writer.BytesWritten; return success; }
        }
        public static bool TryRead(ReadOnlySpan<byte> buffer, ref int byteOffset, FixedNetworkProfile profile, out Fixed32 value, out FixedStatus status)
        {
            fixed (byte* pointer = buffer)
            { var reader = new FixedBufferReader(pointer, buffer.Length, (long)byteOffset * 8); bool success = TryRead(ref reader, profile, out value, out status); if (success) byteOffset = (int)((reader.BitPosition + 7) / 8); return success; }
        }
        public static bool TryWrite(ref FixedBufferWriter writer, UFixed32 value, FixedNetworkProfile profile, out FixedStatus status)
        { if (profile.Kind != UFixed32.Kind) { status = FixedStatus.ProfileMismatch; return false; } return FixedNetworkKernel.Write(ref writer, value.Bits, profile, FixedRuntime.State, out status); }
        public static bool TryRead(ref FixedBufferReader reader, FixedNetworkProfile profile, out UFixed32 value, out FixedStatus status)
        {
            value = UFixed32.NaN;
            if (profile.Kind != UFixed32.Kind) { status = FixedStatus.ProfileMismatch; return false; }
            bool success = FixedNetworkKernel.Read(ref reader, profile, FixedRuntime.State, out ulong raw, out status);
            value = UFixed32.FromBits(raw); return success;
        }
        public static UFixed32 Canonicalize(UFixed32 value, FixedNetworkProfile profile, out FixedStatus status)
        { if (profile.Kind != UFixed32.Kind) { status = FixedStatus.ProfileMismatch; return UFixed32.NaN; } return UFixed32.FromBits(FixedNetworkKernel.Canonicalize(value.Bits, profile, FixedRuntime.State, out status)); }
        public static bool TryWrite(Span<byte> buffer, ref int byteOffset, UFixed32 value, FixedNetworkProfile profile, out FixedStatus status)
        {
            fixed (byte* pointer = buffer)
            { var writer = new FixedBufferWriter(pointer, buffer.Length, (long)byteOffset * 8); bool success = TryWrite(ref writer, value, profile, out status); if (success) byteOffset = writer.BytesWritten; return success; }
        }
        public static bool TryRead(ReadOnlySpan<byte> buffer, ref int byteOffset, FixedNetworkProfile profile, out UFixed32 value, out FixedStatus status)
        {
            fixed (byte* pointer = buffer)
            { var reader = new FixedBufferReader(pointer, buffer.Length, (long)byteOffset * 8); bool success = TryRead(ref reader, profile, out value, out status); if (success) byteOffset = (int)((reader.BitPosition + 7) / 8); return success; }
        }
        public static bool TryWrite(ref FixedBufferWriter writer, Fixed64 value, FixedNetworkProfile profile, out FixedStatus status)
        { if (profile.Kind != Fixed64.Kind) { status = FixedStatus.ProfileMismatch; return false; } return FixedNetworkKernel.Write(ref writer, value.Bits, profile, FixedRuntime.State, out status); }
        public static bool TryRead(ref FixedBufferReader reader, FixedNetworkProfile profile, out Fixed64 value, out FixedStatus status)
        {
            value = Fixed64.NaN;
            if (profile.Kind != Fixed64.Kind) { status = FixedStatus.ProfileMismatch; return false; }
            bool success = FixedNetworkKernel.Read(ref reader, profile, FixedRuntime.State, out ulong raw, out status);
            value = Fixed64.FromBits(raw); return success;
        }
        public static Fixed64 Canonicalize(Fixed64 value, FixedNetworkProfile profile, out FixedStatus status)
        { if (profile.Kind != Fixed64.Kind) { status = FixedStatus.ProfileMismatch; return Fixed64.NaN; } return Fixed64.FromBits(FixedNetworkKernel.Canonicalize(value.Bits, profile, FixedRuntime.State, out status)); }
        public static bool TryWrite(Span<byte> buffer, ref int byteOffset, Fixed64 value, FixedNetworkProfile profile, out FixedStatus status)
        {
            fixed (byte* pointer = buffer)
            { var writer = new FixedBufferWriter(pointer, buffer.Length, (long)byteOffset * 8); bool success = TryWrite(ref writer, value, profile, out status); if (success) byteOffset = writer.BytesWritten; return success; }
        }
        public static bool TryRead(ReadOnlySpan<byte> buffer, ref int byteOffset, FixedNetworkProfile profile, out Fixed64 value, out FixedStatus status)
        {
            fixed (byte* pointer = buffer)
            { var reader = new FixedBufferReader(pointer, buffer.Length, (long)byteOffset * 8); bool success = TryRead(ref reader, profile, out value, out status); if (success) byteOffset = (int)((reader.BitPosition + 7) / 8); return success; }
        }
        public static bool TryWrite(ref FixedBufferWriter writer, UFixed64 value, FixedNetworkProfile profile, out FixedStatus status)
        { if (profile.Kind != UFixed64.Kind) { status = FixedStatus.ProfileMismatch; return false; } return FixedNetworkKernel.Write(ref writer, value.Bits, profile, FixedRuntime.State, out status); }
        public static bool TryRead(ref FixedBufferReader reader, FixedNetworkProfile profile, out UFixed64 value, out FixedStatus status)
        {
            value = UFixed64.NaN;
            if (profile.Kind != UFixed64.Kind) { status = FixedStatus.ProfileMismatch; return false; }
            bool success = FixedNetworkKernel.Read(ref reader, profile, FixedRuntime.State, out ulong raw, out status);
            value = UFixed64.FromBits(raw); return success;
        }
        public static UFixed64 Canonicalize(UFixed64 value, FixedNetworkProfile profile, out FixedStatus status)
        { if (profile.Kind != UFixed64.Kind) { status = FixedStatus.ProfileMismatch; return UFixed64.NaN; } return UFixed64.FromBits(FixedNetworkKernel.Canonicalize(value.Bits, profile, FixedRuntime.State, out status)); }
        public static bool TryWrite(Span<byte> buffer, ref int byteOffset, UFixed64 value, FixedNetworkProfile profile, out FixedStatus status)
        {
            fixed (byte* pointer = buffer)
            { var writer = new FixedBufferWriter(pointer, buffer.Length, (long)byteOffset * 8); bool success = TryWrite(ref writer, value, profile, out status); if (success) byteOffset = writer.BytesWritten; return success; }
        }
        public static bool TryRead(ReadOnlySpan<byte> buffer, ref int byteOffset, FixedNetworkProfile profile, out UFixed64 value, out FixedStatus status)
        {
            fixed (byte* pointer = buffer)
            { var reader = new FixedBufferReader(pointer, buffer.Length, (long)byteOffset * 8); bool success = TryRead(ref reader, profile, out value, out status); if (success) byteOffset = (int)((reader.BitPosition + 7) / 8); return success; }
        }
    }
}
