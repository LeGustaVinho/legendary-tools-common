using System;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed unsafe class FixedQuaternionNetworkTests
    {
        [OneTimeSetUp] public void Initialize() => FixedRuntime.Initialize();
        private static void RawEqual(in FixedQuaternion a, in FixedQuaternion b)
        { for (int i = 0; i < 4; i++) Assert.AreEqual(a[i].RawValue, b[i].RawValue); }
        [TestCase(FixedCodec.FixedWidth)] [TestCase(FixedCodec.VarInt)] [TestCase(FixedCodec.BitPacked)]
        public void ExactCodecsPreserveEveryRepresentationAndPadding(FixedCodec codec)
        {
            var p = codec == FixedCodec.BitPacked ? new FixedQuaternionNetworkProfile(FixedRuntime.Settings, codec, lowerRaw: unchecked((ulong)-1000L), upperRaw: 1000) : new FixedQuaternionNetworkProfile(FixedRuntime.Settings, codec);
            var q = new FixedQuaternion(Fixed64.NegativeZero, Fixed64.NaN, Fixed64.PositiveInfinity, Fixed64.NegativeInfinity);
            var bytes = new byte[40]; Array.Fill(bytes, (byte)0xa5); int offset = 0;
            Assert.IsTrue(FixedQuaternionNetwork.TryWrite(bytes, ref offset, q, p, out var status)); Assert.IsTrue(status.IsSuccess());
            int length = offset; offset = 0;
            Assert.IsTrue(FixedQuaternionNetwork.TryRead(bytes.AsSpan(0, length), ref offset, p, out var result, out status));
            Assert.AreEqual(length, offset); RawEqual(q, result);
            q = FixedQuaternion.FromRaw(-999, 1, 2, 1000); offset = 0;
            Assert.IsTrue(FixedQuaternionNetwork.TryWrite(bytes, ref offset, q, p, out status)); length = offset; offset = 0;
            Assert.IsTrue(FixedQuaternionNetwork.TryRead(bytes.AsSpan(0, length), ref offset, p, out result, out status)); RawEqual(q, result);
            if (codec == FixedCodec.FixedWidth) Assert.AreEqual(32, length);
            if (codec == FixedCodec.BitPacked) { Assert.AreEqual(6, length); Assert.AreEqual(0, bytes[5] & 0xf0); bytes[5] |= 0x80; offset = 0; Assert.IsFalse(FixedQuaternionNetwork.TryRead(bytes, ref offset, p, out result, out status)); Assert.AreEqual(0, offset); Assert.IsTrue(FixedQuaternion.IsNaN(result)); }
        }
        [Test] public void FixedWidthGoldenBytesAreExplicitLittleEndianFields()
        {
            var q = FixedQuaternion.FromRaw(1, -2, 0x12345678, -1); var bytes = new byte[32]; int cursor = 0;
            Assert.IsTrue(FixedQuaternionNetwork.TryWrite(bytes, ref cursor, q, new FixedQuaternionNetworkProfile(FixedRuntime.Settings), out _));
            Assert.AreEqual(1, bytes[0]); for (int i = 1; i < 8; i++) Assert.AreEqual(0, bytes[i]);
            Assert.AreEqual(0xfe, bytes[8]); for (int i = 9; i < 16; i++) Assert.AreEqual(0xff, bytes[i]);
            Assert.AreEqual(0x78, bytes[16]); Assert.AreEqual(0x56, bytes[17]); Assert.AreEqual(0x34, bytes[18]); Assert.AreEqual(0x12, bytes[19]);
            for (int i = 24; i < 32; i++) Assert.AreEqual(0xff, bytes[i]);
        }
        [TestCase(32)] [TestCase(48)] [TestCase(64)]
        public void SmallestThreeHasExpectedSizeHemisphereAndIdentityGoldenBytes(int totalBits)
        {
            Assert.IsFalse(FixedQuaternionNetworkProfile.SmallestThree(FixedRuntime.Settings, totalBits).IsValid);
            var p = FixedQuaternionNetworkProfile.SmallestThree(FixedRuntime.Settings, totalBits, true);
            var bytes = new byte[8]; int offset = 0;
            Assert.IsTrue(FixedQuaternionNetwork.TryWrite(bytes, ref offset, FixedQuaternion.identity, p, out var status));
            Assert.AreEqual(totalBits / 8, offset); Assert.AreEqual(FixedStatus.Inexact, status);
            Assert.AreEqual(3, bytes[0]); for (int i = 1; i < offset; i++) Assert.AreEqual(0, bytes[i]);
            var negative = new byte[8]; int n = 0;
            Assert.IsTrue(FixedQuaternionNetwork.TryWrite(negative, ref n, -FixedQuaternion.identity, p, out status)); CollectionAssert.AreEqual(bytes, negative);
            offset = 0; Assert.IsTrue(FixedQuaternionNetwork.TryRead(bytes.AsSpan(0, n), ref offset, p, out var result, out status)); RawEqual(FixedQuaternion.identity, result);
            var q = FixedQuaternion.Euler(31, -46, 89); offset = 0;
            Assert.IsTrue(FixedQuaternionNetwork.TryWrite(bytes, ref offset, q, p, out status)); offset = 0;
            Assert.IsTrue(FixedQuaternionNetwork.TryRead(bytes, ref offset, p, out result, out status));
            Assert.IsTrue(FixedQuaternion.ApproximatelyRotation(q, result, Fixed64.FromRatio(1, 2)));
        }
        [Test] public void CompactDecoderRejectsReservedCodesInvalidSphereAndPadding()
        {
            byte* bytes = stackalloc byte[8];
            foreach (int totalBits in new[] { 32, 48, 64 })
            {
                var p = FixedQuaternionNetworkProfile.SmallestThree(FixedRuntime.Settings, totalBits, true); int b = p.ComponentBits;
                var w = new FixedBufferWriter(bytes, 8); w.WriteBits(3, 2); w.WriteBits(1UL << (b - 1), b); w.WriteBits(0, b * 2 + totalBits - 2 - b * 3);
                var r = new FixedBufferReader(bytes, totalBits / 8); Assert.IsFalse(FixedQuaternionNetwork.TryRead(ref r, p, out _, out var status)); Assert.AreEqual(FixedStatus.InvalidEncoding, status); Assert.AreEqual(0, r.BitPosition);
                w = new FixedBufferWriter(bytes, 8); w.WriteBits(3, 2); for (int i = 0; i < 3; i++) w.WriteBits((1UL << (b - 1)) - 1, b); w.WriteBits(0, totalBits - 2 - b * 3);
                r = new FixedBufferReader(bytes, totalBits / 8); Assert.IsFalse(FixedQuaternionNetwork.TryRead(ref r, p, out _, out status)); Assert.AreEqual(FixedStatus.InvalidEncoding, status);
                if (totalBits > 32)
                {
                    w = new FixedBufferWriter(bytes, 8); w.WriteBits(3, 2); w.WriteBits(0, b * 3); w.WriteBits(1, totalBits - 2 - b * 3);
                    r = new FixedBufferReader(bytes, totalBits / 8); Assert.IsFalse(FixedQuaternionNetwork.TryRead(ref r, p, out _, out status)); Assert.AreEqual(FixedStatus.InvalidEncoding, status);
                }
            }
        }
        [Test] public void DescriptorRoundTripRestoresNormalizationWithoutChangingScalarFormats()
        {
            foreach (var mode in new[] { FixedQuaternionNormalizationMode.None, FixedQuaternionNormalizationMode.NormalizeResult })
            foreach (var codec in new[] { FixedQuaternionCodec.Components, FixedQuaternionCodec.SmallestThree })
            {
                var s = new FixedSettings(decimalPlaces64: 6, quaternionNormalization: mode);
                var p = codec == FixedQuaternionCodec.Components ? new FixedQuaternionNetworkProfile(s) : FixedQuaternionNetworkProfile.SmallestThree(s, allowLossy: true);
                var bytes = new byte[p.DescriptorBytes]; int offset = 0;
                Assert.IsTrue(FixedQuaternionNetwork.TryWriteDescriptor(bytes, ref offset, p, out var status)); Assert.AreEqual(bytes.Length, offset);
                offset = 0; Assert.IsTrue(FixedQuaternionNetwork.TryReadDescriptor(bytes, ref offset, out var other, out status)); Assert.IsTrue(p.IsCompatible(other)); Assert.AreEqual(p.SchemaFingerprint, other.SchemaFingerprint);
                for (int length = 0; length < bytes.Length; length++) { offset = 0; Assert.IsFalse(FixedQuaternionNetwork.TryReadDescriptor(bytes.AsSpan(0, length), ref offset, out _, out status)); Assert.AreEqual(0, offset); }
                bytes[8]++; offset = 0; Assert.IsFalse(FixedQuaternionNetwork.TryReadDescriptor(bytes, ref offset, out _, out status)); Assert.AreEqual(FixedStatus.ProfileMismatch, status);
            }
        }
        [Test] public void FailedWritesPreserveBytesAndCursorForEveryEncoding()
        {
            foreach (var p in new[] { new FixedQuaternionNetworkProfile(FixedRuntime.Settings), new FixedQuaternionNetworkProfile(FixedRuntime.Settings, FixedCodec.VarInt), FixedQuaternionNetworkProfile.SmallestThree(FixedRuntime.Settings, allowLossy: true) })
            {
                var bytes = new byte[40]; int offset = 0; Assert.IsTrue(FixedQuaternionNetwork.TryWrite(bytes, ref offset, FixedQuaternion.Euler(11,22,33), p, out _)); int required = offset;
                for (int length = 0; length < required; length++)
                {
                    var shortBytes = new byte[length]; Array.Fill(shortBytes, (byte)0xa5); offset = 0;
                    Assert.IsFalse(FixedQuaternionNetwork.TryWrite(shortBytes, ref offset, FixedQuaternion.Euler(11,22,33), p, out var status)); Assert.AreEqual(0, offset); Assert.IsTrue((status & FixedStatus.BufferTooSmall) != 0);
                    for (int i = 0; i < length; i++) Assert.AreEqual(0xa5, shortBytes[i]);
                    offset = 0; Assert.IsFalse(FixedQuaternionNetwork.TryRead(bytes.AsSpan(0, length), ref offset, p, out _, out status)); Assert.AreEqual(0, offset);
                }
            }
        }
        [Test] public void CompactAllPrecisionsAndRoundingsDecodeTheirEncodedDominantTies()
        {
            byte* bytes = stackalloc byte[8];
            foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
            foreach (int precision in new[] { 3, 6, 9, 16, 18 })
            foreach (int totalBits in new[] { 32, 48, 64 })
            {
                var s = new FixedSettings(decimalPlaces64: precision, rounding: rounding); var state = new FixedRuntimeState(s);
                var p = FixedQuaternionNetworkProfile.SmallestThree(s, totalBits, true);
                var half = state.Scale64 / 2; var q = new FixedQuaternionBits(half, half, half, half);
                var w = new FixedBufferWriter(bytes, 8); Assert.IsTrue(FixedQuaternionNetworkKernel.Write(ref w, q, p, state, out var status), rounding + "/p=" + precision);
                var r = new FixedBufferReader(bytes, totalBits / 8); Assert.IsTrue(FixedQuaternionNetworkKernel.Read(ref r, p, state, out var result, out status), rounding + "/p=" + precision);
                Assert.IsTrue(status.IsSuccess()); Assert.IsTrue(FixedQuaternionKernel.Finite(result, state));
            }
        }
        [Test] public void BitCursorsSupportConcatenationAndNumericMismatchesAreExplicit()
        {
            byte* bytes = stackalloc byte[20]; var p = FixedQuaternionNetworkProfile.SmallestThree(FixedRuntime.Settings, allowLossy: true);
            var w = new FixedBufferWriter(bytes, 20, 3); Assert.IsTrue(FixedQuaternionNetwork.TryWrite(ref w, FixedQuaternion.identity, p, out _)); Assert.IsTrue(FixedQuaternionNetwork.TryWrite(ref w, FixedQuaternion.identity, p, out _)); Assert.AreEqual(99, w.BitPosition);
            var r = new FixedBufferReader(bytes, 20, 3); Assert.IsTrue(FixedQuaternionNetwork.TryRead(ref r, p, out _, out _)); Assert.IsTrue(FixedQuaternionNetwork.TryRead(ref r, p, out _, out _)); Assert.AreEqual(99, r.BitPosition);
            var wrong = new FixedQuaternionNetworkProfile(new FixedSettings(quaternionNormalization: FixedQuaternionNormalizationMode.NormalizeResult));
            Assert.IsFalse(FixedQuaternionNetwork.TryWrite(ref w, FixedQuaternion.identity, wrong, out var status)); Assert.AreEqual(FixedStatus.ProfileMismatch, status); Assert.AreEqual(99, w.BitPosition);
        }
    }
}
