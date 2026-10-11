using System;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed unsafe class FixedNetworkTests
    {
        [Test]
        public void AllCodecsRoundTripEveryVariantAcrossFullRangeWithSharedBitCursors()
        {
            var state = new FixedRuntimeState(FixedSettings.Default);
            var random = new Random(32941); byte* buffer = stackalloc byte[32];
            foreach (FixedKind kind in Enum.GetValues(typeof(FixedKind)))
            {
                var f = new FixedFormat(kind, state);
                foreach (FixedCodec codec in Enum.GetValues(typeof(FixedCodec)))
                {
                    var profile = codec == FixedCodec.BitPacked ? new FixedNetworkProfile(state.Settings, kind, codec, lowerRaw: f.MinValue, upperRaw: f.MaxPositive) :
                        new FixedNetworkProfile(state.Settings, kind, codec);
                    Assert.IsTrue(profile.IsValid);
                    for (int i = 0; i < 600; i++)
                    {
                        ulong raw = i == 0 ? f.NaN : i == 1 ? f.PositiveInfinity : i == 2 ? f.NegativeInfinity : i == 3 ? f.NegativeZero :
                            i == 4 ? f.MinValue : i == 5 ? f.MaxPositive : i == 6 ? 0 :
                            f.Finite((((ulong)(uint)random.Next() << 33) ^ (uint)random.Next()) % f.MaxPositive, !f.Unsigned && (i & 1) != 0);
                        var writer = new FixedBufferWriter(buffer, 32, codec == FixedCodec.BitPacked ? 3 : 0);
                        Assert.IsTrue(FixedNetworkKernel.Write(ref writer, raw, profile, state, out _));
                        Assert.IsTrue(FixedNetworkKernel.Write(ref writer, raw, profile, state, out _));
                        var reader = new FixedBufferReader(buffer, writer.BytesWritten, codec == FixedCodec.BitPacked ? 3 : 0);
                        Assert.IsTrue(FixedNetworkKernel.Read(ref reader, profile, state, out ulong decoded, out _));
                        Assert.AreEqual(raw, decoded, kind + " " + codec);
                        Assert.IsTrue(FixedNetworkKernel.Read(ref reader, profile, state, out decoded, out _));
                        Assert.AreEqual(raw, decoded);
                        Assert.AreEqual(writer.BitPosition, reader.BitPosition);
                    }
                }
            }
        }

        [Test]
        public void VarIntIsCanonicalAndFailuresPreserveCursorAndBuffer()
        {
            var state = new FixedRuntimeState(FixedSettings.Default); byte* buffer = stackalloc byte[12];
            foreach (FixedKind kind in Enum.GetValues(typeof(FixedKind)))
            {
                var profile = new FixedNetworkProfile(state.Settings, kind);
                buffer[0] = 0x84; buffer[1] = 0;
                var reader = new FixedBufferReader(buffer, 2);
                Assert.IsFalse(FixedNetworkKernel.Read(ref reader, profile, state, out _, out FixedStatus status));
                Assert.AreEqual(FixedStatus.InvalidEncoding, status); Assert.AreEqual(0, reader.BitPosition);
                buffer[0] = 0x80; reader = new FixedBufferReader(buffer, 1);
                Assert.IsFalse(FixedNetworkKernel.Read(ref reader, profile, state, out _, out status));
                Assert.AreEqual(FixedStatus.BufferTooSmall, status); Assert.AreEqual(0, reader.BitPosition);
                for (int i = 0; i < 10; i++) buffer[i] = 255;
                reader = new FixedBufferReader(buffer, 10);
                Assert.IsFalse(FixedNetworkKernel.Read(ref reader, profile, state, out _, out status));
                Assert.AreEqual(FixedStatus.InvalidEncoding, status); Assert.AreEqual(0, reader.BitPosition);
                buffer[0] = 117;
                var writer = new FixedBufferWriter(buffer, 1);
                var f = new FixedFormat(kind, state);
                Assert.IsFalse(FixedNetworkKernel.Write(ref writer, f.MaxPositive, profile, state, out status));
                Assert.AreEqual(FixedStatus.BufferTooSmall, status); Assert.AreEqual(0, writer.BitPosition); Assert.AreEqual(117, buffer[0]);
            }
        }

        [Test]
        public void PackedUnsigned64BoundsRemainUnsignedAndUnusedCodesAreRejected()
        {
            var state = new FixedRuntimeState(new FixedSettings(0, 0));
            ulong min = (ulong)long.MaxValue + 100;
            var profile = new FixedNetworkProfile(state.Settings, FixedKind.Unsigned64, FixedCodec.BitPacked, lowerRaw: min, upperRaw: min + 10);
            Assert.AreEqual(4, profile.PackedBits);
            byte* buffer = stackalloc byte[1]; var writer = new FixedBufferWriter(buffer, 1);
            Assert.IsTrue(FixedNetworkKernel.Write(ref writer, min + 7, profile, state, out _)); Assert.AreEqual(11, buffer[0] & 15);
            var reader = new FixedBufferReader(buffer, 1);
            Assert.IsTrue(FixedNetworkKernel.Read(ref reader, profile, state, out ulong raw, out _)); Assert.AreEqual(min + 7, raw);
            writer = new FixedBufferWriter(buffer, 1);
            Assert.IsFalse(FixedNetworkKernel.Write(ref writer, min - 1, profile, state, out FixedStatus status));
            Assert.AreEqual(FixedStatus.OutOfRange, status); Assert.AreEqual(0, writer.BitPosition);
            buffer[0] = 15; reader = new FixedBufferReader(buffer, 1);
            Assert.IsFalse(FixedNetworkKernel.Read(ref reader, profile, state, out _, out status));
            Assert.AreEqual(FixedStatus.InvalidEncoding, status); Assert.AreEqual(0, reader.BitPosition);
        }

        [Test]
        public void DescriptorChecksVersionNumericPoliciesAndCanonicalLossyPrecision()
        {
            var state = new FixedRuntimeState(FixedSettings.Default);
            var lossy = new FixedNetworkProfile(state.Settings, FixedKind.Signed64, wireDecimalPlaces: 2, allowLossy: true);
            Assert.IsTrue(lossy.IsValid);
            Assert.IsFalse(new FixedNetworkProfile(state.Settings, FixedKind.Signed64, wireDecimalPlaces: 2).IsValid);
            Assert.AreEqual(1230UL, FixedNetworkKernel.Canonicalize(1234, lossy, state, out FixedStatus status));
            Assert.AreEqual(FixedStatus.Inexact, status);
            byte* buffer = stackalloc byte[64]; var writer = new FixedBufferWriter(buffer, 64);
            Assert.IsTrue(FixedNetworkKernel.WriteDescriptor(ref writer, lossy, out _)); Assert.AreEqual(40, writer.BytesWritten);
            var reader = new FixedBufferReader(buffer, 40);
            Assert.IsTrue(FixedNetworkKernel.ReadDescriptor(ref reader, out FixedNetworkProfile remote, out _));
            Assert.IsTrue(lossy.IsCompatible(remote));
            Assert.IsFalse(lossy.IsCompatible(new FixedNetworkProfile(new FixedSettings(4, 4), FixedKind.Signed64)));
            buffer[4] = 127; reader = new FixedBufferReader(buffer, 40);
            Assert.IsFalse(FixedNetworkKernel.ReadDescriptor(ref reader, out _, out status));
            Assert.AreEqual(FixedStatus.InvalidEncoding, status); Assert.AreEqual(0, reader.BitPosition);
            buffer[4] = 1; buffer[32] ^= 1; reader = new FixedBufferReader(buffer, 40);
            Assert.IsFalse(FixedNetworkKernel.ReadDescriptor(ref reader, out _, out status)); Assert.AreEqual(0, reader.BitPosition);
        }
    }
}
