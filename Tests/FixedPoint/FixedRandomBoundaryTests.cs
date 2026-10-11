using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed unsafe class FixedRandomBoundaryTests
    {
        [OneTimeSetUp] public void Configure() => FixedRuntime.Initialize();
        private static IEnumerable<TestCaseData> TruncatedPackets()
        {
            for (int i = 0; i < 16; i++) yield return new TestCaseData(true, i);
            for (int i = 0; i < 24; i++) yield return new TestCaseData(false, i);
        }
        [TestCaseSource(nameof(TruncatedPackets))]
        public void EveryDescriptorAndSnapshotTruncationPreservesCursorsAndBuffers(bool descriptor, int available)
        {
            var profile = new FixedRandomNetworkProfile(FixedRuntime.Settings);
            var random = FixedRandom.ForEvent(ulong.MaxValue, 1UL << 63, ulong.MaxValue); random.NextUInt32();
            var state = random.CaptureState(); byte[] packet = new byte[40]; int offset = 3;
            Assert.IsTrue(descriptor ? FixedRandomNetwork.TryWriteDescriptor(packet.AsSpan(), ref offset, profile, out _) :
                FixedRandomNetwork.TryWriteState(packet.AsSpan(), ref offset, state, profile, out _));
            int readOffset = 3; bool read;
            if (descriptor) read = FixedRandomNetwork.TryReadDescriptor(packet.AsSpan(0, 3 + available), ref readOffset, out _, out _);
            else read = FixedRandomNetwork.TryReadState(packet.AsSpan(0, 3 + available), ref readOffset, profile, out _, out _);
            Assert.IsFalse(read); Assert.AreEqual(3, readOffset); Assert.AreEqual(state, random.CaptureState());

            byte[] target = new byte[3 + available]; Array.Fill(target, (byte)0xcd); offset = 3; FixedStatus status;
            bool written = descriptor ? FixedRandomNetwork.TryWriteDescriptor(target.AsSpan(), ref offset, profile, out status) :
                FixedRandomNetwork.TryWriteState(target.AsSpan(), ref offset, state, profile, out status);
            Assert.IsFalse(written); Assert.AreEqual(FixedStatus.BufferTooSmall, status); Assert.AreEqual(3, offset);
            foreach (byte value in target) Assert.AreEqual(0xcd, value);
        }

        [TestCase(FixedCodec.VarInt)] [TestCase(FixedCodec.FixedWidth)]
        public void EveryCheckpointBitBoundaryMatchesAnIndependentEncoderAndPointerReader(FixedCodec codec)
        {
            var profile = new FixedRandomNetworkProfile(FixedRuntime.Settings); byte[] buffer = new byte[16];
            var positions = new List<ulong> { 0, 1, ulong.MaxValue };
            for (int bit = 1; bit < 64; bit++) { positions.Add((1UL << bit) - 1); positions.Add(1UL << bit); }
            foreach (ulong position in positions)
            {
                Array.Fill(buffer, (byte)0xaa); int offset = 2;
                Assert.IsTrue(FixedRandomNetwork.TryWritePosition(buffer.AsSpan(), ref offset, position, profile, out _, codec));
                byte[] expected = Encode(position, codec); Assert.AreEqual(2 + expected.Length, offset);
                for (int i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], buffer[2 + i]);
                Assert.AreEqual(0xaa, buffer[1]); Assert.AreEqual(0xaa, buffer[offset]);
                var random = FixedRandom.ForEvent(132, ulong.MaxValue, 17); var identity = random.CaptureState();
                fixed (byte* pointer = buffer)
                {
                    var reader = new FixedBufferReader(pointer, offset, 16);
                    Assert.IsTrue(FixedRandomNetwork.TryReadPosition(ref reader, ref random, profile, out _, codec));
                    Assert.AreEqual((long)offset * 8, reader.BitPosition);
                }
                Assert.AreEqual(new FixedRandomState(identity.Key, identity.SequenceId, position), random.CaptureState());
                for (int length = 0; length < expected.Length; length++)
                {
                    int readOffset = 0; var before = random.CaptureState();
                    Assert.IsFalse(FixedRandomNetwork.TryReadPosition(expected.AsSpan(0, length), ref readOffset, ref random, profile, out _, codec));
                    Assert.AreEqual(0, readOffset); Assert.AreEqual(before, random.CaptureState());
                }
                if (codec == FixedCodec.VarInt)
                {
                    byte[] redundant = new byte[expected.Length + 1]; expected.CopyTo(redundant, 0); redundant[expected.Length - 1] |= 128;
                    int readOffset = 0; var before = random.CaptureState();
                    Assert.IsFalse(FixedRandomNetwork.TryReadPosition(redundant.AsSpan(), ref readOffset, ref random, profile, out _));
                    Assert.AreEqual(0, readOffset); Assert.AreEqual(before, random.CaptureState());
                }
            }
        }
        private static byte[] Encode(ulong position, FixedCodec codec)
        {
            var bytes = new List<byte>();
            if (codec == FixedCodec.FixedWidth)
            { for (int i = 0; i < 8; i++) { bytes.Add((byte)(position % 256)); position /= 256; } }
            else
            { do { byte low = (byte)(position % 128); position /= 128; bytes.Add((byte)(low + (position != 0 ? 128 : 0))); } while (position != 0); }
            return bytes.ToArray();
        }

        [Test]
        public void EveryMagicAndVersionBitMutationIsRejectedAndNumericMutationsCannotRestoreState()
        {
            var profile = new FixedRandomNetworkProfile(FixedRuntime.Settings); byte[] packet = new byte[40]; int offset = 0;
            Assert.IsTrue(FixedRandomNetwork.TryWriteDescriptor(packet.AsSpan(), ref offset, profile, out _));
            Assert.IsTrue(FixedRandomNetwork.TryWriteState(packet.AsSpan(), ref offset, new FixedRandomState(17, 18, 19), profile, out _));
            for (int bit = 0; bit < 128; bit++)
            {
                byte[] mutated = (byte[])packet.Clone(); mutated[bit / 8] ^= (byte)(1 << (bit % 8)); offset = 0;
                bool valid = FixedRandomNetwork.TryReadDescriptor(mutated.AsSpan(), ref offset, out var received, out var status);
                if (bit < 64)
                { Assert.IsFalse(valid); Assert.AreEqual(0, offset); Assert.IsFalse(status.IsSuccess()); }
                else
                {
                    Assert.IsTrue(valid); Assert.IsFalse(profile.IsCompatible(received));
                    Assert.IsFalse(FixedRandomNetwork.TryReadState(mutated.AsSpan(), ref offset, received, out _, out status));
                    Assert.AreEqual(FixedStatus.ProfileMismatch, status); Assert.AreEqual(16, offset);
                }
            }
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)]
        public void AllUnalignedPointerCodecOffsetsFailWithoutMutation(int bitOffset)
        {
            byte* buffer = stackalloc byte[64]; for (int i = 0; i < 64; i++) buffer[i] = 0x5a;
            var profile = new FixedRandomNetworkProfile(FixedRuntime.Settings); var random = FixedRandom.Create(99); var before = random.CaptureState();
            var writer = new FixedBufferWriter(buffer, 64, bitOffset); var reader = new FixedBufferReader(buffer, 64, bitOffset);
            Assert.IsFalse(FixedRandomNetwork.TryWriteDescriptor(ref writer, profile, out var status)); Assert.AreEqual(FixedStatus.InvalidEncoding, status);
            Assert.IsFalse(FixedRandomNetwork.TryWriteState(ref writer, before, profile, out status)); Assert.AreEqual(FixedStatus.InvalidEncoding, status);
            Assert.IsFalse(FixedRandomNetwork.TryWritePosition(ref writer, 10, profile, out status)); Assert.AreEqual(FixedStatus.InvalidEncoding, status);
            Assert.IsFalse(FixedRandomNetwork.TryReadDescriptor(ref reader, out _, out status)); Assert.AreEqual(FixedStatus.InvalidEncoding, status);
            Assert.IsFalse(FixedRandomNetwork.TryReadState(ref reader, profile, out _, out status)); Assert.AreEqual(FixedStatus.InvalidEncoding, status);
            Assert.IsFalse(FixedRandomNetwork.TryReadPosition(ref reader, ref random, profile, out status)); Assert.AreEqual(FixedStatus.InvalidEncoding, status);
            Assert.AreEqual(bitOffset, writer.BitPosition); Assert.AreEqual(bitOffset, reader.BitPosition); Assert.AreEqual(before, random.CaptureState());
            for (int i = 0; i < 64; i++) Assert.AreEqual(0x5a, buffer[i]);
        }

        private static IEnumerable<TestCaseData> GeometryProfiles()
        {
            foreach (int precision in new[] { 0, 3, 18 })
                foreach (FixedMathProfile math in Enum.GetValues(typeof(FixedMathProfile)))
                    foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
                        yield return new TestCaseData(precision, math, rounding);
        }
        [TestCaseSource(nameof(GeometryProfiles))]
        public void HugeRadiusUsesExactWideIntermediatesAndSurfaceQuantizationStaysFinite(int precision, FixedMathProfile math, FixedRounding rounding)
        {
            var state = new FixedRuntimeState(new FixedSettings(Math.Min(precision, 9), precision, rounding: rounding, mathProfile: math));
            var f = new FixedFormat(FixedKind.Signed64, state); var random = FixedRandom.Create(349);
            BigInteger radius = f.MaxPositive, squared = radius * radius;
            for (int i = 0; i < 24; i++)
            {
                Assert.IsTrue(FixedRandomGeometry.Inside(ref random, 0, 0, 0, f.MaxPositive, true, state, out var inside, out var status));
                Assert.IsTrue(status.IsSuccess()); Assert.LessOrEqual(Squared(inside), squared);
                Assert.IsTrue(FixedRandomGeometry.On(ref random, 0, 0, 0, f.MaxPositive, true, state, out var surface, out status));
                Assert.IsTrue(status.IsSuccess()); Assert.LessOrEqual(BigInteger.Abs(Squared(surface) - squared), 6 * radius + 3);
                foreach (Fixed64 axis in new[] { surface.X, surface.Y, surface.Z })
                { Assert.IsTrue(f.Decode(unchecked((ulong)axis.RawValue)).IsFinite); Assert.AreNotEqual(f.NegativeZero, unchecked((ulong)axis.RawValue)); }
            }
        }
        private static BigInteger Squared(FixedPointVector3 point) => (BigInteger)point.X.RawValue * point.X.RawValue +
            (BigInteger)point.Y.RawValue * point.Y.RawValue + (BigInteger)point.Z.RawValue * point.Z.RawValue;

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void GeometryFailuresAndNegativeZeroConstantsPreservePartlyConsumedBlocks(int phase)
        {
            var random = FixedRandom.Create(567); for (int i = 0; i < phase; i++) random.NextUInt32(); var before = random.CaptureState();
            foreach (Fixed64 invalid in new[] { Fixed64.NaN, Fixed64.PositiveInfinity, Fixed64.NegativeInfinity })
            {
                var bad = new FixedPointVector3(Fixed64.Zero, invalid, Fixed64.Zero);
                Assert.IsFalse(random.TryInsideSphere(bad, Fixed64.One, out _, out _)); Assert.IsFalse(random.TryOnSphere(bad, Fixed64.One, out _, out _));
                Assert.IsFalse(random.TryInsideBox(default, bad, out _, out _)); Assert.IsFalse(random.TryInsideTriangle(default, bad, bad, out _, out _));
                Assert.AreEqual(before, random.CaptureState());
            }
            var negativeZero = new FixedPointVector3(Fixed64.NegativeZero, Fixed64.NegativeZero, Fixed64.NegativeZero);
            Assert.AreEqual(default(FixedPointVector3), random.InsideSphere(negativeZero, Fixed64.NegativeZero));
            Assert.AreEqual(default(FixedPointVector3), random.OnSphere(negativeZero, Fixed64.NegativeZero)); Assert.AreEqual(before, random.CaptureState());
            var expected = default(FixedRandom); expected.RestoreState(before); Assert.AreEqual(expected.NextUInt64(), random.NextUInt64());
        }
    }
}
