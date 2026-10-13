using LegendaryTools.FixedPoint;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class FixedQuaternionAdapterTests
    {
        [OneTimeSetUp] public void Initialize() => FixedRuntime.Initialize();
        private static void BytesEqual(byte* a, byte* b, int length) { for (int i = 0; i < length; i++) Assert.AreEqual(a[i], b[i], "byte " + i); }
        [Test] public void PolicyPreservesRawValuesAndSupportsEqualityOrderingAndAtomicFailure()
        {
            var p = default(FixedQuaternionPolicy); var q = new FixedQuaternion(Fixed64.NegativeZero, Fixed64.NaN, Fixed64.PositiveInfinity, Fixed64.NegativeInfinity);
            byte* bytes = stackalloc byte[32]; var writer = new DeterministicWriter(bytes, 32); p.TryWrite(ref writer, q).ThrowIfFailure(); Assert.AreEqual(32, writer.Position);
            var reader = new DeterministicReader(bytes, 32); p.TryRead(ref reader, out var decoded).ThrowIfFailure();
            for (int i = 0; i < 4; i++) Assert.AreEqual(q[i].RawValue, decoded[i].RawValue);
            var zero = FixedQuaternion.identity; var negative = zero; negative.x = Fixed64.NegativeZero;
            Assert.IsTrue(p.Equals(zero, negative)); Assert.AreEqual(p.Hash(zero), p.Hash(negative)); Assert.AreEqual(0, p.Compare(zero, negative));
            Assert.AreNotEqual(p.Hash(zero), p.Hash(-zero));
            for (int length = 0; length < 32; length++)
            { for (int i = 0; i < 32; i++) bytes[i] = 0xa5; writer = new DeterministicWriter(bytes, length); Assert.AreEqual(DeterministicStatus.BufferTooSmall, p.TryWrite(ref writer, q)); Assert.AreEqual(0, writer.Position); for (int i = 0; i < 32; i++) Assert.AreEqual(0xa5, bytes[i]); reader = new DeterministicReader(bytes, length); Assert.AreEqual(DeterministicStatus.BufferTooSmall, p.TryRead(ref reader, out _)); Assert.AreEqual(0, reader.Position); }
            FixedDeterministicProfile.TryFingerprint(out var original).ThrowIfFailure(); FixedDeterministicProfile.TryQuaternionFingerprint(out var rotation).ThrowIfFailure(); Assert.AreNotEqual(original, rotation);
        }
        [TestCase(FixedQuaternionDeltaMode.Forward)] [TestCase(FixedQuaternionDeltaMode.Reversible)]
        public void ExactPatchRoundTripsBitsAndReversesOverAnIdentifiedBaseline(FixedQuaternionDeltaMode mode)
        {
            var p = new FixedQuaternionNetworkProfile(FixedRuntime.Settings);
            byte* source = stackalloc byte[40]; byte* target = stackalloc byte[40]; byte* packet = stackalloc byte[FixedQuaternionDelta.MaxPacketBytes]; byte* destination = stackalloc byte[40]; byte* restored = stackalloc byte[40];
            var a = new FixedQuaternion(Fixed64.NegativeZero, 0, 0, 1); var b = a; b.y = Fixed64.Epsilon;
            Assert.IsTrue(FixedQuaternionDelta.TryCreateCheckpoint(source, 40, 10, a, p, out var before, out _)); Assert.IsTrue(FixedQuaternionDelta.TryCreateCheckpoint(target, 40, 11, b, p, out var after, out _));
            var writer = new DeterministicWriter(packet, FixedQuaternionDelta.MaxPacketBytes);
            FixedQuaternionDelta.TryWrite(ref writer, before, after, p, out var kind, mode).ThrowIfFailure(); Assert.AreEqual(FixedQuaternionTransferKind.Patch, kind); Assert.Less(writer.Position, FixedQuaternionDelta.HeaderBytes + 32);
            var reader = new DeterministicReader(packet, writer.Position);
            FixedQuaternionDelta.TryApply(ref reader, before, p, destination, 40, out var applied, out var q).ThrowIfFailure(); Assert.AreEqual(after.Id, applied.Id); BytesEqual(target, destination, 32); Assert.AreEqual(b.x.RawValue, q.x.RawValue); Assert.AreEqual(b.y.RawValue, q.y.RawValue);
            reader = new DeterministicReader(packet, writer.Position);
            var status = FixedQuaternionDelta.TryApply(ref reader, applied, p, restored, 40, out var old, out _, reverse: true);
            if (mode == FixedQuaternionDeltaMode.Reversible) { status.ThrowIfFailure(); BytesEqual(source, restored, 32); Assert.AreEqual(before.Id, old.Id); }
            else { Assert.AreEqual(DeterministicStatus.InvalidConfiguration, status); Assert.AreEqual(0, reader.Position); }
        }
        [Test] public void FullFallbackReversesVariableLengthAndPreservesCompactPayloadBytes()
        {
            byte* source = stackalloc byte[40]; byte* target = stackalloc byte[40]; byte* packet = stackalloc byte[FixedQuaternionDelta.MaxPacketBytes]; byte* destination = stackalloc byte[40]; byte* restored = stackalloc byte[40];
            foreach (var p in new[] { new FixedQuaternionNetworkProfile(FixedRuntime.Settings, FixedCodec.VarInt), FixedQuaternionNetworkProfile.SmallestThree(FixedRuntime.Settings, allowLossy: true) })
            {
                Assert.IsTrue(FixedQuaternionDelta.TryCreateCheckpoint(source, 40, 0, FixedQuaternion.identity, p, out var before, out _));
                Assert.IsTrue(FixedQuaternionDelta.TryCreateCheckpoint(target, 40, 1, FixedQuaternion.Euler(66,77,88), p, out var after, out _));
                var writer = new DeterministicWriter(packet, FixedQuaternionDelta.MaxPacketBytes);
                FixedQuaternionDelta.TryWrite(ref writer, before, after, p, out var kind, FixedQuaternionDeltaMode.Reversible).ThrowIfFailure();
                if (before.Length != after.Length) Assert.AreEqual(FixedQuaternionTransferKind.Full, kind);
                var reader = new DeterministicReader(packet, writer.Position); FixedQuaternionDelta.TryApply(ref reader, before, p, destination, 40, out var applied, out _).ThrowIfFailure(); BytesEqual(target, destination, after.Length);
                reader = new DeterministicReader(packet, writer.Position); FixedQuaternionDelta.TryApply(ref reader, applied, p, restored, 40, out var original, out _, true).ThrowIfFailure(); BytesEqual(source, restored, before.Length); Assert.AreEqual(before.PayloadHash, original.PayloadHash);
            }
        }
        [Test] public void AllFailuresLeavePacketCursorDestinationAndBaselineUnchanged()
        {
            var p = new FixedQuaternionNetworkProfile(FixedRuntime.Settings);
            byte* source = stackalloc byte[40]; byte* target = stackalloc byte[40]; byte* packet = stackalloc byte[FixedQuaternionDelta.MaxPacketBytes]; byte* shortPacket = stackalloc byte[FixedQuaternionDelta.MaxPacketBytes]; byte* destination = stackalloc byte[40];
            FixedQuaternionDelta.TryCreateCheckpoint(source, 40, 10, FixedQuaternion.identity, p, out var before, out _);
            FixedQuaternionDelta.TryCreateCheckpoint(target, 40, 11, FixedQuaternion.FromRaw(0,1,0,1000), p, out var after, out _);
            var writer = new DeterministicWriter(packet, FixedQuaternionDelta.MaxPacketBytes); FixedQuaternionDelta.TryWrite(ref writer, before, after, p, out _).ThrowIfFailure(); int length = writer.Position;
            for (int capacity = 0; capacity < length; capacity++)
            {
                for (int i = 0; i < FixedQuaternionDelta.MaxPacketBytes; i++) shortPacket[i] = 0xa5;
                var shortWriter = new DeterministicWriter(shortPacket, capacity); Assert.AreEqual(DeterministicStatus.BufferTooSmall, FixedQuaternionDelta.TryWrite(ref shortWriter, before, after, p, out _)); Assert.AreEqual(0, shortWriter.Position);
                for (int i = 0; i < FixedQuaternionDelta.MaxPacketBytes; i++) Assert.AreEqual(0xa5, shortPacket[i]);
                for (int i = 0; i < 40; i++) destination[i] = 0xa5;
                var reader = new DeterministicReader(packet, capacity); Assert.AreEqual(DeterministicStatus.BufferTooSmall, FixedQuaternionDelta.TryApply(ref reader, before, p, destination, 40, out _, out _)); Assert.AreEqual(0, reader.Position);
                for (int i = 0; i < 40; i++) Assert.AreEqual(0xa5, destination[i]);
            }
            var wrongId = new FixedQuaternionCheckpoint(source, before.Length, 12, before.ProfileFingerprint); var r = new DeterministicReader(packet, length);
            Assert.AreEqual(DeterministicStatus.ProtocolConflict, FixedQuaternionDelta.TryApply(ref r, wrongId, p, destination, 40, out _, out _)); Assert.AreEqual(0, r.Position);
            source[1] ^= 1; r = new DeterministicReader(packet, length); Assert.AreEqual(DeterministicStatus.ProtocolConflict, FixedQuaternionDelta.TryApply(ref r, before, p, destination, 40, out _, out _)); source[1] ^= 1;
            packet[length-1] ^= 1; r = new DeterministicReader(packet, length); Assert.AreEqual(DeterministicStatus.InvalidEncoding, FixedQuaternionDelta.TryApply(ref r, before, p, destination, 40, out _, out _)); Assert.AreEqual(0, r.Position);
            for (int i = 0; i < 40; i++) Assert.AreEqual(0xa5, destination[i]);
        }
    }
}
