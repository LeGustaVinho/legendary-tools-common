using System;

namespace LegendaryTools.FixedPoint
{
    public static unsafe class FixedQuaternionNetwork
    {
        public static bool TryNegotiate(FixedQuaternionNetworkProfile local, FixedQuaternionNetworkProfile remote, out FixedStatus status)
        { status = local.IsCompatible(remote) ? FixedStatus.None : FixedStatus.ProfileMismatch; return status.IsSuccess(); }
        public static bool TryWrite(ref FixedBufferWriter writer, in FixedQuaternion value, FixedQuaternionNetworkProfile profile, out FixedStatus status)
            => FixedQuaternionNetworkKernel.Write(ref writer, value.Bits, profile, FixedRuntime.State, out status);
        public static bool TryRead(ref FixedBufferReader reader, FixedQuaternionNetworkProfile profile, out FixedQuaternion value, out FixedStatus status)
        { bool ok = FixedQuaternionNetworkKernel.Read(ref reader, profile, FixedRuntime.State, out var bits, out status); value = FixedQuaternion.FromBits(bits); return ok; }
        public static bool TryWrite(Span<byte> buffer, ref int byteOffset, in FixedQuaternion value, FixedQuaternionNetworkProfile profile, out FixedStatus status)
        {
            fixed (byte* pointer = buffer)
            {
                var writer = new FixedBufferWriter(pointer, buffer.Length, (long)byteOffset * 8);
                if (!TryWrite(ref writer, value, profile, out status)) return false;
                int padding = (int)((8 - (writer.BitPosition & 7)) & 7);
                if (padding > 0) writer.WriteBits(0, padding);
                byteOffset = writer.BytesWritten; return true;
            }
        }
        public static bool TryRead(ReadOnlySpan<byte> buffer, ref int byteOffset, FixedQuaternionNetworkProfile profile, out FixedQuaternion value, out FixedStatus status)
        {
            fixed (byte* pointer = buffer)
            {
                var reader = new FixedBufferReader(pointer, buffer.Length, (long)byteOffset * 8);
                if (!TryRead(ref reader, profile, out value, out status)) return false;
                int padding = (int)((8 - (reader.BitPosition & 7)) & 7);
                if (padding > 0 && reader.ReadBits(padding) != 0) { value = FixedQuaternion.NaN; status |= FixedStatus.InvalidEncoding; return false; }
                byteOffset = (int)(reader.BitPosition / 8); return true;
            }
        }
        public static bool TryWriteDescriptor(ref FixedBufferWriter writer, FixedQuaternionNetworkProfile profile, out FixedStatus status) => FixedQuaternionNetworkKernel.WriteDescriptor(ref writer, profile, out status);
        public static bool TryReadDescriptor(ref FixedBufferReader reader, out FixedQuaternionNetworkProfile profile, out FixedStatus status) => FixedQuaternionNetworkKernel.ReadDescriptor(ref reader, out profile, out status);
        public static bool TryWriteDescriptor(Span<byte> buffer, ref int offset, FixedQuaternionNetworkProfile profile, out FixedStatus status)
        { fixed (byte* p = buffer) { var writer = new FixedBufferWriter(p, buffer.Length, (long)offset * 8); if (!TryWriteDescriptor(ref writer, profile, out status)) return false; offset = writer.BytesWritten; return true; } }
        public static bool TryReadDescriptor(ReadOnlySpan<byte> buffer, ref int offset, out FixedQuaternionNetworkProfile profile, out FixedStatus status)
        { fixed (byte* p = buffer) { var reader = new FixedBufferReader(p, buffer.Length, (long)offset * 8); if (!TryReadDescriptor(ref reader, out profile, out status)) return false; offset = (int)(reader.BitPosition / 8); return true; } }
        /// <summary>Projects once through the selected wire encoding. Keep encoded baselines for compact patches.</summary>
        public static FixedQuaternion Canonicalize(in FixedQuaternion value, FixedQuaternionNetworkProfile profile, out FixedStatus status)
        {
            byte* bytes = stackalloc byte[40]; var writer = new FixedBufferWriter(bytes, 40);
            if (!TryWrite(ref writer, value, profile, out status)) return FixedQuaternion.NaN;
            var reader = new FixedBufferReader(bytes, writer.BytesWritten);
            if (!TryRead(ref reader, profile, out var result, out var readStatus)) { status |= readStatus; return FixedQuaternion.NaN; }
            status |= readStatus; return result;
        }
    }
}
