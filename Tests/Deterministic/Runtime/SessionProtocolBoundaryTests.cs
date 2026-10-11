using System;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class SessionProtocolBoundaryTests
    {
        private static readonly PeerId Sender = new PeerId(new PlayerId(1), 11), Receiver = new PeerId(new PlayerId(2), 7);
        [TestCase(0)] [TestCase(1)] [TestCase(257)]
        public void EverySingleBitCorruptionAndTruncatedDatagramFailsWithoutPublishingCursor(int payloadLength)
        {
            byte* packet = stackalloc byte[400]; byte* payload = stackalloc byte[257]; for (int i = 0; i < payloadLength; i++) payload[i] = (byte)(i * 17 + 3);
            var session = new DeterministicSessionId(93); var header = new DeterministicMessageHeader(session, 23, Sender, Receiver, ulong.MaxValue, 32);
            var writer = new DeterministicWriter(packet, 400, 5);
            DeterministicSessionProtocol.TryWrite(ref writer, header, new ReadOnlySpan<byte>(payload, payloadLength)).ThrowIfFailure();
            for (int length = 5; length < writer.Position; length++)
            {
                var reader = new DeterministicReader(packet, length, 5);
                Assert.IsFalse(DeterministicSessionProtocol.TryRead(ref reader, session, 23, Receiver, Sender, out _, out var data).IsSuccess());
                Assert.AreEqual(5, reader.Position); Assert.AreEqual(0, data.Length);
            }
            for (int position = 5; position < writer.Position; position++) for (int bit = 0; bit < 8; bit++)
            {
                packet[position] ^= (byte)(1 << bit); var reader = new DeterministicReader(packet, writer.Position, 5);
                Assert.IsFalse(DeterministicSessionProtocol.TryRead(ref reader, session, 23, Receiver, Sender, out _, out var data).IsSuccess(), "byte=" + position + ", bit=" + bit);
                Assert.AreEqual(5, reader.Position); Assert.AreEqual(0, data.Length); packet[position] ^= (byte)(1 << bit);
            }
            var valid = new DeterministicReader(packet, writer.Position, 5); DeterministicSessionProtocol.TryRead(ref valid, session, 23, Receiver, Sender, out var decoded, out var restored).ThrowIfFailure();
            Assert.AreEqual(ulong.MaxValue, decoded.Sequence); Assert.AreEqual(payloadLength, restored.Length); Assert.AreEqual(writer.Position, valid.Position);
        }
        [Test]
        public void SessionProfileDestinationAndProcessIdentityAreBoundToTheEnvelope()
        {
            byte* bytes = stackalloc byte[128]; var writer = new DeterministicWriter(bytes, 128);
            var session = new DeterministicSessionId(7); var header = new DeterministicMessageHeader(session, 19, Sender, Receiver, 1, 32);
            DeterministicSessionProtocol.TryWrite(ref writer, header, ReadOnlySpan<byte>.Empty).ThrowIfFailure();
            for (int mismatch = 0; mismatch < 4; mismatch++)
            {
                var reader = new DeterministicReader(bytes, writer.Position);
                var status = DeterministicSessionProtocol.TryRead(ref reader, mismatch == 0 ? new DeterministicSessionId(8) : session,
                    mismatch == 1 ? 20UL : 19UL, mismatch == 2 ? new PeerId(Receiver.Player, 8) : Receiver,
                    mismatch == 3 ? new PeerId(Sender.Player, 12) : Sender, out _, out _);
                Assert.AreEqual(DeterministicStatus.ProfileMismatch, status); Assert.AreEqual(0, reader.Position);
            }
        }
        [Test]
        public void OverlappingOrShortOutputLeavesTheEntirePacketUntouched()
        {
            byte* bytes = stackalloc byte[128]; for (int i = 0; i < 128; i++) bytes[i] = 0xa7;
            var header = new DeterministicMessageHeader(new DeterministicSessionId(7), 19, Sender, Receiver, 0, 32);
            var writer = new DeterministicWriter(bytes, 128);
            Assert.AreEqual(DeterministicStatus.InvalidConfiguration, DeterministicSessionProtocol.TryWrite(ref writer, header, new ReadOnlySpan<byte>(bytes + 9, 16)));
            Assert.AreEqual(0, writer.Position); for (int i = 0; i < 128; i++) Assert.AreEqual(0xa7, bytes[i]);
            writer = new DeterministicWriter(bytes, DeterministicSessionProtocol.Overhead - 1);
            Assert.AreEqual(DeterministicStatus.BufferTooSmall, DeterministicSessionProtocol.TryWrite(ref writer, header, ReadOnlySpan<byte>.Empty));
            for (int i = 0; i < 128; i++) Assert.AreEqual(0xa7, bytes[i]);
        }
    }
}
