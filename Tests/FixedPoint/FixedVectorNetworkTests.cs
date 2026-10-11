using System;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed unsafe class FixedVectorNetworkTests
    {
        [OneTimeSetUp] public void Initialize() => FixedRuntime.Initialize();
        private static FixedVectorNetworkProfile Profile(FixedCodec codec, int dimensions = 3) => new FixedVectorNetworkProfile(
            new FixedNetworkProfile(FixedRuntime.Settings, FixedKind.Signed64, codec, lowerRaw: codec == FixedCodec.BitPacked ? unchecked((ulong)-2L) : 0,
                upperRaw: codec == FixedCodec.BitPacked ? 2UL : 0), dimensions);
        [TestCase(FixedCodec.FixedWidth)] [TestCase(FixedCodec.VarInt)] [TestCase(FixedCodec.BitPacked)]
        public void VectorRoundTripsAndGoldenBytesAreTransportIndependent(FixedCodec codec)
        {
            var p = Profile(codec); var value = FixedVector3.FromRaw(1, -1, 0);
            byte[] buffer = new byte[30]; int offset = 0;
            Assert.IsTrue(FixedVectorNetwork.TryWrite(buffer.AsSpan(), ref offset, value, p, out _));
            int length = offset;
            if (codec == FixedCodec.FixedWidth) { Assert.AreEqual(24, length); Assert.AreEqual(1, buffer[0]); Assert.AreEqual(255, buffer[8]); Assert.AreEqual(255, buffer[15]); }
            if (codec == FixedCodec.VarInt) { Assert.AreEqual(3, length); CollectionAssert.AreEqual(new byte[] { 6, 5, 4 }, buffer.AsSpan(0, length).ToArray()); }
            if (codec == FixedCodec.BitPacked) { Assert.AreEqual(2, length); CollectionAssert.AreEqual(new byte[] { 0x57, 0x06 }, buffer.AsSpan(0, length).ToArray()); }
            offset = 0; Assert.IsTrue(FixedVectorNetwork.TryRead(buffer.AsSpan(0, length), ref offset, p, out FixedVector3 decoded, out _));
            Assert.AreEqual(value, decoded); Assert.AreEqual(length, offset);
        }
        [Test] public void FailuresInTheLastCoordinatePreserveBufferAndCursor()
        {
            byte[] buffer = { 0xA5, 0xA5, 0xA5 }; int offset = 0;
            Assert.IsFalse(FixedVectorNetwork.TryWrite(buffer.AsSpan(), ref offset, FixedVector3.FromRaw(0, 0, 3), Profile(FixedCodec.BitPacked), out FixedStatus status));
            Assert.AreEqual(FixedStatus.OutOfRange, status); Assert.AreEqual(0, offset);
            CollectionAssert.AreEqual(new byte[] { 0xA5, 0xA5, 0xA5 }, buffer);
            Assert.IsFalse(FixedVectorNetwork.TryWrite(buffer.AsSpan(0, 2), ref offset, FixedVector3.zero, Profile(FixedCodec.VarInt), out status));
            Assert.AreEqual(FixedStatus.BufferTooSmall, status); CollectionAssert.AreEqual(new byte[] { 0xA5, 0xA5, 0xA5 }, buffer);
            buffer[0] = buffer[1] = 4;
            Assert.IsFalse(FixedVectorNetwork.TryRead(buffer.AsSpan(0, 2), ref offset, Profile(FixedCodec.VarInt), out FixedVector3 decoded, out status));
            Assert.AreEqual(0, offset); Assert.AreEqual(FixedStatus.BufferTooSmall, status); Assert.IsTrue(FixedVector3.IsNaN(decoded));
            buffer[2] = 0x80;
            Assert.IsFalse(FixedVectorNetwork.TryRead(buffer.AsSpan(), ref offset, Profile(FixedCodec.VarInt), out decoded, out status));
            Assert.AreEqual(0, offset);
        }
        [Test] public void BitPackingContinuesAcrossVectorsWithoutComponentPadding()
        {
            byte* buffer = stackalloc byte[8]; for (int i = 0; i < 8; i++) buffer[i] = 0xA5;
            var writer = new FixedBufferWriter(buffer, 8, 3); var p = Profile(FixedCodec.BitPacked);
            Assert.IsTrue(FixedVectorNetwork.TryWrite(ref writer, FixedVector3.FromRaw(1, -1, 0), p, out _));
            Assert.IsTrue(FixedVectorNetwork.TryWrite(ref writer, FixedVector3.FromRaw(2, -2, 1), p, out _));
            Assert.AreEqual(27, writer.BitPosition); Assert.AreEqual(5, buffer[0] & 7);
            var reader = new FixedBufferReader(buffer, 8, 3);
            Assert.IsTrue(FixedVectorNetwork.TryRead(ref reader, p, out FixedVector3 first, out _));
            Assert.IsTrue(FixedVectorNetwork.TryRead(ref reader, p, out FixedVector3 second, out _));
            Assert.AreEqual(FixedVector3.FromRaw(1, -1, 0), first); Assert.AreEqual(FixedVector3.FromRaw(2, -2, 1), second);
            Assert.AreEqual(27, reader.BitPosition);
        }
        [Test] public void DescriptorNegotiationValidatesDimensionCodecAndConfiguration()
        {
            byte[] buffer = new byte[128]; int offset = 0; var p = Profile(FixedCodec.VarInt);
            Assert.IsTrue(FixedVectorNetwork.TryWriteDescriptor(buffer, ref offset, p, out _)); Assert.AreEqual(128, offset);
            offset = 0; Assert.IsTrue(FixedVectorNetwork.TryReadDescriptor(buffer, ref offset, out FixedVectorNetworkProfile decoded, out _));
            Assert.IsTrue(FixedVectorNetwork.TryNegotiate(p, decoded, out _));
            Assert.IsFalse(FixedVectorNetwork.TryNegotiate(p, Profile(FixedCodec.VarInt, 2), out _));
            buffer[7] = 1; offset = 0;
            Assert.IsFalse(FixedVectorNetwork.TryReadDescriptor(buffer, ref offset, out _, out FixedStatus status));
            Assert.AreEqual(0, offset); Assert.AreEqual(FixedStatus.InvalidEncoding, status);
            var mixed = new FixedVectorNetworkProfile(p.X, new FixedNetworkProfile(FixedRuntime.Settings, FixedKind.Signed64, FixedCodec.FixedWidth), p.Z);
            Assert.IsFalse(mixed.IsValid);
        }
        [Test] public void LossyQuantizationIsExplicitAndCanonicalizationIsIdempotent()
        {
            var component = new FixedNetworkProfile(FixedRuntime.Settings, FixedKind.Signed64, wireDecimalPlaces: 1, allowLossy: true);
            var p = new FixedVectorNetworkProfile(component, 3); var input = FixedVector3.FromRaw(1234, -1250, 1299);
            FixedVector3 canonical = FixedVectorNetwork.Canonicalize(input, p, out FixedStatus status);
            Assert.AreEqual(FixedStatus.Inexact, status); Assert.AreEqual(FixedVector3.FromRaw(1200, -1200, 1300), canonical);
            Assert.AreEqual(canonical, FixedVectorNetwork.Canonicalize(canonical, p, out _));
            byte[] buffer = new byte[30]; int offset = 0;
            Assert.IsTrue(FixedVectorNetwork.TryWrite(buffer, ref offset, input, p, out status)); Assert.AreEqual(FixedStatus.Inexact, status);
            offset = 0; Assert.IsTrue(FixedVectorNetwork.TryRead(buffer, ref offset, p, out FixedVector3 decoded, out _)); Assert.AreEqual(canonical, decoded);
            Assert.IsFalse(new FixedNetworkProfile(FixedRuntime.Settings, FixedKind.Signed64, wireDecimalPlaces: 1).IsValid);
        }
        [Test] public void ExactCodecsPreserveSpecialRawValuesAndWorstCaseVarInts()
        {
            foreach (FixedCodec codec in new[] { FixedCodec.VarInt, FixedCodec.FixedWidth })
            {
                var p = Profile(codec); byte[] buffer = new byte[30]; int offset = 0;
                var value = new FixedVector3(Fixed64.NaN, Fixed64.NegativeZero, Fixed64.PositiveInfinity);
                Assert.IsTrue(FixedVectorNetwork.TryWrite(buffer, ref offset, value, p, out _)); offset = 0;
                Assert.IsTrue(FixedVectorNetwork.TryRead(buffer, ref offset, p, out FixedVector3 decoded, out _));
                Assert.AreEqual(value.x.RawValue, decoded.x.RawValue); Assert.AreEqual(value.y.RawValue, decoded.y.RawValue); Assert.AreEqual(value.z.RawValue, decoded.z.RawValue);
                value = new FixedVector3(Fixed64.MaxValue, Fixed64.MinValue, Fixed64.MaxValue); offset = 0;
                Assert.IsTrue(FixedVectorNetwork.TryWrite(buffer, ref offset, value, p, out _));
                Assert.AreEqual(codec == FixedCodec.VarInt ? 30 : 24, offset);
            }
        }
    }
}
