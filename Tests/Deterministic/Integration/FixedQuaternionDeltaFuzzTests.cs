using System;
using LegendaryTools.FixedPoint;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class FixedQuaternionDeltaFuzzTests
    {
        [OneTimeSetUp] public void Initialize() => FixedRuntime.Initialize();
        private static FixedQuaternionNetworkProfile Profile(int encoding)
        {
            if (encoding >= 3) return FixedQuaternionNetworkProfile.SmallestThree(FixedRuntime.Settings, encoding == 3 ? 32 : encoding == 4 ? 48 : 64, true);
            return new FixedQuaternionNetworkProfile(FixedRuntime.Settings, encoding == 0 ? FixedCodec.FixedWidth : encoding == 1 ? FixedCodec.VarInt : FixedCodec.BitPacked,
                lowerRaw: encoding == 2 ? unchecked((ulong)-2000L) : 0, upperRaw: encoding == 2 ? 2000UL : 0);
        }
        private static void Equal(byte* a, byte* b, int length)
        { for (int i = 0; i < length; i++) Assert.AreEqual(a[i], b[i], "byte " + i); }
        private static ulong Next(ref ulong value) { value ^= value << 13; value ^= value >> 7; value ^= value << 17; return value; }
        private static FixedQuaternion Rotation(ref ulong seed) => FixedQuaternion.FromRaw(
            (long)(Next(ref seed) % 4001) - 2000, (long)(Next(ref seed) % 4001) - 2000, (long)(Next(ref seed) % 4001) - 2000, 1000);

        [TestCase(0, false)] [TestCase(1, false)] [TestCase(2, false)]
        [TestCase(3, false)] [TestCase(4, false)] [TestCase(5, false)]
        [TestCase(0, true)] [TestCase(1, true)] [TestCase(2, true)]
        [TestCase(3, true)] [TestCase(4, true)] [TestCase(5, true)]
        public void SeededCheckpointChainsMeasureApplyInPlaceAndReverseExactWireBytes(int encoding, bool reversible)
        {
            var profile = Profile(encoding); var mode = reversible ? FixedQuaternionDeltaMode.Reversible : FixedQuaternionDeltaMode.Forward;
            byte* baselineBuffer = stackalloc byte[40]; byte* target = stackalloc byte[40]; byte* original = stackalloc byte[40];
            byte* packet = stackalloc byte[FixedQuaternionDelta.MaxPacketBytes + 6]; byte* scratch = stackalloc byte[40];
            ulong seed = 0x45fe385763UL; var quaternion = FixedQuaternion.identity;
            Assert.IsTrue(FixedQuaternionDelta.TryCreateCheckpoint(baselineBuffer, 40, 0, quaternion, profile, out var baseline, out _));
            for (int tick = 1; tick <= 64; tick++)
            {
                int originalLength = baseline.Length;
                for (int i = 0; i < originalLength; i++) original[i] = baselineBuffer[i];
                if (tick % 4 == 0) quaternion = Rotation(ref seed);
                else if (tick % 4 == 1) quaternion.y = Fixed64.FromRaw((long)(Next(ref seed) % 4001) - 2000);
                // Repeated unchanged values exercise the zero-change mask too.
                Assert.IsTrue(FixedQuaternionDelta.TryCreateCheckpoint(target, 40, (ulong)tick, quaternion, profile, out var expected, out _));
                var measure = DeterministicWriter.CreateMeasure(); measure.Position = 3;
                FixedQuaternionDelta.TryWrite(ref measure, baseline, expected, profile, out var measuredKind, mode).ThrowIfFailure();
                for (int i = 0; i < FixedQuaternionDelta.MaxPacketBytes + 6; i++) packet[i] = 0xa5;
                var writer = new DeterministicWriter(packet, FixedQuaternionDelta.MaxPacketBytes + 6, 3);
                FixedQuaternionDelta.TryWrite(ref writer, baseline, expected, profile, out var writtenKind, mode).ThrowIfFailure();
                Assert.AreEqual(measure.Position, writer.Position); Assert.AreEqual(measuredKind, writtenKind);
                Assert.AreEqual(0xa5, packet[2]); Assert.AreEqual(0xa5, packet[writer.Position]);
                var reader = new DeterministicReader(packet, writer.Position + 1, 3);
                FixedQuaternionDelta.TryApply(ref reader, baseline, profile, baselineBuffer, 40, out var applied, out _).ThrowIfFailure();
                Assert.AreEqual(writer.Position, reader.Position); Equal(target, baselineBuffer, expected.Length);
                Assert.AreEqual(expected.Id, applied.Id); Assert.AreEqual(expected.PayloadHash, applied.PayloadHash);
                reader = new DeterministicReader(packet, writer.Position, 3);
                var reverseStatus = FixedQuaternionDelta.TryApply(ref reader, applied, profile, scratch, 40, out var old, out _, true);
                if (reversible) { reverseStatus.ThrowIfFailure(); Equal(original, scratch, originalLength); Assert.AreEqual(baseline.Id, old.Id); }
                else { Assert.AreEqual(DeterministicStatus.InvalidConfiguration, reverseStatus); Assert.AreEqual(3, reader.Position); }
                baseline = applied;
            }
        }

        [Test]
        public void EverySingleBitMutationIsRejectedOrChangesOnlyExplicitTargetMetadata()
        {
            var profile = Profile(0);
            byte* before = stackalloc byte[40]; byte* after = stackalloc byte[40]; byte* destination = stackalloc byte[40];
            byte* packet = stackalloc byte[FixedQuaternionDelta.MaxPacketBytes]; byte* changed = stackalloc byte[FixedQuaternionDelta.MaxPacketBytes];
            FixedQuaternionDelta.TryCreateCheckpoint(before, 40, 10, FixedQuaternion.identity, profile, out var baseline, out _);
            FixedQuaternionDelta.TryCreateCheckpoint(after, 40, 11, FixedQuaternion.FromRaw(0, 1, 0, 1000), profile, out var target, out _);
            var writer = new DeterministicWriter(packet, FixedQuaternionDelta.MaxPacketBytes);
            FixedQuaternionDelta.TryWrite(ref writer, baseline, target, profile, out _, FixedQuaternionDeltaMode.Reversible).ThrowIfFailure();
            int rejected = 0, acceptedMetadata = 0;
            for (int offset = 0; offset < writer.Position; offset++)
            for (int bit = 0; bit < 8; bit++)
            {
                for (int i = 0; i < writer.Position; i++) changed[i] = packet[i];
                changed[offset] ^= (byte)(1 << bit);
                for (int i = 0; i < 40; i++) destination[i] = 0xa5;
                var reader = new DeterministicReader(changed, writer.Position);
                var status = FixedQuaternionDelta.TryApply(ref reader, baseline, profile, destination, 40, out var actual, out _);
                if (!status.IsSuccess())
                {
                    rejected++; Assert.AreEqual(0, reader.Position);
                    for (int i = 0; i < 40; i++) Assert.AreEqual(0xa5, destination[i]);
                }
                else
                {
                    // Envelope metadata is not authenticated. Forward/Reversible mode and target ID do not change the payload.
                    acceptedMetadata++; Assert.IsTrue(offset == 6 || offset >= 24 && offset < 32, "unexpected accepted field " + offset);
                    Equal(after, destination, target.Length); Assert.AreEqual(target.PayloadHash, actual.PayloadHash);
                }
            }
            Assert.Greater(rejected, 300); Assert.AreEqual(65, acceptedMetadata);
        }

        [Test]
        public void PacketAliasingDestinationAndInvalidCheckpointPaddingFailAtomically()
        {
            var profile = Profile(2);
            byte* before = stackalloc byte[40]; byte* after = stackalloc byte[40]; byte* packet = stackalloc byte[FixedQuaternionDelta.MaxPacketBytes];
            FixedQuaternionDelta.TryCreateCheckpoint(before, 40, 1, FixedQuaternion.identity, profile, out var source, out _);
            FixedQuaternionDelta.TryCreateCheckpoint(after, 40, 2, FixedQuaternion.FromRaw(1, 0, 0, 1000), profile, out var target, out _);
            var writer = new DeterministicWriter(packet, FixedQuaternionDelta.MaxPacketBytes);
            FixedQuaternionDelta.TryWrite(ref writer, source, target, profile, out _).ThrowIfFailure();
            var saved = new byte[writer.Position]; for (int i = 0; i < saved.Length; i++) saved[i] = packet[i];
            var reader = new DeterministicReader(packet, writer.Position);
            Assert.AreEqual(DeterministicStatus.InvalidConfiguration, FixedQuaternionDelta.TryApply(ref reader, source, profile, packet + 1, 40, out _, out _));
            Assert.AreEqual(0, reader.Position); for (int i = 0; i < saved.Length; i++) Assert.AreEqual(saved[i], packet[i]);
            // A deliberately narrow per-component profile leaves unused bits in its final byte.
            profile = new FixedQuaternionNetworkProfile(FixedRuntime.Settings, FixedCodec.BitPacked, lowerRaw: unchecked((ulong)-1000L), upperRaw: 1000);
            FixedQuaternionDelta.TryCreateCheckpoint(before, 40, 1, FixedQuaternion.identity, profile, out source, out _);
            FixedQuaternionDelta.TryCreateCheckpoint(after, 40, 2, FixedQuaternion.FromRaw(1, 0, 0, 1000), profile, out target, out _);
            before[source.Length - 1] |= 0x80; writer = new DeterministicWriter(packet, FixedQuaternionDelta.MaxPacketBytes);
            Assert.AreEqual(DeterministicStatus.InvalidEncoding, FixedQuaternionDelta.TryWrite(ref writer, source, target, profile, out _));
            Assert.AreEqual(0, writer.Position); for (int i = 0; i < saved.Length; i++) Assert.AreEqual(saved[i], packet[i]);
        }
    }
}
