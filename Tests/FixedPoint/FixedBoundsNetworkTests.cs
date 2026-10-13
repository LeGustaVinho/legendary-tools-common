using System;
using System.Numerics;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed unsafe class FixedBoundsNetworkTests
    {
        [OneTimeSetUp] public void Initialize()
        { FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize(); }
        private static FixedBoundsNetworkProfile Profile(FixedRuntimeState numeric, FixedBoundsRepresentation rep, int dimensions, FixedCodec codec)
        {
            var axis = new FixedNetworkProfile(numeric.Settings, FixedKind.Signed64, codec,
                lowerRaw: codec == FixedCodec.BitPacked ? unchecked((ulong)-1000L) : 0, upperRaw: codec == FixedCodec.BitPacked ? 1000UL : 0);
            var fields = new FixedVectorNetworkProfile(axis, dimensions); return new FixedBoundsNetworkProfile(rep, fields, fields);
        }
        private static void Same(FixedBoundsBits a, FixedBoundsBits b, int dimensions)
        { for (int i = 0; i < dimensions; i++) { Assert.AreEqual(a.First.Get(i), b.First.Get(i)); Assert.AreEqual(a.Second.Get(i), b.Second.Get(i)); } }
        [Test] public void EveryCodecPreservesStoredFieldsAcrossConfigurationsAndTruncationIsAtomic()
        {
            byte* bytes = stackalloc byte[80];
            byte* tiny = stackalloc byte[80];
            foreach (int dimension in new[] { 2, 3 }) foreach (int precision in new[] { 0, 3, 9, 18 })
            foreach (var rep in new[] { FixedBoundsRepresentation.MinMax, FixedBoundsRepresentation.CenterExtents })
            foreach (FixedCodec codec in Enum.GetValues(typeof(FixedCodec)))
            {
                var numeric = new FixedRuntimeState(new FixedSettings(3, precision)); var geometry = new FixedBoundsRuntimeState(new FixedBoundsSettings(rep));
                var profile = Profile(numeric, rep, dimension, codec);
                var first = new FixedVectorBits(unchecked((ulong)-21L), 3, unchecked((ulong)-17L)); var second = new FixedVectorBits(70, 20, 900);
                var box = new FixedBoundsBits(first, second); Assert.IsTrue(FixedBoundsKernel.Validate(box, dimension, numeric, geometry, out _));
                for (int i = 0; i < 80; i++) bytes[i] = 0xA5;
                var writer = new FixedBufferWriter(bytes, 80);
                Assert.IsTrue(FixedBoundsNetworkKernel.Write(ref writer, box, dimension, profile, numeric, geometry, out var status)); Assert.AreEqual(FixedStatus.None, status);
                if (codec == FixedCodec.FixedWidth)
                {
                    Assert.AreEqual(dimension * 16, writer.BytesWritten);
                    for (int field = 0; field < dimension * 2; field++) for (int b = 0; b < 8; b++)
                    { ulong raw = field < dimension ? first.Get(field) : second.Get(field - dimension); Assert.AreEqual((byte)(raw >> (b * 8)), bytes[field * 8 + b]); }
                }
                else if (codec == FixedCodec.BitPacked) Assert.AreEqual(profile.PackedBits, writer.BitPosition);
                var reader = new FixedBufferReader(bytes, writer.BytesWritten);
                Assert.IsTrue(FixedBoundsNetworkKernel.Read(ref reader, dimension, profile, numeric, geometry, out var decoded, out status)); Same(box, decoded, dimension); Assert.AreEqual(writer.BitPosition, reader.BitPosition);
                for (int prefix = 0; prefix < writer.BytesWritten; prefix++)
                {
                    var truncated = new FixedBufferReader(bytes, prefix); Assert.IsFalse(FixedBoundsNetworkKernel.Read(ref truncated, dimension, profile, numeric, geometry, out _, out status));
                    Assert.AreEqual(FixedStatus.BufferTooSmall, status); Assert.AreEqual(0, truncated.BitPosition);
                    for (int j = 0; j < 80; j++) tiny[j] = 0x5A;
                    var shortWriter = new FixedBufferWriter(tiny, prefix); Assert.IsFalse(FixedBoundsNetworkKernel.Write(ref shortWriter, box, dimension, profile, numeric, geometry, out status));
                    Assert.AreEqual(FixedStatus.BufferTooSmall, status); Assert.AreEqual(0, shortWriter.BitPosition); for (int j = 0; j < 80; j++) Assert.AreEqual(0x5A, tiny[j]);
                }
            }
        }
        [Test] public void BitPackedBoxesShareBytesAndSpanPaddingIsZero()
        {
            var numeric = FixedRuntime.State; var rep = FixedBoundsRuntime.Settings.Representation; var profile = Profile(numeric, rep, 3, FixedCodec.BitPacked);
            Assert.IsTrue(FixedBounds3.TryFromStorage(FixedVector3.FromRaw(1, 2, 3), FixedVector3.FromRaw(4, 5, 6), out var box, out _));
            byte* bytes = stackalloc byte[40]; for (int i = 0; i < 40; i++) bytes[i] = 0xA5;
            var writer = new FixedBufferWriter(bytes, 40, 5);
            Assert.IsTrue(FixedBoundsNetwork.TryWrite(ref writer, box, profile, out _)); Assert.IsTrue(FixedBoundsNetwork.TryWrite(ref writer, box, profile, out _));
            Assert.AreEqual(5 + profile.PackedBits * 2, writer.BitPosition); Assert.AreEqual(0xA5 & 31, bytes[0] & 31);
            var reader = new FixedBufferReader(bytes, writer.BytesWritten, 5);
            Assert.IsTrue(FixedBoundsNetwork.TryRead(ref reader, profile, out FixedBounds3 a, out _)); Assert.AreEqual(box, a);
            Assert.IsTrue(FixedBoundsNetwork.TryRead(ref reader, profile, out a, out _)); Assert.AreEqual(box, a); Assert.AreEqual(writer.BitPosition, reader.BitPosition);
            var packet = new byte[40]; for (int i = 0; i < packet.Length; i++) packet[i] = 0xA5;
            int offset = 0; Assert.IsTrue(FixedBoundsNetwork.TryWrite(packet.AsSpan(), ref offset, box, profile, out _));
            int used = profile.PackedBits & 7; if (used != 0) Assert.AreEqual(0, packet[offset - 1] >> used);
            int readOffset = 0; Assert.IsTrue(FixedBoundsNetwork.TryRead(packet.AsSpan(), ref readOffset, profile, out a, out _)); Assert.AreEqual(box, a); Assert.AreEqual(offset, readOffset);
        }
        [Test] public void ProfilesDescriptorsAndInvalidGeometryAreRejectedBeforePublication()
        {
            var numeric = FixedRuntime.State; var rep = FixedBoundsRuntime.Settings.Representation; var geometry = FixedBoundsRuntime.State;
            byte* bytes = stackalloc byte[300];
            var profile = Profile(numeric, rep, 3, FixedCodec.FixedWidth);
            var writer = new FixedBufferWriter(bytes, 300); Assert.IsTrue(FixedBoundsNetwork.TryWriteDescriptor(ref writer, profile, out _)); Assert.AreEqual(profile.DescriptorBytes, writer.BytesWritten);
            Assert.AreEqual((byte)'F', bytes[0]); Assert.AreEqual((byte)'P', bytes[1]); Assert.AreEqual((byte)'B', bytes[2]); Assert.AreEqual((byte)'1', bytes[3]);
            for (int prefix = 0; prefix < writer.BytesWritten; prefix++)
            { var shortReader = new FixedBufferReader(bytes, prefix); Assert.IsFalse(FixedBoundsNetwork.TryReadDescriptor(ref shortReader, out _, out _)); Assert.AreEqual(0, shortReader.BitPosition); }
            var reader = new FixedBufferReader(bytes, writer.BytesWritten); Assert.IsTrue(FixedBoundsNetwork.TryReadDescriptor(ref reader, out var decoded, out _)); Assert.IsTrue(profile.IsCompatible(decoded));
            foreach (int index in new[] { 0, 4, 5, 6, 7, 8 })
            { byte original = bytes[index]; bytes[index] = 0xFF; reader = new FixedBufferReader(bytes, writer.BytesWritten); Assert.IsFalse(FixedBoundsNetwork.TryReadDescriptor(ref reader, out _, out _)); Assert.AreEqual(0, reader.BitPosition); bytes[index] = original; }
            var otherRep = rep == FixedBoundsRepresentation.MinMax ? FixedBoundsRepresentation.CenterExtents : FixedBoundsRepresentation.MinMax;
            var incompatible = Profile(numeric, otherRep, 3, FixedCodec.FixedWidth);
            Assert.IsFalse(FixedBoundsNetwork.TryNegotiate(profile, incompatible, out var status)); Assert.AreEqual(FixedStatus.ProfileMismatch, status);
            writer = new FixedBufferWriter(bytes, 300); Assert.IsFalse(FixedBoundsNetwork.TryWrite(ref writer, default(FixedBounds3), incompatible, out status)); Assert.AreEqual(FixedStatus.ProfileMismatch, status); Assert.AreEqual(0, writer.BitPosition);
            for (int i = 0; i < 300; i++) bytes[i] = 0xA5;
            writer = new FixedBufferWriter(bytes, 300); Assert.IsFalse(FixedBoundsNetwork.TryWrite(ref writer, FixedBounds3.Invalid, profile, out status)); Assert.AreEqual(0, writer.BitPosition); for (int i = 0; i < 300; i++) Assert.AreEqual(0xA5, bytes[i]);
            writer = new FixedBufferWriter(bytes, 300); var invalidSecond = new FixedVectorBits(unchecked((ulong)-1L), 0, 0);
            Assert.IsTrue(FixedVectorNetworkKernel.Write(ref writer, default, 3, profile.First, numeric, out _));
            Assert.IsTrue(FixedVectorNetworkKernel.Write(ref writer, invalidSecond, 3, profile.Second, numeric, out _));
            reader = new FixedBufferReader(bytes, writer.BytesWritten); Assert.IsFalse(FixedBoundsNetworkKernel.Read(ref reader, 3, profile, numeric, geometry, out _, out status)); Assert.AreEqual(FixedStatus.InvalidEncoding, status); Assert.AreEqual(0, reader.BitPosition);
        }
        [Test] public void LossyTransmissionRequiresOptInAndCanonicalizationIsIdempotent()
        {
            var numeric = FixedRuntime.State;
            if (numeric.Settings.DecimalPlaces64 == 0) return;
            var forbidden = new FixedNetworkProfile(numeric.Settings, FixedKind.Signed64, wireDecimalPlaces: 0); Assert.IsFalse(forbidden.IsValid);
            var axis = new FixedNetworkProfile(numeric.Settings, FixedKind.Signed64, FixedCodec.VarInt, wireDecimalPlaces: 0, allowLossy: true);
            var profile = new FixedBoundsNetworkProfile(new FixedVectorNetworkProfile(axis, 3));
            var box = FixedBounds3.FromMinMax(FixedVector3.FromRaw(1234, 2345, 3456), FixedVector3.FromRaw(6789, 7890, 8901));
            Assert.IsTrue(FixedBoundsNetwork.TryCanonicalize(box, profile, out var canonical, out var status)); Assert.IsTrue((status & FixedStatus.Inexact) != 0);
            var again = FixedBoundsNetwork.Canonicalize(canonical, profile, out status); Assert.AreEqual(canonical, again); Assert.AreEqual(FixedStatus.None, status);
            byte* bytes = stackalloc byte[60]; var writer = new FixedBufferWriter(bytes, 60);
            Assert.IsTrue(FixedBoundsNetwork.TryWrite(ref writer, box, profile, out status));
            var reader = new FixedBufferReader(bytes, writer.BytesWritten); Assert.IsTrue(FixedBoundsNetwork.TryRead(ref reader, profile, out FixedBounds3 restored, out _)); Assert.AreEqual(canonical, restored);
            // Different field grids can invert Min/Max. Such quantization is rejected before writing anything.
            var mm = new FixedBoundsRuntimeState(FixedBoundsSettings.Default);
            var precise = new FixedVectorNetworkProfile(new FixedNetworkProfile(numeric.Settings, FixedKind.Signed64, FixedCodec.VarInt), 2);
            var coarse = new FixedVectorNetworkProfile(axis, 2); var mixed = new FixedBoundsNetworkProfile(FixedBoundsRepresentation.MinMax, coarse, precise);
            long scale = (long)numeric.Scale64;
            FixedBoundsKernel.FromMinMax(new FixedVectorBits((ulong)(scale * 6 / 10), 0), new FixedVectorBits((ulong)(scale * 7 / 10), 0), 2, numeric, mm, out var bad, out _);
            for (int i = 0; i < 60; i++) bytes[i] = 0xA5; writer = new FixedBufferWriter(bytes, 60);
            Assert.IsFalse(FixedBoundsNetworkKernel.Write(ref writer, bad, 2, mixed, numeric, mm, out status)); Assert.IsTrue((status & FixedStatus.InvalidDomain) != 0); Assert.AreEqual(0, writer.BitPosition); for (int i = 0; i < 60; i++) Assert.AreEqual(0xA5, bytes[i]);
        }
        [Test] public void RangeFailureInSecondVectorNeverWritesFirstVector()
        {
            var numeric = FixedRuntime.State; var rep = FixedBoundsRuntime.Settings.Representation;
            var wide = new FixedVectorNetworkProfile(new FixedNetworkProfile(numeric.Settings, FixedKind.Signed64, FixedCodec.BitPacked, lowerRaw: unchecked((ulong)-100L), upperRaw: 100), 3);
            var narrow = new FixedVectorNetworkProfile(new FixedNetworkProfile(numeric.Settings, FixedKind.Signed64, FixedCodec.BitPacked, lowerRaw: 0, upperRaw: 1), 3);
            var profile = new FixedBoundsNetworkProfile(rep, wide, narrow);
            FixedBounds3.TryFromStorage(FixedVector3.FromRaw(0, 0, 0), FixedVector3.FromRaw(2, 2, 2), out var box, out _);
            byte* bytes = stackalloc byte[60]; for (int i = 0; i < 60; i++) bytes[i] = 0xA5; var writer = new FixedBufferWriter(bytes, 60, 3);
            Assert.IsFalse(FixedBoundsNetwork.TryWrite(ref writer, box, profile, out var status)); Assert.AreEqual(FixedStatus.OutOfRange, status); Assert.AreEqual(3, writer.BitPosition); for (int i = 0; i < 60; i++) Assert.AreEqual(0xA5, bytes[i]);
        }
        [TestCase(FixedCodec.VarInt)] [TestCase(FixedCodec.FixedWidth)] [TestCase(FixedCodec.BitPacked)]
        public void InvalidCursorsAndNonzeroSpanOffsetsAreAtomicAndKeepCanaryBytes(FixedCodec codec)
        {
            var numeric = FixedRuntime.State; var rep = FixedBoundsRuntime.Settings.Representation;
            var profile = Profile(numeric, rep, 3, codec); var box = new FixedBounds3(FixedVector3.zero, new FixedVector3(2, 2, 2));
            // Use raw units inside the negotiated bit-packed range.
            Assert.IsTrue(FixedBounds3.TryFromStorage(FixedVector3.FromRaw(1, 2, 3), FixedVector3.FromRaw(4, 5, 6), out box, out _));
            var bytes = new byte[96]; for (int i = 0; i < bytes.Length; i++) bytes[i] = 0xA5;
            foreach (int initial in new[] { -1, int.MinValue, bytes.Length, bytes.Length + 1, int.MaxValue })
            {
                int offset = initial;
                Assert.IsFalse(FixedBoundsNetwork.TryWrite(bytes.AsSpan(), ref offset, box, profile, out _)); Assert.AreEqual(initial, offset);
                offset = initial; Assert.IsFalse(FixedBoundsNetwork.TryRead(bytes.AsSpan(), ref offset, profile, out FixedBounds3 _, out _)); Assert.AreEqual(initial, offset);
                for (int i = 0; i < bytes.Length; i++) Assert.AreEqual(0xA5, bytes[i]);
            }
            fixed (byte* pointer = bytes)
            {
                foreach (long cursor in new[] { -8L, -1L, 96 * 8L, long.MaxValue })
                {
                    var writer = new FixedBufferWriter(pointer, bytes.Length, cursor); Assert.IsFalse(FixedBoundsNetwork.TryWrite(ref writer, box, profile, out _)); Assert.AreEqual(cursor, writer.BitPosition);
                    var reader = new FixedBufferReader(pointer, bytes.Length, cursor); Assert.IsFalse(FixedBoundsNetwork.TryRead(ref reader, profile, out FixedBounds3 _, out _)); Assert.AreEqual(cursor, reader.BitPosition);
                    for (int i = 0; i < bytes.Length; i++) Assert.AreEqual(0xA5, bytes[i]);
                }
                var nullWriter = new FixedBufferWriter(null, 96); Assert.IsFalse(FixedBoundsNetwork.TryWrite(ref nullWriter, box, profile, out _)); Assert.AreEqual(0, nullWriter.BitPosition);
                var nullReader = new FixedBufferReader(null, 96); Assert.IsFalse(FixedBoundsNetwork.TryRead(ref nullReader, profile, out FixedBounds3 _, out _)); Assert.AreEqual(0, nullReader.BitPosition);
            }
            const int start = 7; int end = start;
            Assert.IsTrue(FixedBoundsNetwork.TryWrite(bytes.AsSpan(), ref end, box, profile, out _));
            int read = start; Assert.IsTrue(FixedBoundsNetwork.TryRead(bytes.AsSpan(), ref read, profile, out FixedBounds3 restored, out _)); Assert.AreEqual(end, read);
            box.GetStorage(out var a, out var b); restored.GetStorage(out var c, out var d); Assert.AreEqual(a.x.RawValue, c.x.RawValue); Assert.AreEqual(b.z.RawValue, d.z.RawValue);
            for (int i = 0; i < start; i++) Assert.AreEqual(0xA5, bytes[i]); for (int i = end; i < bytes.Length; i++) Assert.AreEqual(0xA5, bytes[i]);
            for (int length = start; length < end; length++)
            {
                var snapshot = (byte[])bytes.Clone(); int offset = start;
                Assert.IsFalse(FixedBoundsNetwork.TryWrite(bytes.AsSpan(0, length), ref offset, box, profile, out _)); Assert.AreEqual(start, offset); CollectionAssert.AreEqual(snapshot, bytes);
                offset = start; Assert.IsFalse(FixedBoundsNetwork.TryRead(bytes.AsSpan(0, length), ref offset, profile, out FixedBounds3 _, out _)); Assert.AreEqual(start, offset);
            }
        }
        [Test] public void MalformedVarintsAndSpecialValuesInTheFinalFieldNeverAdvanceTheReader()
        {
            byte* bytes = stackalloc byte[80];
            byte[][] malformed = { new byte[] { 0x84, 0 }, new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 2 },
                new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80 }, new byte[] { 0 }, new byte[] { 1 }, new byte[] { 2 } };
            foreach (int dimensions in new[] { 2, 3 }) foreach (FixedBoundsRepresentation rep in Enum.GetValues(typeof(FixedBoundsRepresentation)))
            {
                var numeric = new FixedRuntimeState(FixedSettings.Default); var geometry = new FixedBoundsRuntimeState(new FixedBoundsSettings(rep)); var profile = Profile(numeric, rep, dimensions, FixedCodec.VarInt);
                foreach (byte[] invalid in malformed) foreach (int field in new[] { 0, dimensions * 2 - 1 })
                {
                    const int start = 3; int offset = start;
                    for (int i = 0; i < 80; i++) bytes[i] = 0xA5;
                    for (int i = 0; i < dimensions * 2; i++)
                    { if (i == field) { for (int b = 0; b < invalid.Length; b++) bytes[offset++] = invalid[b]; } else bytes[offset++] = 4; }
                    var reader = new FixedBufferReader(bytes, offset, start * 8);
                    Assert.IsFalse(FixedBoundsNetworkKernel.Read(ref reader, dimensions, profile, numeric, geometry, out _, out var status));
                    Assert.AreEqual(FixedStatus.InvalidEncoding, status); Assert.AreEqual(start * 8, reader.BitPosition);
                }
                for (int i = 0; i < dimensions * 2 - 1; i++) bytes[i] = 4; bytes[dimensions * 2 - 1] = 0x84;
                var truncated = new FixedBufferReader(bytes, dimensions * 2);
                Assert.IsFalse(FixedBoundsNetworkKernel.Read(ref truncated, dimensions, profile, numeric, geometry, out _, out var error)); Assert.AreEqual(FixedStatus.BufferTooSmall, error); Assert.AreEqual(0, truncated.BitPosition);
            }
        }
        [TestCase(FixedCodec.VarInt)] [TestCase(FixedCodec.FixedWidth)] [TestCase(FixedCodec.BitPacked)]
        public void IndependentQuantizationOracleCoversDifferentGridsInEveryStoredField(FixedCodec codec)
        {
            byte* bytes = stackalloc byte[80];
            foreach (int precision in new[] { 0, 3, 9, 18 }) foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
            foreach (int dimensions in new[] { 2, 3 }) foreach (FixedBoundsRepresentation rep in Enum.GetValues(typeof(FixedBoundsRepresentation)))
            {
                var numeric = new FixedRuntimeState(new FixedSettings(3, precision, rounding)); var geometry = new FixedBoundsRuntimeState(new FixedBoundsSettings(rep));
                FixedNetworkProfile Axis(int wire) => new FixedNetworkProfile(numeric.Settings, FixedKind.Signed64, codec, wire, true,
                    codec == FixedCodec.BitPacked ? unchecked((ulong)-1000000L) : 0, codec == FixedCodec.BitPacked ? 1000000UL : 0);
                var a = dimensions == 2 ? new FixedVectorNetworkProfile(Axis(0), Axis(precision / 2)) : new FixedVectorNetworkProfile(Axis(0), Axis(precision / 2), Axis(precision));
                var b = dimensions == 2 ? new FixedVectorNetworkProfile(Axis(precision / 2), Axis(precision)) : new FixedVectorNetworkProfile(Axis(precision / 2), Axis(precision), Axis(0));
                var profile = new FixedBoundsNetworkProfile(rep, a, b);
                for (int sample = 0; sample < 48; sample++)
                {
                    var first = new FixedVectorBits(unchecked((ulong)(sample * 31 - 739L)), unchecked((ulong)(sample * 17 - 517L)), unchecked((ulong)(sample * 19 - 417L)));
                    var second = new FixedVectorBits(791, 827, 911); var box = new FixedBoundsBits(first, second);
                    Assert.IsTrue(FixedBoundsKernel.Validate(box, dimensions, numeric, geometry, out _));
                    var expectedFirst = default(FixedVectorBits); var expectedSecond = default(FixedVectorBits); bool inexact = false, valid = true;
                    for (int i = 0; i < dimensions; i++)
                    {
                        BigInteger divisorA = BigInteger.Pow(10, precision - a.Get(i).WireDecimalPlaces), divisorB = BigInteger.Pow(10, precision - b.Get(i).WireDecimalPlaces);
                        BigInteger rawA = FixedBoundsTests.Integer(first.Get(i)), rawB = FixedBoundsTests.Integer(second.Get(i));
                        var quantizedA = FixedBoundsTests.Round(rawA, divisorA, rounding) * divisorA; var quantizedB = FixedBoundsTests.Round(rawB, divisorB, rounding) * divisorB;
                        // IEEE-style negative zero is retained when a negative finite input rounds to zero.
                        expectedFirst.Set(i, quantizedA == 0 && rawA < 0 ? 0x7FFFFFFFFFFFFFFEUL : unchecked((ulong)(long)quantizedA));
                        expectedSecond.Set(i, unchecked((ulong)(long)quantizedB));
                        inexact |= rawA % divisorA != 0 || rawB % divisorB != 0;
                        valid &= rep == FixedBoundsRepresentation.MinMax ? quantizedA <= quantizedB : quantizedB >= 0;
                    }
                    var canonical = FixedBoundsNetworkKernel.Canonicalize(box, dimensions, profile, numeric, geometry, out var status);
                    Assert.AreEqual(valid, status.IsSuccess());
                    if (valid)
                    {
                        Assert.AreEqual(inexact ? FixedStatus.Inexact : FixedStatus.None, status); Same(new FixedBoundsBits(expectedFirst, expectedSecond), canonical, dimensions);
                        var again = FixedBoundsNetworkKernel.Canonicalize(canonical, dimensions, profile, numeric, geometry, out status); Same(canonical, again, dimensions); Assert.AreEqual(FixedStatus.None, status);
                    }
                    for (int i = 0; i < 80; i++) bytes[i] = 0xA5; var writer = new FixedBufferWriter(bytes, 80, codec == FixedCodec.BitPacked ? 5 : 16);
                    Assert.AreEqual(valid, FixedBoundsNetworkKernel.Write(ref writer, box, dimensions, profile, numeric, geometry, out status));
                    if (!valid) { Assert.AreEqual(FixedStatus.InvalidDomain | (inexact ? FixedStatus.Inexact : FixedStatus.None), status); for (int i = 0; i < 80; i++) Assert.AreEqual(0xA5, bytes[i]); continue; }
                    var reader = new FixedBufferReader(bytes, writer.BytesWritten, codec == FixedCodec.BitPacked ? 5 : 16);
                    Assert.IsTrue(FixedBoundsNetworkKernel.Read(ref reader, dimensions, profile, numeric, geometry, out var restored, out status)); Same(canonical, restored, dimensions); Assert.AreEqual(FixedStatus.None, status); Assert.AreEqual(writer.BitPosition, reader.BitPosition);
                }
            }
        }
        [Test] public void DescriptorFailuresInTheSecondVectorAreAtomicAtANonzeroOffset()
        {
            byte* bytes = stackalloc byte[300]; byte* shortBytes = stackalloc byte[300];
            foreach (int dimensions in new[] { 2, 3 }) foreach (FixedCodec codec in Enum.GetValues(typeof(FixedCodec)))
            {
                var profile = Profile(FixedRuntime.State, FixedBoundsRuntime.Settings.Representation, dimensions, codec); const int start = 3;
                var writer = new FixedBufferWriter(bytes, 300, start * 8); Assert.IsTrue(FixedBoundsNetwork.TryWriteDescriptor(ref writer, profile, out _));
                int end = writer.BytesWritten, second = start + 8 + profile.First.DescriptorBytes;
                foreach (int field in new[] { second, second + 4, second + 5, end - 1 })
                {
                    byte saved = bytes[field]; bytes[field] ^= 0xFF; var reader = new FixedBufferReader(bytes, end, start * 8);
                    Assert.IsFalse(FixedBoundsNetwork.TryReadDescriptor(ref reader, out _, out var status)); Assert.AreEqual(FixedStatus.InvalidEncoding, status); Assert.AreEqual(start * 8, reader.BitPosition); bytes[field] = saved;
                }
                for (int capacity = start; capacity < end; capacity++)
                {
                    for (int i = 0; i < 300; i++) shortBytes[i] = 0xA5;
                    var shortWriter = new FixedBufferWriter(shortBytes, capacity, start * 8); Assert.IsFalse(FixedBoundsNetwork.TryWriteDescriptor(ref shortWriter, profile, out var status)); Assert.AreEqual(FixedStatus.BufferTooSmall, status); Assert.AreEqual(start * 8, shortWriter.BitPosition);
                    for (int i = 0; i < 300; i++) Assert.AreEqual(0xA5, shortBytes[i]);
                }
            }
        }
        [TestCase(FixedCodec.VarInt)] [TestCase(FixedCodec.FixedWidth)] [TestCase(FixedCodec.BitPacked)]
        public void SeededAdversarialPacketsEitherFailAtomicallyOrReencodeTheSameConsumedBits(FixedCodec codec)
        {
            byte* bytes = stackalloc byte[80]; byte* encoded = stackalloc byte[80]; ulong seed = 0x13472BC98105D6AEUL;
            foreach (int dimensions in new[] { 2, 3 }) foreach (FixedBoundsRepresentation rep in Enum.GetValues(typeof(FixedBoundsRepresentation)))
            {
                var numeric = new FixedRuntimeState(FixedSettings.Default); var geometry = new FixedBoundsRuntimeState(new FixedBoundsSettings(rep)); var profile = Profile(numeric, rep, dimensions, codec);
                long start = codec == FixedCodec.BitPacked ? 5 : 16;
                for (int sample = 0; sample < 1024; sample++)
                {
                    for (int i = 0; i < 80; i++) { seed ^= seed << 13; seed ^= seed >> 7; seed ^= seed << 17; bytes[i] = (byte)seed; encoded[i] = 0xA5; }
                    int length = sample % 81; var reader = new FixedBufferReader(bytes, length, start); var duplicate = reader;
                    bool ok = FixedBoundsNetworkKernel.Read(ref reader, dimensions, profile, numeric, geometry, out var value, out var status);
                    bool repeated = FixedBoundsNetworkKernel.Read(ref duplicate, dimensions, profile, numeric, geometry, out var again, out var repeatedStatus);
                    Assert.AreEqual(ok, repeated); Assert.AreEqual(status, repeatedStatus); Assert.AreEqual(reader.BitPosition, duplicate.BitPosition); Same(value, again, dimensions);
                    if (!ok) { Assert.AreEqual(start, reader.BitPosition); continue; }
                    Assert.IsTrue(FixedBoundsKernel.Validate(value, dimensions, numeric, geometry, out _)); var writer = new FixedBufferWriter(encoded, 80, start);
                    Assert.IsTrue(FixedBoundsNetworkKernel.Write(ref writer, value, dimensions, profile, numeric, geometry, out _)); Assert.AreEqual(reader.BitPosition, writer.BitPosition);
                    for (long bit = start; bit < reader.BitPosition; bit++) Assert.AreEqual((bytes[bit >> 3] >> (int)(bit & 7)) & 1, (encoded[bit >> 3] >> (int)(bit & 7)) & 1);
                }
            }
        }
    }
}
