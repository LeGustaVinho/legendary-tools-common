using System;
using System.Globalization;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed unsafe class FixedNetworkGoldenTests
    {
        private static byte[] Hex(string text)
        {
            string[] parts = text.Split(' '); var bytes = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++) bytes[i] = byte.Parse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return bytes;
        }
        private static void Vector(ulong raw, string hex, FixedNetworkProfile profile, FixedRuntimeState state)
        {
            byte[] expected = Hex(hex); byte* buffer = stackalloc byte[16];
            for (int i = 0; i < 16; i++) buffer[i] = 0xa5;
            var writer = new FixedBufferWriter(buffer, 16);
            Assert.IsTrue(FixedNetworkKernel.Write(ref writer, raw, profile, state, out FixedStatus status));
            Assert.AreEqual(FixedStatus.None, status); Assert.AreEqual(expected.Length, writer.BytesWritten);
            for (int i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], buffer[i], hex + " byte " + i);
            for (int i = expected.Length; i < 16; i++) Assert.AreEqual(0xa5, buffer[i]);
            for (int length = 0; length < expected.Length; length++)
            {
                var truncated = new FixedBufferReader(buffer, length);
                Assert.IsFalse(FixedNetworkKernel.Read(ref truncated, profile, state, out _, out status));
                Assert.AreEqual(FixedStatus.BufferTooSmall, status); Assert.AreEqual(0, truncated.BitPosition);
            }
            var reader = new FixedBufferReader(buffer, expected.Length);
            Assert.IsTrue(FixedNetworkKernel.Read(ref reader, profile, state, out ulong decoded, out status));
            Assert.AreEqual(raw, decoded); Assert.AreEqual(expected.Length * 8, reader.BitPosition);
            for (int length = 0; length < expected.Length; length++)
            {
                for (int i = 0; i < 16; i++) buffer[i] = 0xa5;
                var shortWriter = new FixedBufferWriter(buffer, length);
                Assert.IsFalse(FixedNetworkKernel.Write(ref shortWriter, raw, profile, state, out status));
                Assert.AreEqual(FixedStatus.BufferTooSmall, status); Assert.AreEqual(0, shortWriter.BitPosition);
                for (int i = 0; i < 16; i++) Assert.AreEqual(0xa5, buffer[i], "Failed write mutated byte " + i);
            }
        }

        [TestCase(FixedKind.Signed32)]
        [TestCase(FixedKind.Unsigned32)]
        [TestCase(FixedKind.Signed64)]
        [TestCase(FixedKind.Unsigned64)]
        public void LiteralVarIntAndLittleEndianFixturesIncludeExtremesAndEveryTruncation(FixedKind kind)
        {
            var state = new FixedRuntimeState(FixedSettings.Default); var f = new FixedFormat(kind, state);
            var profile = new FixedNetworkProfile(state.Settings, kind);
            Vector(f.NaN, "00", profile, state); Vector(f.NegativeInfinity, "01", profile, state);
            Vector(f.PositiveInfinity, "02", profile, state); Vector(f.NegativeZero, "03", profile, state);
            Vector(0, "04", profile, state);
            if (f.Unsigned)
            {
                Vector(1, "05", profile, state); Vector(123, "7f", profile, state); Vector(124, "80 01", profile, state);
                Vector(f.MaxPositive, f.Bits == 32 ? "ff ff ff ff 0f" : "ff ff ff ff ff ff ff ff ff 01", profile, state);
            }
            else
            {
                Vector(1, "06", profile, state); Vector(f.Finite(1, true), "05", profile, state);
                Vector(61, "7e", profile, state); Vector(62, "80 01", profile, state);
                Vector(f.Finite(62, true), "7f", profile, state); Vector(f.Finite(63, true), "81 01", profile, state);
                Vector(f.MaxPositive, f.Bits == 32 ? "fe ff ff ff 0f" : "fe ff ff ff ff ff ff ff ff 01", profile, state);
                Vector(f.MinValue, f.Bits == 32 ? "ff ff ff ff 0f" : "ff ff ff ff ff ff ff ff ff 01", profile, state);
            }
            var fixedWidth = new FixedNetworkProfile(state.Settings, kind, FixedCodec.FixedWidth);
            Vector(0x12345678, f.Bits == 32 ? "78 56 34 12" : "78 56 34 12 00 00 00 00", fixedWidth, state);
            Vector(f.NaN, f.Unsigned ? f.Bits == 32 ? "ff ff ff ff" : "ff ff ff ff ff ff ff ff" :
                f.Bits == 32 ? "00 00 00 80" : "00 00 00 00 00 00 00 80", fixedWidth, state);
            Vector(f.NegativeZero, f.Unsigned ? f.Bits == 32 ? "fc ff ff ff" : "fc ff ff ff ff ff ff ff" :
                f.Bits == 32 ? "fe ff ff 7f" : "fe ff ff ff ff ff ff 7f", fixedWidth, state);
        }

        [Test]
        public void PackedFixturesMatchLsbFirstBitsAtEveryStartingOffsetAndKeepGuardBits()
        {
            var state = new FixedRuntimeState(FixedSettings.Default); byte* buffer = stackalloc byte[4];
            foreach (FixedKind kind in Enum.GetValues(typeof(FixedKind)))
            {
                var f = new FixedFormat(kind, state);
                ulong lower = f.Unsigned ? f.Bits == 64 ? (ulong)long.MaxValue + 100 : 100 : f.Finite(3, true);
                ulong upper = f.Unsigned ? lower + 10 : 7;
                var profile = new FixedNetworkProfile(state.Settings, kind, FixedCodec.BitPacked, lowerRaw: lower, upperRaw: upper);
                Assert.AreEqual(4, profile.PackedBits);
                for (int start = 0; start < 8; start++)
                for (int code = 0; code <= 15; code++)
                {
                    for (int i = 0; i < 4; i++) buffer[i] = 0xa5;
                    byte[] expected = { 0xa5, 0xa5, 0xa5, 0xa5 };
                    for (int bit = 0; bit < 4; bit++)
                    {
                        int position = start + bit, mask = 1 << (position & 7);
                        expected[position / 8] = (byte)((expected[position / 8] & ~mask) | ((code >> bit & 1) != 0 ? mask : 0));
                    }
                    if (code == 15)
                    {
                        for (int i = 0; i < 4; i++) buffer[i] = expected[i];
                        var invalid = new FixedBufferReader(buffer, 4, start);
                        Assert.IsFalse(FixedNetworkKernel.Read(ref invalid, profile, state, out _, out FixedStatus status));
                        Assert.AreEqual(FixedStatus.InvalidEncoding, status); Assert.AreEqual(start, invalid.BitPosition);
                        continue;
                    }
                    ulong raw = code == 0 ? f.NaN : code == 1 ? f.NegativeInfinity : code == 2 ? f.PositiveInfinity :
                        code == 3 ? f.NegativeZero : unchecked(lower + (ulong)code - 4) & f.Mask;
                    var writer = new FixedBufferWriter(buffer, 4, start);
                    Assert.IsTrue(FixedNetworkKernel.Write(ref writer, raw, profile, state, out _));
                    for (int i = 0; i < 4; i++) Assert.AreEqual(expected[i], buffer[i], kind + " start=" + start + " code=" + code);
                    var reader = new FixedBufferReader(buffer, 4, start);
                    Assert.IsTrue(FixedNetworkKernel.Read(ref reader, profile, state, out ulong decoded, out _)); Assert.AreEqual(raw, decoded);
                }
            }
        }

        [Test]
        public void LossyWireExpansionOverflowAndShortDescriptorsPreserveCursors()
        {
            byte* buffer = stackalloc byte[48];
            foreach (FixedKind kind in Enum.GetValues(typeof(FixedKind)))
            foreach (FixedOverflow overflow in Enum.GetValues(typeof(FixedOverflow)))
            {
                var state = new FixedRuntimeState(new FixedSettings(3, 3, overflow: overflow)); var f = new FixedFormat(kind, state);
                var profile = new FixedNetworkProfile(state.Settings, kind, FixedCodec.FixedWidth, wireDecimalPlaces: 0, allowLossy: true);
                for (int i = 0; i < f.Bits / 8; i++) buffer[i] = (byte)(f.MaxPositive >> (i * 8));
                var reader = new FixedBufferReader(buffer, f.Bits / 8);
                Assert.IsFalse(FixedNetworkKernel.Read(ref reader, profile, state, out ulong raw, out FixedStatus status));
                Assert.AreEqual(FixedStatus.Overflow, status); Assert.AreEqual(f.NaN, raw); Assert.AreEqual(0, reader.BitPosition);
                var writer = new FixedBufferWriter(buffer, 48);
                Assert.IsTrue(FixedNetworkKernel.WriteDescriptor(ref writer, profile, out _));
                for (int length = 0; length < 40; length++)
                {
                    var shortReader = new FixedBufferReader(buffer, length);
                    Assert.IsFalse(FixedNetworkKernel.ReadDescriptor(ref shortReader, out _, out status));
                    Assert.AreEqual(FixedStatus.BufferTooSmall, status); Assert.AreEqual(0, shortReader.BitPosition);
                }
            }
        }
    }
}
