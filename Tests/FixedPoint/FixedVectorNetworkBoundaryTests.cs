using System;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed unsafe class FixedVectorNetworkBoundaryTests
    {
        [OneTimeSetUp] public void Initialize() => FixedRuntime.Initialize();
        private static FixedVectorNetworkProfile Profile(FixedCodec codec, int dimensions) => new FixedVectorNetworkProfile(
            new FixedNetworkProfile(FixedRuntime.Settings, FixedKind.Signed64, codec,
                lowerRaw: codec == FixedCodec.BitPacked ? unchecked((ulong)-1L) : 0,
                upperRaw: codec == FixedCodec.BitPacked ? 1UL : 0), dimensions);
        private static void Fill(byte[] buffer) { for (int i = 0; i < buffer.Length; i++) buffer[i] = 0xA5; }
        private static void Put(byte[] buffer, int start, ulong code, int bits)
        {
            for (int i = 0; i < bits; i++)
            {
                int bit = start + i; byte mask = (byte)(1 << (bit & 7));
                buffer[bit >> 3] = (byte)((buffer[bit >> 3] & ~mask) | (((code >> i) & 1) != 0 ? mask : 0));
            }
        }
        [TestCase(2)] [TestCase(3)]
        public void EveryCombinationOfSpecialsPreservesItsRawCodesInAllCodecs(int dimensions)
        {
            ulong[] raw = { 0x8000000000000000UL, 0x8000000000000001UL, 0x7FFFFFFFFFFFFFFFUL, 0x7FFFFFFFFFFFFFFEUL, 0 };
            byte[] buffer = new byte[30]; var state = new FixedRuntimeState(FixedRuntime.Settings);
            foreach (FixedCodec codec in Enum.GetValues(typeof(FixedCodec)))
            for (int x = 0; x < raw.Length; x++) for (int y = 0; y < raw.Length; y++) for (int z = 0; z < (dimensions == 3 ? raw.Length : 1); z++)
            {
                var input = new FixedVectorBits(raw[x], raw[y], dimensions == 3 ? raw[z] : 0); var profile = Profile(codec, dimensions);
                fixed (byte* bytes = buffer)
                {
                    var writer = new FixedBufferWriter(bytes, buffer.Length);
                    Assert.IsTrue(FixedVectorNetworkKernel.Write(ref writer, input, dimensions, profile, state, out FixedStatus status)); Assert.AreEqual(FixedStatus.None, status);
                    var reader = new FixedBufferReader(bytes, writer.BytesWritten);
                    Assert.IsTrue(FixedVectorNetworkKernel.Read(ref reader, dimensions, profile, state, out FixedVectorBits decoded, out status)); Assert.AreEqual(FixedStatus.None, status);
                    Assert.AreEqual(input.X, decoded.X); Assert.AreEqual(input.Y, decoded.Y); Assert.AreEqual(input.Z, decoded.Z); Assert.AreEqual(writer.BitPosition, reader.BitPosition);
                }
            }
        }
        [TestCase(2, FixedCodec.VarInt)] [TestCase(3, FixedCodec.VarInt)]
        [TestCase(2, FixedCodec.FixedWidth)] [TestCase(3, FixedCodec.FixedWidth)]
        public void EveryPayloadTruncationIsAtomicAtANonzeroCursor(int dimensions, FixedCodec codec)
        {
            byte[] buffer = new byte[40]; var profile = Profile(codec, dimensions);
            var input = new FixedVectorBits((ulong)(long.MaxValue - 2), unchecked((ulong)(long.MinValue + 2)), (ulong)(long.MaxValue - 2));
            int payload = dimensions * (codec == FixedCodec.VarInt ? 10 : 8);
            var state = new FixedRuntimeState(FixedRuntime.Settings);
            fixed (byte* bytes = buffer)
            {
                for (int capacity = 0; capacity < payload + 1; capacity++)
                {
                    Fill(buffer); byte[] before = (byte[])buffer.Clone(); var writer = new FixedBufferWriter(bytes, capacity, 8);
                    Assert.IsFalse(FixedVectorNetworkKernel.Write(ref writer, input, dimensions, profile, state, out FixedStatus status));
                    Assert.AreEqual(FixedStatus.BufferTooSmall, status); Assert.AreEqual(8, writer.BitPosition); CollectionAssert.AreEqual(before, buffer);
                }
                var complete = new FixedBufferWriter(bytes, buffer.Length, 8);
                Assert.IsTrue(FixedVectorNetworkKernel.Write(ref complete, input, dimensions, profile, state, out _)); Assert.AreEqual(payload + 1, complete.BytesWritten);
                for (int length = 0; length < payload + 1; length++)
                {
                    var reader = new FixedBufferReader(bytes, length, 8);
                    Assert.IsFalse(FixedVectorNetworkKernel.Read(ref reader, dimensions, profile, state, out FixedVectorBits value, out FixedStatus status));
                    Assert.AreEqual(8, reader.BitPosition); Assert.AreEqual(FixedStatus.BufferTooSmall, status); Assert.AreEqual(FixedVectorKernel.NaN.X, value.X);
                }
                for (int offset = 1; offset < 8; offset++)
                {
                    Fill(buffer); byte[] before = (byte[])buffer.Clone(); var writer = new FixedBufferWriter(bytes, buffer.Length, offset);
                    Assert.IsFalse(FixedVectorNetworkKernel.Write(ref writer, input, dimensions, profile, state, out FixedStatus status));
                    Assert.AreEqual(FixedStatus.InvalidEncoding, status); Assert.AreEqual(offset, writer.BitPosition); CollectionAssert.AreEqual(before, buffer);
                    var reader = new FixedBufferReader(bytes, buffer.Length, offset);
                    Assert.IsFalse(FixedVectorNetworkKernel.Read(ref reader, dimensions, profile, state, out _, out status));
                    Assert.AreEqual(FixedStatus.InvalidEncoding, status); Assert.AreEqual(offset, reader.BitPosition);
                }
            }
        }
        [TestCase(2)] [TestCase(3)]
        public void MixedAxisPrecisionHasGoldenBitsAtEveryOffset(int dimensions)
        {
            var settings = FixedRuntime.Settings;
            var x = new FixedNetworkProfile(settings, FixedKind.Signed64, FixedCodec.BitPacked, 2, true, unchecked((ulong)-50L), 50);
            var y = new FixedNetworkProfile(settings, FixedKind.Signed64, FixedCodec.BitPacked, 1, true, unchecked((ulong)-12L), 17);
            var z = new FixedNetworkProfile(settings, FixedKind.Signed64, FixedCodec.BitPacked, 3, false, unchecked((ulong)-2L), 1);
            var profile = dimensions == 2 ? new FixedVectorNetworkProfile(x, y) : new FixedVectorNetworkProfile(x, y, z);
            Assert.AreEqual(dimensions == 2 ? 13 : 16, profile.PackedBits);
            byte[] buffer = new byte[8]; var state = new FixedRuntimeState(settings);
            for (int start = 0; start < 8; start++)
            {
                Fill(buffer); byte[] expected = (byte[])buffer.Clone(); Put(expected, start, 66, 7); Put(expected, start + 7, 4, 6);
                if (dimensions == 3) Put(expected, start + 13, 6, 3);
                fixed (byte* bytes = buffer)
                {
                    var writer = new FixedBufferWriter(bytes, buffer.Length, start);
                    Assert.IsTrue(FixedVectorNetworkKernel.Write(ref writer, new FixedVectorBits(123, unchecked((ulong)-1250L), 0), dimensions, profile, state, out FixedStatus status));
                    Assert.AreEqual(FixedStatus.Inexact, status); Assert.AreEqual(start + profile.PackedBits, writer.BitPosition); CollectionAssert.AreEqual(expected, buffer);
                    var reader = new FixedBufferReader(bytes, buffer.Length, start);
                    Assert.IsTrue(FixedVectorNetworkKernel.Read(ref reader, dimensions, profile, state, out FixedVectorBits value, out status));
                    Assert.AreEqual(FixedStatus.None, status); Assert.AreEqual(120, value.X); Assert.AreEqual(unchecked((ulong)-1200L), value.Y); Assert.AreEqual(0, value.Z);
                    Assert.AreEqual(writer.BitPosition, reader.BitPosition);
                }
            }
        }
        [TestCase(2)] [TestCase(3)]
        public void UnusedPackedCodesInEveryAxisPreserveTheReadCursor(int dimensions)
        {
            byte[] buffer = new byte[8]; var profile = Profile(FixedCodec.BitPacked, dimensions); var state = new FixedRuntimeState(FixedRuntime.Settings);
            for (int start = 0; start < 8; start++) for (int invalidAxis = 0; invalidAxis < dimensions; invalidAxis++)
            {
                Fill(buffer); for (int axis = 0; axis < dimensions; axis++) Put(buffer, start + 3 * axis, axis == invalidAxis ? 7UL : 5UL, 3);
                fixed (byte* bytes = buffer)
                {
                    var reader = new FixedBufferReader(bytes, buffer.Length, start);
                    Assert.IsFalse(FixedVectorNetworkKernel.Read(ref reader, dimensions, profile, state, out FixedVectorBits value, out FixedStatus status));
                    Assert.AreEqual(FixedStatus.InvalidEncoding, status); Assert.AreEqual(start, reader.BitPosition);
                    Assert.AreEqual(FixedVectorKernel.NaN.X, value.X); Assert.AreEqual(FixedVectorKernel.NaN.Y, value.Y);
                }
            }
        }
        [TestCase(2)] [TestCase(3)]
        public void EveryDescriptorTruncationAndReservedHeaderByteIsRejectedAtomically(int dimensions)
        {
            var profile = Profile(FixedCodec.VarInt, dimensions); byte[] buffer = new byte[140];
            fixed (byte* bytes = buffer)
            {
                for (int capacity = 0; capacity < profile.DescriptorBytes + 1; capacity++)
                {
                    Fill(buffer); byte[] before = (byte[])buffer.Clone(); var writer = new FixedBufferWriter(bytes, capacity, 8);
                    Assert.IsFalse(FixedVectorNetwork.TryWriteDescriptor(ref writer, profile, out _)); Assert.AreEqual(8, writer.BitPosition); CollectionAssert.AreEqual(before, buffer);
                }
                var complete = new FixedBufferWriter(bytes, buffer.Length, 8);
                Assert.IsTrue(FixedVectorNetwork.TryWriteDescriptor(ref complete, profile, out _));
                CollectionAssert.AreEqual(new byte[] { 0x46, 0x50, 0x56, 0x31, 1, (byte)dimensions, 0, 0 }, buffer.AsSpan(1, 8).ToArray());
                for (int length = 0; length < profile.DescriptorBytes + 1; length++)
                {
                    var reader = new FixedBufferReader(bytes, length, 8); Assert.IsFalse(FixedVectorNetwork.TryReadDescriptor(ref reader, out _, out _)); Assert.AreEqual(8, reader.BitPosition);
                }
                foreach (int offset in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
                {
                    byte before = buffer[offset]; buffer[offset] = offset == 6 ? (byte)4 : (byte)(before ^ 0x80);
                    var reader = new FixedBufferReader(bytes, buffer.Length, 8); Assert.IsFalse(FixedVectorNetwork.TryReadDescriptor(ref reader, out _, out _)); Assert.AreEqual(8, reader.BitPosition); buffer[offset] = before;
                }
            }
        }
    }
}
