using System;

namespace LegendaryTools.FixedPoint
{
    /// <summary>Atomic vector codecs. Negotiate profiles once; individual values contain coordinates only.</summary>
    public static unsafe class FixedVectorNetwork
    {
        public static bool TryNegotiate(FixedVectorNetworkProfile local, FixedVectorNetworkProfile remote, out FixedStatus status)
        { status = local.IsCompatible(remote) ? FixedStatus.None : FixedStatus.ProfileMismatch; return status.IsSuccess(); }
        public static bool TryWriteDescriptor(ref FixedBufferWriter writer, FixedVectorNetworkProfile profile, out FixedStatus status) => FixedVectorNetworkKernel.WriteDescriptor(ref writer, profile, out status);
        public static bool TryReadDescriptor(ref FixedBufferReader reader, out FixedVectorNetworkProfile profile, out FixedStatus status) => FixedVectorNetworkKernel.ReadDescriptor(ref reader, out profile, out status);
        public static bool TryWrite(ref FixedBufferWriter writer, in FixedVector2 value, FixedVectorNetworkProfile profile, out FixedStatus status) => FixedVectorNetworkKernel.Write(ref writer, value.Bits, 2, profile, FixedRuntime.State, out status);
        public static bool TryWrite(ref FixedBufferWriter writer, in FixedVector3 value, FixedVectorNetworkProfile profile, out FixedStatus status) => FixedVectorNetworkKernel.Write(ref writer, value.Bits, 3, profile, FixedRuntime.State, out status);
        public static bool TryRead(ref FixedBufferReader reader, FixedVectorNetworkProfile profile, out FixedVector2 value, out FixedStatus status)
        { bool success = FixedVectorNetworkKernel.Read(ref reader, 2, profile, FixedRuntime.State, out FixedVectorBits bits, out status); value = FixedVector2.FromBits(bits); return success; }
        public static bool TryRead(ref FixedBufferReader reader, FixedVectorNetworkProfile profile, out FixedVector3 value, out FixedStatus status)
        { bool success = FixedVectorNetworkKernel.Read(ref reader, 3, profile, FixedRuntime.State, out FixedVectorBits bits, out status); value = FixedVector3.FromBits(bits); return success; }
        public static FixedVector2 Canonicalize(in FixedVector2 value, FixedVectorNetworkProfile profile, out FixedStatus status) => FixedVector2.FromBits(FixedVectorNetworkKernel.Canonicalize(value.Bits, 2, profile, FixedRuntime.State, out status));
        public static FixedVector3 Canonicalize(in FixedVector3 value, FixedVectorNetworkProfile profile, out FixedStatus status) => FixedVector3.FromBits(FixedVectorNetworkKernel.Canonicalize(value.Bits, 3, profile, FixedRuntime.State, out status));
        private static void Align(ref FixedBufferWriter writer)
        { int padding = (int)((8 - (writer.BitPosition & 7)) & 7); if (padding != 0) writer.WriteBits(0, padding); }
        public static bool TryWrite(Span<byte> buffer, ref int byteOffset, in FixedVector2 value, FixedVectorNetworkProfile profile, out FixedStatus status)
        { fixed (byte* pointer = buffer) { var writer = new FixedBufferWriter(pointer, buffer.Length, (long)byteOffset * 8); if (!TryWrite(ref writer, value, profile, out status)) return false; Align(ref writer); byteOffset = writer.BytesWritten; return true; } }
        public static bool TryWrite(Span<byte> buffer, ref int byteOffset, in FixedVector3 value, FixedVectorNetworkProfile profile, out FixedStatus status)
        { fixed (byte* pointer = buffer) { var writer = new FixedBufferWriter(pointer, buffer.Length, (long)byteOffset * 8); if (!TryWrite(ref writer, value, profile, out status)) return false; Align(ref writer); byteOffset = writer.BytesWritten; return true; } }
        public static bool TryRead(ReadOnlySpan<byte> buffer, ref int byteOffset, FixedVectorNetworkProfile profile, out FixedVector2 value, out FixedStatus status)
        { fixed (byte* pointer = buffer) { var reader = new FixedBufferReader(pointer, buffer.Length, (long)byteOffset * 8); if (!TryRead(ref reader, profile, out value, out status)) return false; byteOffset = (int)((reader.BitPosition + 7) / 8); return true; } }
        public static bool TryRead(ReadOnlySpan<byte> buffer, ref int byteOffset, FixedVectorNetworkProfile profile, out FixedVector3 value, out FixedStatus status)
        { fixed (byte* pointer = buffer) { var reader = new FixedBufferReader(pointer, buffer.Length, (long)byteOffset * 8); if (!TryRead(ref reader, profile, out value, out status)) return false; byteOffset = (int)((reader.BitPosition + 7) / 8); return true; } }
        public static bool TryWriteDescriptor(Span<byte> buffer, ref int byteOffset, FixedVectorNetworkProfile profile, out FixedStatus status)
        { fixed (byte* pointer = buffer) { var writer = new FixedBufferWriter(pointer, buffer.Length, (long)byteOffset * 8); if (!TryWriteDescriptor(ref writer, profile, out status)) return false; byteOffset = writer.BytesWritten; return true; } }
        public static bool TryReadDescriptor(ReadOnlySpan<byte> buffer, ref int byteOffset, out FixedVectorNetworkProfile profile, out FixedStatus status)
        { fixed (byte* pointer = buffer) { var reader = new FixedBufferReader(pointer, buffer.Length, (long)byteOffset * 8); if (!TryReadDescriptor(ref reader, out profile, out status)) return false; byteOffset = (int)(reader.BitPosition / 8); return true; } }
    }
}
