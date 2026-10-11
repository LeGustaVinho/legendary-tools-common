using System;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedRandomNetworkTests
    {
        [OneTimeSetUp] public void Configure() => FixedRuntime.Initialize();
        [Test]
        public void DescriptorAndSnapshotHaveCanonicalLittleEndianBytesAndRestoreMidBlock()
        {
            var profile = new FixedRandomNetworkProfile(FixedRuntime.Settings); byte[] buffer = new byte[48]; int offset = 3;
            Assert.IsTrue(FixedRandomNetwork.TryWriteDescriptor(buffer.AsSpan(), ref offset, profile, out _)); Assert.AreEqual(19, offset);
            CollectionAssert.AreEqual(new byte[] { 70, 82, 78, 71, 1, 1, 1, 0 }, buffer.AsSpan(3, 8).ToArray());
            var state = new FixedRandomState(0x0807060504030201UL, 0x1817161514131211UL, 0x2827262524232221UL);
            Assert.IsTrue(FixedRandomNetwork.TryWriteState(buffer.AsSpan(), ref offset, state, profile, out _)); Assert.AreEqual(43, offset);
            for (int i = 0; i < 8; i++) { Assert.AreEqual(i + 1, buffer[19 + i]); Assert.AreEqual(i + 17, buffer[27 + i]); Assert.AreEqual(i + 33, buffer[35 + i]); }
            offset = 3; Assert.IsTrue(FixedRandomNetwork.TryReadDescriptor(buffer.AsSpan(), ref offset, out var remote, out _)); Assert.IsTrue(profile.IsCompatible(remote));
            Assert.IsTrue(FixedRandomNetwork.TryReadState(buffer.AsSpan(), ref offset, remote, out var received, out _)); Assert.AreEqual(state, received);
            var original = FixedRandom.Create(1234, 567); original.NextUInt32(); state = original.CaptureState(); offset = 0;
            Assert.IsTrue(FixedRandomNetwork.TryWriteState(buffer.AsSpan(), ref offset, state, profile, out _)); offset = 0;
            Assert.IsTrue(FixedRandomNetwork.TryReadState(buffer.AsSpan(), ref offset, profile, out received, out _)); var restored = default(FixedRandom); restored.RestoreState(received);
            for (int i = 0; i < 99; i++) Assert.AreEqual(original.NextUInt64(), restored.NextUInt64());
        }

        [TestCase(0UL, 1)] [TestCase(127UL, 1)] [TestCase(128UL, 2)] [TestCase(ulong.MaxValue, 10)]
        public void PositionCheckpointsUseCanonicalVarUIntAndFixedWidth(ulong position, int bytes)
        {
            var profile = new FixedRandomNetworkProfile(FixedRuntime.Settings); byte[] buffer = new byte[20]; int offset = 2;
            Assert.IsTrue(FixedRandomNetwork.TryWritePosition(buffer.AsSpan(), ref offset, position, profile, out _)); Assert.AreEqual(bytes + 2, offset);
            var random = FixedRandom.ForEvent(92, 13, 82); ulong key = random.CaptureState().Key; offset = 2;
            Assert.IsTrue(FixedRandomNetwork.TryReadPosition(buffer.AsSpan(), ref offset, ref random, profile, out _)); Assert.AreEqual(position, random.Position); Assert.AreEqual(key, random.CaptureState().Key);
            offset = 0; Assert.IsTrue(FixedRandomNetwork.TryWritePosition(buffer.AsSpan(), ref offset, position, profile, out _, FixedCodec.FixedWidth)); Assert.AreEqual(8, offset);
            offset = 0; Assert.IsTrue(FixedRandomNetwork.TryReadPosition(buffer.AsSpan(), ref offset, ref random, profile, out _, FixedCodec.FixedWidth)); Assert.AreEqual(position, random.Position);
        }

        [Test]
        public unsafe void InvalidPacketsProfilesAndBuffersFailWithoutAdvancingOrWriting()
        {
            var profile = new FixedRandomNetworkProfile(FixedRuntime.Settings); var random = FixedRandom.Create(19); var state = random.CaptureState();
            byte[] buffer = new byte[40]; Array.Fill(buffer, (byte)93); int offset = 20;
            Assert.IsFalse(FixedRandomNetwork.TryWriteState(buffer.AsSpan(), ref offset, state, profile, out var status)); Assert.AreEqual(FixedStatus.BufferTooSmall, status);
            Assert.AreEqual(20, offset); foreach (byte b in buffer) Assert.AreEqual(93, b);
            offset = -1; Assert.IsFalse(FixedRandomNetwork.TryReadState(buffer.AsSpan(), ref offset, profile, out _, out _)); Assert.AreEqual(-1, offset);
            var mismatch = new FixedRandomNetworkProfile(new FixedSettings(2, 3)); offset = 0;
            Assert.IsFalse(FixedRandomNetwork.TryWriteState(buffer.AsSpan(), ref offset, state, mismatch, out status)); Assert.AreEqual(FixedStatus.ProfileMismatch, status); Assert.AreEqual(0, offset);
            byte[][] malformed = { new byte[] { 128 }, new byte[] { 128, 0 }, new byte[] { 255, 255, 255, 255, 255, 255, 255, 255, 255, 2 }, new byte[] { 255, 255, 255, 255, 255, 255, 255, 255, 255, 129 } };
            foreach (byte[] packet in malformed)
            { offset = 0; Assert.IsFalse(FixedRandomNetwork.TryReadPosition(packet.AsSpan(), ref offset, ref random, profile, out _)); Assert.AreEqual(0, offset); Assert.AreEqual(state, random.CaptureState()); }
            offset = 0; Assert.IsTrue(FixedRandomNetwork.TryWriteDescriptor(buffer.AsSpan(), ref offset, profile, out _)); buffer[6] = 2; offset = 0;
            Assert.IsFalse(FixedRandomNetwork.TryReadDescriptor(buffer.AsSpan(), ref offset, out _, out status)); Assert.AreEqual(FixedStatus.ProfileMismatch, status); Assert.AreEqual(0, offset);
            fixed (byte* p = buffer)
            {
                var writer = new FixedBufferWriter(p, buffer.Length, 1); Assert.IsFalse(FixedRandomNetwork.TryWriteState(ref writer, state, profile, out status));
                Assert.AreEqual(FixedStatus.InvalidEncoding, status); Assert.AreEqual(1, writer.BitPosition);
            }
        }
    }
}
